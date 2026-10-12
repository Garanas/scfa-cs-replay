using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The props the paint tool paints, with how often each (user story P-2): the Props panel's Palette.
/// </summary>
public sealed record PropPaletteSection() : PanelSection("Palette");

/// <summary>
/// How the place and paint tools stand props on the terrain (user stories P-1, P-2), and whether
/// props follow the terrain when it is sculpted.
/// </summary>
public sealed record PropPlacementSection() : PanelSection("Placing");

/// <summary>
/// The mass and energy of the map's props, and per area (user story P-5).
/// </summary>
public sealed record PropReclaimSection() : PanelSection("Reclaim");

/// <summary>
/// A total of reclaim as a line shows it: the mass and energy after their icons
/// (<c>Controls/EconomyAmount</c>), e.g. <c>Base: [mass] 1,200 [energy] 300 in 45 props</c>.
/// </summary>
/// <param name="Prefix">What comes before the values (an area's name and a colon); null for none.</param>
/// <param name="Suffix">What comes after them: <c>in 45 props</c>.</param>
/// <param name="Text">The whole line in words, as a screen reader says it.</param>
public sealed record ReclaimLine(string? Prefix, double Mass, double Energy, string Suffix, string Text)
{
    /// <summary>
    /// The line of a total, in whole numbers as <see cref="PropsViewModel.Describe"/> says it.
    /// </summary>
    public static ReclaimLine Of(string? prefix, ReclaimTotal total) =>
        new ReclaimLine(prefix, Math.Round(total.Mass), Math.Round(total.Energy), PropsViewModel.InProps(total.Props),
            (prefix is null ? "" : prefix + " ") + PropsViewModel.Describe(total));
}

/// <summary>
/// A way placed props stand, with what the Placing section calls it.
/// </summary>
public sealed record AlignmentChoice(PropAlignment Value, string Title)
{
    public override string ToString() => Title;
}

/// <summary>
/// A prop of the palette, with its weight.
/// </summary>
public sealed partial class PaletteItem(AssetEntry? entry, string blueprint) : ObservableObject
{
    /// <summary>
    /// The blueprint's game path as the map names it, e.g. <c>/env/evergreen/props/trees/pine06_prop.bp</c>.
    /// </summary>
    public string Blueprint => blueprint;

    /// <summary>
    /// The catalogue's key of it (<see cref="AssetEntry.Key"/>).
    /// </summary>
    public string Key => entry?.Key ?? AssetCatalog.KeyOf(blueprint);

    public string Name => entry?.Name ?? Path.GetFileNameWithoutExtension(blueprint);

    public AssetModel? Model => entry?.Model;

    /// <summary>
    /// How often it is chosen against the others; 0 never.
    /// </summary>
    [ObservableProperty]
    public partial double Weight { get; set; } = 1;

    // a typed weight below 0 (or not a number) is 0, as props.addToPalette makes it: the paint tool never meets a negative one
    partial void OnWeightChanged(double value)
    {
        if (!(value >= 0))
        {
            Weight = 0;
        }
    }

    public PaletteEntry ToEntry() => new PaletteEntry(Blueprint, Weight);
}

/// <summary>
/// What a palette preset holds (user story P-2, F-10): the props with their weights, the paint
/// tool's brush and how the props stand.
/// </summary>
public sealed record PropPaletteSettings(
    IReadOnlyList<PaletteEntry> Props,
    double Radius,
    double Density,
    double Spacing,
    PropAlignment Alignment,
    bool RandomTurn,
    double RandomTilt,
    double ScaleMin,
    double ScaleMax,
    double Sink);

/// <summary>
/// The Props mode's part of the editor (user stories P-1, P-2, P-3, P-5): how props are placed
/// (shared by the place and paint tools), the palette, whether props follow the terrain, and the
/// reclaim of the map's props. It makes a prop chosen on the shelf in the Props mode the one the
/// place tool places (or, with the paint tool, a prop of the palette), and places a prop dropped
/// on a view.
/// </summary>
public sealed partial class PropsViewModel : ObservableObject
{
    public const string PaletteKind = PresetKinds.Props;

