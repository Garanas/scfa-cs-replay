using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// A line of the panel that lists the generated results: how one stands, and the command that
/// generates it.
/// </summary>
/// <param name="Status">E.g. <c>Up to date</c>, <c>Out of date: heightmap changed</c>.</param>
public sealed record GeneratedRow(string Id, string Title, string Status, string CommandId, GeneratedStatus State, bool IsRunning)
{
    public bool IsStale => State == GeneratedStatus.Stale;

    public string ButtonText => IsRunning ? "Generating..." : State == GeneratedStatus.Missing ? "Generate" : "Generate again";

    public string ButtonLabel => $"Generate the {Title.ToLowerInvariant()}";
}

/// <summary>
/// How a result came to be made.
/// </summary>
public enum GenerationWay
{
    /// <summary>A person's Generate (or a command that generates it).</summary>
    Asked,

    /// <summary>On its own, a second after a change to its inputs.</summary>
    OnItsOwn,

    /// <summary>With everything out of date (<c>generate.stale</c>).</summary>
    Stale,
}

/// <summary>
/// When a result was last made in this session, how long it took and how.
/// </summary>
public sealed record GenerationTiming(DateTimeOffset Made, TimeSpan Took, GenerationWay Way)
{
    /// <summary>
    /// E.g. <c>Made at 14:02:11 in 2.3 s, on its own</c>.
    /// </summary>
    public string Text => $"Made at {Made.ToLocalTime():HH:mm:ss} in {Duration(Took)}{Way switch
    {
        GenerationWay.OnItsOwn => ", on its own",
        GenerationWay.Stale => ", with everything out of date",
        _ => "",
    }}";

    /// <summary>
    /// E.g. <c>340 ms</c>, <c>2.3 s</c>.
    /// </summary>
    public static string Duration(TimeSpan took) => took.TotalSeconds < 1
        ? $"{Math.Round(took.TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture)} ms"
        : $"{took.TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} s";
}

/// <summary>
/// What the editor generates when asked (user story F-7): the generators of the features
/// (walkability, the normal map and preview, props from rules, ...), their last results for the
/// open map, and which of those are out of date. Nothing generates by itself; the status bar says
/// how many results are out of date, and Generate, Everything out of date makes them again.
/// </summary>
public sealed partial class GenerationViewModel : ObservableObject
{
    private readonly Dictionary<string, string> _commands = new Dictionary<string, string>(StringComparer.Ordinal);
    private readonly HashSet<string> _running = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// The generators and their results; a feature registers its generator here.
    /// </summary>
    public GeneratedResults Results { get; } = new GeneratedResults();

    /// <summary>
    /// Registers a generator with the command that generates it (<c>generate.walkability</c>), for
    /// the panel's lines (<see cref="Rows"/>).
    /// </summary>
    public void Register(IGenerator generator, string commandId)
    {
        Results.Register(generator);
        _commands[generator.Id] = commandId;
        Registered?.Invoke(generator);
    }

    /// <summary>
    /// Raised when a generator with a command is registered: the Procedural mode lists it.
    /// </summary>
    public event Action<IGenerator>? Registered;

    /// <summary>
    /// The generators that have a command, in the order they were registered.
    /// </summary>
    public IReadOnlyList<IGenerator> Listed => [.. Results.Generators.Where(generator => _commands.ContainsKey(generator.Id))];

    /// <summary>
    /// The command that generates a result, e.g. <c>generate.walkability</c>; null for none.
    /// </summary>
    public string? CommandOf(string id) => _commands.GetValueOrDefault(id);

    private readonly Dictionary<string, GenerationTiming> _timings = new Dictionary<string, GenerationTiming>(StringComparer.Ordinal);

    /// <summary>
    /// When a result was last made in this session and how long it took; null when it was not.
    /// </summary>
    public GenerationTiming? TimingOf(string id) => _timings.GetValueOrDefault(id);

    /// <summary>
    /// Says that a result was made and kept: by Generate, on its own, or by Everything out of date.
    /// </summary>
    public void NoteMade(string id, TimeSpan took, GenerationWay way)
    {
        _timings[id] = new GenerationTiming(DateTimeOffset.Now, took, way);
        OnPropertyChanged(nameof(Rows));
    }

