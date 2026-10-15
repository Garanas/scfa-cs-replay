using System.Numerics;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.Project.Checks;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.Controls;

/// <summary>
/// The skirts of the placed structures in the 3D view (user story U-12): in the Units mode a dashed
/// rectangle on the ground around every structure whose skirt reaches beyond its footprint, so the
/// ground the game reserves is seen and a new structure can be set skirt to skirt. The corners are
/// worked out once per version of the armies, the heights and the catalogue; every frame puts the
/// ones in the view on screen.
/// </summary>
public sealed partial class TerrainViewGpu
{
    private (object Armies, Heightmap Heights, AssetCatalog Catalog)? _skirtsOf;
    private List<Vector3[]> _skirts = [];

    /// <summary>
    /// How many skirts the last frame drew (automation).
    /// </summary>
    public int SkirtsDrawn { get; private set; }

    private void UnitSkirts(MapDocument document, ToolView view, List<ToolShape> shapes)
    {
        using Performance.Span measure = Performance.Measure("3d.unit-skirts");
        AssetCatalog catalog = _model!.Assets.Catalog;
        Heightmap heights = document.Terrain.Heightmap;
        if (_skirtsOf is not { } made || !Equals(made.Armies, document.Armies) || !ReferenceEquals(made.Heights, heights) || !ReferenceEquals(made.Catalog, catalog))
        {
            _skirtsOf = (document.Armies, heights, catalog);
            _skirts = [];
            foreach (PlacedUnit placed in document.AllUnits())
            {
                if (catalog.Find(placed.Unit.Blueprint) is not { IsStructure: true } entry || UnitWrecks.IsWreck(placed))
                {
                    continue;
                }
                float x = placed.Unit.Position.X;
                float z = placed.Unit.Position.Z;
                float heading = placed.Unit.Orientation.Y;
                PlacementRect skirt = PlacementChecks.SkirtOf(entry, x, z, heading);
                if (skirt == PlacementChecks.FootprintOf(entry, x, z, heading))
                {
                    continue;
                }
                // a little above the ground, so the terrain's own triangles do not swallow the line
                Vector3 Corner(float cx, float cz) => new Vector3(cx, heights.HeightAt(Math.Clamp(cx, 0, document.Width), Math.Clamp(cz, 0, document.Height)) + 0.1f, cz);
                _skirts.Add([Corner(skirt.MinX, skirt.MinZ), Corner(skirt.MaxX, skirt.MinZ), Corner(skirt.MaxX, skirt.MaxZ), Corner(skirt.MinX, skirt.MaxZ)]);
            }
        }
        float width = (float)Bounds.Width;
        float height = (float)Bounds.Height;
        int drawn = 0;
        foreach (Vector3[] corners in _skirts)
        {
            Vector2[] points = new Vector2[4];
            bool left = true, right = true, above = true, below = true;
            bool shown = true;
            for (int i = 0; i < 4 && shown; i++)
            {
                if (view.ToScreen(corners[i]) is Vector2 at)
                {
                    points[i] = at;
                    left &= at.X < 0;
                    right &= at.X > width;
                    above &= at.Y < 0;
                    below &= at.Y > height;
                }
                else
                {
                    shown = false;
                }
            }
            // behind the camera, or wholly beside the view: nothing to draw
            if (!shown || left || right || above || below)
            {
                continue;
            }
            shapes.Add(new ToolLine(points, true, 1, ToolInk.Skirt) { Dash = ToolDash.Dashed });
            drawn++;
        }
        SkirtsDrawn = drawn;
    }
}
