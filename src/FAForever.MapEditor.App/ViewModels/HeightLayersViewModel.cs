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
/// A height layer as the Terrain panel lists it, the highest first.
/// </summary>
public sealed partial class HeightLayerItem(HeightLayer layer, int index, int count, Func<string, JsonObject, Task> run) : ObservableObject
{
    public HeightLayer Layer => layer;

    public string Name => layer.Name;

    /// <summary>
    /// Its place in the stack: 0 is the base.
    /// </summary>
    public int Index => index;

    public bool IsBase => layer.Kind == HeightLayerKind.Base;

    public bool CanMoveUp => !IsBase && index < count - 1;

    public bool CanMoveDown => !IsBase && index > 1;

    /// <summary>
    /// E.g. <c>Sculpt · add · 50 % · mask rivers</c>.
    /// </summary>
    public string Summary => HeightLayersViewModel.SummaryOf(layer);

    /// <summary>
    /// Whether it counts; unticking hides it (<c>terrain.setLayer</c>).
    /// </summary>
    public bool IsShown
    {
        get => layer.Visible;
        set
        {
            if (value != layer.Visible)
            {
                _ = run("terrain.setLayer", new JsonObject { ["layer"] = layer.Name, ["visible"] = value });
            }
        }
    }

    /// <summary>
    /// Whether edits of its values leave it alone; ticking locks it (<c>terrain.setLayer</c>).
    /// </summary>
    public bool IsLocked
    {
        get => layer.Locked;
        set
        {
            if (value != layer.Locked)
            {
                _ = run("terrain.setLayer", new JsonObject { ["layer"] = layer.Name, ["locked"] = value });
            }
        }
    }

    public override string ToString() => layer.Name;
}

/// <summary>
/// The height layers of the open map (user story T-5) as the Terrain panel lists them, and the
/// chosen one: the sculpt tool writes into it (<see cref="Project.Editing.EditSession.SculptLayer"/>),
/// the inspector shows its settings and the curve tool edits it when it is a curve. The layers
/// themselves are the document's; every change is a command (<c>terrain.addLayer</c>,
/// <c>terrain.removeLayer</c>, <c>terrain.moveLayer</c>, <c>terrain.setLayer</c>,
/// <c>terrain.setCurve</c>, <c>terrain.bake</c>, ...), one step of undo.
/// </summary>
public sealed partial class HeightLayersViewModel : ObservableObject
{
    /// <summary>
    /// What the fields show while no layer is chosen.
    /// </summary>
    private static readonly HeightLayer NoLayer = new HeightLayer { Name = "", Kind = HeightLayerKind.Sculpt, Values = TiledBuffer<float>.Create(1, 1) };

    private readonly Func<string, JsonObject, Task> _run;
    private ImmutableArray<HeightLayer> _shown;
    private string _maskNames = "";
    private string _signature = "";
    private MapDocument? _document;

    public static IReadOnlyList<string> Combines { get; } = [.. Enum.GetNames<HeightCombine>()];

    public static IReadOnlyList<string> Profiles { get; } = [.. Enum.GetNames<CurveProfile>()];

    /// <summary>
    /// What the mask choice says for none.
    /// </summary>
    public const string Everywhere = "Everywhere";

