using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Sets up the Props mode's settings for automation, as the Placing section and the tools'
/// options do (user stories P-1, P-2, P-3): what is left out stays.
/// </summary>
/// <param name="Alignment"><c>ByCategory</c>, <c>Upright</c> or <c>Terrain</c>.</param>
/// <param name="RandomTilt">The most a placed prop leans at random, in degrees.</param>
/// <param name="Sink">How far placed props sink into the terrain, in game units.</param>
/// <param name="Radius">The paint tool's radius in ogrids.</param>
/// <param name="Density">The paint tool's props per 100 square ogrids.</param>
/// <param name="Spacing">The paint tool's least distance between props, in ogrids.</param>
/// <param name="OnlyOn">The layer the paint tool paints only on, by name (<c>Anywhere</c> for none;
/// a stratum by its name or as <c>Layer 3</c>).</param>
/// <param name="NotOn">The layer the paint tool never paints on, likewise.</param>
/// <param name="Threshold">How much a layer covers to count, 0 to 1.</param>
/// <param name="EraseRadius">The eraser's radius in ogrids.</param>
/// <param name="OnlyPalette">Whether the eraser takes only the palette's props.</param>
/// <returns>The settings as they are now.</returns>
[EditorCommand("props.set", "Set up the props tools", Story = "P-2")]
public sealed record SetProps(
    string? Alignment = null,
    bool? RandomTurn = null,
    double? RandomTilt = null,
    double? ScaleMin = null,
    double? ScaleMax = null,
    double? Sink = null,
    double? Radius = null,
    double? Density = null,
    double? Spacing = null,
    string? OnlyOn = null,
    string? NotOn = null,
    double? Threshold = null,
    double? EraseRadius = null,
    bool? OnlyPalette = null) : IRequest<JsonObject>;

public sealed class SetPropsHandler(MainViewModel model) : IRequestHandler<SetProps, JsonObject>
{
    public Task<JsonObject> Handle(SetProps request, CancellationToken cancellationToken)
    {
        PropsViewModel props = model.Props;
        PropPaintTool paint = model.Tool<PropPaintTool>();
        PropEraseTool erase = model.Tool<PropEraseTool>();
        // check everything before changing anything
        PropAlignment? alignment = request.Alignment is string named
            ? Enum.TryParse(named, ignoreCase: true, out PropAlignment parsed) && Enum.IsDefined(parsed)
                ? parsed
                : throw new ArgumentException($"There is no alignment '{named}': ByCategory, Upright or Terrain")
            : null;
        PaintLayer onlyOn = request.OnlyOn is null ? paint.OnlyOn : paint.LayerNamed(request.OnlyOn);
        PaintLayer notOn = request.NotOn is null ? paint.NotOn : paint.LayerNamed(request.NotOn);
        if (request.ScaleMin <= 0 || request.ScaleMax <= 0 || request.Radius <= 0 || request.EraseRadius <= 0
            || request.Density < 0 || request.Spacing < 0 || request.Threshold is < 0 or > 1 || request.RandomTilt is < 0 or >= 90)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Scales and radii are above 0, density and spacing 0 or more, the threshold 0 to 1, the tilt 0 to 90 degrees");
        }
        props.Alignment = alignment ?? props.Alignment;
        props.RandomTurn = request.RandomTurn ?? props.RandomTurn;
        props.RandomTilt = request.RandomTilt ?? props.RandomTilt;
        props.ScaleMin = request.ScaleMin ?? props.ScaleMin;
        props.ScaleMax = request.ScaleMax ?? props.ScaleMax;
        props.Sink = request.Sink ?? props.Sink;
        paint.Radius = request.Radius ?? paint.Radius;
        paint.Density = request.Density ?? paint.Density;
        paint.Spacing = request.Spacing ?? paint.Spacing;
        paint.OnlyOn = onlyOn;
        paint.NotOn = notOn;
        paint.Threshold = request.Threshold ?? paint.Threshold;
        erase.Radius = request.EraseRadius ?? erase.Radius;
        erase.OnlyPalette = request.OnlyPalette ?? erase.OnlyPalette;
        return Task.FromResult(props.Describe());
    }
}
