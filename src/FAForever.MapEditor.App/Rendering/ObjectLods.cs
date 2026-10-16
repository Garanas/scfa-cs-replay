using System.Numerics;
using System.Text.Json;

namespace FAForever.MapEditor.App.Rendering;

/// <summary>
/// How the 3D view chooses a prop's or unit's level of detail, as the engine chooses it (faf-re,
/// Draiget/faf-re commit 2ba55b2: <c>Mesh::ComputeLOD</c> and the depth before it in
/// <c>src/sdk/moho/mesh/Mesh.cpp</c>, the default cutoff in
/// <c>src/sdk/moho/resource/blueprints/RMeshBlueprint.cpp</c>). Tested in <c>ObjectModelsTest</c>.
/// </summary>
public static class ObjectLods
{
    /// <summary>
    /// The cutoff of a level of detail whose blueprint gives none (<c>LODCutoff</c>): the engine's
    /// default, 1000 ogrids (the constructor of <c>RMeshBlueprintLOD</c>).
    /// </summary>
    public const float DefaultCutoff = 1000;

    /// <summary>
    /// A converted blueprint's level's cutoff (<c>cutoff</c>), <see cref="DefaultCutoff"/> when it
    /// has none.
    /// </summary>
    public static float CutoffOf(JsonElement lod) =>
        lod.TryGetProperty("cutoff", out JsonElement c) && c.ValueKind == JsonValueKind.Number ? c.GetSingle() : DefaultCutoff;

    /// <summary>
    /// The level that shows at a depth in the view (<see cref="ViewDepth"/>): the first whose
    /// cutoff reaches it or whose cutoff is 0 or less (shown at every depth), and the last beyond
    /// them all. The game fades the last level out past its cutoff and hides it further on; the
    /// editor keeps it in sight.
    /// </summary>
    /// <param name="cutoffs">The levels' cutoffs, the most detailed first: at least one.</param>
    public static int LevelAt(ReadOnlySpan<float> cutoffs, float depth)
    {
        for (int i = 0; i < cutoffs.Length - 1; i++)
        {
            if (cutoffs[i] <= 0 || depth <= cutoffs[i])
            {
                return i;
            }
        }
        return Math.Max(0, cutoffs.Length - 1);
    }

    /// <summary>
    /// How deep a position lies in the view: its distance from the eye along the camera's line of
    /// sight (<paramref name="forward"/>, of length 1). The engine dots the entity's position with
    /// the camera's plane, so a prop off to the side changes level at the same depth as one ahead.
    /// </summary>
    public static float ViewDepth(Vector3 forward, Vector3 eye, Vector3 position) => Vector3.Dot(forward, position - eye);

    /// <summary>
    /// The camera's line of sight, of length 1, from its view and perspective projection (row
    /// vectors, as <c>System.Numerics</c>): the projection's w is the depth in the view, so its
    /// column is the direction.
    /// </summary>
    public static Vector3 ForwardOf(Matrix4x4 viewProjection)
    {
        Vector3 w = new Vector3(viewProjection.M14, viewProjection.M24, viewProjection.M34);
        return w.LengthSquared() > 0 ? Vector3.Normalize(w) : -Vector3.UnitZ;
    }
}
