using System.Collections.Immutable;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tools;

/// <summary>
/// A grid a placing tool snaps to, as its options offer it.
/// </summary>
public sealed record SnapChoice(MarkerSnap Snap, string Title)
{
    public static IReadOnlyList<SnapChoice> All { get; } =
    [
        new SnapChoice(MarkerSnap.Off, "No snapping"),
        new SnapChoice(MarkerSnap.Half, "Snap to ½ ogrid"),
        new SnapChoice(MarkerSnap.Centre, "Snap to ogrid centres"),
    ];

    public static SnapChoice Of(MarkerSnap snap) => All.First(choice => choice.Snap == snap);

    public override string ToString() => Title;
}

/// <summary>
/// What a placing tool of the Markers mode places.
/// </summary>
public enum MarkerPlacement
{
    Spawn,
    Mass,
    Hydrocarbon,
    Ai,

    /// <summary>
    /// The weather of the map: its style and cloud types (the game reads the first).
    /// </summary>
    WeatherDefinition,

    /// <summary>
    /// A place where clouds form.
    /// </summary>
    WeatherGenerator,
}

/// <summary>
/// Places markers of one kind where one clicks (user story R-1), each one step of undo through
/// <c>markers.place</c>, which selects it so the inspector shows it: spawns (the next army,
/// <c>ARMY_n</c>), mass and hydrocarbon deposits (always on an ogrid's centre, where the game puts
/// them, with a warning on steep ground), AI markers of the chosen type. A press on a marker of the
/// kind it places (any deposit for a deposit) selects it and a drag moves it (with its mirror partners), as the Select tool does, so a click on a
/// deposit never stacks another on it. Escape selects nothing.
/// </summary>
public sealed partial class PlaceMarkerTool(MainViewModel model, MarkerPlacement placement) : ObservableObject, IMapTool
{
    private SelectTool? _select;
    private bool _selecting;

    public MarkerPlacement Placement => placement;

    /// <summary>
    /// The grid it places on; deposits are always on an ogrid's centre.
    /// </summary>
    [ObservableProperty]
    public partial SnapChoice Snap { get; set; } = SnapChoice.Of(placement is MarkerPlacement.Spawn or MarkerPlacement.Mass or MarkerPlacement.Hydrocarbon ? MarkerSnap.Centre : MarkerSnap.Half);

    public IReadOnlyList<SnapChoice> Snaps => SnapChoice.All;

    /// <summary>
    /// Whether the grid is the person's choice: deposits have theirs.
    /// </summary>
    public bool CanSnap => placement is not (MarkerPlacement.Mass or MarkerPlacement.Hydrocarbon);

    /// <summary>
    /// The type of AI marker it places.
    /// </summary>
    [ObservableProperty]
    public partial string AiType { get; set; } = "Rally Point";

    public IReadOnlyList<string> AiTypes => AiMarker.KnownTypes;

    public bool IsAi => placement == MarkerPlacement.Ai;

    /// <summary>
    /// What placing a deposit last warned about, from the Markers part.
    /// </summary>
    public MarkersViewModel Markers => model.Markers;

    public double CursorRadius => 0;

    /// <summary>
    /// What the options bar says about using it.
    /// </summary>
    public string Help => placement switch
    {
        MarkerPlacement.Spawn => "Click to place a spawn: the next playable army",
        MarkerPlacement.Ai => "Click to place an AI marker of the chosen type",
        MarkerPlacement.WeatherDefinition => "Click to place the weather of the map (the game reads the first); the inspector sets its style and clouds",
        MarkerPlacement.WeatherGenerator => "Click to place a place where clouds form; the inspector sets how many and how high",
        _ => "Click to place a deposit on an ogrid's centre",
    };

    /// <summary>
    /// The name of the kind in <c>markers.place</c>.
    /// </summary>
    public string KindName => placement switch
    {
        MarkerPlacement.Spawn => "spawn",
        MarkerPlacement.Mass => "mass",
        MarkerPlacement.Hydrocarbon => "hydrocarbon",
        MarkerPlacement.WeatherDefinition => "weatherDefinition",
        MarkerPlacement.WeatherGenerator => "weatherGenerator",
        _ => "ai",
    };

    public void Press(float x, float z) => Press(x, z, ToolInput.Default);

    public void Press(float x, float z, ToolInput input)
    {
        if (model.ShownDocument is null)
        {
            return;
        }
        if (MarkerUnder(model.ShownDocument, x, z, input) is not null)
        {
            // a marker under the mouse is chosen and dragged, as the Select tool does, rather than another stacked on it
            _select ??= new SelectTool(model, ObjectKinds.Markers | ObjectKinds.PathNodes);
            _selecting = true;
            _select.Press(x, z, input);
            return;
        }
        JsonObject arguments = new JsonObject
        {
            ["kind"] = KindName,
            ["x"] = CanSnap ? MarkerEdits.Snap(x, Snap.Snap) : x,
            ["z"] = CanSnap ? MarkerEdits.Snap(z, Snap.Snap) : z,
        };
        if (IsAi)
        {
            arguments["type"] = AiType;
        }
        _ = model.RunWithAsync("markers.place", arguments);
    }

