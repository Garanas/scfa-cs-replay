using System.Collections.Immutable;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.Rendering;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The Paint mode's inspector part that edits the rules of a mask for tools (user story K-2).
/// </summary>
public sealed record MaskRulesSection() : PanelSection("Mask rules");

/// <summary>
/// A kind of mask rule as the rule editor offers it.
/// </summary>
public sealed record MaskRuleKindChoice(MaskRuleKind Kind, string Title, string Help)
{
    public override string ToString() => Title;
}

/// <summary>
/// One rule of the mask being edited: its place, kind, fields and buttons.
/// </summary>
public sealed partial class MaskRuleItem(MaskRulesViewModel owner, int index, MaskRule rule, SettingGroup settings)
{
    public int Index => index;

    public MaskRuleKind Kind => rule.Kind;

    /// <summary>
    /// E.g. <c>2. Slope</c>.
    /// </summary>
    public string Title => $"{index + 1}. {MaskRulesViewModel.TitleOf(rule.Kind)}";

    /// <summary>
    /// What the rule does with what the rules before made, in words.
    /// </summary>
    public string Help => MaskRulesViewModel.Kinds.First(kind => kind.Kind == rule.Kind).Help + (index == 0 && rule.MakesValue ? " The first rule is the start." : "");

    public SettingGroup Settings => settings;

    public bool CanMoveUp => index > 0;

    public bool CanMoveDown => index < owner.Rules.Count - 1;

    [RelayCommand]
    private Task MoveUp() => owner.Run("paint.moveMaskRule", new JsonObject { ["index"] = index, ["to"] = index - 1 });

    [RelayCommand]
    private Task MoveDown() => owner.Run("paint.moveMaskRule", new JsonObject { ["index"] = index, ["to"] = index + 1 });

    [RelayCommand]
    private Task Remove() => owner.Run("paint.removeMaskRule", new JsonObject { ["index"] = index });
}

/// <summary>
/// The rules of the mask for tools being edited (user story K-2), as the Paint mode's inspector
/// shows them: a list of rules, each with its fields (<see cref="Setting"/>s that run
/// <c>paint.setMaskRule</c>), a kind to add, how the generated masks stand, and a preview of what
/// the rules make, which the top view shows in place of the mask until it is generated
/// (<c>generate.masks</c>). Rules are the document's (<see cref="MapDocument.MaskRules"/>).
/// </summary>
public sealed partial class MaskRulesViewModel : ObservableObject
{
    private readonly Func<string, JsonObject, Task> _run;
    private readonly Func<IReadOnlyList<Stratum>> _strata;
    private readonly ILogger _log;
    private readonly SynchronizationContext? _ui;
    private string _builtKey = "";
    private MaskRuleSet? _previewOf;
    private TiledBuffer<byte>? _previewOfMask;
    private int _previewVersion;
    private MapDocument? _document;

    public MaskRulesViewModel(Func<string, JsonObject, Task> run, Func<IReadOnlyList<Stratum>> strata, ILogger log, SynchronizationContext? ui)
    {
        _run = run;
        _strata = strata;
        _log = log;
        _ui = ui;
    }

    /// <summary>
    /// The kinds of rules, in the order the editor offers them.
    /// </summary>
    public static IReadOnlyList<MaskRuleKindChoice> Kinds { get; } =
    [
        new MaskRuleKindChoice(MaskRuleKind.Slope, "Slope", "Where the terrain's slope, in degrees, lies in the range."),
        new MaskRuleKindChoice(MaskRuleKind.Height, "Height", "Where the terrain's height, in game units, lies in the range."),
        new MaskRuleKindChoice(MaskRuleKind.WaterDistance, "Distance to water", "Where the distance to the water, in ogrids, lies in the range (0 under it)."),
        new MaskRuleKindChoice(MaskRuleKind.Layer, "Layer", "Another mask or a stratum's mask."),
        new MaskRuleKindChoice(MaskRuleKind.Noise, "Noise", "Smooth noise, features of the scale in ogrids."),
        new MaskRuleKindChoice(MaskRuleKind.Blur, "Blur", "Blurs what the rules before made."),
        new MaskRuleKindChoice(MaskRuleKind.Grow, "Grow", "Widens what the rules before made."),
        new MaskRuleKindChoice(MaskRuleKind.Shrink, "Shrink", "Narrows what the rules before made."),
        new MaskRuleKindChoice(MaskRuleKind.Threshold, "Threshold", "Makes what the rules before made sharp."),
        new MaskRuleKindChoice(MaskRuleKind.Invert, "Invert", "Turns what the rules before made around."),
    ];

