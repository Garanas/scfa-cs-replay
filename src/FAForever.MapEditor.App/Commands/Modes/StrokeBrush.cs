using System.Text.Json.Nodes;
using FAForever.MapEditor.App.ViewModels;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Makes one brush stroke with the active tool, as the mouse does in the top view: pressed at the first
/// place, dragged through the others and released at the last (user story T-1). It is one step of undo and
/// the tool's own: the sculpt tool changes the heights, the strata tool paints, the masks tool paints its mask.
/// For automation and tours (a step that asks the person for a stroke waits for <c>edits</c>).
/// </summary>
/// <param name="Points">The places as <c>[[x, z], ...]</c>, in ogrids.</param>
[EditorCommand("brush.stroke", "Make a brush stroke", Story = "T-1", Needs = Needs.Map)]
public sealed record StrokeBrush(JsonNode? Points = null) : IRequest;

public sealed class StrokeBrushHandler(MainViewModel model) : IRequestHandler<StrokeBrush>
{
    public Task Handle(StrokeBrush request, CancellationToken cancellationToken)
    {
        JsonArray places = request.Points as JsonArray ?? throw new ArgumentException("points: [[x, z], ...] is missing");
        model.Stroke([.. places.Select(point => (point![0]!.GetValue<float>(), point[1]!.GetValue<float>()))]);
        return Task.CompletedTask;
    }
}
