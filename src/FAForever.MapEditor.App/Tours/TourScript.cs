using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace FAForever.MapEditor.App.Tours;

/// <summary>
/// A guided tour (user story H-1), read from a <c>*.tour.json</c> file: the steps that dim the
/// window, ring a control or an area, and explain it on a card.
/// </summary>
/// <param name="Id">The tour's name in commands and in the progress file: <c>window</c>.</param>
/// <param name="Summary">One line for the list of tours.</param>
/// <param name="Order">Where it comes in the list of tours (the editor's own go 10, 20, ...; a map maker's tour without one comes last).</param>
/// <param name="Needs">What the tour needs before it starts: <c>Map</c> (an open map).</param>
/// <param name="Start">Commands that run without a person when the tour starts, to prepare it.</param>
public sealed record Tour(
    string Id,
    string Title,
    string Summary,
    int Order,
    IReadOnlyList<string> Needs,
    IReadOnlyList<TourAction> Start,
    IReadOnlyList<TourStep> Steps);

/// <summary>
/// One step of a tour: what to point at, the card's words and, for a step that asks the person to
/// act, what it waits for.
/// </summary>
/// <param name="Id">Unique in the tour; the guide names its pictures after it.</param>
/// <param name="Target">What to ring; left out, the card stands in the middle of the window.</param>
/// <param name="Before">Commands that run without a person when the step is entered, going on or back.</param>
/// <param name="Until">A step that asks the person to act: the state it waits for.</param>
/// <param name="DoItForMe">What the step's button runs instead of the person.</param>
/// <param name="Done">What the card says once <paramref name="Until"/> holds.</param>
/// <param name="Guide">What only the generated guide reads.</param>
public sealed record TourStep(
    string Id,
    string Title,
    string Text,
    TourTarget? Target,
    IReadOnlyList<TourAction> Before,
    TourCondition? Until,
    TourAction? DoItForMe,
    string? Done,
    TourGuide? Guide)
{
    /// <summary>
    /// Whether the step asks the person to act: the ring's hole lets the mouse through.
    /// </summary>
    public bool Acts => Until is not null;
}

/// <summary>
/// What a step rings. <see cref="Label"/> (with <see cref="Type"/>, <see cref="Under"/> and
/// <see cref="Index"/>) finds a control as automation's <c>click</c> does; <see cref="Part"/> names a
/// region of the window as <c>screenshot part</c> does; <see cref="Ref"/> or <see cref="X"/> and
/// <see cref="Z"/> mark a place on the map, as <c>view-point</c> shows it.
/// </summary>
public sealed record TourTarget(
    string? Label = null,
    string? Type = null,
    string? Under = null,
    int? Index = null,
    string? Part = null,
    string? Ref = null,
    double? X = null,
    double? Z = null)
{
    /// <summary>
    /// A short text for the step's own description and for errors: <c>label "Modes"</c>.
    /// </summary>
    public override string ToString() =>
        Label is not null ? $"label \"{Label}\"" + (Type is not null ? $" {Type}" : "") + (Under is not null ? $" under \"{Under}\"" : "")
        : Part is not null ? $"part \"{Part}\""
        : Ref is not null ? $"ref {Ref}"
        : X is not null ? $"place {X}, {Z}"
        : "nothing";
}

/// <summary>
/// A command that runs without a person: its id, and its arguments (the properties besides
/// <c>run</c>, or those of <c>args</c>).
/// </summary>
public sealed record TourAction
{
    [JsonPropertyName("run")]
    public string Run { get; init; } = "";

    /// <summary>
    /// For a command that has an argument called <c>run</c> or <c>args</c> itself.
    /// </summary>
    public JsonObject? Args { get; init; }

    /// <summary>
    /// The other properties, which are arguments.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }

    /// <summary>
    /// The command's arguments by name.
    /// </summary>
    public JsonObject Arguments()
    {
        JsonObject arguments = [];
        foreach ((string key, JsonElement value) in Extra ?? [])
        {
            arguments[key] = JsonNode.Parse(value.GetRawText());
        }
        foreach ((string key, JsonNode? value) in Args ?? [])
        {
            arguments[key] = value?.DeepClone();
        }
        return arguments;
    }

    public override string ToString() => Run;
}

/// <summary>
/// A state the step waits for: the value at a path of the editor's state (keys joined by dots, as
/// automation's <c>wait</c> reads it): <c>tab</c> = <c>3d</c>.
/// </summary>
/// <param name="Path">The state's path.</param>
/// <param name="Value">What it must read; for <c>edits</c> how many edits the person must have made since the step began.</param>
/// <param name="Prefix">The value is the beginning of the text read (a layer named again gets a number: <c>Add the height layer Sculpt2</c>).</param>
public sealed record TourCondition(string Path, JsonElement Value, bool Prefix = false);

