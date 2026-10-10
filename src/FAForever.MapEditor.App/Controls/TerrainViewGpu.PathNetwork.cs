using System.Numerics;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Controls;

/// <summary>
/// The generated path networks in the 3D view (user story R-3), while the overlay
/// <c>generatedPathNodes</c> is on: the links as faint lines in their layer's colour a little above
/// the ground, each node a small faint cross, in the scene (a hill in front hides them). The places
/// are made once per preview and heightmap, put on screen every frame.
/// </summary>
public sealed partial class TerrainViewGpu
{
    private (MarkerSet Preview, Heightmap Heights)? _generatedOf;
    private List<(PathLayer Layer, Vector3 From, Vector3 To)> _generatedLinks = [];
    private List<(PathLayer Layer, Vector3 At)> _generatedNodes = [];

    /// <summary>
    /// How many generated path nodes the 3D view drew the last frame (automation).
    /// </summary>
    public int GeneratedNodesDrawn { get; private set; }

    private static Vector4 LayerColor(PathLayer layer, float alpha)
    {
        Color32 color = new PathNode { Id = 0, Name = "", Layer = layer }.Color;
        return new Vector4(color.Red / 255f, color.Green / 255f, color.Blue / 255f, alpha);
    }

    private void GeneratedPaths(MapDocument document, ToolView view, List<ToolShape> shapes)
    {
        GeneratedNodesDrawn = 0;
        if (_model?.PreviewMarkers is not MarkerSet preview)
        {
            return;
        }
        using Performance.Span measure = Performance.Measure("3d.generated-paths");
        Heightmap heights = document.Terrain.Heightmap;
        if (_generatedOf is not { } made || !ReferenceEquals(made.Preview, preview) || !ReferenceEquals(made.Heights, heights))
        {
            _generatedOf = (preview, heights);
            _generatedLinks = [];
            _generatedNodes = [];
            Dictionary<int, Vector3> places = [];
            foreach (PathNode node in preview.PathNodes)
            {
                Vector3 at = new Vector3(node.Position.X, heights.HeightAt(node.Position.X, node.Position.Z) + 0.5f, node.Position.Z);
                places[node.Id] = at;
                _generatedNodes.Add((node.Layer, at));
            }
            foreach (PathNode node in preview.PathNodes)
            {
                foreach (int other in node.AdjacentTo.Where(other => other > node.Id))
                {
                    _generatedLinks.Add((node.Layer, places[node.Id], places[other]));
                }
            }
        }

        MarkerKinds shown = _model.Markers.ShownKinds;
        float width = (float)Bounds.Width;
        float height = (float)Bounds.Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }
        Matrix4x4 viewProjection = _model.Camera.ViewProjection(width / height);
        (Vector2 Pixel, float Depth)? OnScreen(Vector3 place)
        {
            Vector4 clip = Vector4.Transform(new Vector4(place, 1), viewProjection);
            return clip.W <= 1e-6f ? null : (new Vector2((clip.X / clip.W + 1) / 2 * width, (1 - clip.Y / clip.W) / 2 * height), clip.Z / clip.W);
        }
        bool Outside(Vector2 a, Vector2 b) =>
            (a.X < 0 && b.X < 0) || (a.Y < 0 && b.Y < 0) || (a.X > width && b.X > width) || (a.Y > height && b.Y > height);
        foreach ((PathLayer layer, Vector3 from, Vector3 to) in _generatedLinks)
        {
            if ((shown & MarkerEdits.KindOf(layer)) != 0 && OnScreen(from) is (Vector2 a, float da) && OnScreen(to) is (Vector2 b, float db) && !Outside(a, b))
            {
                shapes.Add(new ToolMapLine(a, b, 1.5f, LayerColor(layer, 0.45f)) { Depth = (da, db) });
            }
        }
        foreach ((PathLayer layer, Vector3 at) in _generatedNodes)
        {
            if ((shown & MarkerEdits.KindOf(layer)) != 0 && OnScreen(at) is (Vector2 p, float depth) && p.X >= 0 && p.Y >= 0 && p.X <= width && p.Y <= height)
            {
                Vector4 color = LayerColor(layer, 0.6f);
                shapes.Add(new ToolMapLine(p - new Vector2(3, 0), p + new Vector2(3, 0), 2, color) { Depth = (depth, depth) });
                shapes.Add(new ToolMapLine(p - new Vector2(0, 3), p + new Vector2(0, 3), 2, color) { Depth = (depth, depth) });
                GeneratedNodesDrawn++;
            }
        }
        if (_model.PathNodes.Chosen is PreviewPathNode chosen
            && view.ToScreen(new Vector3(chosen.Node.X, heights.HeightAt(chosen.Node.X, chosen.Node.Z) + 0.5f, chosen.Node.Z)) is Vector2 marked)
        {
            shapes.Add(new ToolSquare(marked, 5, ToolInk.Fill, ToolInk.Selection));
        }
    }
}
