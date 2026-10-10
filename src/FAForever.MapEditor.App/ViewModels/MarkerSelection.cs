using System.Globalization;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The selected markers (user stories R-1, R-2, R-5): their names and kinds, and the fields of
/// their kinds, editable for one or many at once through <c>markers.set</c>: the name (of one
/// marker; a spawn keeps its army's), the resource of deposits, the type of AI markers, the layer
/// of path nodes, the no-rush offset of spawns, and the army the deposits of an adaptive map wait
/// for. A field the selected markers differ in shows <see cref="SelectionInspector.Mixed"/>, a
/// choice nothing. Deposits on steep ground say so.
/// </summary>
public sealed partial class MarkerSelection : SelectionSection
{
    /// <summary>
    /// The choice of <see cref="SpawnWithArmy"/> for a deposit that is always there.
    /// </summary>
    public const string Always = "Always";

    private bool _refreshing;
    private string _name = "";
    private string? _resource;
    private string? _aiType;
    private string? _layer;
    private string _noRushX = "";
    private string _noRushZ = "";
    private string? _spawnWithArmy;

    public override ObjectKind Kind => ObjectKind.Marker;

    /// <summary>
    /// The fields of the selected weather markers.
    /// </summary>
    public WeatherSelection Weather { get; }

    public MarkerSelection()
    {
        Weather = new WeatherSelection((command, arguments) => Run(command, arguments));
    }

    public IReadOnlyList<string> Resources { get; } = Enum.GetNames<ResourceKind>();

    public IReadOnlyList<string> Layers { get; } = Enum.GetNames<PathLayer>();

    /// <summary>
    /// The AI types to choose from: those the map editors know and those the selected markers have.
    /// </summary>
    public IReadOnlyList<string> AiTypes { get; private set; } = AiMarker.KnownTypes;

    /// <summary>
    /// <see cref="Always"/> and the playable armies.
    /// </summary>
    public IReadOnlyList<string> Armies { get; private set; } = [Always];

    public bool CanRename { get; private set; }

    public bool HasResources { get; private set; }

    public bool HasAiMarkers { get; private set; }

    public bool HasPathNodes { get; private set; }

    public bool HasSpawns { get; private set; }

    /// <summary>
    /// The first deposits of the selection on steep ground, or empty.
    /// </summary>
    public string Warning { get; private set; } = "";

    public bool HasWarning => Warning.Length > 0;

    /// <summary>
    /// The one selected marker's name; typing another renames it.
    /// </summary>
    public string Name
    {
        get => _name;
        set
        {
            if (!_refreshing && CanRename && value.Trim() != _name && value.Trim().Length > 0)
            {
                _ = Run("markers.set", new JsonObject { ["name"] = value.Trim() });
            }
            else
            {
                Reset(ref _name, value, nameof(Name));
            }
        }
    }

    /// <summary>
    /// <c>Mass</c> or <c>Hydrocarbon</c>; null when the deposits differ.
    /// </summary>
    public string? Resource
    {
        get => _resource;
        set => Choose(ref _resource, value, "resource");
    }

    public string? AiType
    {
        get => _aiType;
        set => Choose(ref _aiType, value, "type");
    }

    public string? Layer
    {
        get => _layer;
        set => Choose(ref _layer, value, "layer");
    }

    /// <summary>
    /// Where the spawns' no-rush circle lies from them, across, in game units; empty for around them.
    /// </summary>
    public string NoRushX
    {
        get => _noRushX;
        set => TypeNoRush(ref _noRushX, value, nameof(NoRushX), "noRushX");
    }

    public string NoRushZ
    {
        get => _noRushZ;
        set => TypeNoRush(ref _noRushZ, value, nameof(NoRushZ), "noRushZ");
    }

    /// <summary>
    /// The army the deposits wait for on an adaptive map, <see cref="Always"/> for none; null when they differ.
    /// </summary>
    public string? SpawnWithArmy
    {
        get => _spawnWithArmy;
        set
        {
            if (_refreshing || value is null || value == _spawnWithArmy)
            {
                return;
            }
            _ = Run("markers.set", new JsonObject { ["spawnWithArmy"] = value == Always ? "" : value });
        }
    }

    /// <summary>
    /// Each selected deposit waits for the army of the spawn nearest to it.
    /// </summary>
    [RelayCommand]
    private Task WaitForNearestArmy() => Run("markers.set", new JsonObject { ["spawnWithArmy"] = "nearest" });

