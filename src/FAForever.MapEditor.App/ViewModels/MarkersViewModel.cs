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
/// The Markers panel's list (user stories R-1 to R-6): the kinds of markers with their counts and
/// show check boxes, the path nodes per layer, the chains and the adaptive map; the overlays Markers
/// and Path nodes are the check boxes of their headings.
/// </summary>
public sealed record MarkerListSection() : PanelSection("Markers");

/// <summary>
/// The entry chosen in the Markers panel's list, in the inspector: a kind's or layer's counts, Select
/// and Clear; a chain's name and markers; the adaptive map's settings.
/// </summary>
public sealed record MarkerDetailsSection() : PanelSection("Marker kind, chain or option");

/// <summary>
/// A kind of marker (or the path nodes of a layer): an entry of the Markers panel's list with its
/// colour, count and show check box, and what the inspector shows of it while it is chosen.
/// </summary>
public sealed partial class MarkerKindItem(MarkerKindChoice choice, MarkersViewModel owner) : ObservableObject
{
    public MarkerKinds Kind => choice.Kind;

    /// <summary>
    /// What the command <c>markers.showKind</c> calls it: <c>mass</c>, <c>landPaths</c>, ...
    /// </summary>
    public string Id => choice.Id;

    /// <summary>
    /// Its entry in the panel's list (<c>view.inspectPart</c>): <c>markers.kind.mass</c>, <c>markers.paths.land</c>.
    /// </summary>
    public string PartId => choice.PartId;

    /// <summary>
    /// Its entry's title: <c>Mass</c>, <c>Land</c>.
    /// </summary>
    public string Title => choice.Entry;

    /// <summary>
    /// What its entry is called for a screen reader and automation, apart from the tools of the same
    /// name: <c>Mass deposits</c>, <c>Land path nodes</c>, <c>Spawns</c>.
    /// </summary>
    public string EntryLabel => char.ToUpperInvariant(MarkersViewModel.Noun(choice.Kind, 1)[0]) + MarkersViewModel.Noun(choice.Kind, 1)[1..] + "s";

    /// <summary>
    /// Whether it is the path nodes of a layer.
    /// </summary>
    public bool IsPathLayer => choice.Layer is not null;

    /// <summary>
    /// The colour the game and the views give its markers, <c>0xAARRGGBB</c>.
    /// </summary>
    public uint Swatch => choice.Colour;

    /// <summary>
    /// E.g. <c>Mass (48)</c>.
    /// </summary>
    [ObservableProperty]
    public partial string Label { get; private set; } = choice.Title;

    /// <summary>
    /// How many the map has.
    /// </summary>
    [ObservableProperty]
    public partial int Count { get; private set; }

    /// <summary>
    /// How many links the path nodes of the layer have, a link both ways once; 0 for other kinds.
    /// </summary>
    [ObservableProperty]
    public partial int Links { get; private set; }

    /// <summary>
    /// How the export treats the Procedural mode's generated network of the layer beside these
    /// nodes (joined or replacing them); empty for other kinds.
    /// </summary>
    [ObservableProperty]
    public partial string ExportText { get; private set; } = "";

    /// <summary>
    /// The short form of <see cref="ExportText"/>: <c>joined at export</c>, <c>replace at export</c>, <c>not written</c>.
    /// </summary>
    [ObservableProperty]
    public partial string ExportFact { get; private set; } = "";

    public bool IsOn => owner.Shows(choice.Kind);

    public string ShownText => IsOn ? "yes" : "no";

    /// <summary>
    /// The name of its show check box: <c>Show mass</c>, <c>Show land path nodes</c>.
    /// </summary>
    public string ShowLabel => $"Show {choice.Title.ToLowerInvariant().Replace("ai ", "AI ", StringComparison.Ordinal)}";

    /// <summary>
    /// What its Clear button says and does.
    /// </summary>
    public string ClearLabel => IsPathLayer ? "Clear the layer" : "Clear them";

    public string ClearHelp => $"Removes every {MarkersViewModel.Noun(choice.Kind, 1)} of the map, one step of undo; links and chains to them go with them"
        + (choice.Kind == MarkerKinds.Spawns ? ", and each spawn's playable army (one with units stays as an extra army)" : "");

    public string SelectHelp => $"Selects every {MarkersViewModel.Noun(choice.Kind, 1)} of the map";

    internal void Refresh(int count, int links, (string Text, string Fact) export)
    {
        Count = count;
        Links = links;
        (ExportText, ExportFact) = export;
        Label = $"{choice.Title} ({count})";
        RefreshShown();
    }

    internal void RefreshShown()
    {
        OnPropertyChanged(nameof(IsOn));
        OnPropertyChanged(nameof(ShownText));
    }

