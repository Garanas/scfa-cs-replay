using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Chooses the points of the chosen curve (user story T-4) as a click on the inspector's table or
/// with the curve tool does: the views draw them larger, Even the grade works between the first
/// and last of them, Snap to the ground on them, and + and - change their heights. No step of
/// undo: the choice is not in the map.
/// </summary>
/// <param name="Points">The indices of the points; left out or empty, none.</param>
/// <param name="Layer">The curve layer, which is chosen too; left out, the chosen one.</param>
[EditorCommand("terrain.chooseCurvePoints", "Choose points of a curve", Story = "T-4", Needs = Needs.Map)]
public sealed record ChooseCurvePoints(IReadOnlyList<int>? Points = null, string? Layer = null) : IRequest;

public sealed class ChooseCurvePointsHandler(MainViewModel model) : IRequestHandler<ChooseCurvePoints>
{
    public Task Handle(ChooseCurvePoints request, CancellationToken cancellationToken)
    {
        if (request.Layer is not null)
        {
            model.HeightStack.Choose(request.Layer);
        }
        if (model.HeightStack.Nodes.Curve is null)
        {
            throw new InvalidOperationException("The chosen height layer is not a curve");
        }
        int count = model.HeightStack.Nodes.Curve.Points.Length;
        foreach (int index in request.Points ?? [])
        {
            if (index < 0 || index >= count)
            {
                throw new ArgumentException($"The curve has {count} points: there is no point {index}");
            }
        }
        model.HeightStack.Nodes.Choose(request.Points ?? []);
        model.Status = model.HeightStack.Nodes.ChosenText;
        return Task.CompletedTask;
    }
}

/// <summary>
/// The curve tool's and the curve table's own settings (user story T-4), which are the window's
/// rather than the map's, so no step of undo: where a new point's height comes from, the typed
/// height, whether the views draw the width and fade lines, and the grade above which the table
/// marks a stretch.
/// </summary>
/// <param name="PointHeight"><c>Automatic</c>, <c>Last point's</c>, <c>Ground</c> or <c>Typed</c>.</param>
/// <param name="TypedHeight">The height of a new point when <paramref name="PointHeight"/> is Typed.</param>
/// <param name="ShowReach">Draw the lines of the chosen curve's width and fade.</param>
/// <param name="GradeLimit">The grade, in percent, above which the table's stretch shows in the warning colour.</param>
[EditorCommand("terrain.setCurveTool", "Set the curve tool's options", Story = "T-4", Needs = Needs.Map)]
public sealed record SetCurveTool(string? PointHeight = null, double? TypedHeight = null, bool? ShowReach = null, double? GradeLimit = null) : IRequest;

public sealed class SetCurveToolHandler(MainViewModel model) : IRequestHandler<SetCurveTool>
{
    public Task Handle(SetCurveTool request, CancellationToken cancellationToken)
    {
        CurveTool tool = model.Tool<CurveTool>();
        if (request.PointHeight is string choice)
        {
            string match = CurveTool.PointHeightChoices.FirstOrDefault(known => known.StartsWith(choice, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"pointHeight is one of {string.Join(", ", CurveTool.PointHeightChoices)}, not {choice}");
            tool.PointHeightChoice = match;
        }
        if (request.TypedHeight is double typed)
        {
            tool.TypedHeight = typed;
        }
        if (request.ShowReach is bool show)
        {
            tool.ShowReach = show;
        }
        if (request.GradeLimit is double limit)
        {
            model.HeightStack.Nodes.GradeLimit = limit;
        }
        return Task.CompletedTask;
    }
}
