using System.Text.Json.Nodes;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Generates again every result whose inputs changed since it was generated (user story F-7):
/// nothing generates by itself. A result never generated for the map is left to its own command.
/// </summary>
/// <param name="List">Only list what is out of date, and generate nothing.</param>
/// <returns>What was out of date: per result its id, title and the inputs that changed.</returns>
[EditorCommand("generate.stale", "Everything out of date", Story = "F-7", Needs = Needs.Map | Needs.Idle | Needs.NoStroke)]
public sealed record GenerateStale(bool? List = null) : IRequest<JsonArray>;

public sealed class GenerateStaleHandler(MainViewModel model, GenerationViewModel generation) : IRequestHandler<GenerateStale, JsonArray>
{
    public Task<JsonArray> Handle(GenerateStale request, CancellationToken cancellationToken)
    {
        IReadOnlyList<GeneratedState> stale = request.List == true
            ? generation.Results.Stale(model.Map!.Document)
            : model.GenerateStale();
        model.Status = stale.Count == 0
            ? "Nothing is out of date"
            : $"{(request.List == true ? "Out of date" : "Generated again")}: {string.Join(", ", stale.Select(state => $"{state.Generator.Title} ({string.Join(", ", state.ChangedInputs)} changed)"))}";
        return Task.FromResult(new JsonArray([.. stale.Select(state => (JsonNode)new JsonObject
        {
            ["id"] = state.Generator.Id,
            ["title"] = state.Generator.Title,
            ["story"] = state.Generator.Story,
            ["changed"] = new JsonArray([.. state.ChangedInputs.Select(input => (JsonNode)JsonValue.Create(input))]),
        })]));
    }
}

/// <summary>
/// Generates everything (user story F-7): what is out of date, and what was never made for the map
/// (walkability, the navigation mesh, the path networks, the normal map, the preview, the map-wide
/// decals, the masks and props of rules the map has), in the order one feeds the next. The sky cube
/// map is left out (it writes a file among the map's files and is chosen as the sky: its own
/// command makes it), as are results with nothing to make from (no rules, a decal taken from a file);
/// the status line and the answer say why each was left.
/// </summary>
/// <param name="List">Only list what would be made and what left, and generate nothing.</param>
/// <returns>Per result its id, title, whether it was missing and the inputs that changed; then those
/// left out with why (<c>left</c>).</returns>
[EditorCommand("generate.all", "Everything out of date or missing", Story = "F-7", Needs = Needs.Map | Needs.Idle | Needs.NoStroke)]
public sealed record GenerateAll(bool? List = null) : IRequest<JsonObject>;

public sealed class GenerateAllHandler(MainViewModel model, GenerationViewModel generation) : IRequestHandler<GenerateAll, JsonObject>
{
    public Task<JsonObject> Handle(GenerateAll request, CancellationToken cancellationToken)
    {
        MapDocument document = model.Map!.Document;
        IReadOnlyList<GeneratedState> made;
        IReadOnlyList<(IGenerator Generator, string Reason)> left;
        if (request.List == true)
        {
            IReadOnlyList<GeneratedState> states = generation.Results.States(document);
            made = [.. generation.Results.InOrder().Select(generator => states.First(state => state.Generator == generator))
                .Where(state => state.Status == GeneratedStatus.Stale
                    || (state.Status == GeneratedStatus.Missing && generation.CommandOf(state.Generator.Id) is not null && generation.WhyLeftWhenMissing(state.Generator.Id, document) is null))];
            left = [.. states.Where(state => state.Status == GeneratedStatus.Missing && generation.CommandOf(state.Generator.Id) is not null)
                .Select(state => (state.Generator, Reason: generation.WhyLeftWhenMissing(state.Generator.Id, document)))
                .Where(pair => pair.Reason is not null).Select(pair => (pair.Generator, pair.Reason!))];
        }
        else
        {
            made = model.GenerateStale(missing: true, out left);
            model.Logger("Editor").LogInformation("Generated everything: {Made} made, {Left} left out", made.Count, left.Count);
        }
        string Line(GeneratedState state) => state.Status == GeneratedStatus.Missing
            ? $"{state.Generator.Title} (never made)"
            : $"{state.Generator.Title} ({string.Join(", ", state.ChangedInputs)} changed)";
        string verb = request.List == true ? "To make" : "Made";
        string text = made.Count == 0 ? "Nothing is out of date or missing" : $"{verb}: {string.Join(", ", made.Select(Line))}";
        if (left.Count > 0)
        {
            text += $"; left out: {string.Join(", ", left.Select(pair => $"{pair.Generator.Title} ({pair.Reason})"))}";
        }
        model.Status = text;
        return Task.FromResult(new JsonObject
        {
            ["made"] = new JsonArray([.. made.Select(state => (JsonNode)new JsonObject
            {
                ["id"] = state.Generator.Id,
                ["title"] = state.Generator.Title,
                ["missing"] = state.Status == GeneratedStatus.Missing,
                ["changed"] = new JsonArray([.. state.ChangedInputs.Select(input => (JsonNode)JsonValue.Create(input))]),
            })]),
            ["left"] = new JsonArray([.. left.Select(pair => (JsonNode)new JsonObject { ["id"] = pair.Generator.Id, ["title"] = pair.Generator.Title, ["why"] = pair.Reason })]),
        });
    }
}
