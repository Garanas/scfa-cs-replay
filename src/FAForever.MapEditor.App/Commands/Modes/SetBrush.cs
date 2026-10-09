using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Sets the brush, as the tool options do; what is left out stays. Choosing a layer chooses the
/// tool that paints it: 0 the sculpt tool, 1 to 8 the strata tool.
/// </summary>
/// <param name="Layer">0 paints the heights, 1 to 8 the mask of a terrain layer.</param>
/// <param name="Mode">How sculpting changes the heights: raise, lower, smooth or flatten.</param>
/// <param name="Radius">In ogrids.</param>
/// <param name="Strength">Of the brush that paints: the heights' or the mask's.</param>
/// <param name="Target">The height that flattening moves towards, in game units.</param>
/// <param name="Erase">Whether painting a mask takes the layer away.</param>
[EditorCommand("brush.set", "Set the brush", Story = "T-1")]
public sealed record SetBrush(
    int? Layer = null,
    string? Mode = null,
    double? Radius = null,
    double? Strength = null,
    double? Target = null,
    bool? Erase = null) : IRequest;

public sealed class SetBrushHandler(MainViewModel model) : IRequestHandler<SetBrush>
{
    public Task Handle(SetBrush request, CancellationToken cancellationToken)
    {
        if (request.Layer is int layer)
        {
            model.SelectPaintTarget(layer);
        }
        if (request.Mode is string mode)
        {
            model.BrushMode = Enum.TryParse(mode, ignoreCase: true, out HeightBrushMode parsed)
                ? parsed
                : throw new ArgumentException($"There is no brush mode '{mode}': {string.Join(", ", Enum.GetNames<HeightBrushMode>())}");
        }
        model.MaskErase = request.Erase ?? model.MaskErase;
        model.BrushRadius = request.Radius ?? model.BrushRadius;
        if (model.IsPaintingLayer)
        {
            model.MaskStrength = request.Strength ?? model.MaskStrength;
        }
        else
        {
            model.BrushStrength = request.Strength ?? model.BrushStrength;
        }
        model.BrushTarget = request.Target ?? model.BrushTarget;
        return Task.CompletedTask;
    }
}