    // how far the sliders of how props stand reach, in the Placing section and in a rule's fields alike:
    // the rule starts from the Placing section's values, so its sliders must hold them
    public const double MaxTilt = 45;
    public const double MinScale = 0.1;
    public const double MaxScale = 4;
    public const double MaxSink = 5;

    private readonly MainViewModel _model;
    private readonly ILogger _log;
    private (ImmutableArray<Prop> Props, ImmutableArray<Area> Areas, AssetCatalog? Catalog)? _reclaimOf;

    public PropsViewModel(MainViewModel model)
    {
        _model = model;
        _log = model.Logger("Editor");
        model.Assets.PropertyChanged += OnAssetsChanged;
        model.AssetDropped += OnAssetDropped;
        model.Events.DocumentChanged += _ => RefreshReclaim();
        model.MapChanged += (_, _) => RefreshReclaim();
        model.Selection.Inspector.Catalog = () => model.Assets.Catalog;
        model.Presets.Register(new PresetKind(PaletteKind, "Palettes", Capture, Apply));
        Palette.CollectionChanged += (_, _) =>
        {
            if (Palette.Count > 0)
            {
                // a new palette begun: what Clear took away is not offered any more
                _cleared = null;
            }
            OnPropertyChanged(nameof(IsPaletteEmpty));
            OnPropertyChanged(nameof(CanRestorePalette));
        };
        model.MapChanged += (_, _) => _clearedAt = null;
    }

    #region Placing

    /// <summary>
    /// The ways a placed prop can stand, as the Placing section offers them.
    /// </summary>
    public IReadOnlyList<AlignmentChoice> Alignments { get; } =
    [
        new AlignmentChoice(PropAlignment.ByCategory, "By kind"),
        new AlignmentChoice(PropAlignment.Upright, "Upright"),
        new AlignmentChoice(PropAlignment.Terrain, "With the terrain"),
    ];

    /// <summary>
    /// <see cref="Alignment"/> as the Placing section's choice.
    /// </summary>
    public AlignmentChoice ChosenAlignment
    {
        get => Alignments.First(choice => choice.Value == Alignment);
        set
        {
            if (value is not null)
            {
                Alignment = value.Value;
            }
        }
    }

    partial void OnAlignmentChanged(PropAlignment value) => OnPropertyChanged(nameof(ChosenAlignment));

    /// <summary>
    /// How placed props stand: by category (rocks tilt with the ground, trees stand upright),
    /// always upright, or always with the terrain.
    /// </summary>
    [ObservableProperty]
    public partial PropAlignment Alignment { get; set; } = PropAlignment.ByCategory;

    /// <summary>
    /// Whether each placed prop gets a heading at random.
    /// </summary>
    [ObservableProperty]
    public partial bool RandomTurn { get; set; } = true;

    /// <summary>
    /// The most a placed prop leans at random, in degrees.
    /// </summary>
    [ObservableProperty]
    public partial double RandomTilt { get; set; }

    /// <summary>
    /// The smallest scale of a placed prop, chosen at random up to <see cref="ScaleMax"/>.
    /// </summary>
    [ObservableProperty]
    public partial double ScaleMin { get; set; } = 1;

    [ObservableProperty]
    public partial double ScaleMax { get; set; } = 1;

    /// <summary>
    /// How far into the terrain a placed prop is set, in game units; also what Drop to ground sinks.
    /// </summary>
    [ObservableProperty]
    public partial double Sink { get; set; }

    /// <summary>
    /// Whether the props on terrain that a brush raises or lowers move with it, in the same step of
    /// undo as the stroke.
    /// </summary>
    [ObservableProperty]
    public partial bool FollowTerrain { get; set; } = true;

    /// <summary>
    /// What random turns, tilts, scales and variants are drawn from; tests give it a seed.
    /// </summary>
    public Random Random { get; set; } = new Random();

