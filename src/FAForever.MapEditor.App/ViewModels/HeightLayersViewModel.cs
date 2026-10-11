using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.Project;
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

    /// <summary>
    /// The filters by use: every layer of the shown heightmap, the shared ones, those only in the
    /// draft or only in the final.
    /// </summary>
    public static IReadOnlyList<string> UseFilters { get; } = ["all", "shared", "draft", "final"];

    /// <summary>
    /// The filters by source: every layer, sculpting (painted), image, curve, mask.
    /// </summary>
    public static IReadOnlyList<string> SourceFilters { get; } = ["all", "sculpt", "image", "curve", "mask"];

    /// <summary>
    /// The height layers a filter by use keeps, in words: <c>the height layers shared by the draft and the final</c>.
    /// </summary>
    public static string UseWords(string use) => use switch
    {
        "shared" => "the height layers shared by the draft and the final",
        "draft" => "the height layers only in the draft",
        "final" => "the height layers only in the final",
        _ => "every height layer",
    };

    /// <summary>
    /// The height layers a filter by source keeps, in words: <c>the curves among the height layers</c>.
    /// </summary>
    public static string SourceWords(string source) => source switch
    {
        "sculpt" => "the sculpted height layers",
        "image" => "the height layers read from images",
        "curve" => "the curves among the height layers",
        "mask" => "the height layers that read a mask",
        _ => "height layers of every source",
    };

    /// <summary>
    /// What both filters keep, for the status line: <c>the height layers only in the draft, and of them the curves</c>.
    /// </summary>
    public static string FiltersWords(string use, string source) => (use, source) switch
    {
        ("all", "all") => "every height layer",
        ("all", _) => SourceWords(source),
        (_, "all") => UseWords(use),
        _ => $"{UseWords(use)}, and of them {source switch { "sculpt" => "the sculpted ones", "image" => "those read from images", "curve" => "the curves", _ => "those that read a mask" }}",
    };

    /// <summary>
    /// The "Used in" choices of the inspector.
    /// </summary>
    public static IReadOnlyList<string> Uses { get; } = ["Draft and final", "Draft only", "Final only"];

    private readonly Func<HeightStackKind> _shownHeights;

    /// <param name="shown">Which heightmap the list shows (the editor's switch); the final when left out.</param>
    public HeightLayersViewModel(Func<string, JsonObject, Task> run, Func<HeightStackKind>? shown = null) : base(run, document => HeightStacks.Of(document, shown?.Invoke() ?? HeightStackKind.Final), HeightCommands,
    [
        new LayerAddChoice("Sculpt layer", "Add a sculpting layer", "A layer the sculpt tool writes into, above the chosen one", "terrain.addLayer", new JsonObject { ["kind"] = "sculpt" }),
        new LayerAddChoice("Image...", "Add a heightmap image as a layer", "A heightmap of World Machine, Gaea or another tool, read again when it changes in the project folder",
            "terrain.addLayer", new JsonObject { ["kind"] = "imported" }),
        new LayerAddChoice("Curve", "Draw a curve", "Chooses the curve tool: click in a view to start a curve layer", "tool.terrain.curve"),
        DecalMasksChoice("terrain.addLayer", new JsonObject { ["kind"] = "decals" }),
    ])
    {
        _run = run;
        _shownHeights = shown ?? (() => HeightStackKind.Final);
        UseChips = [.. UseFilters.Select(value => new LayerFilterChip(char.ToUpperInvariant(value[0]) + value[1..], $"Show {UseWords(value)}", value,
            new AsyncRelayCommand(() => run("terrain.filterLayers", new JsonObject { ["use"] = value }))))];
        SourceChips = [.. SourceFilters.Select(value => new LayerFilterChip(char.ToUpperInvariant(value[0]) + value[1..], $"Show {SourceWords(value)}", value,
            new AsyncRelayCommand(() => run("terrain.filterLayers", new JsonObject { ["source"] = value }))))];
        UpdateChips();
        JsonObject Fixed() => new JsonObject { ["layer"] = Chosen };
        Use = new SettingGroup("Use",
        [
            new ChoiceSetting("Used in", "terrain.setLayer", "use", Uses, d => UseTitle(HeightStacks.UseOf(d, Chosen ?? "") ?? LayerUse.Shared), run, Fixed)
            {
                Help = "Which heightmaps the layer shapes: both (it keeps a place and an on/off of its own in each), the draft only or the final only",
            },
        ]);
        Curve = MakeCurveGroups(run, Fixed);
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

    /// <summary>
    /// The chosen layer's use (draft and final, draft only, final only).
    /// </summary>
    public SettingGroup Use { get; }

    /// <summary>
    /// The chips that filter the list by use, one of them on.
    /// </summary>
    public IReadOnlyList<LayerFilterChip> UseChips { get; }

    /// <summary>
    /// The chips that filter the list by source, one of them on.
    /// </summary>
    public IReadOnlyList<LayerFilterChip> SourceChips { get; }

    /// <summary>
    /// The filter by use: <c>all</c>, <c>shared</c>, <c>draft</c> or <c>final</c>.
    /// </summary>
    public string UseFilter { get; private set; } = "all";

    /// <summary>
    /// The filter by source: <c>all</c>, <c>sculpt</c>, <c>image</c>, <c>curve</c> or <c>mask</c>.
    /// </summary>
    public string SourceFilter { get; private set; } = "all";

    /// <summary>
    /// The heightmap the list shows.
    /// </summary>
    public HeightStackKind ShownHeights => _shownHeights();

    /// <summary>
    /// Filters the list by use and by source (each left as it is when null).
    /// </summary>
    /// <exception cref="ArgumentException">A filter the list does not have.</exception>
    public void SetFilter(string? use, string? source)
    {
        string? u = use?.Trim().ToLowerInvariant();
        string? s = source?.Trim().ToLowerInvariant();
        if (u is not null && !UseFilters.Contains(u))
        {
            throw new ArgumentException($"The height layers filter by use: {string.Join(", ", UseFilters)}, not '{use}'");
        }
        if (s is not null && !SourceFilters.Contains(s))
        {
            throw new ArgumentException($"The height layers filter by source: {string.Join(", ", SourceFilters)}, not '{source}'");
        }
        UseFilter = u ?? UseFilter;
        SourceFilter = s ?? SourceFilter;
        UpdateChips();
        OnPropertyChanged(nameof(UseFilter));
        OnPropertyChanged(nameof(SourceFilter));
        Refresh(Document);
    }

    private void UpdateChips()
    {
        foreach (LayerFilterChip chip in UseChips)
        {
            chip.IsOn = chip.Value == UseFilter;
        }
        foreach (LayerFilterChip chip in SourceChips)
        {
            chip.IsOn = chip.Value == SourceFilter;
        }
    }

    /// <summary>
    /// The "Used in" title of a use.
    /// </summary>
    public static string UseTitle(LayerUse use) => use switch
    {
        LayerUse.Draft => Uses[1],
        LayerUse.Final => Uses[2],
        _ => Uses[0],
    };

    /// <summary>
    /// A use from its title or name: <c>Draft and final</c>, <c>shared</c>, <c>both</c>, <c>draft</c>, <c>Draft only</c>, <c>final</c>, ...
    /// </summary>
    /// <exception cref="ArgumentException">No use.</exception>
    public static LayerUse ParseUse(string text)
    {
        string t = text.Trim().ToLowerInvariant();
        return t switch
        {
            "shared" or "both" or "draft and final" => LayerUse.Shared,
            "draft" or "draft only" => LayerUse.Draft,
            "final" or "final only" => LayerUse.Final,
            _ => throw new ArgumentException($"A height layer is used in the draft and final (shared), the draft or the final, not '{text}'"),
        };
    }

    protected override string FilterKey => UseFilter + "/" + SourceFilter + "/" + ShownHeights;

    protected override bool Shows(MapDocument document, Layer layer)
    {
        if (UseFilter != "all")
        {
            LayerUse? use = HeightStacks.UseOf(document, layer.Name);
            bool matches = UseFilter switch
            {
                "shared" => use == LayerUse.Shared,
                "draft" => use == LayerUse.Draft,
                _ => use == LayerUse.Final,
            };
            if (!matches)
            {
                return false;
            }
        }
        return SourceFilter switch
        {
            "sculpt" => layer.Source is PaintedSource,
            "image" => layer.Source is ImageSource,
            "curve" => layer.Source is CurveSource,
            "mask" => layer.Source is MaskRefSource,
            _ => true,
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
        { IsBase: true, Source: ImageSource image } => $"The final's base: read from {image.Path} again whenever the file changes (a procedural tool's output); the lowest layer.",
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
    /// The chosen curve's profile, width and height; the other groups are <see cref="CurveGroups"/>.
    /// </summary>
    public SettingGroup Curve { get; }

    /// <summary>
    /// The mask choices of a document: everywhere, its masks for tools, the strata.
    /// </summary>
    public static IReadOnlyList<string> MaskChoices(MapDocument document) => ClipChoices(document);

    public override string KindOf(Layer layer) => layer.Source is PaintedSource && !layer.IsBase ? "Sculpt" : base.KindOf(layer);

    /// <summary>
    /// E.g. <c>Sculpt · add · shared</c>: with where it is used once the draft and the final differ.
    /// </summary>
    public override string SummaryOf(Layer layer)
    {
        string summary = base.SummaryOf(layer);
        if (Document is not MapDocument document || HeightStacks.MakeTheSame(document.HeightStack, document.DraftStack))
        {
            return summary;
        }
        return HeightStacks.UseOf(document, layer.Name) switch
        {
            LayerUse.Shared => summary + " · shared",
            LayerUse.Draft => summary + " · draft only",
            LayerUse.Final => summary + " · final only",
            _ => summary,
        };
    }

    protected override string SummaryOfStack(int count) =>
        count == 0 ? "" : string.Create(CultureInfo.InvariantCulture, $"{count} layers; the heightmap is what they add up to, the highest last");

    protected override Layer? Preferred(MapDocument? document, ImmutableArray<Layer> layers) =>
        layers.LastOrDefault(layer => document is not null && HeightLayers.SculptProblem(document, layer.Name, ShownHeights) is null) ?? layers.LastOrDefault();

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
        RefreshCurve();
        DecalMasks?.Refresh(IsDecalMasks ? Document : null);
        Use?.Refresh(Chosen is null ? null : Document);
    }

    /// <summary>
    /// The final makes the map's heightmap; the draft is no file of the project, only written for
    /// World Machine and Gaea when asked.
    /// </summary>
    protected override string ResultNoteOf(MapDocument document, LayerStack stack) =>
        _shownHeights?.Invoke() == HeightStackKind.Draft
            ? $"Makes the draft, kept in memory; Write the draft saves it as {ProceduralFiles.DraftHeightmap} and .r16, the editor's files for World Machine and Gaea"
            : base.ResultNoteOf(document, stack);

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
