using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.Tools;

/// <summary>
/// Places props one click at a time (user story P-1): the prop chosen with Choose (the picker),
/// Ctrl+P or on the shelf, standing on the terrain as the Props panel's Placing says
/// (<see cref="PropsViewModel.Placement"/>). Each click is one step of undo and selects the new
/// prop, so Q and E turn it and Delete takes it away again. A prop dragged from the shelf onto a
/// view is placed where it is dropped.
/// </summary>
public sealed partial class PropPlaceTool(MainViewModel model) : ObservableObject, IMapTool, IAssetDropTarget
{
    /// <summary>
    /// The prop a click places; null until one is chosen.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProp), nameof(Name))]
    public partial AssetEntry? Prop { get; set; }

    public bool HasProp => Prop is not null;

    /// <summary>
    /// The chosen prop's name, or what to do before placing.
    /// </summary>
    public string Name => Prop?.Name ?? "No prop chosen";

    public double CursorRadius => 0;

    /// <summary>
    /// What the options bar says about using it.
    /// </summary>
    public string Help => "Click to place; Q and E turn the new prop, Delete takes it away";

    public void Press(float x, float z)
    {
        if (model.ShownDocument is null)
        {
            return;
        }
        if (Prop is null)
        {
            model.Status = "Choose a prop to place first: Choose..., Ctrl+P, or a prop on the shelf";
            return;
        }
        _ = model.RunWithAsync("props.place", new JsonObject { ["x"] = x, ["z"] = z });
    }

    public void Drag(float x, float z)
    {
    }

    public void Release()
    {
    }

    public bool Key(string key, ToolModifiers modifiers)
    {
        if (model.Selection.IsEmpty || modifiers.HasFlag(ToolModifiers.Control) || modifiers.HasFlag(ToolModifiers.Alt))
        {
            return false;
        }
        float turn = modifiers.HasFlag(ToolModifiers.Shift) ? 90 : 15;
        (string Command, JsonObject Arguments)? run = key switch
        {
            "Q" => ("edit.rotate", new JsonObject { ["degrees"] = -turn }),
            "E" => ("edit.rotate", new JsonObject { ["degrees"] = turn }),
            "Escape" => ("select.none", new JsonObject()),
            _ => null,
        };
        if (run is not (string command, JsonObject arguments))
        {
            return false;
        }
        _ = model.RunWithAsync(command, arguments);
        return true;
    }

    public bool Accepts(AssetEntry entry) => entry.Kind == AssetKind.Prop;

    public void Drop(AssetEntry entry, float x, float z)
    {
        Prop = entry;
        _ = model.RunWithAsync("props.place", new JsonObject { ["key"] = entry.Key, ["x"] = x, ["z"] = z });
    }
}
