using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Diagnostics;

namespace FAForever.MapEditor.App.Automation;

/// <summary>
/// How much of the editor's state an answer carries, and the reads that pick parts of it: every
/// command but <c>state</c> answers its own result with a short state (<see cref="ShortState"/>);
/// <c>full: true</c> adds the whole state, <c>quiet: true</c> none, and <c>paths: [...]</c> (on
/// <c>state</c> and <c>perf</c> also <c>path</c>) only the values at those paths. <c>perf</c> saves
/// its spans to a file and compares two such files.
/// </summary>
public sealed partial class AutomationServer
{
    /// <summary>
    /// These arguments, for <c>help</c>.
    /// </summary>
    private static readonly string[] AnswerCommands =
    [
        "any command {quiet?: true (no state), full?: true (the whole state), paths?: [\"selection.count\", ...] (only those values)}: by default a command answers its result and a short state",
        "state {path? | paths?}: the whole state, or only the values at those paths (keys joined by dots)",
        "perf {reset?, gpu?, spans?: part of a name, path? | paths?, save?: file, before?: file, after?: file}: save writes the spans to a file; before (and after, else now) compares two, slowest change first",
    ];

    /// <summary>
    /// What of the state an answer carries.
    /// </summary>
    /// <param name="Paths">Only the values at these paths; null for all or the short state.</param>
    private sealed record StateFilter(bool Quiet, bool Full, IReadOnlyList<string>? Paths);