    public HeightLayersViewModel(Func<string, JsonObject, Task> run)
    {
        _run = run;
        Layer = MakeLayerSettings([Everywhere]);
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
        ]);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty), nameof(HasLayers))]
    public partial IReadOnlyList<HeightLayerItem> Items { get; private set; } = [];

    public bool IsEmpty => Items.Count == 0;

    public bool HasLayers => Items.Count > 0;

    /// <summary>
    /// The chosen layer's name; null without layers.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChosenItem), nameof(ChosenLayer), nameof(CanRemove), nameof(CanMoveUp), nameof(CanMoveDown),
        nameof(IsCurve), nameof(IsImported), nameof(ChosenTitle), nameof(ChosenFacts))]
    public partial string? Chosen { get; set; }

    public HeightLayerItem? ChosenItem
    {
        get => Items.FirstOrDefault(item => item.Name == Chosen);
        set
        {
            if (value is not null)
            {
                Chosen = value.Name;
            }
        }
    }

    public HeightLayer? ChosenLayer => ChosenItem?.Layer;

    public bool CanRemove => ChosenItem is { IsBase: false };

    public bool CanMoveUp => ChosenItem?.CanMoveUp == true;

    public bool CanMoveDown => ChosenItem?.CanMoveDown == true;

    public bool IsCurve => ChosenLayer?.Kind == HeightLayerKind.Curve;

    public bool IsImported => ChosenLayer?.Kind == HeightLayerKind.Imported;

    public string ChosenTitle => ChosenLayer is HeightLayer layer ? $"{layer.Name} ({KindOf(layer).ToLowerInvariant()})" : "No height layer";

    /// <summary>
    /// What else the inspector says about the chosen layer: an imported layer's file, a curve's
    /// points, the base's part.
    /// </summary>
    public string ChosenFacts => ChosenLayer switch
    {
        { Kind: HeightLayerKind.Base } => "The map's heights as they were when the stack began; the lowest layer.",
        { Kind: HeightLayerKind.Imported } layer => $"Read from {layer.Source ?? "a file"}; read again when the file changes.",
        { Kind: HeightLayerKind.Curve, Curve: HeightCurve curve } => string.Create(CultureInfo.InvariantCulture,
            $"{curve.Points.Length} points. With the curve tool: click to add a point, drag a point to move it, control and a click removes it."),
        { Kind: HeightLayerKind.Sculpt } => "The sculpt tool writes into it while it is chosen.",
        _ => "",
    };

    /// <summary>
    /// The chosen layer's name, way of combining, strength, mask, visibility and lock.
    /// </summary>
    [ObservableProperty]
    public partial SettingGroup Layer { get; private set; }

    /// <summary>
    /// The chosen curve's profile, width, falloff, height and whether it is closed.
    /// </summary>
    public SettingGroup Curve { get; }

    /// <summary>
    /// What the panel says about the stack: how many layers, which one is sculpted.
    /// </summary>
    [ObservableProperty]
    public partial string Summary { get; private set; } = "";

    /// <summary>
    /// Raised when another layer is chosen.
    /// </summary>
    public event EventHandler? Changed;

    partial void OnChosenChanged(string? value)
    {
        RefreshSettings();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Shows the layers of a document; the chosen one stays while the document has it, else the
    /// highest layer that can be sculpted, else the highest.
    /// </summary>
    public void Refresh(MapDocument? document)
    {
        _document = document;
        ImmutableArray<HeightLayer> layers = document?.HeightLayers ?? [];
        string maskNames = document is null ? "" : string.Join("|", MaskChoices(document));
        if (maskNames != _maskNames)
        {
            _maskNames = maskNames;
            Layer = MakeLayerSettings(document is null ? [Everywhere] : MaskChoices(document));
        }
        // a dab of the sculpt tool changes a layer's values, not what the panel shows of it
        string signature = string.Join("|", layers.Select(layer => layer.Name + ":" + SummaryOf(layer) + ":" + layer.Source));
        if (document is not null && (layers == _shown || signature == _signature))
        {
            _shown = layers;
            return;
        }
        _shown = layers;
        _signature = signature;
        int count = layers.Length;
        Items = [.. layers.Select((layer, index) => new HeightLayerItem(layer, index, count, _run)).Reverse()];
        Summary = count == 0
            ? ""
            : string.Create(CultureInfo.InvariantCulture, $"{count} layers; the heightmap is what they add up to, the highest last");
        string? chosen = Chosen is string name && layers.Any(layer => layer.Name == name)
            ? name
            : (layers.LastOrDefault(layer => HeightLayers.SculptProblem(document!, layer.Name) is null) ?? layers.LastOrDefault())?.Name;
        if (chosen == Chosen)
        {
            OnPropertyChanged(nameof(ChosenItem));
            OnPropertyChanged(nameof(ChosenLayer));
            OnPropertyChanged(nameof(CanRemove));
            OnPropertyChanged(nameof(CanMoveUp));
            OnPropertyChanged(nameof(CanMoveDown));
            OnPropertyChanged(nameof(IsCurve));
            OnPropertyChanged(nameof(IsImported));
            OnPropertyChanged(nameof(ChosenTitle));
            OnPropertyChanged(nameof(ChosenFacts));
            RefreshSettings();
        }
        Chosen = chosen;
    }

    /// <summary>
    /// Chooses a layer by name (exactly, else without regard to case).
    /// </summary>
    /// <exception cref="ArgumentException">The map has no such layer.</exception>
    public void Choose(string name) =>
        Chosen = Items.FirstOrDefault(item => item.Name == name)?.Name
            ?? Items.FirstOrDefault(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Name
            ?? throw new ArgumentException(Items.Count == 0
                ? "The map has no height layers"
                : $"The map has no height layer '{name}': {string.Join(", ", Items.Select(item => item.Name))}");

    /// <summary>
    /// The mask choices of a document: everywhere, its masks for tools, the strata.
    /// </summary>
    public static IReadOnlyList<string> MaskChoices(MapDocument document) =>
        [Everywhere,
            .. document.Masks.Keys.Select(name => HeightLayers.MaskTitle(HeightLayers.MaskKey(name))),
            .. Enumerable.Range(1, document.Terrain.StratumMasks.Layers.Length).Select(layer => HeightLayers.MaskTitle(HeightLayers.StratumKey(layer)))];

    /// <summary>
    /// The mask key a choice names (a key, a title such as <c>mask rivers</c>, or a mask's name),
    /// or null for everywhere.
    /// </summary>
    /// <exception cref="ArgumentException">The document has no such mask.</exception>
    public static string? MaskKeyOf(MapDocument document, string choice)
    {
        choice = choice.Trim();
        if (choice.Length == 0 || choice.Equals(Everywhere, StringComparison.OrdinalIgnoreCase) || choice.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        if (HeightLayers.MaskOf(document, choice) is not null)
        {
            return choice;
        }
        IEnumerable<string> keys = document.Masks.Keys.Select(HeightLayers.MaskKey)
            .Concat(Enumerable.Range(1, document.Terrain.StratumMasks.Layers.Length).Select(HeightLayers.StratumKey));
        return keys.FirstOrDefault(key => HeightLayers.MaskTitle(key).Equals(choice, StringComparison.OrdinalIgnoreCase))
            ?? (document.Masks.ContainsKey(choice) ? HeightLayers.MaskKey(choice) : null)
            ?? throw new ArgumentException($"The map has no mask '{choice}': {string.Join(", ", MaskChoices(document))}");
    }

    /// <summary>
    /// E.g. <c>Sculpt · add · 50 % · mask rivers</c>.
    /// </summary>
    public static string SummaryOf(HeightLayer layer)
    {
        List<string> parts = [KindOf(layer)];
        if (layer.Kind != HeightLayerKind.Base)
        {
            parts.Add(layer.Combine.ToString().ToLowerInvariant());
        }
        if (layer.Strength < 1)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{layer.Strength * 100:0} %"));
        }
        if (layer.Mask is string mask)
        {
            parts.Add(HeightLayers.MaskTitle(mask));
        }
        if (layer.Curve is HeightCurve curve)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{curve.Profile.ToString().ToLowerInvariant()}, {curve.Points.Length} points"));
        }
        if (!layer.Visible)
        {
            parts.Add("hidden");
        }
        if (layer.Locked)
        {
            parts.Add("locked");
        }
        return string.Join(" · ", parts);
    }

    public static string KindOf(HeightLayer layer) => layer.Kind switch
    {
        HeightLayerKind.Base => "Base",
        HeightLayerKind.Sculpt => "Sculpt",
        HeightLayerKind.Imported => "Image",
        _ => "Curve",
    };

    [RelayCommand]
    private Task AddSculpt() => _run("terrain.addLayer", new JsonObject { ["kind"] = "sculpt" });

    [RelayCommand]
    private Task AddImage() => _run("terrain.addLayer", new JsonObject { ["kind"] = "imported" });

    [RelayCommand]
    private Task AddCurve() => _run("tool.terrain.curve", []);

    [RelayCommand]
    private Task Remove() => Chosen is string name ? _run("terrain.removeLayer", new JsonObject { ["layer"] = name }) : Task.CompletedTask;

    [RelayCommand]
    private Task MoveUp() => Chosen is string name ? _run("terrain.moveLayer", new JsonObject { ["layer"] = name, ["by"] = 1 }) : Task.CompletedTask;

    [RelayCommand]
    private Task MoveDown() => Chosen is string name ? _run("terrain.moveLayer", new JsonObject { ["layer"] = name, ["by"] = -1 }) : Task.CompletedTask;

    [RelayCommand]
    private Task Reload() => Chosen is string name ? _run("terrain.reloadLayer", new JsonObject { ["layer"] = name }) : Task.CompletedTask;

    [RelayCommand]
    private Task Bake() => _run("terrain.bake", []);

    private SettingGroup MakeLayerSettings(IReadOnlyList<string> masks)
    {
        JsonObject Fixed() => new JsonObject { ["layer"] = Chosen };
        HeightLayer Of(MapDocument d) => HeightLayers.Find(d, Chosen) ?? NoLayer;
        const string set = "terrain.setLayer";
        return new SettingGroup("Layer",
        [
            new TextSetting("Name", set, "name", d => Of(d).Name, _run, Fixed) { Help = "Letters, digits, spaces, dashes and underscores; also the name of its file in layers/" },
            new ChoiceSetting("Combine", set, "combine", Combines, d => Of(d).Combine.ToString(), _run, Fixed)
            {
                Help = "Add its heights, keep the higher (maximum) or lower (minimum) of it and the layers below, or replace them",
            },
            new NumberSetting("Strength", set, "strength", d => Of(d).Strength, _run, Fixed) { Minimum = 0, Maximum = 1, HasSlider = true, Help = "From 0 (not at all) to 1" },
            new ChoiceSetting("Mask", set, "mask", masks, d => Of(d).Mask is string key ? HeightLayers.MaskTitle(key) : Everywhere, _run, Fixed)
            {
                Help = "Where it works: everywhere, a mask for tools (Paint mode) or a stratum's mask",
            },
            new BoolSetting("Shown", set, "visible", d => Of(d).Visible, _run, Fixed) { Help = "A hidden layer counts for nothing" },
            new BoolSetting("Locked", set, "locked", d => Of(d).Locked, _run, Fixed) { Help = "The sculpt tool and the curve tool leave a locked layer alone" },
        ]);
    }

    private void RefreshSettings()
    {
        MapDocument? document = Chosen is null ? null : _document;
        Layer.Refresh(document);
        Curve.Refresh(IsCurve ? document : null);
    }
}