    /// <summary>
    /// The settings of placing as the library takes them.
    /// </summary>
    public PropPlacement Placement => new PropPlacement
    {
        Alignment = Alignment,
        RandomTurn = RandomTurn,
        RandomTilt = (float)RandomTilt,
        ScaleMin = (float)Math.Min(ScaleMin, ScaleMax),
        ScaleMax = (float)Math.Max(ScaleMin, ScaleMax),
        Sink = (float)Sink,
    };

    /// <summary>
    /// Sets the props on the terrain that a stroke raised or lowered back on it (when
    /// <see cref="FollowTerrain"/>), into the stroke's own edit: one step of undo with it. Only the
    /// props on the parts of the heightmap the stroke changed are looked at.
    /// </summary>
    /// <param name="before">The document before the stroke.</param>
    public void FollowStroke(EditTransaction stroke, MapDocument before)
    {
        if (!FollowTerrain || ReferenceEquals(stroke.Document.Terrain.Heightmap.Samples, before.Terrain.Heightmap.Samples))
        {
            return;
        }
        using Performance.Span measure = Performance.Measure("tool.props-follow-terrain");
        int moved = 0;
        stroke.Apply(document => document.PropsFollowTerrain(before.Terrain.Heightmap, out moved));
        if (moved > 0)
        {
            _log.LogDebug("{Count} props followed the terrain of the stroke {Stroke}", moved, stroke.Description);
        }
    }

    #endregion

    #region Palette

    /// <summary>
    /// The props the paint tool paints, each with its weight.
    /// </summary>
    public ObservableCollection<PaletteItem> Palette { get; } = [];

    public bool IsPaletteEmpty => Palette.Count == 0;

    /// <summary>
    /// The palette as the library paints it.
    /// </summary>
    public IReadOnlyList<PaletteEntry> PaletteEntries => [.. Palette.Select(item => item.ToEntry())];

    /// <summary>
    /// Adds a prop of the workspace to the palette, or gives the one there a weight.
    /// </summary>
    /// <exception cref="ArgumentException">The workspace has no such prop.</exception>
    public PaletteItem AddToPalette(string key, double? weight = null)
    {
        AssetEntry entry = _model.Assets.Catalog.Find(key) is { Kind: AssetKind.Prop } found
            ? found
            : throw new ArgumentException($"The workspace has no prop '{key}'");
        return AddToPalette(entry, BlueprintOf(entry), weight);
    }

    private PaletteItem AddToPalette(AssetEntry? entry, string blueprint, double? weight)
    {
        PaletteItem? item = Palette.FirstOrDefault(item => string.Equals(item.Key, entry?.Key ?? AssetCatalog.KeyOf(blueprint), StringComparison.Ordinal));
        if (item is null)
        {
            item = new PaletteItem(entry, blueprint);
            Palette.Add(item);
        }
        if (weight is double given)
        {
            item.Weight = Math.Max(0, given);
        }
        return item;
    }

    // what Clear took away, while the palette stays empty; and the map's version it was cleared at
    private PaletteItem[]? _cleared;
    private MapDocument? _clearedAt;

    /// <summary>
    /// Empties the palette. The palette is the tool's, like a brush's settings, so this is no step of
    /// the map's history; what it held can be brought back (<see cref="RestorePalette"/>) while the
    /// palette stays empty.
    /// </summary>
    /// <param name="at">The map's version as it is cleared: undo right after it (the map unchanged since) restores the palette.</param>
    /// <returns>How many props it held.</returns>
    public int ClearPalette(MapDocument? at = null)
    {
        int count = Palette.Count;
        PaletteItem[] held = [.. Palette];
        Palette.Clear();
        if (count > 0)
        {
            _cleared = held;
            _clearedAt = at;
            OnPropertyChanged(nameof(CanRestorePalette));
        }
        return count;
    }

    /// <summary>
    /// Whether the props Clear took away can be brought back: the palette is still empty.
    /// </summary>
    public bool CanRestorePalette => _cleared is not null && Palette.Count == 0;

