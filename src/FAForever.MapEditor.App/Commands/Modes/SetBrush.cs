using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Sets the brush tools up, as the tool options do; what is left out stays. A layer chooses the
/// tool that paints it: 0 the sculpt tool, 1 to 8 the strata tool with that layer; a water mask,
/// a mask for tools or a terrain type chooses the tool that paints those.
/// </summary>
/// <param name="Layer">0 paints the heights, 1 to 8 the mask of a terrain layer.</param>
/// <param name="Mode">How the sculpt tool changes the heights: raise, lower, smooth, flatten,
/// setHeight, noise, erode or scale.</param>
/// <param name="Radius">Of the active tool's brush, in ogrids.</param>
/// <param name="Strength">Of the active tool's brush.</param>
/// <param name="Target">The height that the sculpt tool flattens towards, sets, or scales around, in game units.</param>
/// <param name="Erase">Whether the painting tools take their mask away.</param>
/// <param name="Shape">Of the active tool's brush: <c>round</c>, or a brush of the workspace by its key
/// (<c>brushes/craters/crater01.png</c>) or its path in the brushes folder (<c>craters/crater01.png</c>).</param>
/// <param name="Rotation">Of the active tool's shape, in degrees clockwise in the top view.</param>
/// <param name="RandomRotation">Whether the active tool turns each dab at random.</param>
/// <param name="Falloff">How the active tool's round brush fades: smooth, linear or sharp.</param>
/// <param name="Factor">What the sculpt tool's scaling multiplies by.</param>
/// <param name="NoiseScale">The size of the sculpt tool's noise, in ogrids.</param>
/// <param name="MinHeight">The lowest height the sculpt tool makes, or from which the strata tool
/// paints, in game units; turns that limit on.</param>
/// <param name="MaxHeight">The highest height, as <paramref name="MinHeight"/>.</param>
/// <param name="MinSlope">The gentlest slope the strata tool paints, in degrees; turns the slope limit on.</param>
/// <param name="MaxSlope">The steepest slope the strata tool paints, in degrees; turns the slope limit on.</param>
/// <param name="Limits">False turns the active tool's limits off (before the limits given turn theirs on), true on.</param>
/// <param name="WaterMask">Chooses the water tool with this mask: foam, flatness or depthBias.</param>
/// <param name="Mask">Chooses the masks tool with the mask for tools of this name.</param>
/// <param name="TerrainType">Chooses the terrain types tool with this type, by code or name.</param>
[EditorCommand("brush.set", "Set the brush", Story = "T-1")]
public sealed record SetBrush(
    int? Layer = null,
    string? Mode = null,
    double? Radius = null,
    double? Strength = null,
    double? Target = null,
    bool? Erase = null,
    string? Shape = null,
    double? Rotation = null,
    bool? RandomRotation = null,
    string? Falloff = null,
    double? Factor = null,
    double? NoiseScale = null,
    double? MinHeight = null,
    double? MaxHeight = null,
    double? MinSlope = null,
    double? MaxSlope = null,
    bool? Limits = null,
    string? WaterMask = null,
    string? Mask = null,
    string? TerrainType = null) : IRequest;

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
        if (request.WaterMask is string waterMask)
        {
            model.Tool<WaterPaintTool>().Choose(waterMask);
            model.SelectTool("water.paint");
        }
        if (request.Mask is string mask)
        {
            model.Masks.Choose(mask);
            model.SelectTool("paint.masks");
        }
        if (request.TerrainType is string type)
        {
            model.Tool<TerrainTypeTool>().Choose(type);
            model.SelectTool("terrain.types");
        }
        if (request.Mode is string mode)
        {
            sculpt.Mode = Enum.TryParse(mode, ignoreCase: true, out HeightBrushMode parsed)
                ? parsed
                : throw new ArgumentException($"There is no brush mode '{mode}': {string.Join(", ", Enum.GetNames<HeightBrushMode>())}");
        }
        sculpt.Target = request.Target ?? sculpt.Target;
        sculpt.Factor = request.Factor ?? sculpt.Factor;
        sculpt.NoiseScale = request.NoiseScale ?? sculpt.NoiseScale;
        BrushFalloff? falloff = request.Falloff is string named
            ? Enum.TryParse(named, ignoreCase: true, out BrushFalloff parsedFalloff)
                ? parsedFalloff
                : throw new ArgumentException($"There is no falloff '{named}': {string.Join(", ", Enum.GetNames<BrushFalloff>())}")
            : null;
        switch (model.ActiveTool?.Tool)
        {
            case SculptTool:
                sculpt.Radius = request.Radius ?? sculpt.Radius;
                sculpt.Strength = request.Strength ?? sculpt.Strength;
                sculpt.Falloff = falloff ?? sculpt.Falloff;
                sculpt.MinHeight = request.MinHeight ?? sculpt.MinHeight;
                sculpt.MaxHeight = request.MaxHeight ?? sculpt.MaxHeight;
                sculpt.Limited = (request.Limits ?? sculpt.Limited) || request.MinHeight is not null || request.MaxHeight is not null;
                break;
            case GridPaintTool grid:
                grid.Radius = request.Radius ?? grid.Radius;
                grid.Strength = request.Strength ?? grid.Strength;
                grid.Erase = request.Erase ?? grid.Erase;
                grid.Falloff = falloff ?? grid.Falloff;
                if (grid is StrataTool { Limiting: var limits })
                {
                    limits.MinHeight = request.MinHeight ?? limits.MinHeight;
                    limits.MaxHeight = request.MaxHeight ?? limits.MaxHeight;
                    limits.MinSlope = request.MinSlope ?? limits.MinSlope;
                    limits.MaxSlope = request.MaxSlope ?? limits.MaxSlope;
                    // false turns both off first; a limit given turns its own on
                    limits.ByHeight = (request.Limits ?? limits.ByHeight) || request.MinHeight is not null || request.MaxHeight is not null;
                    limits.BySlope = (request.Limits ?? limits.BySlope) || request.MinSlope is not null || request.MaxSlope is not null;
                }
                break;
            default:
                if (request.Radius is not null || request.Strength is not null || request.Shape is not null || request.Rotation is not null
                    || request.RandomRotation is not null || falloff is not null)
                {
                    throw new ArgumentException($"The active tool ({model.ActiveTool?.Definition.Title}) has no brush: give a layer to choose one");
                }
                break;
        }
        if (request.Erase is bool erase && model.ActiveTool?.Tool is not GridPaintTool)
        {
            strata.Erase = erase;
        }
        if (model.ActiveTool?.Tool is IShapedBrushTool shaped)
        {
            if (request.Shape is not null)
            {
                shaped.Shape.Choose(request.Shape);
            }
            shaped.Shape.Rotation = request.Rotation ?? shaped.Shape.Rotation;
            shaped.Shape.RandomRotation = request.RandomRotation ?? shaped.Shape.RandomRotation;
        }
        return Task.CompletedTask;
    }
}
