using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Sets the brush tools up, as the tool options do; what is left out stays. A layer chooses the
/// tool that paints it: 0 the sculpt tool, 1 to 8 the strata tool with that layer.
/// </summary>
/// <param name="Layer">0 paints the heights, 1 to 8 the mask of a terrain layer.</param>
/// <param name="Mode">How the sculpt tool changes the heights: raise, lower, smooth or flatten.</param>
/// <param name="Radius">Of the active tool's brush, in ogrids.</param>
/// <param name="Strength">Of the active tool's brush.</param>
/// <param name="Target">The height that the sculpt tool flattens towards, in game units.</param>
/// <param name="Erase">Whether the strata tool takes the layer away.</param>
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
        SculptTool sculpt = model.Tool<SculptTool>();
        StrataTool strata = model.Tool<StrataTool>();
        if (request.Layer is 0)
        {
            model.SelectTool("terrain.sculpt");
        }
        else if (request.Layer is int layer)
        {
            strata.Choose(layer);
            model.SelectTool("paint.strata");
        }
        if (request.Mode is string mode)
        {
            sculpt.Mode = Enum.TryParse(mode, ignoreCase: true, out HeightBrushMode parsed)
                ? parsed
                : throw new ArgumentException($"There is no brush mode '{mode}': {string.Join(", ", Enum.GetNames<HeightBrushMode>())}");
        }
        sculpt.Target = request.Target ?? sculpt.Target;
        strata.Erase = request.Erase ?? strata.Erase;
        switch (model.ActiveTool?.Tool)
        {
            case SculptTool:
                sculpt.Radius = request.Radius ?? sculpt.Radius;
                sculpt.Strength = request.Strength ?? sculpt.Strength;
                break;
            case StrataTool:
                strata.Radius = request.Radius ?? strata.Radius;
                strata.Strength = request.Strength ?? strata.Strength;
                break;
            default:
                if (request.Radius is not null || request.Strength is not null)
                {
                    throw new ArgumentException($"The active tool ({model.ActiveTool?.Definition.Title}) has no brush: give a layer to choose one");
                }
                break;
        }
        return Task.CompletedTask;
    }
}
