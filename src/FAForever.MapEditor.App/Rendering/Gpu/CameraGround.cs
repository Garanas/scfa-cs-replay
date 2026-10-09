using System.Diagnostics;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Rendering.Gpu;

/// <summary>
/// The ground under the <see cref="EditorCamera"/>: the shown terrain blurred, which the camera
/// rests on, and the terrain itself, which only its eye keeps clear of.
/// </summary>
/// <param name="heightmap">The heights shown now; null without a map.</param>
public sealed class CameraGround(Func<Heightmap?> heightmap)
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private SmoothGround? _smooth;
    private Heightmap? _smoothOf;
    private TimeSpan _smoothMade;

    /// <summary>
    /// The blurred height over about <paramref name="radius"/> ogrids around a place.
    /// </summary>
    public float Smooth(float x, float z, float radius) => Blurred().Height(x, z, radius);

    /// <summary>
    /// The height of the terrain, between its samples; outside the map that of its edge.
    /// </summary>
    public float Exact(float x, float z)
    {
        Heightmap? heights = heightmap();
        if (heights is null)
        {
            return 0;
        }
        float cx = Math.Clamp(x, 0, heights.Width - 1);
        float cz = Math.Clamp(z, 0, heights.Height - 1);
        int x0 = (int)cx;
        int z0 = (int)cz;
        int x1 = Math.Min(x0 + 1, heights.Width - 1);
        int z1 = Math.Min(z0 + 1, heights.Height - 1);
        float fx = cx - x0;
        float fz = cz - z0;
        float top = heights.GetHeight(x0, z0) * (1 - fx) + heights.GetHeight(x1, z0) * fx;
        float bottom = heights.GetHeight(x0, z1) * (1 - fx) + heights.GetHeight(x1, z1) * fx;
        return top * (1 - fz) + bottom * fz;
    }

    /// <summary>
    /// The blurred terrain, made again when the heights change; while they keep changing (a brush
    /// stroke) at most twice a second, as making it reads every sample.
    /// </summary>
    private SmoothGround Blurred()
    {
        Heightmap? heights = heightmap();
        if (heights is null)
        {
            return _smooth ??= SmoothGround.Flat(0);
        }
        if (!ReferenceEquals(heights, _smoothOf) && (_smooth is null || _smoothOf?.Width != heights.Width
            || _smoothOf.Height != heights.Height || _clock.Elapsed - _smoothMade > TimeSpan.FromSeconds(0.5)))
        {
            using Performance.Span measure = Performance.Measure("camera.smooth-ground");
            _smooth = SmoothGround.From(heights);
            _smoothOf = heights;
            _smoothMade = _clock.Elapsed;
        }
        return _smooth!;
    }
}
