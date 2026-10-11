using System.Collections.Immutable;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// One chip of the Select by popover (user story U-10): a choice that is on or off, as a button
/// that stays down.
/// </summary>
public sealed partial class SelectByChip(string title, string label, Func<bool> get, Action<bool> set) : ObservableObject
{
    public string Title => title;

    /// <summary>
    /// For screen readers and automation: <c>Select by faction UEF</c>.
    /// </summary>
    public string Label => label;

    public bool IsOn
    {
        get => get();
        set
        {
            if (get() != value)
            {
                set(value);
            }
        }
    }

    internal void Refresh() => OnPropertyChanged(nameof(IsOn));
}

/// <summary>
/// The popover the select tool's Select by button opens (user story U-10): what to choose units by,
/// the number that match as it changes, and Add and Select, which run <c>select.where</c>. It lies
/// over the top view, in the window's tree.
/// </summary>
public sealed partial class SelectByViewModel : ObservableObject
{
    /// <summary>
    /// The army choice that asks for every army.
    /// </summary>
    public const string AnyArmy = "Any army";

    private readonly MainViewModel _model;
    private readonly HashSet<string> _factions = [];
    private readonly HashSet<string> _kinds = [];
    private readonly HashSet<int> _tech = [];
    private bool _loading;

    public SelectByViewModel(MainViewModel model)
    {
        _model = model;
        Factions = [.. UnitQuery.FactionNames.Select(faction => Chip(_factions, faction, $"Select by faction {Title(faction)}"))];
        Kinds = [.. UnitQuery.Kinds.Select(kind => Chip(_kinds, kind, $"Select by kind {kind}"))];
        Tech = [.. Enumerable.Range(1, 4).Select(level => new SelectByChip(level.ToString(), $"Select by tech {level}",
            () => _tech.Contains(level), on => Toggle(_tech, level, on)))];
        Wrecks = [.. new[] { (WreckChoice.Leave, "Leave out"), (WreckChoice.Only, "Only"), (WreckChoice.Include, "Include") }
            .Select(choice => new SelectByChip(choice.Item2, $"Wrecks: {choice.Item2.ToLowerInvariant()}", () => WreckChoice == choice.Item1, on => { if (on) { WreckChoice = choice.Item1; } }))];
        Places = [.. new[] { ("map", "Map"), ("selection", "Selection"), ("area", "Area") }
            .Select(place => new SelectByChip(place.Item2, $"Within the {place.Item1}", () => Within == place.Item1, on => { if (on) { Within = place.Item1; } }))];
        ArmyChoices = [AnyArmy];
    }

    private SelectByChip Chip(HashSet<string> set, string value, string label) =>
        new SelectByChip(Title(value), label, () => set.Contains(value), on => Toggle(set, value, on));

