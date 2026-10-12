using System.Numerics;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.Rendering.Gpu;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Controls;

/// <summary>
/// The map maker's sketch in the 3D view (user story N-3): the strokes of the shown sheets over the
/// terrain, following its heights, as lines of the same width on screen at every distance; outside
/// the Sketch mode at each sheet's opacity; hidden with the Sketch overlay. The projected images are
/// not drawn here: a picture would stretch over the hills.
/// </summary>
public sealed partial class TerrainViewGpu
{
    /// <summary>
    /// What the last frame drew of the sketch: sheets, lines (a stroke is one), pieces the lines were cut
    /// into, lines being drawn, and the pieces made on the processor that frame (for automation).
    /// </summary>
    public (int Sheets, int Lines, int Segments, int Pending, int Built) SketchDrawn =>
        _renderer is null ? default : (_renderer.SketchDrawn.Sheets, _renderer.SketchDrawn.Lines, _renderer.SketchDrawn.Segments, _renderer.SketchDrawn.Pending, _renderer.SketchDrawn.Built);

    /// <summary>
    /// Whether overlaps within a sheet are kept from darkening at its opacity (the frame buffer has a stencil).
    /// </summary>
    public bool SketchHasStencil => _renderer?.SketchHasStencil ?? false;

    private SketchFrame? SketchFrameOf(MapDocument document)
    {
        if (_model is null)
        {
            return null;
        }
        using Performance.Span measure = Performance.Measure("3d.sketch-frame");
        IReadOnlyList<SketchStroke> pending = _model.Sketch.Pending;
        // the line being drawn shows whether or not the overlay is on: drawing turns it on
        IReadOnlyList<SketchLayer> layers = _model.ShowSketch ? SketchLines.Of(document.Sketch, _model.InSketchMode) : [];
        IReadOnlyCollection<string> alive = document.Sketch.Sheets.Select(sheet => sheet.Name).ToArray();
        return new SketchFrame(layers, alive, pending, new Vector2((float)Bounds.Width, (float)Bounds.Height));
    }
}