    /// <summary>
    /// Its check box in the panel: shows or hides the kind (<c>markers.showKind</c>).
    /// </summary>
    [RelayCommand]
    private Task ToggleShown() => owner.ToggleKindCommand.ExecuteAsync(this);

    /// <summary>
    /// Selects every marker of the kind (<c>markers.select</c>).
    /// </summary>
    [RelayCommand]
    private Task Select() => owner.Run("markers.select", choice.Arguments());

    /// <summary>
    /// Removes every marker of the kind, one step of undo (<c>markers.clear</c>).
    /// </summary>
    [RelayCommand]
    private Task Clear() => owner.Run("markers.clear", choice.Arguments());

    /// <summary>
    /// Removes the path nodes of every layer, one step of undo (<c>markers.clear kind=path</c>).
    /// </summary>
    [RelayCommand]
    private Task ClearEveryPathNode() => owner.Run("markers.clear", new JsonObject { ["kind"] = MarkersViewModel.PathKind });
}

/// <summary>
/// A kind of marker by its name in commands and its title in the panel.
/// </summary>
/// <param name="Id">Its name in <c>markers.showKind</c>: <c>mass</c>, <c>landPaths</c>.</param>
/// <param name="Title">What it is called alone: <c>Mass</c>, <c>Land path nodes</c>.</param>
/// <param name="Entry">Its entry's title in the panel's list, under its heading: <c>Mass</c>, <c>Land</c>.</param>
/// <param name="Colour">The colour its markers have, <c>0xAARRGGBB</c> (the library's <c>Marker.Color</c>).</param>
/// <param name="Layer">The layer of path nodes; null for the other kinds.</param>
public sealed record MarkerKindChoice(MarkerKinds Kind, string Id, string Title, string Entry, uint Colour, PathLayer? Layer = null)
{
    /// <summary>
    /// Its entry in the Markers panel's list: <c>markers.kind.mass</c>, <c>markers.paths.land</c>.
    /// </summary>
    public string PartId => Layer is PathLayer layer ? $"markers.paths.{LayerName(layer)}" : $"markers.kind.{Id}";

    /// <summary>
    /// The arguments of <c>markers.clear</c> and <c>markers.select</c> for it: <c>{kind: mass}</c>,
    /// <c>{kind: path, layer: land}</c>.
    /// </summary>
    public JsonObject Arguments() => Layer is PathLayer layer
        ? new JsonObject { ["kind"] = MarkersViewModel.PathKind, ["layer"] = LayerName(layer) }
        : new JsonObject { ["kind"] = Id };

