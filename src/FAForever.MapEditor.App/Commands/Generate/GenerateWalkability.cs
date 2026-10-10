using System.Text.Json.Nodes;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Generation;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Generates walkability (user story T-7): where land, hover, amphibious and naval units can go,
/// as the game's AI sees it, on a thread of the pool. The views show it (the overlay turns on), and
/// from the second time on what changed since the time before: the counts in the status line, the
/// ogrids in the views, new passages and closed paths in the Problems tab.
/// </summary>
/// <returns>The layer compared, the counts of ogrids opened and blocked, the new passages and closed paths.</returns>
[EditorCommand("generate.walkability", "Walkability", Story = "T-7", Needs = Needs.Map)]
public sealed record GenerateWalkability : IRequest<JsonObject>;

public sealed class GenerateWalkabilityHandler(MainViewModel model, WalkabilityViewModel walkability) : IRequestHandler<GenerateWalkability, JsonObject>
{
    public async Task<JsonObject> Handle(GenerateWalkability request, CancellationToken cancellationToken)
    {
        await model.GenerateAsync(WalkabilityGenerator.GeneratorId);
        model.ShowWalkability = true;
        model.Status = $"Generated walkability. {walkability.ChangesText}";
        return Describe(walkability);
    }

    /// <summary>
    /// The walkability as automation reads it.
    /// </summary>
    public static JsonObject Describe(WalkabilityViewModel walkability)
    {
        WalkabilityChanges? changes = walkability.Changes;
        return new JsonObject
        {
            ["generated"] = walkability.Navigation is not null,
            ["layer"] = walkability.Layer.ToString(),
            ["showChanges"] = walkability.ShowChanges,
            ["passable"] = walkability.Grid is null ? null : walkability.Passable,
            ["cells"] = walkability.Grid?.Cells.Length,
            ["text"] = walkability.ChangesText,
            ["changes"] = changes is null ? null : new JsonObject
            {
                ["opened"] = changes.Opened,
                ["blocked"] = changes.Blocked,
                ["passages"] = Links(changes.Passages),
                ["closures"] = Links(changes.Closures),
            },
        };
    }

    private static JsonArray Links(IReadOnlyList<WalkabilityLink> links) => new JsonArray([.. links.Select(link => (JsonNode)new JsonObject
    {
        ["x"] = link.Place.X,
        ["z"] = link.Place.Y,
        ["regions"] = link.Regions,
    })]);
}
