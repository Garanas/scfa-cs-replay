using System.Numerics;
using Avalonia.Controls;
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
    /// into, lines being drawn, and the pieces made on the processor that frame, sheets laid over the scene as a layer (for automation).
    /// </summary>
    public (int Sheets, int Lines, int Segments, int Pending, int Built, int Layers, int Labels) SketchDrawn =>
        _renderer is null ? default : (_renderer.SketchDrawn.Sheets, _renderer.SketchDrawn.Lines, _renderer.SketchDrawn.Segments, _renderer.SketchDrawn.Pending, _renderer.SketchDrawn.Built, _renderer.SketchDrawn.Layers, _renderer.SketchDrawn.Labels);

    /// <summary>
    /// Whether a sheet at less than full opacity is laid over the scene as one layer, so that its overlaps do not darken.
    /// </summary>
    public bool SketchLayersWork => _renderer?.SketchLayersWork ?? false;

    private SketchLabelPainter? _labelPainter;

    private LabelBitmaps? _labelBitmaps;

    /// <summary>
    /// The labels' pictures still being made, which a screenshot waits for.
    /// </summary>
    internal int LabelsLoading => _labelBitmaps?.Pending ?? 0;

    private SketchFrame? SketchFrameOf(MapDocument document)
    {
        if (_model is null)
        {
            return null;
        }
        using Performance.Span measure = Performance.Measure("3d.sketch-frame");
        // the line being drawn and the symbols being stamped (their lines), with their images by the symmetry
        IReadOnlyList<SketchStroke> pending = _model.Sketch.PendingSymbols.Count == 0
            ? _model.Sketch.Pending
            : [.. _model.Sketch.Pending, .. _model.Sketch.PendingSymbols.SelectMany(symbol => SketchLines.Of(symbol))];
        // the line being drawn shows whether or not the overlay is on: drawing turns it on
        IReadOnlyList<SketchLayer> layers = _model.ShowSketch ? SketchLines.Of(document.Sketch, _model.SketchDrawnFully) : [];
        IReadOnlyCollection<string> alive = document.Sketch.Sheets.Select(sheet => sheet.Name).ToArray();
        _labelBitmaps ??= new LabelBitmaps(_labelPainter ??= new SketchLabelPainter(() => new Avalonia.Media.Typeface(
            this.TryFindResource("HandFontFamily", ActualThemeVariant, out object? font) && font is Avalonia.Media.FontFamily family ? family : Avalonia.Media.FontFamily.Default,
            Avalonia.Media.FontStyle.Normal, Avalonia.Media.FontWeight.Bold)),
            () => RequestNextFrameRendering(), Log);
        float scale = (float)(TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
        return new SketchFrame(layers, alive, pending, new Vector2((float)Bounds.Width, (float)Bounds.Height), _model.ShowSketch ? _labelBitmaps : null, scale);
    }
}