    public override void Refresh(MapDocument document, IReadOnlyList<ObjectRef> things)
    {
        base.Refresh(document, things);
        Marker[] markers = [.. things.Select(document.Find).OfType<Marker>()];
        ResourceMarker[] deposits = [.. markers.OfType<ResourceMarker>()];
        AiMarker[] ais = [.. markers.OfType<AiMarker>()];
        PathNode[] nodes = [.. markers.OfType<PathNode>()];
        Spawn[] spawns = [.. markers.OfType<Spawn>()];
        Weather.Refresh(document, markers);
        _refreshing = true;
        try
        {
            Set(CanRename, markers.Length == 1 && markers[0] is not Spawn, value => CanRename = value, nameof(CanRename));
            Set(HasResources, deposits.Length > 0, value => HasResources = value, nameof(HasResources));
            Set(HasAiMarkers, ais.Length > 0, value => HasAiMarkers = value, nameof(HasAiMarkers));
            Set(HasPathNodes, nodes.Length > 0, value => HasPathNodes = value, nameof(HasPathNodes));
            Set(HasSpawns, spawns.Length > 0, value => HasSpawns = value, nameof(HasSpawns));

            IReadOnlyList<string> types = [.. AiMarker.KnownTypes.Concat(ais.Select(ai => ai.Type)).Distinct().Order(StringComparer.Ordinal)];
            if (!types.SequenceEqual(AiTypes))
            {
                AiTypes = types;
                OnPropertyChanged(nameof(AiTypes));
            }
            IReadOnlyList<string> armies = [Always, .. document.Scenario.PlayableArmies];
            if (!armies.SequenceEqual(Armies))
            {
                Armies = armies;
                OnPropertyChanged(nameof(Armies));
            }

            Show(ref _name, markers.Length == 1 ? markers[0].Name : "", nameof(Name));
            Show(ref _resource, Shared(deposits.Select(deposit => deposit.Kind.ToString())), nameof(Resource));
            Show(ref _aiType, Shared(ais.Select(ai => ai.Type)), nameof(AiType));
            Show(ref _layer, Shared(nodes.Select(node => node.Layer.ToString())), nameof(Layer));
            Show(ref _noRushX, SharedText(spawns.Select(spawn => spawn.NoRushOffset is { } offset ? Number(offset.X) : "")), nameof(NoRushX));
            Show(ref _noRushZ, SharedText(spawns.Select(spawn => spawn.NoRushOffset is { } offset ? Number(offset.Y) : "")), nameof(NoRushZ));
            Show(ref _spawnWithArmy, Shared(deposits.Select(deposit => deposit.SpawnWithArmy ?? Always)), nameof(SpawnWithArmy));

            string[] steep = [.. deposits
                .Select(deposit => MarkerEdits.Steepness(document.Terrain.Heightmap, deposit.Kind, deposit.Position.X, deposit.Position.Z) is float steepness ? PlaceMarkerHandler.SteepWarning(deposit, steepness) : null)
                .OfType<string>()];
            string warning = steep.Length switch
            {
                0 => "",
                1 => steep[0],
                _ => $"{steep[0]} (and {steep.Length - 1} more on steep ground)",
            };
            if (warning != Warning)
            {
                Warning = warning;
                OnPropertyChanged(nameof(Warning));
                OnPropertyChanged(nameof(HasWarning));
            }
        }
        finally
        {
            _refreshing = false;
        }
    }

    protected override IEnumerable<string> LinesOf(MapDocument document, IReadOnlyList<ObjectRef> things) =>
        things.Select(document.Find).OfType<Marker>().Select(marker => $"{marker.Name} ({KindOf(marker)})");

    /// <summary>
    /// What a marker is, in a word or two: <c>spawn</c>, <c>mass</c>, <c>Rally Point</c>, <c>land path node</c>.
    /// </summary>
    public static string KindOf(Marker marker) => marker switch
    {
        Spawn => "spawn",
        ResourceMarker resource => resource.Kind.ToString().ToLowerInvariant(),
        PathNode node => $"{node.Layer.ToString().ToLowerInvariant()} path node",
        AiMarker ai => ai.Type,
        WeatherDefinition => "weather",
        WeatherGenerator => "weather generator",
        _ => "marker",
    };

    private void Choose(ref string? field, string? value, string argument)
    {
        if (_refreshing || value is null || value == field)
        {
            return;
        }
        _ = Run("markers.set", new JsonObject { [argument] = value });
    }

    /// <summary>
    /// A number typed for the no-rush offset goes to every selected spawn; nothing typed puts their
    /// circles around them again; anything else changes nothing.
    /// </summary>
    private void TypeNoRush(ref string field, string value, string property, string argument)
    {
        if (_refreshing || value == field)
        {
            return;
        }
        string text = value.Trim();
        if (text.Length == 0)
        {
            _ = Run("markers.set", new JsonObject { ["clearNoRush"] = true });
        }
        else if (EditorCulture.TryParse(text, out float number))
        {
            _ = Run("markers.set", new JsonObject { [argument] = number });
        }
        else
        {
            Reset(ref field, value, property);
        }
    }

    /// <summary>
    /// The binding wrote a value the field does not take: show the field's own again.
    /// </summary>
    private void Reset(ref string field, string value, string property)
    {
        string shown = field;
        field = value;
        OnPropertyChanged(property);
        field = shown;
        OnPropertyChanged(property);
    }

    private void Show<T>(ref T field, T value, string property)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            OnPropertyChanged(property);
        }
    }

    private void Set(bool current, bool value, Action<bool> set, string property)
    {
        if (current != value)
        {
            set(value);
            OnPropertyChanged(property);
        }
    }

    /// <summary>
    /// The value all have, or null when they differ or there are none.
    /// </summary>
    private static string? Shared(IEnumerable<string> values)
    {
        string[] distinct = [.. values.Distinct()];
        return distinct.Length == 1 ? distinct[0] : null;
    }

    private static string SharedText(IEnumerable<string> values)
    {
        string[] distinct = [.. values.Distinct()];
        return distinct.Length switch
        {
            0 => "",
            1 => distinct[0],
            _ => SelectionInspector.Mixed,
        };
    }

    private static string Number(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
