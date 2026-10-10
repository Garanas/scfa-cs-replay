using System.Numerics;

namespace FAForever.MapEditor.App.Rendering;

/// <summary>
/// Props the game shows as an effect where they stand (steam over lava, blowing sand or snow, mist,
/// bubbles under water): their blueprints name the game's marker mesh
/// (<c>/env/common/props/marker01_lod0.scm</c>), which the game does not draw. The 3D view draws a
/// small diamond in the theme's <c>MapEffectPropBrush</c> in their place, and picks them by it.
/// </summary>
public static class EffectProps
{
    /// <summary>
    /// The box around the diamond drawn for an effect: 0.64 ogrids wide, 1 high, standing on its place.
    /// </summary>
    public static ObjectBounds Bounds { get; } = new ObjectBounds(new Vector3(-0.32f, 0, -0.32f), new Vector3(0.32f, 1, 0.32f));

    /// <summary>
    /// Whether a mesh's file is the game's marker, which effects stand on: <c>marker01_lod0.glb</c>.
    /// </summary>
    public static bool IsEffectMesh(string file) => Path.GetFileName(file).StartsWith("marker", StringComparison.OrdinalIgnoreCase);
}