    /// <summary>
    /// How every result stands against the open map, as last refreshed.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Stale), nameof(HasStale), nameof(Summary), nameof(Rows))]
    public partial IReadOnlyList<GeneratedState> States { get; private set; } = [];

    /// <summary>
    /// The results whose inputs changed since they were generated.
    /// </summary>
    public IReadOnlyList<GeneratedState> Stale => [.. States.Where(state => state.Status == GeneratedStatus.Stale)];

    public bool HasStale => Stale.Count > 0;

    /// <summary>
    /// For the status bar, e.g. <c>Out of date: Walkability</c>; empty when nothing is.
    /// </summary>
    public string Summary => HasStale ? $"Out of date: {string.Join(", ", Stale.Select(state => state.Generator.Title))}" : "";

    /// <summary>
    /// The results that have a command, as the panel lists them.
    /// </summary>
    public IReadOnlyList<GeneratedRow> Rows => [.. States.Where(state => _commands.ContainsKey(state.Generator.Id)).Select(state => new GeneratedRow(
        state.Generator.Id,
        state.Generator.Title,
        StatusOf(state),
        _commands[state.Generator.Id],
        state.Status,
        _running.Contains(state.Generator.Id)))];

    private string StatusOf(GeneratedState state) => _running.Contains(state.Generator.Id)
        ? "Generating..."
        : state.Status switch
        {
            GeneratedStatus.Missing => "Not generated yet",
            GeneratedStatus.Stale => $"Out of date: {string.Join(", ", state.ChangedInputs)} changed",
            _ => "Up to date",
        };

    /// <summary>
    /// Whether a generator is running on a thread of the pool.
    /// </summary>
    public bool IsRunning(string id) => _running.Contains(id);

    /// <summary>
    /// Says that a generator started or finished on a thread of the pool.
    /// </summary>
    /// <returns>False when it was running already, or was not.</returns>
    internal bool SetRunning(string id, bool running)
    {
        bool changed = running ? _running.Add(id) : _running.Remove(id);
        OnPropertyChanged(nameof(Rows));
        return changed;
    }

    /// <summary>
    /// Compares the results with the document as it is now: after every step, not every dab.
    /// </summary>
    public void Refresh(MapDocument? document)
    {
        using Performance.Span measure = Performance.Measure("model.generated");
        States = document is null ? [] : Results.States(document);
    }

    /// <summary>
    /// Forgets the results: another map is open.
    /// </summary>
    public void Forget()
    {
        Results.Forget();
        _timings.Clear();
        States = [];
    }

    /// <summary>
    /// Generates every result that is out of date from <paramref name="document"/>, one at a time
    /// in the order in which one feeds the next (<see cref="GeneratedResults.InOrder"/>): each from
    /// the document as the ones before left it, and a result made out of date by one put in place
    /// before it (props from rules after their masks) is generated too. Each generator runs at most
    /// once, so one run leaves nothing out of date unless generators feed each other in a cycle.
    /// </summary>
    /// <param name="apply">Puts a result that is part of the map (<see cref="IDocumentGenerator"/>)
    /// into it and gives the document it then is; without it such a result is only kept.</param>
    /// <returns>What was out of date, and why, in the order generated.</returns>
    public IReadOnlyList<GeneratedState> GenerateStale(MapDocument document, Func<IDocumentGenerator, object, MapDocument>? apply = null)
    {
        List<GeneratedState> generated = [];
        HashSet<string> done = new HashSet<string>(StringComparer.Ordinal);
        while (Results.Stale(document).FirstOrDefault(state => !done.Contains(state.Generator.Id)) is GeneratedState state)
        {
            using Performance.Span measure = Performance.Measure("model.generate");
            done.Add(state.Generator.Id);
            generated.Add(state);
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            GeneratedResult result = Results.Generate(state.Generator.Id, document);
            NoteMade(state.Generator.Id, System.Diagnostics.Stopwatch.GetElapsedTime(start), GenerationWay.Stale);
            if (state.Generator is IDocumentGenerator generator && apply is not null)
            {
                document = apply(generator, result.Value);
            }
        }
        Refresh(document);
        return generated;
    }
}
