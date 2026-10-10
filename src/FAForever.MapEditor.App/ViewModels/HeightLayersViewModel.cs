using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The Terrain panel's "Height layers" (user story T-5).
/// </summary>
public sealed record HeightLayersSection() : PanelSection("Height layers");

/// <summary>
/// The Terrain inspector's "Layer or curve": the chosen height layer's settings and, for a curve,
/// its shape (user stories T-5, T-4).
/// </summary>
public sealed record HeightLayerSection() : PanelSection("Layer or curve");

/// <summary>
/// The heightmap's stack of layers (user story T-5) as the Terrain panel lists it (the first user
/// of the stack control, <see cref="LayerStackViewModel"/>), and the chosen layer: the sculpt tool
/// writes into it (<see cref="Project.Editing.EditSession.SculptLayer"/>), the inspector shows its
/// settings and the curve tool edits it when it is a curve. Every change is a command
/// (<c>terrain.addLayer</c>, <c>terrain.removeLayer</c>, <c>terrain.moveLayer</c>,
/// <c>terrain.setLayer</c>, <c>terrain.setCurve</c>, <c>terrain.bake</c>, ...), one step of undo.
/// </summary>
public sealed partial class HeightLayersViewModel : LayerStackViewModel
{
    private readonly Func<string, JsonObject, Task> _run;

    public static IReadOnlyList<string> Profiles { get; } = [.. Enum.GetNames<CurveProfile>()];

    /// <summary>
    /// The commands of the heightmap's stack.
    /// </summary>
    public static LayerStackCommands HeightCommands { get; } = new LayerStackCommands("terrain.setLayer", "terrain.moveLayer", "terrain.removeLayer", "height layer");