    public void Drag(float x, float z) => Drag(x, z, ToolInput.Default);

    public void Drag(float x, float z, ToolInput input)
    {
        if (_selecting)
        {
            _select!.Drag(x, z, input);
        }
    }

    public void Release()
    {
        if (_selecting)
        {
            _selecting = false;
            _select!.Release();
        }
    }

    public bool Key(string key, ToolModifiers modifiers) => MarkerToolKeys.Escape(model, key);

    /// <summary>
    /// The marker of the kind it places under the mouse: the one the 3D view picked, else the
    /// nearest within a few pixels (and at least within its own ogrid); null for none.
    /// </summary>
    private ObjectRef? MarkerUnder(MapDocument document, float x, float z, ToolInput input)
    {
        if (input.Picks)
        {
            return input.Picked is ObjectRef picked && document.Find(picked) is Marker marker && IsPlaced(marker) ? picked : null;
        }
        float reach = MathF.Max(0.75f, 4 * input.OgridsPerPixel);
        Marker? nearest = null;
        float best = reach * reach;
        foreach (Marker candidate in document.Markers.All)
        {
            float dx = candidate.Position.X - x;
            float dz = candidate.Position.Z - z;
            float distance = dx * dx + dz * dz;
            if (distance <= best && IsPlaced(candidate) && model.IsPickable(ObjectRef.Marker(candidate.Id)))
            {
                best = distance;
                nearest = candidate;
            }
        }
        return nearest is null ? null : ObjectRef.Marker(nearest.Id);
    }

    /// <summary>
    /// Whether a marker is of the kind it places (any deposit for a deposit's tool).
    /// </summary>
    private bool IsPlaced(Marker marker) => placement switch
    {
        MarkerPlacement.Spawn => marker is Spawn,
        MarkerPlacement.Mass or MarkerPlacement.Hydrocarbon => marker is ResourceMarker,
        MarkerPlacement.Ai => marker is AiMarker,
        MarkerPlacement.WeatherDefinition => marker is WeatherDefinition,
        MarkerPlacement.WeatherGenerator => marker is WeatherGenerator,
        _ => false,
    };
}

/// <summary>
/// Places path nodes and links them, per layer (user story R-3 by hand). A click on empty ground
/// places a node of the tool's layer linked to the selected node of that layer, and selects it, so
/// clicks one after the other lay a path (shift starts a new one); a click on a node selects it to
/// go on from there. Dragging from a node to another links the two both ways, with control it
/// takes their link away. Escape selects nothing, so the next node starts a new path.
/// </summary>
public sealed partial class PathTool(MainViewModel model) : ObservableObject, IMapTool
{
    /// <summary>
    /// How far, in pixels, the mouse moves before a press becomes a drag.
    /// </summary>
    private const float DragPixels = 3;

    private ToolInput _input = ToolInput.Default;
    private (float X, float Z) _from;
    private (float X, float Z) _at;
    private bool _dragging;
    private ObjectRef? _node;

    /// <summary>
    /// The layer whose nodes it places.
    /// </summary>
    [ObservableProperty]
    public partial PathLayer Layer { get; set; } = PathLayer.Land;

    public IReadOnlyList<PathLayer> Layers { get; } = Enum.GetValues<PathLayer>();

    partial void OnLayerChanged(PathLayer value) => model.Markers.Show(MarkerEdits.KindOf(value), true);

    [ObservableProperty]
    public partial SnapChoice Snap { get; set; } = SnapChoice.Of(MarkerSnap.Half);

    public IReadOnlyList<SnapChoice> Snaps => SnapChoice.All;

    public double CursorRadius => 0;

    public string Help => "Click to place a node linked to the selected one (shift: a new path); click a node to go on from it; drag from a node to another to link them, with control to unlink";

    public void Press(float x, float z) => Press(x, z, ToolInput.Default);

    public void Drag(float x, float z) => Drag(x, z, _input);

    public void Press(float x, float z, ToolInput input)
    {
        _input = input;
        _from = (x, z);
        _at = (x, z);
        _dragging = false;
        _node = model.ShownDocument is null ? null : NodeAt(x, z, input);
    }

    public void Drag(float x, float z, ToolInput input)
    {
        _at = (x, z);
        if (!_dragging && MathF.Sqrt((x - _from.X) * (x - _from.X) + (z - _from.Z) * (z - _from.Z)) >= DragPixels * _input.OgridsPerPixel)
        {
            _dragging = true;
        }
    }

