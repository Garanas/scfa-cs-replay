using System.Text.Json.Nodes;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Checks;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Runs every check of the map now (user story F-14) and shows the Problems tab. The checks also
/// run by themselves a moment after each step, and before a save or export.
/// </summary>
/// <returns>What they found: the counts, and per problem its severity, check, message, thing and place.</returns>
[EditorCommand("plugins.check", "Check the map", Story = "F-14", Needs = Needs.Map)]
public sealed record CheckMap : IRequest<JsonObject>;

public sealed class CheckMapHandler(MainViewModel model, ProblemsViewModel problems) : IRequestHandler<CheckMap, JsonObject>
{
    public Task<JsonObject> Handle(CheckMap request, CancellationToken cancellationToken)
    {
        problems.Run();
        model.Dock = DockTab.Problems;
        model.Status = problems.Summary;
        return Task.FromResult(Describe(problems));
    }

    /// <summary>
    /// The problems as automation reads them: the counts and the first 200.
    /// </summary>
    public static JsonObject Describe(ProblemsViewModel problems) => new JsonObject
    {
        ["errors"] = problems.Errors,
        ["warnings"] = problems.Warnings,
        ["infos"] = problems.Infos,
        ["summary"] = problems.Summary,
        ["items"] = new JsonArray([.. problems.Items.Take(200).Select(item => (JsonNode)Describe(item.Problem))]),
    };

    private static JsonObject Describe(Problem problem) => new JsonObject
    {
        ["severity"] = problem.Severity.ToString(),
        ["check"] = problem.Check,
        ["message"] = problem.Message,
        ["thing"] = problem.Thing?.ToString(),
        ["related"] = problem.Related is { Count: > 0 } related ? new JsonArray([.. related.Select(thing => (JsonNode)thing.ToString()!)]) : null,
        ["place"] = problem.Place is { } place ? new JsonArray(place.X, place.Y) : null,
    };
}
