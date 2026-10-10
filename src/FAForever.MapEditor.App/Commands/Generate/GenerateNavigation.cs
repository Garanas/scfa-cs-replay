using System.Text.Json.Nodes;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Generation;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Generates the navigation mesh (user story T-8): the game's navigational mesh with its regions per
/// layer, the starts and deposits in each, and the routes between starts, on a thread of the pool.
/// The views colour the regions (the overlay turns on).
/// </summary>
/// <returns>The shown layer, its regions and routes.</returns>
[EditorCommand("generate.navigation", "Navigation mesh", Story = "T-8", Needs = Needs.Map)]
public sealed record GenerateNavigation : IRequest<JsonObject>;

public sealed class GenerateNavigationHandler(MainViewModel model, NavigationViewModel navigation) : IRequestHandler<GenerateNavigation, JsonObject>
{
    public async Task<JsonObject> Handle(GenerateNavigation request, CancellationToken cancellationToken)
    {
        await model.GenerateAsync(NavigationMeshGenerator.GeneratorId);
        model.ShowNavigation = true;
        model.Status = $"Generated the navigation mesh. {navigation.Summary}";
        return Describe(navigation);
    }

    /// <summary>
    /// The navigation mesh as automation reads it.
    /// </summary>
    public static JsonObject Describe(NavigationViewModel navigation) => new JsonObject
    {
        ["generated"] = navigation.Mesh is not null,
        ["layer"] = navigation.Layer.ToString(),
        ["summary"] = navigation.Summary,
        ["chosenRegion"] = navigation.ChosenRegion,
        ["regions"] = new JsonArray([.. navigation.Regions.Select(row => (JsonNode)new JsonObject
        {
            ["id"] = row.Region.Id,
            ["cells"] = row.Region.Cells,
            ["area"] = Math.Round(row.Region.Area, 3),
            ["starts"] = new JsonArray([.. row.Region.Starts.Select(start => (JsonNode)JsonValue.Create(start))]),
            ["mass"] = row.Region.Mass,
            ["hydrocarbons"] = row.Region.Hydrocarbons,
        })]),
        ["routes"] = new JsonArray([.. navigation.Routes.Select(row => (JsonNode)new JsonObject
        {
            ["from"] = row.Route.From,
            ["to"] = row.Route.To,
            ["straight"] = Math.Round(row.Route.Straight, 1),
            ["length"] = row.Route.Route is null ? null : Math.Round(row.Route.Route.Length, 1),
            ["text"] = row.Text,
        })]),
    };
}