    /// <summary>
    /// A layer's name in commands: <c>land</c>, <c>water</c>, <c>amphibious</c>, <c>air</c>.
    /// </summary>
    public static string LayerName(PathLayer layer) => layer.ToString().ToLowerInvariant();
}

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
        Parts = MakeParts();
    }

    /// <summary>
    /// Runs a command of the editor, as the panel's and the inspector's buttons do.
    /// </summary>
    internal Task Run(string id, JsonObject arguments) => _run(id, arguments);

    /// <summary>
    /// The kinds one by one, in the order of the panel.
    /// </summary>
    public static IReadOnlyList<MarkerKindChoice> Choices { get; } =
    [
        // the colours of the library's Marker.Color: a spawn's, a deposit's, an expansion area's (the
        // most common AI marker), a blank marker's, a weather marker's, and each layer's path nodes
        new MarkerKindChoice(MarkerKinds.Spawns, "spawns", "Spawns", "Spawns", 0xff800080),
        new MarkerKindChoice(MarkerKinds.Mass, "mass", "Mass", "Mass", 0xff808080),
        new MarkerKindChoice(MarkerKinds.Hydrocarbon, "hydrocarbon", "Hydrocarbon", "Hydrocarbon", 0xff008000),
        new MarkerKindChoice(MarkerKinds.AiMarkers, "ai", "AI markers", "AI markers", 0xff008080),
        new MarkerKindChoice(MarkerKinds.Blank, "blank", "Blank markers", "Blank markers", 0xff800080),
        new MarkerKindChoice(MarkerKinds.Weather, "weather", "Weather", "Weather", 0xff808000),
        new MarkerKindChoice(MarkerKinds.LandPaths, "landPaths", "Land path nodes", "Land", 0xff00ff00, PathLayer.Land),
        new MarkerKindChoice(MarkerKinds.AmphibiousPaths, "amphibiousPaths", "Amphibious path nodes", "Amphibious", 0xff00ffff, PathLayer.Amphibious),
        new MarkerKindChoice(MarkerKinds.WaterPaths, "waterPaths", "Water path nodes", "Water", 0xff0000ff, PathLayer.Water),
        new MarkerKindChoice(MarkerKinds.AirPaths, "airPaths", "Air path nodes", "Air", 0xffffffff, PathLayer.Air),
    ];

    /// <summary>
    /// The kind <c>markers.clear</c> and <c>markers.select</c> call the path nodes, with a layer or of every layer.
    /// </summary>
    public const string PathKind = "path";

    /// <summary>
    /// The kinds named by <c>markers.clear {kind, layer?}</c> and <c>markers.select</c>: <c>spawns</c>,
    /// <c>mass</c>, <c>hydrocarbon</c>, <c>ai</c>, <c>blank</c>, <c>weather</c>, or <c>path</c> with a layer
    /// (<c>land</c>, <c>water</c>, <c>amphibious</c>, <c>air</c>) or without one for every layer; a name
    /// of <c>markers.showKind</c> (<c>landPaths</c>) works too. In any case.
    /// </summary>
    /// <exception cref="ArgumentException">No kind or layer has the name, or a layer is given with a kind that has none.</exception>
    public static MarkerKinds KindsOf(string kind, string? layer)
    {
        if (string.Equals(kind, PathKind, StringComparison.OrdinalIgnoreCase) || string.Equals(kind, "paths", StringComparison.OrdinalIgnoreCase) || string.Equals(kind, "pathNodes", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(layer))
            {
                return MarkerKinds.PathNodes;
            }
            return Enum.TryParse(layer.Trim(), ignoreCase: true, out PathLayer named) && Enum.IsDefined(named)
                ? MarkerEdits.KindOf(named)
                : throw new ArgumentException($"There is no layer of path nodes '{layer}': land, water, amphibious, air");
        }
        MarkerKinds kinds = KindNamed(string.Equals(kind, "spawn", StringComparison.OrdinalIgnoreCase) ? "spawns" : kind);
        return string.IsNullOrWhiteSpace(layer)
            ? kinds
            : throw new ArgumentException($"Only path nodes have a layer: give kind={PathKind} layer={layer}");
    }

    /// <summary>
    /// What markers of some kinds are called in the status line: <c>1 spawn</c>, <c>412 land path
    /// nodes</c>, <c>3 path nodes</c> (of every layer); with a count of 1 and <paramref name="number"/>
    /// off, the noun alone (<c>mass deposit</c>).
    /// </summary>
    public static string Noun(MarkerKinds kinds, int count, bool number = false)
    {
        string one = kinds switch
        {
            MarkerKinds.Spawns => "spawn",
            MarkerKinds.Mass => "mass deposit",
            MarkerKinds.Hydrocarbon => "hydrocarbon deposit",
            MarkerKinds.AiMarkers => "AI marker",
            MarkerKinds.Blank => "blank marker",
            MarkerKinds.Weather => "weather marker",
            MarkerKinds.LandPaths => "land path node",
            MarkerKinds.WaterPaths => "water path node",
            MarkerKinds.AmphibiousPaths => "amphibious path node",
            MarkerKinds.AirPaths => "air path node",
            MarkerKinds.PathNodes => "path node",
            _ => "marker",
        };
        string noun = count == 1 ? one : $"{one}s";
        return number || count != 1 ? $"{count} {noun}" : noun;
    }

    /// <summary>
    /// A kind by its name in commands (<see cref="MarkerKindChoice.Id"/>), in any case.
    /// </summary>
    /// <exception cref="ArgumentException">No kind has the name.</exception>
    public static MarkerKinds KindNamed(string name) =>
        Choices.FirstOrDefault(choice => string.Equals(choice.Id, name, StringComparison.OrdinalIgnoreCase))?.Kind
        ?? throw new ArgumentException($"There is no kind of marker '{name}': {string.Join(", ", Choices.Select(choice => choice.Id))}");

    public IReadOnlyList<MarkerKindItem> Kinds { get; }

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
            item.RefreshShown();
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
        if (ReferenceEquals(document?.Markers, _document?.Markers) && ReferenceEquals(document?.Scenario, _document?.Scenario)
            && ReferenceEquals(document?.Procedural.PathNodes, _document?.Procedural.PathNodes) && document is not null)
        {
            _document = document;
            return;
        }
        using Performance.Span measure = Performance.Measure("model.markers-panel");
        _document = document;
        RefreshKinds(document);
        if (document is null)
        {
            Chains = [];
            SelectedChain = null;
            IsAdaptive = false;
            AdaptiveArmies = [];
            AlwaysThere = "";
            SyncChainParts();
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
        IsAdaptive = document.Scenario.AdaptiveMap;
        SyncChainParts();
        SelectedChain = Chains.FirstOrDefault(chain => chain.Name == chosen);
        if (SelectedChain is not null)
        {
            _chosenAfterRefresh = null;
        }

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
}
