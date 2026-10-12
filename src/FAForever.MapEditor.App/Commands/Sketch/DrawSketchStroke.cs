using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json.Nodes;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Draws a stroke into a sheet of the sketch (user story N-1), one step of undo: what the pencil
/// does when the mouse is released. The stroke is repeated by the map's symmetry (Mirror), smoothed
/// and thinned (Smooth), or a straight line from its first place to its last (Straight). A sketch
/// without sheets gets its first for the stroke, and a hidden sheet is shown.
/// </summary>
/// <param name="Points">The places the line goes through, <c>[[x, z], ...]</c> in ogrids; one is a dot. A line
/// that leaves the map is cut at its edge (what lies beyond is left out, the parts that come back are strokes of
/// their own); one wholly beyond the map is refused.</param>
/// <param name="Colour">Red, yellow, green, blue, white or black, or 1 to 6; left out, the pencil's.</param>
/// <param name="Width">Thin, medium or thick; left out, the pencil's.</param>
/// <param name="Mirror">Left out, the pencil's.</param>
/// <param name="Smooth">Left out, the pencil's.</param>
/// <param name="Straight">A line from the first place to the last; the pencil's Shift.</param>
/// <param name="Tolerance">How far Smooth may stray from the line, in ogrids; the pencil passes about a pixel of the view.</param>
/// <param name="Sheet">The sheet to draw into; left out, the one chosen in the panel.</param>
/// <returns>How many strokes were made (more than one with the symmetry).</returns>
[UndoStep]
[EditorCommand("sketch.draw", "Draw a stroke in the sketch", Story = "N-1")]
public sealed record DrawSketchStroke(
    float[][] Points,
    JsonNode? Colour = null,
    JsonNode? Width = null,
    bool? Mirror = null,
    bool? Smooth = null,
    bool? Straight = null,
    float? Tolerance = null,
    string? Sheet = null) : IRequest<int>;

public sealed class DrawSketchStrokeHandler(MainViewModel model, CurrentEdit edit) : IRequestHandler<DrawSketchStroke, int>
{
    public Task<int> Handle(DrawSketchStroke r, CancellationToken cancellationToken)
    {
        PencilTool pencil = model.Tool<PencilTool>();
        Vector2[] raw = SketchArguments.Points(r.Points);
        SketchColour colour = r.Colour is { } c ? SketchArguments.Colour(c) : pencil.Colour;
        SketchWidth width = r.Width is { } w ? SketchArguments.Width(w) : pencil.Width;
        bool smooth = r.Smooth ?? pencil.Smooth;
        float tolerance = r.Tolerance is > 0 ? r.Tolerance.Value : 0.5f;

        ImmutableArray<Vector2> line = r.Straight == true
            ? SketchGeometry.Straight(raw)
            : smooth ? SketchGeometry.Smooth(SketchGeometry.Thin(raw, tolerance / 2), tolerance) : SketchGeometry.Thin(raw, tolerance / 2);

        MapDocument document = edit.Transaction.Document;
        IReadOnlyList<MapSymmetryTransform> parts = r.Mirror ?? pencil.Mirror ? model.EditTransforms(document) : [];
        // a line that leaves the map is cut at its edge: the parts on the map are strokes of their own
        IReadOnlyList<ImmutableArray<Vector2>> onMap = SketchGeometry.ClipToMap(line, document.Width, document.Height);
        if (onMap.Count == 0)
        {
            throw new ArgumentException($"The stroke lies wholly beyond the map ({document.Width} by {document.Height} ogrids)");
        }
        List<SketchStroke> strokes = [.. onMap.SelectMany(piece => SketchEdits.WithImages(new SketchStroke(colour, width, piece), parts))];
        string used = "";
        edit.Transaction.Apply(d =>
        {
            // the sheet named, else the one chosen; one that does not exist yet is made, as the first one is
            string? sheet = r.Sheet ?? (d.Sketch.IsEmpty ? null : SketchArguments.Sheet(model, d, null));
            if (sheet is not null && d.Sketch.Find(sheet) is null && !d.Sketch.IsEmpty)
            {
                d = d.AddSheet(sheet, out sheet);
            }
            return d.AddStrokes(sheet, strokes, out used).ShowSheet(used, true);
        });
        model.Sketch.ChooseOnNextRefresh(used);
        model.ShowSketch = true;
        model.Status = $"Drew {SketchArguments.Strokes(strokes.Count)} in {used}{(strokes.Count > 1 ? (onMap.Count > 1 ? ", cut at the edge of the map" : ", mirrored by the symmetry") : "")}";
        return Task.FromResult(strokes.Count);
    }
}
