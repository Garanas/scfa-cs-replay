using System.Numerics;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tools;

/// <summary>
/// Where a place of the map is in a view, for what the sketch's tools show in it (user stories N-3 and N-4): in the top view
/// the place itself, in the 3D view the place on the ground, so that an outline or a text box lies where the sketch is
/// drawn.
/// </summary>
internal static class SketchPlaces
{
    /// <summary>
    /// The place (x, z) as a point of the view's space: at the terrain's height in the 3D view, at 0 in the top view.
    /// </summary>
    public static Vector3 OnGround(ToolView view, MapDocument? document, float x, float z) =>
        new Vector3(x, view.IsTopDown || document is null ? 0 : document.Terrain.Heightmap.HeightAt(x, z), z);
}
