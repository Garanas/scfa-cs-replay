using System.Numerics;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Files;

namespace FAForever.MapEditor.App.Tools;

/// <summary>
/// The Procedural mode's Path nodes tool (user story R-3): edits by hand on top of the generated
/// path networks, each a step of undo kept with the map and applied again after every generation.
/// A click on empty ground adds a node of the tool's layer (linked to its nearest generated
/// neighbours); a click on a node chooses it; dragging a node onto empty ground moves it, onto
/// another node links the two (control cuts their link); control and a click, or Delete with a node
/// chosen, removes it. Hand-placed path-node markers are the Markers mode's Path tool's; this tool
/// never touches the markers.
/// </summary>
public sealed partial class PathNodeEditTool(MainViewModel model) : ObservableObject, IMapTool
{
    public double CursorRadius => 0;

    /// <summary>
    /// The layer whose nodes it adds and edits.
    /// </summary>
    [ObservableProperty]
    public partial PathLayer Layer { get; set; } = PathLayer.Land;

    public static IReadOnlyList<PathLayer> Layers { get; } = [PathLayer.Land, PathLayer.Water, PathLayer.Amphibious, PathLayer.Air];

    public string Help => "Click to add a node; drag a node to move it, or onto another to link them (control cuts); control and a click, or Delete, removes one";

    private PreviewPathNode? _node;
    private Vector2 _from;
    private Vector2 _at;
    private bool _dragging;
    private bool _control;
    private ToolInput _input = ToolInput.Default;

    partial void OnLayerChanged(PathLayer value)
    {
        model.ShowGeneratedPathNodes = true;
        model.Markers.Show(Project.Editing.MarkerEdits.KindOf(value), true);
    }

    /// <summary>
    /// How near a node the mouse must be to take it: eight pixels, at least an ogrid and a half.
    /// </summary>
    private float Reach(ToolInput input) => Math.Max(1.5f, input.OgridsPerPixel * 8);

    public void Press(float x, float z) => Press(x, z, ToolInput.Default);

    public void Press(float x, float z, ToolInput input)
    {
        _input = input;
        _from = _at = new Vector2(x, z);
        _dragging = false;
        _control = input.Modifiers.HasFlag(ToolModifiers.Control);
        model.ShowGeneratedPathNodes = true;
        if (model.PathNodes.Preview is null)
        {
            model.Status = "Generate the path networks first (Generate in the panel)";
            _node = null;
            return;
        }
        _node = model.PathNodes.NodeAt(x, z, Reach(input), Layer);
        if (_node is not null)
        {
            model.PathNodes.Chosen = _node;
        }
    }

    public void Drag(float x, float z) => Drag(x, z, _input);

    public void Drag(float x, float z, ToolInput input)
    {
        _at = new Vector2(x, z);
        if (_node is not null && Vector2.Distance(_at, _from) > Reach(input) / 2)
        {
            _dragging = true;
        }
    }

    public void Release()
    {
        if (model.PathNodes.Preview is null)
        {
            return;
        }
        PreviewPathNode? node = _node;
        _node = null;
        if (node is null)
        {
            if (!_dragging)
            {
                _ = model.RunWithAsync("generate.addPathNode", new JsonObject { ["x"] = _from.X, ["z"] = _from.Y, ["layer"] = Layer.ToString() });
            }
            return;
        }
        if (_dragging)
        {
            _dragging = false;
            if (model.PathNodes.NodeAt(_at.X, _at.Y, Reach(_input), Layer) is PreviewPathNode target && target.Index != node.Index)
            {
                _ = model.RunWithAsync("generate.linkPathNodes", new JsonObject
                {
                    ["x"] = node.Node.X, ["z"] = node.Node.Z, ["toX"] = target.Node.X, ["toZ"] = target.Node.Z,
                    ["cut"] = _control, ["layer"] = Layer.ToString(), ["within"] = 0.01f,
                });
            }
            else
            {
                _ = model.RunWithAsync("generate.movePathNode", new JsonObject
                {
                    ["x"] = node.Node.X, ["z"] = node.Node.Z, ["toX"] = _at.X, ["toZ"] = _at.Y, ["layer"] = Layer.ToString(), ["within"] = 0.01f,
                });
            }
            return;
        }
        if (_control)
        {
            _ = Remove(node);
        }
    }

    private Task Remove(PreviewPathNode node) => model.RunWithAsync("generate.removePathNode", new JsonObject
    {
        ["x"] = node.Node.X, ["z"] = node.Node.Z, ["layer"] = node.Layer.ToString(), ["within"] = 0.01f,
    });

    public bool Key(string key, ToolModifiers modifiers)
    {
        if (model.PathNodes.Chosen is not PreviewPathNode chosen)
        {
            return false;
        }
        switch (key)
        {
            case "Delete" or "Back":
                _ = Remove(chosen);
                return true;
            case "Escape":
                model.PathNodes.Chosen = null;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// While a node is dragged: a line from it to the mouse.
    /// </summary>
    public ToolMarks? Marks => _node is not null && _dragging
        ? new ToolMarks([(_at.X, _at.Y)], (_node.Node.X, _node.Node.Z, _at.X, _at.Y))
        : null;
}
