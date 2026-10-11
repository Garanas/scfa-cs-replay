using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The stack of a grid of coverage as the stack control shows it (user stories K-1, K-2, S-1, W-2):
/// a mask for tools (the Masks mode), a stratum's mask (the Paint mode) or one of the water's maps
/// (the Water mode). Its commands are the <c>layers.*</c> commands with the target's key
/// (<c>mask-forest</c>, <c>stratum-2</c>, <c>water-foam</c>); a grid without a stack of its own
/// shows its one painted layer (<see cref="CoverageLayers.StackOf"/>). The chosen painted layer is
/// the one the target's brush paints.
/// </summary>
/// <remarks>
/// Besides the layer's settings it shows what a mask reference reads (<see cref="Reference"/>:
/// the mask, threshold, invert); a rules layer's rules are the rule editor's
/// (<see cref="MaskRulesViewModel"/>). A new source (wave 9C's decal masks) is one more add choice
/// and, when it has fields, a group like <see cref="Reference"/>.
/// </remarks>
public sealed partial class CoverageStackViewModel : LayerStackViewModel
{
    private readonly Func<LayerTarget?> _target;
    private readonly Func<string, JsonObject, Task> _run;
    private string _referenceChoices = "";
    private string _decalChoices = "";

    /// <param name="target">The grid shown now; null for none (no mask chosen).</param>
    /// <param name="word">What a layer is called in labels: <c>mask layer</c>.</param>
    public CoverageStackViewModel(Func<string, JsonObject, Task> run, Func<LayerTarget?> target, string word)
        : this(Targeted(run, target), target, word, 0)
    {
    }

    private CoverageStackViewModel(Func<string, JsonObject, Task> run, Func<LayerTarget?> target, string word, int _)
        : base(run, document => target() is LayerTarget shown && CoverageLayers.Exists(document, shown) ? CoverageLayers.StackOf(document, shown) : null,
            new LayerStackCommands("layers.set", "layers.move", "layers.remove", word, ClipArgument: "clip", ClipTitle: "Clip"), AddChoicesOf(word))
    {
        _target = target;
        _run = run;
        Reference = MakeReference([]);
        DecalMasks = MakeDecalMasks([], []);
    }

    /// <summary>
    /// What the chosen decal-masks layer draws (user story D-1, decision 3): which named mask of each
    /// placed decal set (<c>main</c>, <c>moss</c>), and of which sets (every set, or one).
    /// </summary>
    [ObservableProperty]
    public partial SettingGroup DecalMasks { get; private set; }

    /// <summary>
    /// Whether the chosen layer draws the masks of the placed decal sets.
    /// </summary>
    public bool IsDecalMasks => ChosenLayer?.Source is DecalMasksSource;

    /// <summary>
    /// The grid shown now.
    /// </summary>
    public LayerTarget? Target => _target();

    /// <summary>
    /// What the chosen mask reference reads: the mask, its threshold, whether inverted.
    /// </summary>
    [ObservableProperty]
    public partial SettingGroup Reference { get; private set; }

    /// <summary>
    /// Whether the chosen layer reads another mask.
    /// </summary>
    public bool IsReference => ChosenLayer?.Source is MaskRefSource;

    /// <summary>
    /// Whether the chosen layer is made by rules (the rule editor shows its rules).
    /// </summary>
    public bool IsRules => ChosenLayer?.Source is RulesSource;

    /// <summary>
    /// Shows another grid (another mask or stratum chosen): its painted layer is chosen.
    /// </summary>
    public void Retarget(MapDocument? document)
    {
        Chosen = null;
        ForgetShown();
        Refresh(document);
    }

    protected override Layer? Preferred(MapDocument? document, ImmutableArray<Layer> layers) =>
        layers.LastOrDefault(layer => LayerSources.CanPaint(layer.Source) && !layer.Locked && layer.Visible) ?? layers.LastOrDefault();

    protected override string SummaryOfStack(int count) =>
        count <= 1 ? "Its own painted layer: add a layer to build it from others" : string.Create(CultureInfo.InvariantCulture, $"{count} layers, the highest first");

    protected override bool KeepsLastLayer => true;

    protected override string CombineHelp => "Add or subtract its coverage, keep the higher (maximum) or lower (minimum) of it and the layers below, replace them, or multiply";

    protected override void RefreshSettings()
    {
        base.RefreshSettings();
        string choices = Document is null ? "" : string.Join("|", ReferenceChoices(Document));
        if (choices != _referenceChoices)
        {
            _referenceChoices = choices;
            Reference = MakeReference(Document is null ? [] : ReferenceChoices(Document));
        }
        Reference.Refresh(IsReference ? Document : null);
        (IReadOnlyList<string> masks, IReadOnlyList<string> sets) = Document is null ? ([], []) : DecalChoices(Document);
        string decalChoices = string.Join("|", masks) + "/" + string.Join("|", sets);
        if (decalChoices != _decalChoices)
        {
            _decalChoices = decalChoices;
            DecalMasks = MakeDecalMasks(masks, sets);
        }
        DecalMasks.Refresh(IsDecalMasks ? Document : null);
        OnPropertyChanged(nameof(IsDecalMasks));
        OnPropertyChanged(nameof(IsReference));
        OnPropertyChanged(nameof(IsRules));
    }