    private static string Title(string value) => value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];

    private void Toggle<T>(HashSet<T> set, T value, bool on)
    {
        if (on)
        {
            set.Add(value);
        }
        else
        {
            set.Remove(value);
        }
        Changed();
    }

    public IReadOnlyList<SelectByChip> Factions { get; }

    public IReadOnlyList<SelectByChip> Kinds { get; }

    public IReadOnlyList<SelectByChip> Tech { get; }

    public IReadOnlyList<SelectByChip> Wrecks { get; }

    public IReadOnlyList<SelectByChip> Places { get; }

    /// <summary>
    /// Whether the popover shows.
    /// </summary>
    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    /// <summary>
    /// <c>Any army</c> and the map's armies.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<string> ArmyChoices { get; private set; }

    [ObservableProperty]
    public partial string ArmyChoice { get; set; } = AnyArmy;

    [ObservableProperty]
    public partial string Blueprint { get; set; } = "";

    [ObservableProperty]
    public partial WreckChoice WreckChoice { get; set; } = WreckChoice.Leave;

    /// <summary>
    /// <c>map</c>, <c>selection</c> or <c>area</c> (the area of <see cref="AreaChoice"/>).
    /// </summary>
    [ObservableProperty]
    public partial string Within { get; set; } = "map";

    /// <summary>
    /// The map's areas.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<string> AreaChoices { get; private set; } = [];

    [ObservableProperty]
    public partial string? AreaChoice { get; set; }

    public bool InArea => Within == "area";

    /// <summary>
    /// How many units match now.
    /// </summary>
    [ObservableProperty]
    public partial int Count { get; private set; }

    /// <summary>
    /// <c>28 units match</c>, or what is wrong with the choice.
    /// </summary>
    [ObservableProperty]
    public partial string CountText { get; private set; } = "";

    partial void OnArmyChoiceChanged(string value) => Changed();

    partial void OnBlueprintChanged(string value) => Changed();

    partial void OnWreckChoiceChanged(WreckChoice value) => Changed();

    partial void OnWithinChanged(string value)
    {
        OnPropertyChanged(nameof(InArea));
        Changed();
    }

    partial void OnAreaChoiceChanged(string? value) => Changed();

    partial void OnIsOpenChanged(bool value)
    {
        if (value)
        {
            Refresh();
        }
    }

    /// <summary>
    /// Shows or hides the popover.
    /// </summary>
    [RelayCommand]
    public void Toggle() => IsOpen = !IsOpen;

    public void Close() => IsOpen = false;

    /// <summary>
    /// Selects what matches, replacing the selection.
    /// </summary>
    [RelayCommand]
    private Task Select() => _model.RunWithAsync("select.where", Arguments(add: false));

    /// <summary>
    /// Adds what matches to the selection.
    /// </summary>
    [RelayCommand]
    private Task Add() => _model.RunWithAsync("select.where", Arguments(add: true));

    /// <summary>
    /// Asks for every unit again.
    /// </summary>
    [RelayCommand]
    private void Reset()
    {
        _loading = true;
        _factions.Clear();
        _kinds.Clear();
        _tech.Clear();
        ArmyChoice = AnyArmy;
        Blueprint = "";
        WreckChoice = WreckChoice.Leave;
        Within = "map";
        _loading = false;
        Changed();
    }

    /// <summary>
    /// The arguments of <c>select.where</c> that say what the popover has chosen; what is left as it is
    /// stays out.
    /// </summary>
    public JsonObject Arguments(bool add = false)
    {
        JsonObject arguments = [];
        if (ArmyChoice != AnyArmy)
        {
            arguments["army"] = ArmyChoice;
        }
        if (_factions.Count > 0)
        {
            arguments["faction"] = string.Join(",", UnitQuery.FactionNames.Where(_factions.Contains));
        }
        if (_kinds.Count > 0)
        {
            arguments["kind"] = string.Join(",", UnitQuery.Kinds.Where(_kinds.Contains));
        }
        if (_tech.Count > 0)
        {
            arguments["tech"] = string.Join(",", _tech.Order());
        }
        if (Blueprint.Trim().Length > 0)
        {
            arguments["blueprint"] = Blueprint.Trim();
        }
        if (WreckChoice != WreckChoice.Leave)
        {
            arguments["wrecks"] = WreckChoice.ToString().ToLowerInvariant();
        }
        if (Within == "selection" || Within == "area" && AreaChoice is not null)
        {
            arguments["within"] = Within == "area" ? $"area:{AreaChoice}" : "selection";
        }
        if (add)
        {
            arguments["add"] = true;
        }
        return arguments;
    }

    /// <summary>
    /// What the popover holds, for <c>state</c>.
    /// </summary>
    public JsonObject State() => new JsonObject
    {
        ["open"] = IsOpen,
        ["army"] = ArmyChoice == AnyArmy ? null : ArmyChoice,
        ["factions"] = new JsonArray([.. UnitQuery.FactionNames.Where(_factions.Contains).Select(value => (JsonNode)JsonValue.Create(value)!)]),
        ["kinds"] = new JsonArray([.. UnitQuery.Kinds.Where(_kinds.Contains).Select(value => (JsonNode)JsonValue.Create(value)!)]),
        ["tech"] = new JsonArray([.. _tech.Order().Select(value => (JsonNode)JsonValue.Create(value)!)]),
        ["blueprint"] = Blueprint.Length == 0 ? null : Blueprint,
        ["wrecks"] = WreckChoice.ToString().ToLowerInvariant(),
        ["within"] = Within == "area" ? $"area:{AreaChoice}" : Within,
        ["count"] = Count,
        ["countText"] = CountText,
        ["armies"] = new JsonArray([.. ArmyChoices.Select(value => (JsonNode)JsonValue.Create(value)!)]),
        ["areas"] = new JsonArray([.. AreaChoices.Select(value => (JsonNode)JsonValue.Create(value)!)]),
    };

    /// <summary>
    /// The selection changed: counts again while the popover asks within it.
    /// </summary>
    public void SelectionChanged()
    {
        if (IsOpen && Within == "selection")
        {
            Recount();
        }
    }

    /// <summary>
    /// The document changed: the armies, areas and the count again while the popover shows.
    /// </summary>
    public void DocumentChanged()
    {
        if (IsOpen)
        {
            Refresh();
        }
    }

    private void Changed()
    {
        if (_loading)
        {
            return;
        }
        foreach (SelectByChip chip in Factions.Concat(Kinds).Concat(Tech).Concat(Wrecks).Concat(Places))
        {
            chip.Refresh();
        }
        if (IsOpen)
        {
            Recount();
        }
    }

    private void Refresh()
    {
        MapDocument? document = _model.ShownDocument;
        ImmutableArray<string> armies = document?.Armies.Select(army => army.Name).ToImmutableArray() ?? [];
        if (!ArmyChoices.Skip(1).SequenceEqual(armies))
        {
            ArmyChoices = [AnyArmy, .. armies];
            if (!ArmyChoices.Contains(ArmyChoice))
            {
                ArmyChoice = AnyArmy;
            }
        }
        string[] areas = [.. document?.Markers.Areas.Select(area => area.Name) ?? []];
        if (!AreaChoices.SequenceEqual(areas))
        {
            AreaChoices = areas;
            if (AreaChoice is null || !areas.Contains(AreaChoice))
            {
                AreaChoice = areas.FirstOrDefault();
            }
        }
        Recount();
    }

    private void Recount()
    {
        if (_model.ShownDocument is null)
        {
            Count = 0;
            CountText = "No map is open";
            return;
        }
        try
        {
            using Performance.Span measure = Performance.Measure("model.select-by-count");
            string within = Within == "area" ? AreaChoice is null ? "map" : $"area:{AreaChoice}" : Within;
            Count = _model.UnitsWhere(Query(), within).Length;
            CountText = Within == "area" && AreaChoice is null ? "The map has no areas" : $"{MainViewModel.Units(Count)} match";
        }
        catch (ArgumentException exception)
        {
            Count = 0;
            CountText = exception.Message;
        }
    }

    private UnitQuery Query() => new UnitQuery
    {
        Army = ArmyChoice == AnyArmy ? null : ArmyChoice,
        Factions = [.. UnitQuery.FactionNames.Where(_factions.Contains)],
        Layers = [.. UnitQuery.Kinds.Where(_kinds.Contains)],
        Tech = [.. _tech.Order()],
        Blueprint = Blueprint,
        Wrecks = WreckChoice,
    };
}
