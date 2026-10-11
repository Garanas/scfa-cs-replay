using System.Collections.Immutable;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The Props panel's rules (user story P-4): the list, add and delete, and generating them.
/// </summary>
public sealed record PropRulesSection() : PanelSection("Rules");

/// <summary>
/// The Props mode's inspector part that edits the rule chosen in the panel (user story P-4).
/// </summary>
public sealed record PropRuleSection() : PanelSection("Rule");

/// <summary>
/// A prop rule as the panel lists it.
/// </summary>
/// <param name="Summary">E.g. <c>pine06, 2 more · 1 234 props</c>.</param>
public sealed record PropRuleItem(string Name, string Summary)
{
    public override string ToString() => Name;
}

/// <summary>
/// A layer condition of the chosen rule: its fields and its cross.
/// </summary>
public sealed partial class PropRuleConditionItem(PropRulesViewModel owner, int index, SettingGroup settings)
{
    public int Index => index;

    public SettingGroup Settings => settings;

    [RelayCommand]
    private Task Remove() => owner.Run("props.removeRuleCondition", new JsonObject { ["index"] = index });
}

/// <summary>
/// The rules that place props from the layers (user story P-4), as the Props panel lists them and
/// the inspector edits the chosen one: its palette (taken from the panel's palette), the layers
/// that allow and forbid, density, spacing, clustering, slope, water and how its props stand. Each
/// field runs <c>props.setRule</c>; Generate runs <c>generate.props</c>, which replaces the props
/// of every rule and leaves the props placed by hand. The rules are the document's
/// (<see cref="MapDocument.PropRules"/>).
/// </summary>
public sealed partial class PropRulesViewModel : ObservableObject
{
    private readonly Func<string, JsonObject, Task> _run;
    private readonly Func<IReadOnlyList<Stratum>> _strata;
    private ImmutableArray<PropRule> _shownRules;
    private ImmutableArray<Prop> _shownProps;
    private string _builtKey = "";
    private MapDocument? _document;

    public PropRulesViewModel(Func<string, JsonObject, Task> run, Func<IReadOnlyList<Stratum>> strata)
    {
        _run = run;
        _strata = strata;
    }

    public static IReadOnlyList<string> Clusterings { get; } = [.. Enum.GetNames<PropClustering>()];

    public static IReadOnlyList<string> Alignments { get; } = [.. Enum.GetNames<PropAlignment>()];

    public static IReadOnlyList<string> ConditionKinds { get; } = ["Allows", "Forbids"];

    /// <summary>
    /// The map's rules, in the order they place.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial IReadOnlyList<PropRuleItem> Items { get; private set; } = [];

    public bool IsEmpty => Items.Count == 0;

    /// <summary>
    /// The chosen rule's name; null when the map has none.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChosenItem), nameof(HasChosen), nameof(Heading))]
    public partial string? Chosen { get; set; }

