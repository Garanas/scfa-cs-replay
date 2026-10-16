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
    /// <summary>
    /// From this many samples on (a map of 20 km and more) the ground of another map is made on a
    /// thread of the pool.
    /// </summary>
    private const long LargeMap = 1025L * 1025;

    private SmoothGround? _smooth;
    private Heightmap? _smoothOf;
    private Task<(Heightmap Of, SmoothGround Ground)>? _making;
    private Heightmap? _makingFor;
    private (Heightmap Of, SmoothGround Ground)? _waiting;

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
    /// The blurred terrain, following the heights as they change: only the squares over the tiles
    /// that changed are averaged again (<see cref="SmoothGround.Update"/>), so it keeps up with every
    /// dab of a brush. Another map makes it anew: a large one on a thread of the pool (an 81 km
    /// map's takes about 100 ms), the camera resting on the height of its middle meanwhile.
    /// </summary>
    private SmoothGround Blurred()
    {
        Heightmap? heights = heightmap();
        if (heights is null)
        {
            return _smooth ??= SmoothGround.Flat(0);
        }
        if (_making is { IsCompleted: true } made)
        {
            _making = null;
            if (made.IsCompletedSuccessfully)
            {
                (_smoothOf, _smooth) = made.Result;
            }
        }
        if (ReferenceEquals(heights, _smoothOf))
        {
            return _smooth!;
        }
        bool anew = _smooth is null || _smoothOf is null || SmoothGround.MustRemake(_smoothOf, heights);
        if (anew && (long)heights.Samples.Width * heights.Samples.Height >= LargeMap)
        {
            if (_making is null || _makingFor is null || SmoothGround.MustRemake(_makingFor, heights))
            {
                Heightmap of = heights;
                _makingFor = of;
                _making = Task.Run(() =>
                {
                    using Performance.Span measure = Performance.Measure("camera.smooth-ground");
                    return (of, SmoothGround.From(of));
                });
            }
            return _waiting is { } waiting && ReferenceEquals(waiting.Of, heights)
                ? waiting.Ground
                : (_waiting = (heights, SmoothGround.Flat(heights.GetHeight(heights.Width / 2, heights.Height / 2)))).Value.Ground;
        }
        using (Performance.Measure("camera.smooth-ground"))
        {
            _smooth = anew ? SmoothGround.From(heights) : _smooth!.Update(_smoothOf!, heights);
            _smoothOf = heights;
        }
        return _smooth;
    }
}
