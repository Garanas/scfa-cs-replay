using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Commands;

namespace FAForever.MapEditor.App.Automation;

/// <summary>
/// The Procedural mode's state (<c>state.procedural</c>): the generated results as the panel lists
/// them, what regenerates on its own, the navigation mesh, and the export's steps.
/// </summary>
public sealed partial class AutomationServer
{
    private JsonObject ProceduralState() => new JsonObject
    {
        ["rows"] = new JsonArray([.. model.Procedural.Rows.Select(row => (JsonNode)new JsonObject
        {
            ["id"] = row.Id,
            ["state"] = row.Row.State.ToString(),
            ["status"] = row.Row.Status,
            ["running"] = row.Row.IsRunning,
            ["overlay"] = row.Overlay,
            ["preview"] = row.PreviewOn,
            ["canAuto"] = row.CanAuto,
            ["auto"] = row.AutoOn,
            ["export"] = row.Export.ToString(),
            ["onExport"] = row.OnExport,
        })]),
        ["auto"] = new JsonObject
        {
            ["delay"] = model.AutoGeneration.Delay.TotalMilliseconds,
            ["pending"] = new JsonArray([.. model.AutoGeneration.Allowed.Where(model.AutoGeneration.IsPending).Select(id => (JsonNode)JsonValue.Create(id))]),
            ["kept"] = model.AutoGeneration.Kept,
            ["cancelled"] = model.AutoGeneration.Cancelled,
        },
        ["list"] = model.DescribeProceduralList(),
        ["navigation"] = GenerateNavigationHandler.Describe(model.Navigation),
        ["showNavigation"] = model.ShowNavigation,
        ["export"] = new JsonObject
        {
            ["running"] = model.Export.IsRunning,
            ["lastFolder"] = model.Export.LastFolder,
            ["steps"] = new JsonArray([.. model.Export.Lines.Select(line => (JsonNode)new JsonObject
            {
                ["id"] = line.Id,
                ["title"] = line.Title,
                ["status"] = line.Status.ToString(),
                ["note"] = line.Note,
            })]),
        },
    };
}