    /// <summary>
    /// The masks a reference may read: the masks for tools and the strata, but not the grid itself.
    /// </summary>
    private IReadOnlyList<string> ReferenceChoices(MapDocument document) =>
        [.. ClipChoices(document).Skip(1).Where(title => LayerStacks.StackKeyOf(LayerStacks.KeyOf(document, title)) != Target?.Key)];

    private SettingGroup MakeReference(IReadOnlyList<string> masks)
    {
        JsonObject Fixed() => new JsonObject { [Commands.LayerArgument] = Chosen };
        MaskRefSource Of(MapDocument d) => _read(d)?.Find(Chosen)?.Source as MaskRefSource ?? new MaskRefSource("");
        return new SettingGroup("Reads",
        [
            new ChoiceSetting("Mask", "layers.set", "reads", masks, d => LayerStacks.MaskTitle(Of(d).Key), _run, Fixed)
            {
                Help = "The mask for tools or stratum this layer reads; a change of it changes this grid",
            },
            new NumberSetting("Threshold", "layers.set", "threshold", d => Of(d).Threshold, _run, Fixed) { Minimum = 0, Maximum = 1, HasSlider = true, Help = "Below this the mask counts as nothing" },
            new BoolSetting("Inverted", "layers.set", "invert", d => Of(d).Invert, _run, Fixed) { Help = "Where the mask is not" },
        ]);
    }

    /// <summary>
    /// The value of the decal sets' choice that draws the masks of every set.
    /// </summary>
    public const string EverySet = "Every set";

    /// <summary>
    /// The names of masks the placed decal sets have (<c>main</c> first), and the placed sets by their
    /// short name (<c>rock01</c>).
    /// </summary>
    public static (IReadOnlyList<string> Masks, IReadOnlyList<string> Sets) DecalChoices(MapDocument document)
    {
        IReadOnlyList<string> placed = DecalMaskLayers.MaskNamesOf(document);
        IReadOnlyList<string> masks = [DecalSets.MainMask, .. placed.Where(name => name != DecalSets.MainMask)];
        IReadOnlyList<string> sets = [EverySet, .. document.Decals.Where(decal => decal.Set is not null)
            .Select(decal => Path.GetFileName(decal.Set!.Name)).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];
        return (masks, sets);
    }

    /// <summary>
    /// A decal-masks layer's sets as the choice shows them: <see cref="EverySet"/>, or their short names.
    /// </summary>
    public static string SetsTitle(DecalMasksSource source) =>
        source.Sets.IsEmpty ? EverySet : string.Join(", ", source.Sets.Select(set => Path.GetFileName(set)));

    private SettingGroup MakeDecalMasks(IReadOnlyList<string> masks, IReadOnlyList<string> sets)
    {
        JsonObject Fixed() => new JsonObject { [Commands.LayerArgument] = Chosen };
        DecalMasksSource Of(MapDocument d) => _read(d)?.Find(Chosen)?.Source as DecalMasksSource ?? DecalMasksSource.Every;
        return new SettingGroup("Decal masks",
        [
            new ChoiceSetting("Mask", "layers.setDecalMasks", "mask", masks, d => Of(d).Mask, _run, Fixed)
            {
                Help = "Which mask of each decal set it draws: main for the plain _mask, else the name after _mask_ (moss of rock01_mask_moss)",
            },
            new ChoiceSetting("Decal sets", "layers.setDecalMasks", "sets", sets, d => SetsTitle(Of(d)), _run, Fixed)
            {
                Help = "Whose masks it draws: every placed decal set, or the placed decals of one set",
            },
        ]);
    }

    private LayerStack? _read(MapDocument document) =>
        _target() is LayerTarget shown && CoverageLayers.Exists(document, shown) ? CoverageLayers.StackOf(document, shown) : null;

    /// <summary>
    /// The ways to add a layer to a grid of coverage.
    /// </summary>
    private static IReadOnlyList<LayerAddChoice> AddChoicesOf(string word) =>
    [
        new LayerAddChoice("Painted", $"Add a painted {word}", "A layer of its own for the brush: nothing until painted, then it replaces where it was painted", "layers.add", new JsonObject { ["source"] = "painted" }),
        new LayerAddChoice("Image", $"Add an image {word}...", "The grey of an image file (World Machine, Gaea, Krita), read again when it changes", "layers.add", new JsonObject { ["source"] = "image" }),
        new LayerAddChoice("Mask", $"Add a {word} that reads a mask", "Another mask for tools, read as it changes; choose which in its settings", "layers.add", new JsonObject { ["source"] = "mask" }),
        new LayerAddChoice("Stratum", $"Add a {word} that reads a stratum", "A stratum's mask, read as it changes; choose which in its settings", "layers.add", new JsonObject { ["source"] = "stratum" }),
        new LayerAddChoice("Rules", $"Add a rules {word}", "Slope, height, distance to water, other masks and noise; generated when asked", "layers.add", new JsonObject { ["source"] = "rules" }),
        DecalMasksChoice("layers.add", new JsonObject { ["source"] = "decals" }),
    ];

    /// <summary>
    /// The run function with the shown target's key in every command's arguments.
    /// </summary>
    private static Func<string, JsonObject, Task> Targeted(Func<string, JsonObject, Task> run, Func<LayerTarget?> target) => (id, arguments) =>
    {
        if (target() is LayerTarget shown && !arguments.ContainsKey("target"))
        {
            arguments["target"] = shown.Key;
        }
        return run(id, arguments);
    };
}