    /// <summary>
    /// Whether undo should restore the palette rather than undo a step of the map: the palette was
    /// cleared last, at this version of the map, and is still empty.
    /// </summary>
    public bool UndoesClear(MapDocument? document) => CanRestorePalette && _clearedAt is not null && ReferenceEquals(_clearedAt, document);

    /// <summary>
    /// Brings back the props Clear took away, with their weights.
    /// </summary>
    /// <returns>How many came back; 0 when there is nothing to bring back.</returns>
    public int RestorePalette()
    {
        if (!CanRestorePalette)
        {
            return 0;
        }
        PaletteItem[] held = _cleared!;
        _cleared = null;
        _clearedAt = null;
        foreach (PaletteItem item in held)
        {
            Palette.Add(item);
        }
        OnPropertyChanged(nameof(CanRestorePalette));
        return held.Length;
    }

    /// <summary>
    /// Takes a prop out of the palette.
    /// </summary>
    /// <returns>Whether it was in it.</returns>
    public bool RemoveFromPalette(string key)
    {
        string wanted = AssetCatalog.KeyOf(key);
        PaletteItem? item = Palette.FirstOrDefault(item => item.Key == wanted);
        return item is not null && Palette.Remove(item);
    }

    /// <summary>
    /// A palette item's cross in the panel.
    /// </summary>
    [RelayCommand]
    private Task RemoveItem(PaletteItem item) => _model.RunWithAsync("props.removeFromPalette", new JsonObject { ["key"] = item.Key });

    /// <summary>
    /// The game path a map names a prop of the catalogue by: <c>/env/.../pine06_prop.bp</c>.
    /// </summary>
    public static string BlueprintOf(AssetEntry entry) => "/" + entry.Key;

    private (string Name, JsonObject Settings) Capture()
    {
        if (Palette.Count == 0)
        {
            throw new InvalidOperationException("The palette is empty: add props to it before saving it");
        }
        PropPaintTool paint = _model.Tool<PropPaintTool>();
        PropPaletteSettings settings = new PropPaletteSettings(PaletteEntries, paint.Radius, paint.Density, paint.Spacing,
            Alignment, RandomTurn, RandomTilt, ScaleMin, ScaleMax, Sink);
        string name = string.Join(", ", Palette.Take(2).Select(item => item.Name)) + (Palette.Count > 2 ? $" and {Palette.Count - 2} more" : "");
        return (name, Preset.Of(PaletteKind, name, settings).Settings);
    }

    private void Apply(Preset preset)
    {
        PropPaletteSettings settings = preset.Read<PropPaletteSettings>();
        Palette.Clear();
        int unknown = 0;
        foreach (PaletteEntry prop in settings.Props ?? [])
        {
            AssetEntry? entry = _model.Assets.Catalog.Find(prop.Blueprint);
            unknown += entry is null ? 1 : 0;
            AddToPalette(entry, prop.Blueprint, prop.Weight);
        }
        PropPaintTool paint = _model.Tool<PropPaintTool>();
        paint.Radius = settings.Radius;
        paint.Density = settings.Density;
        paint.Spacing = settings.Spacing;
        Alignment = settings.Alignment;
        RandomTurn = settings.RandomTurn;
        RandomTilt = settings.RandomTilt;
        ScaleMin = settings.ScaleMin;
        ScaleMax = settings.ScaleMax;
        Sink = settings.Sink;
        _model.SelectTool("props.paint");
        if (unknown > 0)
        {
            _log.LogWarning("The palette {Name} has {Count} props the workspace has not", preset.Name, unknown);
            _model.Status = $"The palette {preset.Name} has {unknown} props the workspace has not; they are painted without a model in the editor";
        }
    }

    #endregion

    #region Reclaim

    /// <summary>
    /// The reclaim of the map's props, e.g. <c>12 340 mass, 2 100 energy in 8 304 props</c>.
    /// </summary>
    [ObservableProperty]
    public partial string ReclaimText { get; private set; } = "";

    /// <summary>
    /// What the reclaim leaves out: props whose blueprint the workspace has not; empty when none.
    /// </summary>
    [ObservableProperty]
    public partial string ReclaimNote { get; private set; } = "";

