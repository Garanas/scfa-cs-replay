using System.Numerics;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.Project.Generation
{
    /// <summary>
    /// The six faces of a cube map and the direction each pixel looks in, as Direct3D and OpenGL lay
    /// them out (and the game's DDS cube maps hold them): +x, -x, +y, -y, +z, -z, each face's first
    /// row at the top. A face's pixel (column, row) looks along the face's axis with
    /// <c>sc = 2 (column + 0.5) / size - 1</c> and <c>tc = 2 (row + 0.5) / size - 1</c> across it:
    /// <list type="table">
    /// <item><term>+x</term><description>(1, -tc, -sc)</description></item>
    /// <item><term>-x</term><description>(-1, -tc, sc)</description></item>
    /// <item><term>+y</term><description>(sc, 1, tc)</description></item>
    /// <item><term>-y</term><description>(sc, -1, -tc)</description></item>
    /// <item><term>+z</term><description>(sc, -tc, 1)</description></item>
    /// <item><term>-z</term><description>(-sc, -tc, -1)</description></item>
    /// </list>
    /// The directions are the map's: x east, y up, z south (towards the map's bottom edge), as the
    /// game samples its cube maps and the editor's 3D view draws them.
    /// </summary>
    public static class CubeFaces
    {
        public const int PositiveX = 0;
        public const int NegativeX = 1;
        public const int PositiveY = 2;
        public const int NegativeY = 3;
        public const int PositiveZ = 4;
        public const int NegativeZ = 5;

        /// <summary>
        /// The faces' names in their order, as the status lines and tests name them.
        /// </summary>
        public static IReadOnlyList<string> Names { get; } = ["+x", "-x", "+y", "-y", "+z", "-z"];

        /// <summary>
        /// The direction (not of length one) a point of a face looks in.
        /// </summary>
        /// <param name="face">0 to 5.</param>
        /// <param name="sc">Across the face from left (-1) to right (1).</param>
        /// <param name="tc">Down the face from its first row (-1) to its last (1).</param>
        public static Vector3 Direction(int face, float sc, float tc) => face switch
        {
            PositiveX => new Vector3(1, -tc, -sc),
            NegativeX => new Vector3(-1, -tc, sc),
            PositiveY => new Vector3(sc, 1, tc),
            NegativeY => new Vector3(sc, -1, -tc),
            PositiveZ => new Vector3(sc, -tc, 1),
            NegativeZ => new Vector3(-sc, -tc, -1),
            _ => throw new ArgumentOutOfRangeException(nameof(face)),
        };

        /// <summary>
        /// The direction of length one the middle of a pixel looks in.
        /// </summary>
        public static Vector3 PixelDirection(int face, int column, int row, int size) =>
            Vector3.Normalize(Direction(face, 2 * (column + 0.5f) / size - 1, 2 * (row + 0.5f) / size - 1));

        /// <summary>
        /// The face a direction meets and where on it: <paramref name="sc"/> and <paramref name="tc"/>
        /// from -1 to 1 as <see cref="Direction"/> takes them.
        /// </summary>
        public static int FaceOf(Vector3 direction, out float sc, out float tc)
        {
            float ax = MathF.Abs(direction.X);
            float ay = MathF.Abs(direction.Y);
            float az = MathF.Abs(direction.Z);
            if (ax >= ay && ax >= az)
            {
                float m = Math.Max(ax, 1e-20f);
                tc = -direction.Y / m;
                sc = direction.X > 0 ? -direction.Z / m : direction.Z / m;
                return direction.X > 0 ? PositiveX : NegativeX;
            }
            if (ay >= az)
            {
                float m = Math.Max(ay, 1e-20f);
                sc = direction.X / m;
                tc = direction.Y > 0 ? direction.Z / m : -direction.Z / m;
                return direction.Y > 0 ? PositiveY : NegativeY;
            }
            float n = Math.Max(az, 1e-20f);
            tc = -direction.Y / n;
            sc = direction.Z > 0 ? direction.X / n : -direction.X / n;
            return direction.Z > 0 ? PositiveZ : NegativeZ;
        }

        /// <summary>
        /// The colour of a strip of six faces (<see cref="CubeMaps"/>) in a direction, between the
        /// four pixels nearest to it on its face, as red, green, blue from 0 to 1.
        /// </summary>
        public static Vector3 Sample(DecodedTexture strip, Vector3 direction)
        {
            int size = strip.Height;
            int face = FaceOf(direction, out float sc, out float tc);
            float x = Math.Clamp((sc + 1) / 2 * size - 0.5f, 0, size - 1);
            float y = Math.Clamp((tc + 1) / 2 * size - 0.5f, 0, size - 1);
            int x0 = (int)x;
            int y0 = (int)y;
            int x1 = Math.Min(x0 + 1, size - 1);
            int y1 = Math.Min(y0 + 1, size - 1);
            float u = x - x0;
            float v = y - y0;
            Vector3 Pixel(int px, int py)
            {
                int at = (py * strip.Width + face * size + px) * 4;
                return new Vector3(strip.Rgba[at], strip.Rgba[at + 1], strip.Rgba[at + 2]) / 255f;
            }
            Vector3 top = Vector3.Lerp(Pixel(x0, y0), Pixel(x1, y0), u);
            Vector3 bottom = Vector3.Lerp(Pixel(x0, y1), Pixel(x1, y1), u);
            return Vector3.Lerp(top, bottom, v);
        }
    }
}