    public static string TitleOf(MaskRuleKind kind) => Kinds.First(choice => choice.Kind == kind).Title;

    /// <summary>
    /// How a rule's value meets the ones before, as the field offers it.
    /// </summary>
    public static IReadOnlyList<string> Combines { get; } = [.. Enum.GetNames<MaskCombine>()];

    /// <summary>
    /// The mask whose rules are shown; null when none is.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing), nameof(Heading))]
    public partial string? Editing { get; set; }

    public bool IsEditing => Editing is not null;

    public string Heading => Editing is null ? "No mask chosen" : $"Rules of the mask {Editing}";

    /// <summary>
    /// The rules of the mask, a line each.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRules))]
    public partial IReadOnlyList<MaskRuleItem> Rules { get; private set; } = [];

    public bool HasRules => Rules.Count > 0;

    /// <summary>
    /// The kind the Add button adds.
    /// </summary>
    [ObservableProperty]
    public partial MaskRuleKindChoice NewKind { get; set; } = Kinds[0];

    /// <summary>
    /// How the masks from rules stand, e.g. <c>Out of date: heightmap changed</c>.
    /// </summary>
    [ObservableProperty]
    public partial string Status { get; private set; } = "";

    /// <summary>
    /// Whether the top view shows what the rules make, before they are generated.
    /// </summary>
    [ObservableProperty]
    public partial bool ShowPreview { get; set; } = true;

    /// <summary>
    /// What the top view shows while the rules are edited: what they make, in the mask overlay's
    /// colour; null while none is shown or the preview is not made yet.
    /// </summary>
    [ObservableProperty]
    public partial GridOverlay? PreviewOverlay { get; private set; }

    /// <summary>
    /// Raised when the preview changes, for the top view.
    /// </summary>
    public event EventHandler? PreviewChanged;

    partial void OnShowPreviewChanged(bool value) => UpdatePreview(force: true);

    partial void OnPreviewOverlayChanged(GridOverlay? value) => PreviewChanged?.Invoke(this, EventArgs.Empty);

    partial void OnEditingChanged(string? value)
    {
        _builtKey = "";
        Refresh(_document);
        UpdatePreview(force: true);
    }

    internal Task Run(string id, JsonObject arguments)
    {
        arguments["mask"] = Editing;
        return _run(id, arguments);
    }

    [RelayCommand]
    private Task Add() => Run("paint.addMaskRule", new JsonObject { ["kind"] = NewKind.Kind.ToString() });

    [RelayCommand]
    private void Close() => Editing = null;

    /// <summary>
    /// A mask's Rules... button in the Paint panel.
    /// </summary>
    [RelayCommand]
    private Task Open(string name) => _run("paint.maskRules", new JsonObject { ["name"] = name });

