using System.Text.Json.Nodes;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Workspaces;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// The mass and energy of the map's props when reclaimed, and per area (user story P-5): shows
/// the Props panel, where its Reclaim section lists them, and says the total in the status line.
/// </summary>
/// <returns>The totals: mass, energy, props, the props whose blueprint the workspace has not, and per area.</returns>
[EditorCommand("props.reclaim", "Reclaim", Story = "P-5", Needs = Needs.Map)]
public sealed record ShowReclaim : IRequest<JsonObject>;

public sealed class ShowReclaimHandler(MainViewModel model) : IRequestHandler<ShowReclaim, JsonObject>
{
    public Task<JsonObject> Handle(ShowReclaim request, CancellationToken cancellationToken)
    {
        if (model.SelectedMode?.Definition.Id != "props")
        {
            model.SelectMode("props");
        }
        model.Props.RefreshReclaim();
        ReclaimTotal total = model.Props.Reclaim;
        model.Status = $"Reclaim: {PropsViewModel.Describe(total)}";
        return Task.FromResult(new JsonObject
        {
            ["mass"] = total.Mass,
            ["energy"] = total.Energy,
            ["props"] = total.Props,
            ["unknown"] = total.Unknown,
            ["areas"] = new JsonArray([.. model.Props.ReclaimPerArea.Select(area => (JsonNode)new JsonObject
            {
                ["area"] = area.Area,
                ["mass"] = area.Total.Mass,
                ["energy"] = area.Total.Energy,
                ["props"] = area.Total.Props,
            })]),
        });
    }
}
