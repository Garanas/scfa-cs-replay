using System.Text.Json.Serialization;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Changes what players see of the map (user story M-1), one step of undo; the steps that change
/// the same fields one after the other are one step (typing in a field again, a slider dragged).
/// Without arguments it shows the Map panel, whose fields run it.
/// </summary>
/// <param name="Name">The name players see.</param>
/// <param name="Version">The map's version in the vault; 0 for none.</param>
/// <param name="Type"><c>skirmish</c> or <c>campaign_coop</c>.</param>
/// <param name="Preview">The path of a preview image the scenario names.</param>
/// <param name="NoRushRadius">In game units.</param>
/// <remarks>The reclaim the scenario names is not typed: the export writes the props' totals (its step <c>reclaim</c>).</remarks>
[EditorCommand("map.information", "Information...", Story = "M-1", Needs = Needs.Map | Needs.NoStroke)]
public sealed record EditMapInformation(
    string? Name = null,
    string? Description = null,
    string? Author = null,
    int? Version = null,
    string? Type = null,
    string? Preview = null,
    double? NoRushRadius = null,
    bool? AdaptiveMap = null) : IRequest
{
    /// <summary>
    /// The arguments given, by name, for the step's merge key and description.
    /// </summary>
    [JsonIgnore]
    public IEnumerable<string> Given =>
        new (string Name, object? Value)[]
        {
            ("name", Name), ("description", Description), ("author", Author), ("version", Version), ("type", Type), ("preview", Preview),
            ("no-rush radius", NoRushRadius), ("adaptive", AdaptiveMap),
        }.Where(argument => argument.Value is not null).Select(argument => argument.Name);
}

public sealed class EditMapInformationHandler(MainViewModel model) : IRequestHandler<EditMapInformation>
{
    public Task Handle(EditMapInformation request, CancellationToken cancellationToken)
    {
        string[] given = [.. request.Given];
        if (given.Length == 0)
        {
            model.SelectMode("map");
            model.Status = "The map's information is in the Map panel";
            return Task.CompletedTask;
        }
        ScenarioType? type = request.Type switch
        {
            null => null,
            "skirmish" => ScenarioType.Skirmish,
            "campaign_coop" or "campaign" => ScenarioType.CampaignCoop,
            _ => throw new ArgumentException($"A map's type is skirmish or campaign_coop, not '{request.Type}'"),
        };
        if (request.NoRushRadius is < 0)
        {
            throw new ArgumentException("The no-rush radius is 0 or more");
        }
        if (request.Name is { } name && name.Trim().Length == 0)
        {
            throw new ArgumentException("A map needs a name");
        }
        model.EditMap($"Map {string.Join(", ", given)}", document =>
        {
            ScenarioInfo scenario = document.Scenario;
            return document with
            {
                Version = request.Version is int version ? version > 0 ? version : null : document.Version,
                Scenario = scenario with
                {
                    Name = request.Name?.Trim() ?? scenario.Name,
                    Description = request.Description ?? scenario.Description,
                    Author = request.Author ?? scenario.Author,
                    Type = type ?? scenario.Type,
                    PreviewPath = request.Preview ?? scenario.PreviewPath,
                    NoRushRadius = request.NoRushRadius ?? scenario.NoRushRadius,
                    AdaptiveMap = request.AdaptiveMap ?? scenario.AdaptiveMap,
                },
            };
        }, $"map.information:{string.Join(",", given)}");
        return Task.CompletedTask;
    }
}