    /// <summary>
    /// Shows the rules of a version of the document: the list is made again when the rules' kinds,
    /// the mask or the layers to choose from change, else each field shows its value.
    /// </summary>
    public void Refresh(MapDocument? document)
    {
        _document = document;
        if (Editing is string name && document?.Masks.ContainsKey(name) != true)
        {
            Editing = null;
            return;
        }
        ImmutableArray<MaskRule> rules = Editing is string mask && document is not null ? document.MaskRulesOf(mask) : [];
        IReadOnlyList<string> layers = RuleLayers.Choices(_strata(), document, Editing);
        string key = $"{Editing}|{string.Join(",", rules.Select(rule => rule.Kind))}|{string.Join(",", layers)}";
        if (key != _builtKey)
        {
            _builtKey = key;
            Rules = [.. rules.Select((rule, index) => new MaskRuleItem(this, index, rule, SettingsOf(index, rule, layers)))];
        }
        foreach (MaskRuleItem item in Rules)
        {
            item.Settings.Refresh(document);
        }
        UpdatePreview(force: false);
    }

    /// <summary>
    /// Shows how the generated masks stand.
    /// </summary>
    public void RefreshStatus(GeneratedState? state, bool running) => Status = running
        ? "Generating..."
        : state?.Status switch
        {
            null or GeneratedStatus.Missing => "Not generated yet in this session",
            GeneratedStatus.Stale => $"Out of date: {string.Join(", ", state.ChangedInputs)} changed",
            _ => "Generated: up to date",
        };

    private SettingGroup SettingsOf(int index, MaskRule rule, IReadOnlyList<string> layers)
    {
        const string set = "paint.setMaskRule";
        string mask = Editing!;
        JsonObject Fixed() => new JsonObject { ["mask"] = mask, ["index"] = index };
        MaskRule Read(MapDocument document)
        {
            ImmutableArray<MaskRule> rules = document.MaskRulesOf(mask);
            return index < rules.Length ? rules[index] : rule;
        }
        List<Setting> settings = [];
        if (rule.MakesValue && index > 0)
        {
            settings.Add(new ChoiceSetting("Combine", set, "combine", Combines, d => Read(d).Combine.ToString(), _run, Fixed)
            {
                Help = "How its value meets what the rules before made: Multiply where both are, Max where either is, Subtract takes it away",
            });
        }
        switch (rule.Kind)
        {
            case MaskRuleKind.Slope:
            case MaskRuleKind.Height:
            case MaskRuleKind.WaterDistance:
            {
                (string unit, double max) = rule.Kind switch
                {
                    MaskRuleKind.Slope => ("degrees", 90.0),
                    MaskRuleKind.Height => ("game units", 512.0),
                    _ => ("ogrids", 256.0),
                };
                settings.Add(new NumberSetting("From", set, "min", d => Read(d).Min, _run, Fixed) { Minimum = 0, Maximum = max, HasSlider = true, Help = $"The lower end of the range, in {unit}" });
                settings.Add(new NumberSetting("To", set, "max", d => Read(d).Max, _run, Fixed) { Minimum = 0, Maximum = max, HasSlider = true, Help = $"The upper end of the range, in {unit}" });
                settings.Add(new NumberSetting("Soft edge", set, "soft", d => Read(d).Soft, _run, Fixed) { Minimum = 0, Maximum = max / 4, HasSlider = true, Help = $"How far beyond the range it fades to nothing, in {unit}" });
                break;
            }
            case MaskRuleKind.Layer:
                settings.Add(new ChoiceSetting("Layer", set, "source", layers, d => RuleLayers.Label(Read(d).Source, _strata()), _run, Fixed) { Help = "A stratum's mask or another mask for tools" });
                settings.Add(new NumberSetting("Full from", set, "min", d => Read(d).Min, _run, Fixed) { Minimum = 0, Maximum = 1, HasSlider = true, Help = "The layer's value from which on it counts as full; 0 takes it as it is" });
                break;
            case MaskRuleKind.Noise:
                settings.Add(new NumberSetting("Scale", set, "scale", d => Read(d).Scale, _run, Fixed) { Minimum = 1, Maximum = 256, HasSlider = true, Help = "The size of its features, in ogrids" });
                settings.Add(new NumberSetting("Seed", set, "seed", d => Read(d).Seed, _run, Fixed) { Help = "Another seed, another noise" });
                break;
            case MaskRuleKind.Blur:
            case MaskRuleKind.Grow:
            case MaskRuleKind.Shrink:
                settings.Add(new NumberSetting("Radius", set, "radius", d => Read(d).Radius, _run, Fixed) { Minimum = 0, Maximum = 32, HasSlider = true, Help = "In ogrids" });
                break;
            case MaskRuleKind.Threshold:
                settings.Add(new NumberSetting("From", set, "min", d => Read(d).Min, _run, Fixed) { Minimum = 0, Maximum = 1, HasSlider = true, Help = "Below this the mask is empty" });
                settings.Add(new NumberSetting("Soft edge", set, "soft", d => Read(d).Soft, _run, Fixed) { Minimum = 0, Maximum = 1, HasSlider = true, Help = "How far above it the mask becomes full; 0 for a hard edge" });
                break;
        }
        if (rule.MakesValue)
        {
            settings.Add(new BoolSetting("Inverted", set, "invert", d => Read(d).Invert, _run, Fixed) { Help = "Its own value turned around before it combines" });
        }
        return new SettingGroup(TitleOf(rule.Kind), settings);
    }

