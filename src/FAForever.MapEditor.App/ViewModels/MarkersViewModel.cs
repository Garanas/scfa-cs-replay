using System.Collections.Immutable;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The Markers panel's kinds, each shown or hidden in both views (user story R-6).
/// </summary>
public sealed record MarkerKindsSection() : PanelSection("By kind");

/// <summary>
/// The Markers panel's chains: make, rename, put in order, delete (user story R-4).
/// </summary>
public sealed record ChainsSection() : PanelSection("Chains");

/// <summary>
/// The Markers panel's adaptive map: whether it adapts, and which deposits wait for which army
/// (user story R-5).
/// </summary>
public sealed record AdaptiveSection() : PanelSection("Adaptive map");

/// <summary>
/// A kind of marker in the panel: a check box with how many the map has.
/// </summary>
public sealed partial class MarkerKindItem(MarkerKindChoice choice, MarkersViewModel owner) : ObservableObject
{
    public MarkerKinds Kind => choice.Kind;

    /// <summary>
    /// What the command <c>markers.showKind</c> calls it: <c>mass</c>, <c>landPaths</c>, ...
    /// </summary>
    public string Id => choice.Id;

    /// <summary>
    /// E.g. <c>Mass (48)</c>.
    /// </summary>
    [ObservableProperty]
    public partial string Label { get; private set; } = choice.Title;

    public bool IsOn => owner.Shows(choice.Kind);

    internal void Refresh(int count)
    {
        Label = $"{choice.Title} ({count})";
        OnPropertyChanged(nameof(IsOn));
    }
}

/// <summary>
/// A kind of marker by its name in commands and its title in the panel.
/// </summary>
public sealed record MarkerKindChoice(MarkerKinds Kind, string Id, string Title);

/// <summary>
/// A chain in the panel, with its markers in order.
/// </summary>
public sealed record ChainItem(string Name, IReadOnlyList<ChainEntry> Entries)
{
    public string Summary => $"{Name} ({Entries.Count})";

    public override string ToString() => Summary;
}

/// <summary>
/// A marker of a chain, at its place in the order.
/// </summary>
public sealed record ChainEntry(string Chain, int Index, int Id, string Name)
{
    public string Line => $"{Index + 1}. {Name}";
}

/// <summary>
/// A playable army of an adaptive map with the deposits that wait for it.
/// </summary>
public sealed record AdaptiveArmy(string Army, int Deposits)
{
    public string Line => $"{Army}: {Deposits} {(Deposits == 1 ? "deposit" : "deposits")}";
}

/// <summary>
/// The Markers mode's state outside the tools and the inspector (user stories R-4, R-5, R-6):
/// which kinds of markers the views show, the chains and the one chosen, the adaptive map. The
/// panel's controls run commands (<c>markers.showKind</c>, <c>markers.addChain</c>, ...); this
/// holds what they show, refreshed with the document.
/// </summary>
public sealed partial class MarkersViewModel : ObservableObject
{
    private readonly Func<string, JsonObject, Task> _run;
    private MapDocument? _document;
    private string _chainName = "";

    public MarkersViewModel(Func<string, JsonObject, Task> run)
    {
        _run = run;
        Kinds = [.. Choices.Select(choice => new MarkerKindItem(choice, this))];
        Paths = new PathNetworkPanel(run);
    }

    /// <summary>
    /// The kinds one by one, in the order of the panel.
    /// </summary>
    public static IReadOnlyList<MarkerKindChoice> Choices { get; } =
    [
        new MarkerKindChoice(MarkerKinds.Spawns, "spawns", "Spawns"),
        new MarkerKindChoice(MarkerKinds.Mass, "mass", "Mass"),
        new MarkerKindChoice(MarkerKinds.Hydrocarbon, "hydrocarbon", "Hydrocarbon"),
        new MarkerKindChoice(MarkerKinds.AiMarkers, "ai", "AI markers"),
        new MarkerKindChoice(MarkerKinds.Weather, "weather", "Weather"),
        new MarkerKindChoice(MarkerKinds.LandPaths, "landPaths", "Land path nodes"),
        new MarkerKindChoice(MarkerKinds.WaterPaths, "waterPaths", "Water path nodes"),
        new MarkerKindChoice(MarkerKinds.AmphibiousPaths, "amphibiousPaths", "Amphibious path nodes"),
        new MarkerKindChoice(MarkerKinds.AirPaths, "airPaths", "Air path nodes"),
    ];