/// <summary>
/// What only the guide reads (user story H-2): a longer text, a crop larger or smaller than the
/// target, a step left out, and a picture after the action.
/// </summary>
public sealed record TourGuide(string? More = null, int? Margin = null, bool? Skip = null, bool? After = null);

/// <summary>
/// Reads tour files. A file that cannot be read gives its error, never an exception.
/// </summary>
public static class TourScript
{
    /// <summary>
    /// The suffix of a tour's file.
    /// </summary>
    public const string Suffix = ".tour.json";

    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Reads a tour from JSON text.
    /// </summary>
    /// <returns>The tour, or null with what is wrong with it.</returns>
    public static Tour? Parse(string json, out string? error)
    {
        TourFile? file;
        try
        {
            file = JsonSerializer.Deserialize<TourFile>(json, Options);
        }
        catch (JsonException exception)
        {
            error = $"not a tour: {exception.Message}";
            return null;
        }
        if (file is null)
        {
            error = "the file is empty";
            return null;
        }
        List<string> problems = [];
        if (string.IsNullOrWhiteSpace(file.Id) || !file.Id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        {
            problems.Add("the id is missing, or holds more than letters, digits, dashes and underscores");
        }
        if (string.IsNullOrWhiteSpace(file.Title))
        {
            problems.Add("the title is missing");
        }
        List<TourStep> steps = [];
        HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
        foreach ((TourStepFile step, int index) in (file.Steps ?? []).Select((step, index) => (step, index)))
        {
            string name = $"step {index + 1}" + (string.IsNullOrWhiteSpace(step.Id) ? "" : $" ({step.Id})");
            if (string.IsNullOrWhiteSpace(step.Id))
            {
                problems.Add($"{name} has no id");
            }
            else if (!ids.Add(step.Id))
            {
                problems.Add($"{name}: the id is used twice");
            }
            if (string.IsNullOrWhiteSpace(step.Title) || string.IsNullOrWhiteSpace(step.Text))
            {
                problems.Add($"{name} needs a title and a text");
            }
            if (step.Target is { } target && !(target.Label is not null || target.Part is not null || target.Ref is not null || target.X is not null && target.Z is not null))
            {
                problems.Add($"{name}: the target names a label, a part, a ref, or x and z");
            }
            if (step.Until is { Path: not { Length: > 0 } })
            {
                problems.Add($"{name}: until needs a path");
            }
            if (step.Until is not null && step.DoItForMe is null)
            {
                problems.Add($"{name}: a step that waits needs doItForMe, for a person who cannot do it");
            }
            if (step.Until is { Path.Length: > 0 } until && !TourState.Known(until.Path))
            {
                problems.Add($"{name}: the editor's state has no path '{until.Path}' for a tour to wait on ({string.Join(", ", TourState.Paths)})");
            }
            steps.Add(new TourStep(step.Id ?? "", step.Title ?? "", step.Text ?? "", step.Target, step.Before ?? [], step.Until, step.DoItForMe, step.Done, step.Guide));
        }
        if (steps.Count == 0)
        {
            problems.Add("the tour has no steps");
        }
        foreach (TourAction action in (file.Start ?? []).Concat(steps.SelectMany(step => step.Before)).Concat(steps.Select(step => step.DoItForMe).OfType<TourAction>()))
        {
            if (string.IsNullOrWhiteSpace(action.Run))
            {
                problems.Add("an action has no run (the command's id)");
            }
            else if (TourRefusals.Why(action.Run) is string why)
            {
                problems.Add($"{action.Run}: {why}");
            }
        }
        if (problems.Count > 0)
        {
            error = string.Join("; ", problems);
            return null;
        }
        error = null;
        return new Tour(file.Id!, file.Title!, file.Summary ?? "", file.Order ?? 1000, file.Needs ?? [], file.Start ?? [], steps);
    }

    /// <summary>
    /// Reads a tour's file.
    /// </summary>
    public static Tour? Read(string path, out string? error)
    {
        try
        {
            return Parse(File.ReadAllText(path), out error);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error = $"cannot read the file: {exception.Message}";
            return null;
        }
    }

    private sealed class TourFile
    {
        public string? Id { get; set; }

        public string? Title { get; set; }

        public string? Summary { get; set; }

        public int? Order { get; set; }

        public List<string>? Needs { get; set; }

        public List<TourAction>? Start { get; set; }

        public List<TourStepFile>? Steps { get; set; }
    }

    private sealed class TourStepFile
    {
        public string? Id { get; set; }

        public string? Title { get; set; }

        public string? Text { get; set; }

        public TourTarget? Target { get; set; }

        public List<TourAction>? Before { get; set; }

        public TourCondition? Until { get; set; }

        public TourAction? DoItForMe { get; set; }

        public string? Done { get; set; }

        public TourGuide? Guide { get; set; }
    }
}