    /// <summary>
    /// Takes the arguments that say how much state to answer out of the arguments, which then go
    /// to the command without them. <c>path</c> is one of them only for <c>state</c> and <c>perf</c>,
    /// since other commands (<c>screenshot</c>, <c>wait</c>) take a path of their own.
    /// </summary>
    private static StateFilter TakeStateFilter(string command, JsonObject arguments)
    {
        bool quiet = arguments["quiet"]?.GetValueKind() == JsonValueKind.True;
        bool full = arguments["full"]?.GetValueKind() == JsonValueKind.True;
        arguments.Remove("quiet");
        arguments.Remove("full");
        List<string>? paths = null;
        if (arguments["paths"] is JsonNode many)
        {
            paths = many switch
            {
                JsonArray list => [.. list.Select(item => item?.GetValue<string>() ?? "")],
                JsonValue one when one.TryGetValue(out string? text) => [.. text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
                _ => throw new ArgumentException("paths is a list of paths: [\"selection.count\", \"history\"]"),
            };
            arguments.Remove("paths");
        }
        if (command is "state" or "perf" && arguments["path"]?.GetValue<string>() is string path)
        {
            (paths ??= []).Add(path);
            arguments.Remove("path");
        }
        return new StateFilter(quiet, full, paths);
    }

    /// <summary>
    /// Adds the state to an answer as the filter asks: the values at its paths, the whole state for
    /// <c>state</c> or <c>full</c>, nothing when quiet, else the short state.
    /// </summary>
    private void AddState(JsonObject result, string command, StateFilter filter)
    {
        if (filter.Quiet)
        {
            return;
        }
        if (filter.Paths is { } paths)
        {
            JsonObject state = State();
            foreach (string path in paths)
            {
                result[path] = At(state, path)?.DeepClone();
            }
            return;
        }
        JsonObject shown = command == "state" || filter.Full ? State() : ShortState();
        foreach ((string key, JsonNode? value) in shown)
        {
            if (!result.ContainsKey(key))
            {
                result[key] = value?.DeepClone();
            }
        }
    }

    /// <summary>
    /// What every answer carries unless asked otherwise: enough to see what a command did (the
    /// mode and tool, the status line, the steps of undo, the selection's count, a question that
    /// opened) without the whole state, which <c>state</c> or <c>full: true</c> give.
    /// </summary>
    private JsonObject ShortState() => new JsonObject
    {
        ["title"] = model.Title,
        ["stage"] = model.Setup.Stage.ToString(),
        ["busy"] = model.IsBusy,
        ["map"] = model.Map?.Document is { } document ? new JsonObject { ["name"] = document.Scenario.Name } : null,
        ["modified"] = model.Map?.Session.IsModified ?? false,
        ["mode"] = model.SelectedMode?.Definition.Id,
        ["tool"] = model.ActiveTool?.Definition.Id,
        ["status"] = model.Status,
        ["undo"] = model.Map?.Session.UndoDescription,
        ["redo"] = model.Map?.Session.RedoDescription,
        ["selection"] = new JsonObject { ["count"] = model.Selection.Count, ["kinds"] = model.Selection.Summary },
        ["dialog"] = model.Question.Describe(),
        ["popups"] = PopupsState(),
    };

    /// <summary>
    /// <c>perf</c>: where the time goes, per measured span, and the recent stalls; with <c>save</c>
    /// written to a file, with <c>before</c> (and <c>after</c>, else now) compared.
    /// </summary>
    private JsonObject Perf(JsonObject arguments, StateFilter filter)
    {
        if (arguments["reset"]?.GetValue<bool>() == true)
        {
            Performance.Reset();
        }
        if (arguments["gpu"]?.GetValue<bool>() is bool gpu)
        {
            // the card's time per frame of the 3D view: 3d.gpu-frame (or 3d.gpu-frame-finish)
            Rendering.Gpu.GpuTimer.Enabled = gpu;
            window.View3D.RequestNextFrameRendering();
        }
        JsonObject performance = Performance.Snapshot();
        performance["gpuTiming"] = new JsonObject { ["on"] = Rendering.Gpu.GpuTimer.Enabled, ["method"] = Rendering.Gpu.GpuTimer.Method };
        if (arguments["spans"]?.GetValue<string>() is { Length: > 0 } part && performance["spans"] is JsonObject spans)
        {
            // only the spans whose name holds the text
            foreach (string name in spans.Select(pair => pair.Key).Where(name => !name.Contains(part, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                spans.Remove(name);
            }
        }
        if (arguments["save"]?.GetValue<string>() is { Length: > 0 } save)
        {
            string file = Path.GetFullPath(save);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, performance.ToJsonString(Json));
            performance["saved"] = file;
        }
        if (arguments["before"]?.GetValue<string>() is { Length: > 0 } before)
        {
            JsonObject after = arguments["after"]?.GetValue<string>() is { Length: > 0 } afterFile ? ReadPerformance(afterFile) : performance;
            return new JsonObject
            {
                ["before"] = Path.GetFullPath(before),
                ["after"] = arguments["after"]?.GetValue<string>() is { Length: > 0 } a ? Path.GetFullPath(a) : "now",
                ["compare"] = ComparePerformance(ReadPerformance(before), after),
                ["ok"] = true,
            };
        }
        if (filter.Paths is { } paths)
        {
            JsonObject picked = [];
            foreach (string path in paths)
            {
                picked[path] = At(performance, path)?.DeepClone();
            }
            picked["ok"] = true;
            return picked;
        }
        return performance;
    }

    private static JsonObject ReadPerformance(string file)
    {
        try
        {
            return JsonNode.Parse(File.ReadAllText(file)) as JsonObject ?? throw new ArgumentException($"{file} holds no spans of perf save");
        }
        catch (JsonException exception)
        {
            throw new ArgumentException($"{file} is not what perf save writes: {exception.Message}", exception);
        }
        catch (FileNotFoundException)
        {
            throw new ArgumentException($"There is no file {file}: write it with perf save=<file>");
        }
    }

    /// <summary>
    /// The spans of two snapshots of <c>perf</c> side by side, the one whose mean grew most first:
    /// per span its count and mean and 95th percentile before and after, and the change of the mean
    /// and of the total. A span in only one of them counts as zero in the other.
    /// </summary>
    public static JsonArray ComparePerformance(JsonObject before, JsonObject after)
    {
        JsonObject spansBefore = before["spans"] as JsonObject ?? [];
        JsonObject spansAfter = after["spans"] as JsonObject ?? [];
        static double Read(JsonNode? span, string key) => span?[key] is JsonValue value && value.GetValueKind() == JsonValueKind.Number
            ? double.Parse(value.ToJsonString(), CultureInfo.InvariantCulture)
            : 0;
        static JsonObject? Side(JsonNode? span) => span is null ? null : new JsonObject
        {
            ["count"] = Read(span, "count"),
            ["meanMs"] = Read(span, "meanMs"),
            ["p95Ms"] = Read(span, "p95Ms"),
            ["totalMs"] = Read(span, "totalMs"),
        };
        IEnumerable<string> names = spansBefore.Select(pair => pair.Key).Union(spansAfter.Select(pair => pair.Key), StringComparer.Ordinal);
        return [.. names
            .Select(name => (Name: name, Before: spansBefore[name], After: spansAfter[name]))
            .Select(span => (span.Name, span.Before, span.After, Mean: Read(span.After, "meanMs") - Read(span.Before, "meanMs"), Total: Read(span.After, "totalMs") - Read(span.Before, "totalMs")))
            .OrderByDescending(span => span.Mean)
            .ThenByDescending(span => span.Total)
            .Select(span => (JsonNode)new JsonObject
            {
                ["span"] = span.Name,
                ["meanChangeMs"] = Math.Round(span.Mean, 2),
                ["totalChangeMs"] = Math.Round(span.Total, 1),
                ["before"] = Side(span.Before),
                ["after"] = Side(span.After),
            })];
    }
}
