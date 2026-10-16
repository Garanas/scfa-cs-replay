using System.Collections.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// Ways to select more easily (user story F-11): select similar things, invert the selection,
/// select what lies inside an area, lock kinds so that clicks and boxes leave them alone (the
/// select tools' filter), hide decals, and isolate the selection so that only it shows and can
/// be picked.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// The kinds no click, box or Select all picks (the select tools' filter): they show, but stay
    /// as they are. None at first; another map keeps the choice.
    /// </summary>
    [ObservableProperty]
    public partial ObjectKinds LockedKinds { get; set; }

    partial void OnLockedKindsChanged(ObjectKinds value)
    {
        // what is locked leaves the selection: it stays as it is
        if (ShownDocument is MapDocument document && Selection.Items.Any(thing => (KindsOf(document, thing) & value) != 0))
        {
            Selection.Set([.. Selection.Items.Where(thing => (KindsOf(document, thing) & value) == 0)]);
        }
        RefreshOverlays();
    }

    /// <summary>
    /// Whether the 3D view draws the decals; hidden, they are not picked either.
    /// </summary>
    [ObservableProperty]
    public partial bool ShowDecals { get; set; } = true;

    partial void OnShowDecalsChanged(bool value)
    {
        _hiddenDecalsOf = null;
        RefreshOverlays();
    }

    /// <summary>
    /// The things that alone show and can be picked while the selection is isolated; null while
    /// it is not. Another map ends it.
    /// </summary>
    [ObservableProperty]
    public partial ImmutableHashSet<ObjectRef>? Isolated { get; private set; }

    partial void OnIsolatedChanged(ImmutableHashSet<ObjectRef>? value)
    {
        _hiddenDecalsOf = null;
        _isolatedMarkersOf = null;
        RefreshOverlays();
    }

    /// <summary>
    /// Whether a click, box or Select all may pick the thing besides its kind: not a decal of a
    /// hidden group, and one of the isolated things while the selection is isolated.
    /// </summary>
    public bool IsPickable(ObjectRef thing) => Decals.IsShown(thing) && (Isolated is null || Isolated.Contains(thing));

    /// <summary>
    /// Whether the views draw the thing, as far as isolating the selection goes.
    /// </summary>
    public bool IsDrawn(ObjectRef thing) => Isolated is null || Isolated.Contains(thing);

    private (MapDocument Document, IReadOnlySet<int> Groups, ImmutableHashSet<ObjectRef>? Isolated, IReadOnlySet<int> Hidden)? _hiddenDecalsOf;

    /// <summary>
    /// The ids of the decals the 3D view leaves out: those of hidden groups, every decal while
    /// decals are hidden, and those not isolated while the selection is.
    /// </summary>
    public IReadOnlySet<int> HiddenDecalIds
    {
        get
        {
            if (ShowDecals && Isolated is null)
            {
                return Decals.HiddenDecals;
            }
            if (ShownDocument is not MapDocument document)
            {
                return Decals.HiddenDecals;
            }
            if (_hiddenDecalsOf is { } made && made.Document.Decals == document.Decals && ReferenceEquals(made.Groups, Decals.HiddenDecals) && made.Isolated == Isolated)
            {
                return made.Hidden;
            }
            using Performance.Span measure = Performance.Measure("model.hidden-decals");
            HashSet<int> hidden = [.. document.Decals.Where(decal => !ShowDecals || Decals.HiddenDecals.Contains(decal.Id) || !IsDrawn(ObjectRef.Decal(decal.Id))).Select(decal => decal.Id)];
            _hiddenDecalsOf = (document, Decals.HiddenDecals, Isolated, hidden);
            return hidden;
        }
    }

    private (MarkerSet Markers, ImmutableHashSet<ObjectRef> Isolated, MarkerSet Shown)? _isolatedMarkersOf;

    /// <summary>
    /// The markers the top view draws: all of them, or the isolated ones while the selection is.
    /// </summary>
    public MarkerSet DrawnMarkers(MapDocument document)
    {
        if (Isolated is not ImmutableHashSet<ObjectRef> isolated)
        {
            return document.Markers;
        }
        if (_isolatedMarkersOf is { } made && ReferenceEquals(made.Markers, document.Markers) && made.Isolated == isolated)
        {
            return made.Shown;
        }
        bool Keep(Marker marker) => isolated.Contains(ObjectRef.Marker(marker.Id));
        MarkerSet markers = document.Markers;
        MarkerSet shown = markers with
        {
            Spawns = [.. markers.Spawns.Where(Keep)],
            Resources = [.. markers.Resources.Where(Keep)],
            AiMarkers = [.. markers.AiMarkers.Where(Keep)],
            PathNodes = [.. markers.PathNodes.Where(Keep)],
            WeatherDefinitions = [.. markers.WeatherDefinitions.Where(Keep)],
            WeatherGenerators = [.. markers.WeatherGenerators.Where(Keep)],
            Areas = [.. markers.Areas.Where(area => isolated.Contains(ObjectRef.Area(area.Name)))],
        };
        _isolatedMarkersOf = (markers, isolated, shown);
        return shown;
    }

    private void InitializeSelectionTools()
    {
        MapChanged += (_, _) => Isolated = null;
    }

    /// <summary>
    /// The choice of kinds that holds the thing's kind (a path node is <see cref="ObjectKinds.PathNodes"/>).
    /// </summary>
    public static ObjectKinds KindsOf(MapDocument document, ObjectRef thing) => ObjectRef.KindsOf(thing.Kind, document.IsPathNode(thing));

    /// <summary>
    /// Selects every thing like the selected ones (user story F-11), of the kinds that can be
    /// picked: a prop or unit of the same blueprint (with <paramref name="family"/>, of the same
    /// family, such as every pine), a decal of the same texture, a marker of the same kind (a
    /// deposit of the same resource, a path node of the same layer).
    /// </summary>
    /// <returns>How many are selected.</returns>
    public int SelectSimilar(bool family = false)
    {
        if (ShownDocument is not MapDocument document || Selection.IsEmpty)
        {
            return 0;
        }
        using Performance.Span measure = Performance.Measure("model.select-similar");
        string? Likeness(ObjectRef thing) => document.Find(thing) is object found ? LikenessOf(found, family) : null;
        HashSet<string> wanted = [.. Selection.Items.Select(Likeness).OfType<string>()];
        ObjectKinds kinds = Selection.Items.Aggregate(ObjectKinds.None, (all, thing) => all | KindsOf(document, thing)) & SelectableKinds;
        Selection.Set([.. document.All(kinds, ShownMarkerKinds).Where(IsPickable).Where(thing => Likeness(thing) is string likeness && wanted.Contains(likeness))]);
        return Selection.Count;
    }

    private string? LikenessOf(object thing, bool family)
    {
        if (family && (thing is Prop or PlacedUnit))
        {
            string blueprint = thing is Prop prop ? prop.Blueprint : ((PlacedUnit)thing).Unit.Blueprint;
            if (Assets.Catalog.Find(blueprint) is AssetEntry entry)
            {
                return $"{entry.Kind}:{entry.Group}/{entry.Category}/{entry.Family}";
            }
        }
        return thing is Area ? "area" : SymmetryEdits.LikenessOf(thing);
    }

    /// <summary>
    /// Selects what can be picked and is not selected, and takes what was out of the selection.
    /// </summary>
    public void InvertSelection()
    {
        if (ShownDocument is not MapDocument document)
        {
            return;
        }
        HashSet<ObjectRef> selected = [.. Selection.Items];
        Selection.Set([.. document.All(BoxKinds, ShownMarkerKinds).Where(IsPickable).Where(thing => !selected.Contains(thing))]);
    }

    /// <summary>
    /// Selects what lies inside an area (its rectangle): the things the mode works on, other areas
    /// left out; in the Map mode every kind the views show.
    /// </summary>
    /// <exception cref="ArgumentException">The map has no such area.</exception>
    public void SelectInsideArea(string name, SelectionChange change = SelectionChange.Replace)
    {
        MapDocument document = ShownDocument ?? throw new InvalidOperationException("No map is open");
        Area area = document.Markers.Areas.FirstOrDefault(a => a.Name == name) ?? throw new ArgumentException($"The map has no area '{name}'");
        // the things of the mode inside it; in the Map mode, whose tools pick areas alone, every kind shown
        ObjectKinds kinds = SelectableKinds & ~ObjectKinds.Areas;
        if (kinds == ObjectKinds.None)
        {
            kinds = ShownKinds & ~LockedKinds & ~ObjectKinds.Areas;
        }
        Selection.Apply([.. document.Inside((float)area.X0, (float)area.Z0, (float)area.X1, (float)area.Z1, kinds, ShownMarkerKinds).Where(IsPickable)], change);
    }

    /// <summary>
    /// Isolates the selection: only it shows in the views and can be picked, until it is ended
    /// (<see cref="EndIsolation"/>) or another map opens.
    /// </summary>
    /// <exception cref="InvalidOperationException">Nothing is selected.</exception>
    public void IsolateSelection()
    {
        if (Selection.IsEmpty)
        {
            throw new InvalidOperationException("Select what to isolate first");
        }
        Isolated = [.. Selection.Items];
    }

    /// <summary>
    /// Shows every thing again after <see cref="IsolateSelection"/>.
    /// </summary>
    public void EndIsolation() => Isolated = null;
}
