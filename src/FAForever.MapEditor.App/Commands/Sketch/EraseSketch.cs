using FAForever.FileFormats.Map;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Rubs out a sheet's strokes where an eraser passed along some places (user story N-1), one step of
/// undo: what the eraser does while it is dragged. A stroke is cut in two where the eraser crosses its
/// middle; with Whole (Alt), a stroke, symbol or label it touches goes entirely. With Mirror it also erases where the
/// map's symmetry repeats it.
/// </summary>
/// <param name="Points">The places the eraser went through, <c>[[x, z], ...]</c> in ogrids; one is a click.</param>
/// <param name="Radius">How far it reaches, in ogrids; left out, the eraser's.</param>
/// <param name="Whole">Whole strokes (the eraser's Alt); left out, the eraser's.</param>
/// <param name="Mirror">Left out, the eraser's.</param>
/// <param name="Sheet">The sheet to erase in; left out, the one chosen in the panel.</param>
/// <returns>How many strokes the eraser touched, with Whole also the symbols and labels it removed.</returns>
[UndoStep]
[EditorCommand("sketch.erase", "Erase in the sketch", Story = "N-1")]
public sealed record EraseSketch(float[][] Points, float? Radius = null, bool? Whole = null, bool? Mirror = null, string? Sheet = null) : IRequest<int>;

public sealed class EraseSketchHandler(MainViewModel model, CurrentEdit edit) : IRequestHandler<EraseSketch, int>
{
    public Task<int> Handle(EraseSketch r, CancellationToken cancellationToken)
    {
        EraserTool eraser = model.Tool<EraserTool>();
        MapDocument document = edit.Transaction.Document;
        string sheet = SketchArguments.Sheet(model, document, r.Sheet);
        float radius = r.Radius ?? (float)eraser.Radius;
        bool whole = r.Whole ?? eraser.Whole;
        IReadOnlyList<MapSymmetryTransform> parts = r.Mirror ?? eraser.Mirror ? model.EditTransforms(document) : [];
        int erased = 0;
        edit.Transaction.Apply(d => d.Erase(sheet, SketchArguments.Points(r.Points), radius, whole, parts, out erased));
        model.Status = erased == 0 ? $"The eraser touched nothing of {sheet}" : $"Erased {erased} {(erased == 1 ? "item" : "items")} of {sheet}";
        return Task.FromResult(erased);
    }
}