    /// <summary>
    /// Makes the preview again on a thread of the pool when the rules or the mask being edited changed.
    /// </summary>
    private void UpdatePreview(bool force)
    {
        MapDocument? document = _document;
        if (!ShowPreview || Editing is not string mask || document is null || !document.MaskRules.TryGetValue(mask, out MaskRuleSet? rules)
            || !document.Masks.TryGetValue(mask, out TiledBuffer<byte>? held))
        {
            _previewOf = null;
            PreviewOverlay = null;
            return;
        }
        if (!force && ReferenceEquals(rules, _previewOf) && ReferenceEquals(held, _previewOfMask))
        {
            return;
        }
        _previewOf = rules;
        _previewOfMask = held;
        int version = ++_previewVersion;
        _ = Task.Run(() =>
        {
            try
            {
                using Performance.Span measure = Performance.Measure("model.mask-rules-preview");
                // the masks with rules this one reads are made first, as generating does
                MapDocument working = document;
                foreach (string name in MaskRules.Order(document))
                {
                    if (name == mask)
                    {
                        break;
                    }
                    working = working with { Masks = working.Masks.SetItem(name, MaskRules.Generate(working, name, document.MaskRules[name])) };
                }
                TiledBuffer<byte> preview = MaskRules.Generate(working, mask, rules);
                UiThread.Run(_ui, () =>
                {
                    if (version == _previewVersion && Editing == mask && ShowPreview)
                    {
                        PreviewOverlay = new GridOverlay($"rules preview of {mask} {version}", _ => preview);
                    }
                });
            }
            catch (Exception exception) when (exception is ArgumentException)
            {
                UiThread.Run(_ui, () =>
                {
                    if (version == _previewVersion)
                    {
                        Status = exception.Message;
                        PreviewOverlay = null;
                    }
                });
            }
            catch (Exception exception)
            {
                _log.LogError(exception, "The preview of the rules of the mask {Mask} failed: {Reason}", mask, exception.Message);
            }
        });
    }

    /// <summary>
    /// The mask rules' part of automation's state.
    /// </summary>
    public JsonObject Describe(MapDocument? document) => new JsonObject
    {
        ["editing"] = Editing,
        ["status"] = Status,
        ["preview"] = PreviewOverlay is not null,
        ["rules"] = new JsonArray([.. Rules.Select(item => (JsonNode)new JsonObject
        {
            ["index"] = item.Index,
            ["kind"] = item.Kind.ToString(),
            ["fields"] = new JsonArray([.. item.Settings.Settings.Select(setting => (JsonNode)setting.Label)]),
        })]),
        ["masks"] = new JsonArray([.. (document?.MaskRules ?? ImmutableSortedDictionary<string, MaskRuleSet>.Empty).Select(entry => (JsonNode)new JsonObject
        {
            ["mask"] = entry.Key,
            ["rules"] = entry.Value.Rules.Length,
        })]),
    };
}