    public HeightLayersViewModel(Func<string, JsonObject, Task> run) : base(run, document => document.HeightStack, HeightCommands,
    [
        new LayerAddChoice("Sculpt layer", "Add a sculpting layer", "A layer the sculpt tool writes into, above the chosen one", "terrain.addLayer", new JsonObject { ["kind"] = "sculpt" }),
        new LayerAddChoice("Image...", "Add a heightmap image as a layer", "A heightmap of World Machine, Gaea or another tool, read again when it changes in the project folder",
            "terrain.addLayer", new JsonObject { ["kind"] = "imported" }),
        new LayerAddChoice("Curve", "Draw a curve", "Chooses the curve tool: click in a view to start a curve layer", "tool.terrain.curve"),
        DecalMasksChoice("terrain.addLayer", new JsonObject { ["kind"] = "decals" }),
    ])
    {
        _run = run;
        JsonObject Fixed() => new JsonObject { ["layer"] = Chosen };
        HeightCurve CurveOf(MapDocument d) => HeightLayers.Find(d, Chosen)?.Curve ?? new HeightCurve();
        const string set = "terrain.setCurve";
        Curve = new SettingGroup("Curve",
        [
            new ChoiceSetting("Profile", set, "profile", Profiles, d => CurveOf(d).Profile.ToString(), run, Fixed)
            {
                Help = "Ridge raises, valley lowers, road flattens to the points' heights, cliff raises the left side",
            },
            new NumberSetting("Width", set, "width", d => CurveOf(d).Width, run, Fixed) { Minimum = 0, Maximum = 64, HasSlider = true, Help = "The part at full height, in ogrids" },
            new NumberSetting("Falloff", set, "falloff", d => CurveOf(d).Falloff, run, Fixed) { Minimum = 0, Maximum = 64, HasSlider = true, Help = "How far beyond the width it fades out, in ogrids" },
            new NumberSetting("Height", set, "height", d => CurveOf(d).Points.FirstOrDefault()?.Height ?? 0, run, Fixed)
            {
                // as the other fields: a slider beside it; a height typed may go beyond its end
                Minimum = 0,
                Maximum = 256,
                HasSlider = true,
                Help = "Every point's height: how much a ridge, valley or cliff rises, or the road's height in game units",
            },
            new BoolSetting("Closed", set, "closed", d => CurveOf(d).Closed, run, Fixed) { Help = "The last point joins the first" },
            new BoolSetting("Mirrored", set, "mirrored", d => CurveOf(d).Symmetry is not null, run, Fixed)
            {
                Help = "The layer draws the curve's images in the other parts of the map too, by the chosen symmetry",
            },
        ]);
        DecalMasksSource DecalsOf(MapDocument d) => HeightLayers.Find(d, Chosen)?.Source as DecalMasksSource ?? DecalMasksSource.Every;
        const string decals = "terrain.setDecalMasks";
        DecalMasks = new SettingGroup("Decal masks",
        [
            new TextSetting("Mask", decals, "mask", d => DecalsOf(d).Mask, run, Fixed)
            {
                Help = "Which mask of each set: main for the plain _mask, else the name after _mask_ (moss of rock01_mask_moss)",
            },
            new TextSetting("Decal sets", decals, "sets", d => string.Join(", ", DecalsOf(d).Sets), run, Fixed)
            {
                Help = "The sets whose masks it draws, by name (/env/rocky/decals/rock01), separated by commas; empty for every set",
            },
            new NumberSetting("Height", decals, "height", d => DecalsOf(d).Height, run, Fixed)
            {
                Minimum = 0,
                Maximum = 64,
                HasSlider = true,
                Help = "How much a full mask raises the terrain, in game units",
            },
        ]);
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Chosen))
            {
                OnPropertyChanged(nameof(IsDecalMasks));
                OnPropertyChanged(nameof(IsCurve));
                OnPropertyChanged(nameof(IsImported));
                OnPropertyChanged(nameof(ChosenTitle));
                OnPropertyChanged(nameof(ChosenFacts));
            }
        };
    }

    public bool IsCurve => ChosenLayer?.Source is CurveSource;

    /// <summary>
    /// Whether the chosen layer draws the decal sets' masks.
    /// </summary>
    public bool IsDecalMasks => ChosenLayer?.Source is DecalMasksSource;

    /// <summary>
    /// The chosen decal-masks layer's mask name, sets and height.
    /// </summary>
    public SettingGroup DecalMasks { get; }

    public bool IsImported => ChosenLayer?.Source is ImageSource;

    public string ChosenTitle => ChosenLayer is Layer layer ? $"{layer.Name} ({KindOf(layer).ToLowerInvariant()})" : "No height layer";

    /// <summary>
    /// What else the inspector says about the chosen layer: an imported layer's file, a curve's
    /// points, the base's part.
    /// </summary>
    public string ChosenFacts => ChosenLayer switch
    {
        { IsBase: true } => "The map's heights as they were when the stack began; the lowest layer.",
        { Source: ImageSource image } => $"Read from {image.Path}; read again when the file changes.",
        { Source: CurveSource { Curve: HeightCurve curve } } => string.Create(CultureInfo.InvariantCulture,
            $"{curve.Points.Length} points. With the curve tool: click to add a point, drag a point to move it, control and a click removes it."),
        { Source: PaintedSource } => "The sculpt tool writes into it while it is chosen.",
        { Source: MaskRefSource reference } => $"Raises the terrain by {LayerStacks.MaskTitle(reference.Key)}.",
        { Source: DecalMasksSource decals } => $"Draws the {DecalMasksSummary(decals)} at each decal's place, turn and size, and follows the decals when they move.",
        _ => "",
    };

    /// <summary>
    /// The chosen curve's profile, width, falloff, height and whether it is closed.
    /// </summary>
    public SettingGroup Curve { get; }

    /// <summary>
    /// The mask choices of a document: everywhere, its masks for tools, the strata.
    /// </summary>
    public static IReadOnlyList<string> MaskChoices(MapDocument document) => ClipChoices(document);

    public override string KindOf(Layer layer) => layer.Source is PaintedSource && !layer.IsBase ? "Sculpt" : base.KindOf(layer);

    protected override string SummaryOfStack(int count) =>
        count == 0 ? "" : string.Create(CultureInfo.InvariantCulture, $"{count} layers; the heightmap is what they add up to, the highest last");

    protected override Layer? Preferred(MapDocument? document, ImmutableArray<Layer> layers) =>
        layers.LastOrDefault(layer => document is not null && HeightLayers.SculptProblem(document, layer.Name) is null) ?? layers.LastOrDefault();

    protected override string CombineHelp => "Add its heights, subtract them, keep the higher (maximum) or lower (minimum) of it and the layers below, replace them, or multiply them by its values";

    protected override string NameHelp => "Letters, digits, spaces, dashes and underscores; also the name of its file in stacks/heightmap/";

    protected override void OnChosenRefreshed()
    {
        OnPropertyChanged(nameof(IsCurve));
        OnPropertyChanged(nameof(IsImported));
        OnPropertyChanged(nameof(IsDecalMasks));
        OnPropertyChanged(nameof(ChosenTitle));
        OnPropertyChanged(nameof(ChosenFacts));
    }

    protected override void RefreshSettings()
    {
        base.RefreshSettings();
        // the base's constructor refreshes before the curve's group exists
        Curve?.Refresh(IsCurve ? Document : null);
        DecalMasks?.Refresh(IsDecalMasks ? Document : null);
    }

    [RelayCommand]
    private Task AddSculpt() => _run("terrain.addLayer", new JsonObject { ["kind"] = "sculpt" });

    [RelayCommand]
    private Task AddImage() => _run("terrain.addLayer", new JsonObject { ["kind"] = "imported" });

    [RelayCommand]
    private Task AddCurve() => _run("tool.terrain.curve", []);

    [RelayCommand]
    private Task Reload() => Chosen is string name ? _run("terrain.reloadLayer", new JsonObject { ["layer"] = name }) : Task.CompletedTask;

    [RelayCommand]
    private Task Bake() => _run("terrain.bake", []);
}
