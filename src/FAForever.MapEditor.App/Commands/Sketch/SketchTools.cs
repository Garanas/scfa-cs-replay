using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Sets up the pencil (user story N-1): its colour (the keys 1 to 6), width (<c>[</c> and <c>]</c>),
/// Mirror and Smooth, and chooses the pencil in the Sketch mode. What the options row's choices do.
/// </summary>
/// <param name="Colour">Red, yellow, green, blue, white or black, or 1 to 6.</param>
/// <param name="Width">Thin, medium or thick.</param>
/// <param name="Mirror">Whether a stroke is repeated by the map's symmetry.</param>
/// <param name="Smooth">Whether a line is smoothed and thinned to few places.</param>
[EditorCommand("sketch.setPencil", "Set up the pencil", Story = "N-1", Needs = Needs.Map)]
public sealed record SetSketchPencil(JsonNode? Colour = null, JsonNode? Width = null, bool? Mirror = null, bool? Smooth = null) : IRequest;

public sealed class SetSketchPencilHandler(MainViewModel model) : IRequestHandler<SetSketchPencil>
{
    public Task Handle(SetSketchPencil r, CancellationToken cancellationToken)
    {
        PencilTool pencil = model.Tool<PencilTool>();
        if (r.Colour is { } colour)
        {
            pencil.Colour = SketchArguments.Colour(colour);
        }
        if (r.Width is { } width)
        {
            pencil.Width = SketchArguments.Width(width);
        }
        if (r.Mirror is bool mirror)
        {
            pencil.Mirror = mirror;
        }
        if (r.Smooth is bool smooth)
        {
            pencil.Smooth = smooth;
        }
        if (model.ActiveTool?.Definition.Id != "sketch.pencil" && (r.Colour is not null || r.Width is not null || r.Mirror is not null || r.Smooth is not null))
        {
            model.SelectTool("sketch.pencil");
        }
        model.Status = $"Pencil: {pencil.Colour.ToString().ToLowerInvariant()}, {pencil.Width.ToString().ToLowerInvariant()}{(pencil.MirrorChecked ? ", mirrored" : "")}{(pencil.Smooth ? ", smooth" : "")}";
        return Task.CompletedTask;
    }
}

/// <summary>
/// Sets up the eraser (user story N-1): its radius, Mirror and Whole strokes, and chooses the eraser
/// in the Sketch mode.
/// </summary>
/// <param name="Radius">In ogrids, 1 to 64.</param>
/// <param name="Mirror">Whether it also erases where the map's symmetry repeats it.</param>
/// <param name="Whole">Whether it removes whole strokes (what Alt does).</param>
[EditorCommand("sketch.setEraser", "Set up the eraser", Story = "N-1", Needs = Needs.Map)]
public sealed record SetSketchEraser(double? Radius = null, bool? Mirror = null, bool? Whole = null) : IRequest;

public sealed class SetSketchEraserHandler(MainViewModel model) : IRequestHandler<SetSketchEraser>
{
    public Task Handle(SetSketchEraser r, CancellationToken cancellationToken)
    {
        EraserTool eraser = model.Tool<EraserTool>();
        if (r.Radius is double radius)
        {
            if (!(radius >= EraserTool.MinimumRadius && radius <= EraserTool.MaximumRadius))
            {
                throw new ArgumentException($"The eraser's radius lies between {EraserTool.MinimumRadius} and {EraserTool.MaximumRadius} ogrids, not {radius}");
            }
            eraser.Radius = radius;
        }
        if (r.Mirror is bool mirror)
        {
            eraser.Mirror = mirror;
        }
        if (r.Whole is bool whole)
        {
            eraser.Whole = whole;
        }
        if (model.ActiveTool?.Definition.Id != "sketch.eraser" && (r.Radius is not null || r.Mirror is not null || r.Whole is not null))
        {
            model.SelectTool("sketch.eraser");
        }
        model.Status = $"Eraser: radius {eraser.Radius:0.#} ogrids{(eraser.MirrorChecked ? ", mirrored" : "")}{(eraser.Whole ? ", whole strokes" : "")}";
        return Task.CompletedTask;
    }
}