    public void Release()
    {
        MapDocument? document = model.ShownDocument;
        if (document is null)
        {
            return;
        }
        bool control = _input.Modifiers.HasFlag(ToolModifiers.Control);
        if (_node is ObjectRef node)
        {
            if (!_dragging)
            {
                // go on from this node
                model.Selection.Set([node]);
            }
            else if (NodeAt(_at.X, _at.Z, _input with { Picks = false }) is ObjectRef target && target != node)
            {
                _ = model.RunWithAsync("markers.link", new JsonObject { ["a"] = node.Id, ["b"] = target.Id, ["unlink"] = control });
            }
        }
        else if (!_dragging)
        {
            JsonObject arguments = new JsonObject
            {
                ["kind"] = "path",
                ["x"] = MarkerEdits.Snap(_from.X, Snap.Snap),
                ["z"] = MarkerEdits.Snap(_from.Z, Snap.Snap),
                ["layer"] = Layer.ToString(),
            };
            if (!_input.Modifiers.HasFlag(ToolModifiers.Shift) && LastNode(document) is int last)
            {
                arguments["linkTo"] = last;
            }
            _ = model.RunWithAsync("markers.place", arguments);
        }
        _node = null;
        _dragging = false;
    }

    public bool Key(string key, ToolModifiers modifiers) => MarkerToolKeys.Escape(model, key);

    /// <summary>
    /// The node a new one links to: the one selected node of the tool's layer, or null.
    /// </summary>
    private int? LastNode(MapDocument document) =>
        model.Selection.Count == 1 && document.Find(model.Selection.Items[0]) is PathNode node && node.Layer == Layer ? node.Id : null;

    private ObjectRef? NodeAt(float x, float z, ToolInput input)
    {
        if (input.Picks)
        {
            return input.Picked is ObjectRef picked && model.ShownDocument!.IsPathNode(picked) ? picked : null;
        }
        return model.PickAt(x, z, input.OgridsPerPixel, ObjectKinds.PathNodes);
    }
}

/// <summary>
/// Puts markers in order in a chain (user story R-4): a click on a marker adds it to the end of the
/// chain chosen in the panel (the first click without one makes a new chain), with control takes
/// it out. Escape lets go of the chain, so the next click starts another.
/// </summary>
public sealed partial class ChainTool(MainViewModel model) : ObservableObject, IMapTool
{
    public double CursorRadius => 0;

    public MarkersViewModel Markers => model.Markers;

    public string Help => "Click markers in order to add them to the chosen chain (the first click makes one); control takes a marker out; Escape starts another chain";

    public void Press(float x, float z) => Press(x, z, ToolInput.Default);

    public void Press(float x, float z, ToolInput input)
    {
        if (model.ShownDocument is not MapDocument document)
        {
            return;
        }
        ObjectRef? under = input.Picks
            ? input.Picked is ObjectRef picked && picked.Kind == ObjectKind.Marker ? picked : null
            : model.PickAt(x, z, input.OgridsPerPixel, ObjectKinds.Markers | ObjectKinds.PathNodes);
        if (under is not ObjectRef marker)
        {
            return;
        }
        Chain? chain = model.Markers.SelectedChain is ChainItem chosen ? document.Markers.Chains.FirstOrDefault(c => c.Name == chosen.Name) : null;
        if (input.Modifiers.HasFlag(ToolModifiers.Control))
        {
            if (chain is not null && chain.MarkerIds.Contains(marker.Id))
            {
                _ = model.RunWithAsync("markers.setChain", ChainArguments(chain.Name, chain.MarkerIds.Remove(marker.Id)));
            }
            return;
        }
        _ = chain is null
            ? model.RunWithAsync("markers.addChain", new JsonObject { ["markers"] = new JsonArray(marker.Id) })
            : model.RunWithAsync("markers.setChain", ChainArguments(chain.Name, chain.MarkerIds.Add(marker.Id)));
    }

    public void Drag(float x, float z)
    {
    }

    public void Release()
    {
    }

    public bool Key(string key, ToolModifiers modifiers)
    {
        if (key != "Escape" || model.Markers.SelectedChain is null)
        {
            return false;
        }
        model.Markers.ChooseChain(null);
        return true;
    }

    private static JsonObject ChainArguments(string name, ImmutableArray<int> ids) => new JsonObject
    {
        ["name"] = name,
        ["markers"] = new JsonArray([.. ids.Select(id => (JsonNode)JsonValue.Create(id))]),
    };
}

/// <summary>
/// The keys the marker tools share.
/// </summary>
internal static class MarkerToolKeys
{
    /// <summary>
    /// Escape selects nothing, when something is selected.
    /// </summary>
    public static bool Escape(MainViewModel model, string key)
    {
        if (key != "Escape" || model.Selection.IsEmpty)
        {
            return false;
        }
        _ = model.RunWithAsync("select.none", []);
        return true;
    }
}