    /// <summary>
    /// The reclaim per area of the map, a line each.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<string> ReclaimAreas { get; private set; } = [];

    /// <summary>
    /// The reclaim of the map's props as the Props panel shows it, after the mass and energy
    /// icons; null without a map.
    /// </summary>
    [ObservableProperty]
    public partial ReclaimLine? ReclaimShown { get; private set; }

    /// <summary>
    /// The reclaim per area, likewise, a line each.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<ReclaimLine> ReclaimAreaLines { get; private set; } = [];

    /// <summary>
    /// The reclaim of the map's props, as last counted.
    /// </summary>
    public ReclaimTotal Reclaim { get; private set; } = ReclaimTotal.None;

    /// <summary>
    /// The reclaim per area, as last counted.
    /// </summary>
    public IReadOnlyList<AreaReclaim> ReclaimPerArea { get; private set; } = [];

    /// <summary>
    /// Counts the reclaim again when the props, the areas or the catalogue changed.
    /// </summary>
    public void RefreshReclaim()
    {
        MapDocument? document = _model.Map?.Document;
        var key = (document?.Props ?? default, document?.Markers.Areas ?? default, _model.Assets.Catalog);
        if (_reclaimOf is { } last && last.Props == key.Item1 && last.Areas == key.Item2 && ReferenceEquals(last.Catalog, key.Item3))
        {
            return;
        }
        _reclaimOf = key;
        using Performance.Span measure = Performance.Measure("model.props-reclaim");
        if (document is null)
        {
            Reclaim = ReclaimTotal.None;
            ReclaimPerArea = [];
        }
        else
        {
            Reclaim = PropReclaim.Of(document.Props, _model.Assets.Catalog);
            ReclaimPerArea = PropReclaim.PerArea(document, _model.Assets.Catalog);
        }
        ReclaimText = document is null ? "" : Describe(Reclaim);
        ReclaimNote = Reclaim.Unknown == 0 ? "" : $"{Count(Reclaim.Unknown)} props have a blueprint the workspace has not and count as nothing.";
        ReclaimAreas = [.. ReclaimPerArea.Select(area => $"{area.Area}: {Describe(area.Total)}")];
        ReclaimShown = document is null ? null : ReclaimLine.Of(null, Reclaim);
        ReclaimAreaLines = [.. ReclaimPerArea.Select(area => ReclaimLine.Of($"{area.Area}:", area.Total))];
    }

    /// <summary>
    /// A total as the panel and the inspector show it: <c>1 200 mass, 300 energy in 45 props</c>.
    /// </summary>
    public static string Describe(ReclaimTotal total) =>
        $"{Count(total.Mass)} mass, {Count(total.Energy)} energy {InProps(total.Props)}";

    /// <summary>
    /// The props a total counts: <c>in 45 props</c>, <c>in 1 prop</c>.
    /// </summary>
    public static string InProps(int props) => $"in {Count(props)} {(props == 1 ? "prop" : "props")}";

    private static string Count(double value) => Math.Round(value).ToString("#,0", CultureInfo.InvariantCulture);

    #endregion

    #region The shelf and dropping

    private AssetCatalog? _catalog;