    public PropRuleItem? ChosenItem
    {
        get => Items.FirstOrDefault(item => item.Name == Chosen);
        set
        {
            if (value is not null && value.Name != Chosen)
            {
                Chosen = value.Name;
                ChosenInList?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>
    /// Raised when a person chooses another rule in the list: the inspector shows it, in place of
    /// the selection.
    /// </summary>
    public event EventHandler? ChosenInList;

    public bool HasChosen => Chosen is not null;

    public string Heading => Chosen is null ? "No rule chosen" : $"The rule {Chosen}";

    /// <summary>
    /// The name the panel's field holds for a new rule.
    /// </summary>
    [ObservableProperty]
    public partial string NewName { get; set; } = "";

    /// <summary>
    /// How the props from rules stand, e.g. <c>Out of date: prop rules changed</c>.
    /// </summary>
    [ObservableProperty]
    public partial string Status { get; private set; } = "";

    /// <summary>
    /// The chosen rule's fields, in groups.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<SettingGroup> Groups { get; private set; } = [];

    /// <summary>
    /// The chosen rule's layer conditions.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConditions))]
    public partial IReadOnlyList<PropRuleConditionItem> Conditions { get; private set; } = [];

    public bool HasConditions => Conditions.Count > 0;

    /// <summary>
    /// The chosen rule's palette in words, e.g. <c>pine06 ×2, pine07 ×1</c>.
    /// </summary>
    [ObservableProperty]
    public partial string PaletteText { get; private set; } = "";

    /// <summary>
    /// The layer a new condition reads.
    /// </summary>
    [ObservableProperty]
    public partial string? NewLayer { get; set; }

    /// <summary>
    /// The layers to choose from.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<string> Layers { get; private set; } = [];

    internal Task Run(string id, JsonObject arguments)
    {
        arguments["rule"] = Chosen;
        return _run(id, arguments);
    }

    partial void OnChosenChanged(string? value)
    {
        _builtKey = "";
        Refresh(_document);
    }

    [RelayCommand]
    private Task Add() => _run("props.addRule", NewName.Trim().Length > 0 ? new JsonObject { ["name"] = NewName.Trim() } : []);

    [RelayCommand]
    private Task Delete() => Run("props.removeRule", []);

    [RelayCommand]
    private Task TakePalette() => Run("props.setRulePalette", []);

    [RelayCommand]
    private Task AddCondition() => Run("props.addRuleCondition", NewLayer is null ? [] : new JsonObject { ["layer"] = NewLayer });

    [RelayCommand]
    private Task Generate() => _run("generate.props", []);

    /// <summary>
    /// Shows the rules of a version of the document; the inspector's fields are made again when the
    /// chosen rule, its conditions or the layers to choose from change.
    /// </summary>
    public void Refresh(MapDocument? document)
    {
        _document = document;
        ImmutableArray<PropRule> rules = document?.PropRules ?? [];
        ImmutableArray<Prop> props = document?.Props ?? [];
        if (rules != _shownRules || props != _shownProps)
        {
            _shownRules = rules;
            _shownProps = props;
            Dictionary<string, int> counts = props.Where(prop => prop.Rule is not null).GroupBy(prop => prop.Rule!).ToDictionary(group => group.Key, group => group.Count());
            Items = [.. rules.Select(rule => new PropRuleItem(rule.Name, SummaryOf(rule, counts.GetValueOrDefault(rule.Name))))];
            if (Chosen is null || document?.FindPropRule(Chosen) is null)
            {
                Chosen = Items.FirstOrDefault()?.Name;
                return;
            }
            OnPropertyChanged(nameof(ChosenItem));
        }
        PropRule? chosen = Chosen is string name ? document?.FindPropRule(name) : null;
        IReadOnlyList<string> layers = RuleLayers.Choices(_strata(), document);
        string key = $"{chosen?.Name}|{chosen?.Conditions.Length}|{string.Join(",", layers)}";
        if (key != _builtKey)
        {
            _builtKey = key;
            Layers = layers;
            NewLayer = layers.Contains(NewLayer ?? "") ? NewLayer : layers.FirstOrDefault();
            Groups = chosen is null ? [] : GroupsOf(chosen.Name);
            Conditions = chosen is null ? [] : [.. chosen.Conditions.Select((_, index) => new PropRuleConditionItem(this, index, ConditionOf(chosen.Name, index, layers)))];
        }
        PaletteText = chosen is null ? "" : chosen.Palette.IsEmpty
            ? "None yet: put props in the panel's palette and take it."
            : string.Join(", ", chosen.Palette.Select(entry => $"{Path.GetFileNameWithoutExtension(entry.Blueprint).Replace("_prop", "")} ×{Setting.Format(entry.Weight)}"));
        foreach (SettingGroup group in Groups)
        {
            group.Refresh(document);
        }
        foreach (PropRuleConditionItem condition in Conditions)
        {
            condition.Settings.Refresh(document);
        }
    }

    /// <summary>
    /// Shows how the props from rules stand.
    /// </summary>
    public void RefreshStatus(GeneratedState? state, bool running) => Status = running
        ? "Generating..."
        : state?.Status switch
        {
            null or GeneratedStatus.Missing => "Not generated yet in this session",
            GeneratedStatus.Stale => $"Out of date: {string.Join(", ", state.ChangedInputs)} changed",
            _ => "Generated: up to date",
        };

    private static string SummaryOf(PropRule rule, int count)
    {
        string palette = rule.Palette.IsEmpty
            ? "no props"
            : Path.GetFileNameWithoutExtension(rule.Palette[0].Blueprint).Replace("_prop", "") + (rule.Palette.Length > 1 ? $", {rule.Palette.Length - 1} more" : "");
        return $"{palette} · {count.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture)} props";
    }

    private IReadOnlyList<SettingGroup> GroupsOf(string name)
    {
        const string set = "props.setRule";
        string current = name;
        JsonObject Fixed() => new JsonObject { ["rule"] = current };
        PropRule Read(MapDocument document) => document.FindPropRule(current) ?? new PropRule { Name = current };
        return
        [
            new SettingGroup("Where and how many",
            [
                new TextSetting("Name", set, "name", d => Read(d).Name, _run, Fixed) { Help = "Its props carry it; renaming takes them along" },
                new NumberSetting("Density", set, "density", d => Read(d).Density, _run, Fixed) { Minimum = 0, Maximum = 20, HasSlider = true, Help = "Props per 100 square ogrids where everything allows" },
                new NumberSetting("Spacing", set, "spacing", d => Read(d).Spacing, _run, Fixed) { Minimum = 0, Maximum = 16, HasSlider = true, Help = "The least distance between two props of the rules, in ogrids" },
                new BoolSetting("Fewer where the layers cover less", set, "fade", d => Read(d).Fade, _run, Fixed) { Help = "The chance of a place is the least value of the layers that allow" },
                new NumberSetting("Least slope", set, "minSlope", d => Read(d).MinSlope, _run, Fixed) { Minimum = 0, Maximum = 90, HasSlider = true, Help = "In degrees" },
                new NumberSetting("Most slope", set, "maxSlope", d => Read(d).MaxSlope, _run, Fixed) { Minimum = 0, Maximum = 90, HasSlider = true, Help = "In degrees" },
                new BoolSetting("Out of the water", set, "aboveWater", d => Read(d).AboveWater, _run, Fixed),
                new NumberSetting("Seed", set, "seed", d => Read(d).Seed, _run, Fixed) { Help = "Another seed, other places" },
                new BoolSetting("Mirror with the map's symmetry", set, "mirror", d => Read(d).Mirror, _run, Fixed)
                {
                    Help = "On a symmetric map each prop comes with its image in every other part",
                },
            ]),
            new SettingGroup("Clustering",
            [
                new ChoiceSetting("Clustering", set, "clustering", Clusterings, d => Read(d).Clustering.ToString(), _run, Fixed)
                {
                    Help = "None: spread evenly; Noise: groves in patches; Clusters: round copses",
                },
                new NumberSetting("Size", set, "clusterSize", d => Read(d).ClusterSize, _run, Fixed) { Minimum = 2, Maximum = 128, HasSlider = true, Help = "The size of a patch or the radius of a copse, in ogrids" },
                new NumberSetting("Cover", set, "clusterCover", d => Read(d).ClusterCover, _run, Fixed) { Minimum = 0, Maximum = 1, HasSlider = true, Help = "How much of the allowed ground the patches or copses cover" },
            ]),
            new SettingGroup("How they stand",
            [
                new ChoiceSetting("Alignment", set, "alignment", Alignments, d => Read(d).Placement.Alignment.ToString(), _run, Fixed)
                {
                    Help = "ByCategory: rocks tilt with the ground, trees stand upright",
                },
                new BoolSetting("Random turn", set, "randomTurn", d => Read(d).Placement.RandomTurn, _run, Fixed),
                new NumberSetting("Random tilt", set, "randomTilt", d => Read(d).Placement.RandomTilt, _run, Fixed) { Minimum = 0, Maximum = 30, HasSlider = true, Help = "The most a prop leans at random, in degrees" },
                new NumberSetting("Smallest scale", set, "scaleMin", d => Read(d).Placement.ScaleMin, _run, Fixed) { Minimum = 0.1, Maximum = 3, HasSlider = true },
                new NumberSetting("Largest scale", set, "scaleMax", d => Read(d).Placement.ScaleMax, _run, Fixed) { Minimum = 0.1, Maximum = 3, HasSlider = true },
                new NumberSetting("Sink", set, "sink", d => Read(d).Placement.Sink, _run, Fixed) { Minimum = 0, Maximum = 2, HasSlider = true, Help = "How far into the terrain, in game units" },
            ]),
        ];
    }

    private SettingGroup ConditionOf(string rule, int index, IReadOnlyList<string> layers)
    {
        const string set = "props.setRuleCondition";
        JsonObject Fixed() => new JsonObject { ["rule"] = rule, ["index"] = index };
        PropRuleCondition Read(MapDocument document) =>
            document.FindPropRule(rule) is PropRule found && index < found.Conditions.Length ? found.Conditions[index] : new PropRuleCondition("");
        return new SettingGroup($"Condition {index + 1}",
        [
            new ChoiceSetting("Layer", set, "layer", layers, d => RuleLayers.Label(Read(d).Layer, _strata()), _run, Fixed) { Help = "A stratum's mask or a mask for tools" },
            new ChoiceSetting("It", set, "kind", ConditionKinds, d => Read(d).Forbid ? "Forbids" : "Allows", _run, Fixed)
            {
                Help = "Allows: props only where it covers; Forbids: none where it covers",
            },
            new NumberSetting("Covers from", set, "threshold", d => Read(d).Threshold, _run, Fixed) { Minimum = 0, Maximum = 1, HasSlider = true, Help = "The layer covers a place where it is at least this" },
        ]);
    }

    /// <summary>
    /// The prop rules' part of automation's state.
    /// </summary>
    public JsonObject Describe() => new JsonObject
    {
        ["chosen"] = Chosen,
        ["status"] = Status,
        ["rules"] = new JsonArray([.. Items.Select(item => (JsonNode)new JsonObject { ["name"] = item.Name, ["summary"] = item.Summary })]),
        ["palette"] = PaletteText,
        ["conditions"] = Conditions.Count,
    };
}
