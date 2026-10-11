using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The settings of placing decals along a height curve (user story T-4, D2), which the curve's
/// inspector part ("Decals along the curve") shows: the decal, the spacing, width and offset, and
/// whether they alternate sides. <c>decals.alongCurve</c> takes these where its arguments leave
/// something out; <c>decals.chooseAlongCurve</c> chooses the decal.
/// </summary>
public sealed partial class DecalsAlongCurveViewModel : ObservableObject
{
    private readonly MainViewModel _model;

    public DecalsAlongCurveViewModel(MainViewModel model)
    {
        _model = model;
        ChooseCommand = new AsyncRelayCommand(() => model.RunWithAsync("decals.chooseAlongCurve", []));
        PlaceCommand = new AsyncRelayCommand(() => model.RunWithAsync("decals.alongCurve", Arguments()));
        PlaceToolCommand = new AsyncRelayCommand(PlaceToolAlongAsync);
    }

    /// <summary>
    /// The decal to place: an asset's key or a decal set's name, as the place tool holds it; null
    /// before one is chosen (then <c>decals.alongCurve</c> asks with the picker).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(Image), nameof(HasDecal))]
    public partial string? Decal { get; set; }

    public bool HasDecal => Decal is not null;

    /// <summary>
    /// What the section calls the decal.
    /// </summary>
    public string Title => Decal is not string key ? "No decal chosen"
        : _model.Assets.Find(key)?.Name ?? $"{Path.GetFileName(key)} (set)";

    /// <summary>
    /// The decal's picture (a set's first texture's), for the thumbnail.
    /// </summary>
    public string? Image => Decal is not string key ? null
        : _model.Assets.Find(key)?.Image
        ?? (DecalSets.Find(_model.Assets.AllEntries, _model.ShownDocument, key)?.First is string first ? _model.Assets.Find(first)?.Image : null);

    /// <summary>
    /// The distance between two decals' middles along the curve, in ogrids; 0 for their length
    /// (end to end).
    /// </summary>
    [ObservableProperty]
    public partial double Spacing { get; set; }

    /// <summary>
    /// Each decal's width across the curve, in ogrids; its length follows the texture's shape.
    /// </summary>
    [ObservableProperty]
    public partial double Width { get; set; } = 8;

    /// <summary>
    /// How far beside the curve the decals lie, in ogrids: positive on its left (walking from the
    /// first point), negative on its right.
    /// </summary>
    [ObservableProperty]
    public partial double Offset { get; set; }

    /// <summary>
    /// Every other decal on the other side.
    /// </summary>
    [ObservableProperty]
    public partial bool Alternate { get; set; }

    /// <summary>
    /// Opens the picker for the decal (<c>decals.chooseAlongCurve</c>).
    /// </summary>
    public IAsyncRelayCommand ChooseCommand { get; }

    /// <summary>
    /// Places the decals along the chosen curve (<c>decals.alongCurve</c>).
    /// </summary>
    public IAsyncRelayCommand PlaceCommand { get; }

    /// <summary>
    /// The place tool's "Along a curve...": its decal, width and cut-off along the chosen curve (or
    /// the topmost curve), with the section's spacing, offset and sides.
    /// </summary>
    public IAsyncRelayCommand PlaceToolCommand { get; }

    /// <summary>
    /// The section's settings as <c>decals.alongCurve</c>'s arguments.
    /// </summary>
    public JsonObject Arguments()
    {
        JsonObject arguments = new JsonObject
        {
            ["width"] = Width,
            ["offset"] = Offset,
            ["alternate"] = Alternate,
        };
        if (Decal is string key)
        {
            arguments["key"] = key;
        }
        if (Spacing > 0)
        {
            arguments["spacing"] = Spacing;
        }
        return arguments;
    }

    /// <summary>
    /// What automation's <c>state.decalsAlongCurve</c> shows.
    /// </summary>
    public JsonObject Describe() => new JsonObject
    {
        ["decal"] = Decal,
        ["spacing"] = Spacing,
        ["width"] = Width,
        ["offset"] = Offset,
        ["alternate"] = Alternate,
    };

    private Task PlaceToolAlongAsync()
    {
        DecalPlaceTool tool = _model.DecalPlaceTool;
        JsonObject arguments = Arguments();
        arguments["width"] = tool.Width;
        arguments["cutOff"] = tool.CutOffLod;
        arguments.Remove("key");
        if (tool.Decal is string key)
        {
            arguments["key"] = key;
        }
        return _model.RunWithAsync("decals.alongCurve", arguments);
    }
}