    private void OnAssetsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AssetsViewModel.Kinds) && _model.Assets.Catalog != _catalog)
        {
            _catalog = _model.Assets.Catalog;
            RefreshReclaim();
        }
        else if (e.PropertyName == nameof(AssetsViewModel.Shown)
            && _model.Assets.Shown is { Kind: AssetKind.Prop } prop
            && _model.Assets.Selected is AssetTile tile
            // the shelf changes the tile and its member one after the other: a member of another tile is not chosen
            && (tile.Entry == prop || tile.Members.Contains(prop))
            && _model.SelectedMode?.Definition.Id == "props")
        {
            Chose(prop, tile);
        }
    }

    /// <summary>
    /// The tile chosen last on the shelf with the paint tool, and the palette item that added.
    /// </summary>
    private (AssetTile? Tile, PaletteItem? Added) _lastChoice;

    /// <summary>
    /// A prop chosen on the shelf or in the picker while the Props mode is on: with the paint tool
    /// it goes into the palette, else it becomes the prop the place tool places (the status line says
    /// so, and that the tool is to be chosen when another is active).
    /// </summary>
    /// <param name="tile">The shelf's tile it was chosen on: another variant of the tile chosen
    /// just before takes the place of the one that added in the palette.</param>
    public void Chose(AssetEntry prop, AssetTile? tile = null)
    {
        if (_model.ActiveTool?.Tool is PropPaintTool)
        {
            if (tile is not null && _lastChoice.Tile == tile && _lastChoice.Added is PaletteItem previous && previous.Key != prop.Key)
            {
                Palette.Remove(previous);
            }
            bool added = Palette.All(item => item.Key != prop.Key);
            PaletteItem item = AddToPalette(prop, BlueprintOf(prop), null);
            if (added || _lastChoice.Tile != tile)
            {
                _lastChoice = (tile, added ? item : null);
            }
            _model.Status = added ? $"Added {prop.Name} to the palette" : $"{prop.Name} is in the palette";
        }
        else
        {
            _model.Tool<PropPlaceTool>().Prop = prop;
            // another tool (Select) stays: say what the choice is for
            _model.Status = _model.ActiveTool?.Tool is PropPlaceTool
                ? $"Placing {prop.Name}: click on the map"
                : $"{prop.Name} is the prop the Place tool places: choose the Place tool to put it on the map";
        }
    }

    /// <summary>
    /// A prop dropped on a view that the active tool did not take: it is placed there, and becomes
    /// the place tool's prop.
    /// </summary>
    private void OnAssetDropped(object? sender, AssetDropEventArgs drop)
    {
        if (drop.Handled || drop.Entry.Kind != AssetKind.Prop || _model.ShownDocument is null)
        {
            return;
        }
        drop.Handled = true;
        _model.Tool<PropPlaceTool>().Prop = drop.Entry;
        _ = _model.RunWithAsync("props.place", new JsonObject { ["key"] = drop.Entry.Key, ["x"] = drop.X, ["z"] = drop.Z });
    }

    #endregion

    /// <summary>
    /// The props part of automation's state.
    /// </summary>
    public JsonObject Describe()
    {
        PropPlaceTool place = _model.Tool<PropPlaceTool>();
        PropPaintTool paint = _model.Tool<PropPaintTool>();
        PropEraseTool erase = _model.Tool<PropEraseTool>();
        return new JsonObject
        {
            ["place"] = place.Prop?.Key,
            ["alignment"] = Alignment.ToString(),
            ["randomTurn"] = RandomTurn,
            ["randomTilt"] = RandomTilt,
            ["scale"] = new JsonArray(ScaleMin, ScaleMax),
            ["sink"] = Sink,
            ["followTerrain"] = FollowTerrain,
            ["palette"] = new JsonArray([.. Palette.Select(item => (JsonNode)new JsonObject { ["key"] = item.Key, ["weight"] = item.Weight })]),
            ["canRestorePalette"] = CanRestorePalette,
            ["paint"] = new JsonObject
            {
                ["radius"] = paint.Radius,
                ["density"] = paint.Density,
                ["spacing"] = paint.Spacing,
                ["onlyOn"] = paint.OnlyOn.Name,
                ["notOn"] = paint.NotOn.Name,
                ["threshold"] = paint.Threshold,
            },
            ["erase"] = new JsonObject { ["radius"] = erase.Radius, ["onlyPalette"] = erase.OnlyPalette },
            ["reclaim"] = new JsonObject
            {
                ["mass"] = Reclaim.Mass,
                ["energy"] = Reclaim.Energy,
                ["props"] = Reclaim.Props,
                ["unknown"] = Reclaim.Unknown,
                ["areas"] = new JsonArray([.. ReclaimPerArea.Select(area => (JsonNode)new JsonObject { ["area"] = area.Area, ["mass"] = area.Total.Mass, ["energy"] = area.Total.Energy, ["props"] = area.Total.Props })]),
            },
        };
    }
}
