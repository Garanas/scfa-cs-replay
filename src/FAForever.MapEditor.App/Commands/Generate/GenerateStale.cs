using System.Text.Json.Nodes;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Generation;
using MediatR;

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