    /// <summary>
    /// A kind by its name in commands (<see cref="MarkerKindChoice.Id"/>), in any case.
    /// </summary>
    /// <exception cref="ArgumentException">No kind has the name.</exception>
    public static MarkerKinds KindNamed(string name) =>
        Choices.FirstOrDefault(choice => string.Equals(choice.Id, name, StringComparison.OrdinalIgnoreCase))?.Kind
        ?? throw new ArgumentException($"There is no kind of marker '{name}': {string.Join(", ", Choices.Select(choice => choice.Id))}");

    public IReadOnlyList<MarkerKindItem> Kinds { get; }

    /// <summary>
    /// The generated path networks' choices and how they stand (user story R-3).
    /// </summary>
    public PathNetworkPanel Paths { get; }

    /// <summary>
    /// The kinds the views show and picking takes, of those the overlays Markers and Path nodes show.
    /// </summary>
    public MarkerKinds ShownKinds { get; private set; } = MarkerKinds.All;

    /// <summary>
    /// Raised when a kind is shown or hidden: the views draw again.
    /// </summary>
    public event EventHandler? Changed;

    public bool Shows(MarkerKinds kind) => (ShownKinds & kind) == kind;

    /// <summary>
    /// Shows or hides kinds of markers in both views.
    /// </summary>
    public void Show(MarkerKinds kinds, bool on)
    {
        MarkerKinds shown = on ? ShownKinds | kinds : ShownKinds & ~kinds;
        if (shown == ShownKinds)
        {
            return;
        }
        ShownKinds = shown;
        foreach (MarkerKindItem item in Kinds)
        {
            item.Refresh(Count(item.Kind));
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// A check box of the panel: shows or hides its kind (<c>markers.showKind</c>).
    /// </summary>
    [RelayCommand]
    private Task ToggleKind(MarkerKindItem item) => _run("markers.showKind", new JsonObject { ["kind"] = item.Id, ["on"] = !Shows(item.Kind) });

    #region Chains

    /// <summary>
    /// The map's chains, in its order.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<ChainItem> Chains { get; private set; } = [];

    /// <summary>
    /// The chain the panel shows and the chain tool adds to; null to make a new one.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChain))]
    [NotifyCanExecuteChangedFor(nameof(DeleteChainCommand), nameof(SelectChainMarkersCommand))]
    public partial ChainItem? SelectedChain { get; set; }

    public bool HasChain => SelectedChain is not null;

    partial void OnSelectedChainChanged(ChainItem? value)
    {
        _chainName = value?.Name ?? "";
        OnPropertyChanged(nameof(ChainName));
    }

    /// <summary>
    /// The chosen chain's name; typing another renames it (<c>markers.renameChain</c>).
    /// </summary>
    public string ChainName
    {
        get => _chainName;
        set
        {
            if (SelectedChain is not ChainItem chain || value == chain.Name)
            {
                return;
            }
            string trimmed = value.Trim();
            if (trimmed.Length == 0)
            {
                // a chain keeps a name: show it again
                _chainName = value;
                OnPropertyChanged();
                _chainName = chain.Name;
                OnPropertyChanged();
                return;
            }
            _ = _run("markers.renameChain", new JsonObject { ["name"] = chain.Name, ["to"] = trimmed });
        }
    }

    /// <summary>
    /// Chooses a chain by name (null for none), as a click in the panel's list does.
    /// </summary>
    public void ChooseChain(string? name) => SelectedChain = Chains.FirstOrDefault(chain => chain.Name == name);

    /// <summary>
    /// Makes a chain of the selected markers, in the order they were selected.
    /// </summary>
    [RelayCommand]
    private Task AddChain() => _run("markers.addChain", []);

    [RelayCommand(CanExecute = nameof(HasChain))]
    private Task DeleteChain() => SelectedChain is ChainItem chain ? _run("markers.deleteChain", new JsonObject { ["name"] = chain.Name }) : Task.CompletedTask;

    /// <summary>
    /// Selects the chosen chain's markers on the map.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasChain))]
    private Task SelectChainMarkers() => SelectedChain is ChainItem chain
        ? _run("select.set", new JsonObject { ["refs"] = new JsonArray([.. chain.Entries.Select(entry => (JsonNode)JsonValue.Create(ObjectRef.Marker(entry.Id).ToString()))]) })
        : Task.CompletedTask;

    [RelayCommand]
    private Task MoveUp(ChainEntry entry) => Reorder(entry, entry.Index - 1);

    [RelayCommand]
    private Task MoveDown(ChainEntry entry) => Reorder(entry, entry.Index + 1);

    [RelayCommand]
    private Task RemoveEntry(ChainEntry entry) => SetChain(entry.Chain, ids => ids.RemoveAt(entry.Index));

    private Task Reorder(ChainEntry entry, int to) => SetChain(entry.Chain, ids =>
        to < 0 || to >= ids.Length ? ids : ids.RemoveAt(entry.Index).Insert(to, entry.Id));

    private Task SetChain(string name, Func<ImmutableArray<int>, ImmutableArray<int>> change)
    {
        Chain? chain = _document?.Markers.Chains.FirstOrDefault(c => c.Name == name);
        if (chain is null)
        {
            return Task.CompletedTask;
        }
        ImmutableArray<int> ids = change(chain.MarkerIds);
        return ids == chain.MarkerIds
            ? Task.CompletedTask
            : _run("markers.setChain", new JsonObject { ["name"] = name, ["markers"] = new JsonArray([.. ids.Select(id => (JsonNode)JsonValue.Create(id))]) });
    }

    #endregion

    #region Adaptive map

    /// <summary>
    /// Whether the map adapts its deposits to the armies that play (<c>markers.adaptive</c>).
    /// </summary>
    [ObservableProperty]
    public partial bool IsAdaptive { get; private set; }

    [RelayCommand]
    private Task ToggleAdaptive() => _run("markers.adaptive", new JsonObject { ["on"] = !IsAdaptive });

    /// <summary>
    /// The playable armies with how many deposits wait for each.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<AdaptiveArmy> AdaptiveArmies { get; private set; } = [];

    /// <summary>
    /// How many deposits exist whatever armies play.
    /// </summary>
    [ObservableProperty]
    public partial string AlwaysThere { get; private set; } = "";

    #endregion

    /// <summary>
    /// What placing a deposit last warned about (steep ground), or null.
    /// </summary>
    [ObservableProperty]
    public partial string? PlacementWarning { get; set; }

    /// <summary>
    /// Shows the markers of a version of the document: the counts per kind, the chains, the armies.
    /// </summary>
    public void Refresh(MapDocument? document)
    {
        if (ReferenceEquals(document?.Markers, _document?.Markers) && ReferenceEquals(document?.Scenario, _document?.Scenario) && document is not null)
        {
            _document = document;
            return;
        }
        using Performance.Span measure = Performance.Measure("model.markers-panel");
        _document = document;
        foreach (MarkerKindItem item in Kinds)
        {
            item.Refresh(Count(item.Kind));
        }
        if (document is null)
        {
            Chains = [];
            SelectedChain = null;
            IsAdaptive = false;
            AdaptiveArmies = [];
            AlwaysThere = "";
            return;
        }

        string? chosen = _chosenAfterRefresh ?? SelectedChain?.Name;
        Dictionary<int, string> names = [];
        foreach (Marker marker in document.Markers.All)
        {
            names.TryAdd(marker.Id, marker.Name);
        }
        Chains = [.. document.Markers.Chains.Select(chain => new ChainItem(chain.Name,
            [.. chain.MarkerIds.Select((id, index) => new ChainEntry(chain.Name, index, id, names.GetValueOrDefault(id, $"marker {id}")))]))];
        SelectedChain = Chains.FirstOrDefault(chain => chain.Name == chosen);
        if (SelectedChain is not null)
        {
            _chosenAfterRefresh = null;
        }

        IsAdaptive = document.Scenario.AdaptiveMap;
        AdaptiveArmies = [.. document.Scenario.PlayableArmies.Select(army => new AdaptiveArmy(army, document.Markers.Resources.Count(resource => resource.SpawnWithArmy == army)))];
        int always = document.Markers.Resources.Count(resource => resource.SpawnWithArmy is null);
        AlwaysThere = $"{always} {(always == 1 ? "deposit is" : "deposits are")} there whoever plays";
    }

    /// <summary>
    /// After a chain was made or renamed: the panel shows it.
    /// </summary>
    internal void Chose(string name)
    {
        ChainItem? chain = Chains.FirstOrDefault(c => c.Name == name);
        if (chain is not null)
        {
            SelectedChain = chain;
        }
        else
        {
            // the document's change arrives after the command: choose it then
            _chosenAfterRefresh = name;
        }
    }

    private string? _chosenAfterRefresh;

    private int Count(MarkerKinds kind) => _document?.Markers.All.Count(marker => MarkerEdits.KindOf(marker) == kind) ?? 0;
}
