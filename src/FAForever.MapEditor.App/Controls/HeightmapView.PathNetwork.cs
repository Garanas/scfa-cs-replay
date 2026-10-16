using Avalonia;
using Avalonia.Media;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Controls;

/// <summary>
/// The generated path networks in the top view (user story R-3): drawn as the path-node markers are,
/// at half their strength, while the overlay <c>generatedPathNodes</c> is on; the node the Path nodes
/// tool chose gets a ring of the selection.
/// </summary>
public sealed partial class HeightmapView
{
    private readonly MarkerLayer _generatedPaths = new MarkerLayer();

    /// <summary>
    /// How many generated path nodes the top view drew the last time (automation).
    /// </summary>
    public int GeneratedNodesDrawn { get; private set; }

    private void DrawGeneratedPathNodes(DrawingContext context, Overlay overlay)
    {
        GeneratedNodesDrawn = 0;
        if (_model is not { ShowGeneratedPathNodes: true } || _model.PreviewMarkers is not MarkerSet preview)
        {
            return;
        }
        using Performance.Span measure = Performance.Measure("top.generated-paths");
        MarkerKinds shown = _model.GeneratedPathKinds;
        using (context.PushOpacity(0.5))
        {
            _generatedPaths.Render(context, overlay, preview, shown, _zoom, _offset);
        }
        GeneratedNodesDrawn = preview.PathNodes.Count(node => (shown & MarkerEdits.KindOf(node.Layer)) != 0);
        if (_model.PathNodes.Chosen is PreviewPathNode chosen)
        {
            Point at = MapToControl(chosen.Node.X, chosen.Node.Z);
            context.DrawEllipse(null, overlay.Selection, at, 6, 6);
        }
    }
}
