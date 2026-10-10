using System.Numerics;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// Curves that shape the terrain along them (user story T-4): the spline through a curve's
    /// points, and the values of its layer made from it (<see cref="Rasterize"/>). A curve is a
    /// height layer (<see cref="HeightLayerKind.Curve"/>), so editing the curve makes its values
    /// again and the heightmap follows.
    /// </summary>
    public static class HeightCurves
    {
        /// <summary>
        /// About how far apart the spline is sampled, in ogrids.
        /// </summary>
        private const float Step = 0.5f;

        /// <summary>
        /// How a new curve layer of a profile combines: a road replaces the terrain where it lies (by
        /// its coverage), the others add their rise or sink.
        /// </summary>
        public static HeightCombine CombineOf(CurveProfile profile) => profile == CurveProfile.Road ? HeightCombine.Replace : HeightCombine.Add;

        /// <summary>
        /// The spline through the points (Catmull-Rom), as a line of places with their heights,
        /// about half an ogrid apart; the points themselves are on it.
        /// </summary>
        public static IReadOnlyList<CurvePoint> Spline(HeightCurve curve)
        {
            var points = curve.Points;
            if (points.Length < 2)
            {
                return [.. points];
            }
            int count = points.Length;
            int segments = curve.Closed ? count : count - 1;
            List<CurvePoint> line = [points[0]];
            for (int s = 0; s < segments; s++)
            {
                CurvePoint p1 = points[s];
                CurvePoint p2 = points[(s + 1) % count];
                CurvePoint p0 = s > 0 ? points[s - 1] : curve.Closed ? points[count - 1] : Mirror(p1, p2);
                CurvePoint p3 = s + 2 < count ? points[s + 2] : curve.Closed ? points[(s + 2) % count] : Mirror(p2, p1);
                float length = MathF.Sqrt((p2.X - p1.X) * (p2.X - p1.X) + (p2.Z - p1.Z) * (p2.Z - p1.Z));
                int steps = Math.Max(1, (int)MathF.Ceiling(length / Step));
                for (int i = 1; i <= steps; i++)
                {
                    float t = i / (float)steps;
                    line.Add(new CurvePoint(
                        CatmullRom(p0.X, p1.X, p2.X, p3.X, t),
                        CatmullRom(p0.Z, p1.Z, p2.Z, p3.Z, t),
                        p1.Height + (p2.Height - p1.Height) * t));
                }
            }
            return line;
        }

        /// <summary>
        /// The values (and, for a road, the coverage) of a curve's layer on a map of that size; with
        /// <paramref name="previousValues"/> and <paramref name="previousCoverage"/> the tiles that
        /// come out the same are those buffers' own, so only the tiles the change touched are added
        /// up again.
        /// </summary>
        public static (TiledBuffer<float> Values, TiledBuffer<byte>? Coverage) Rasterize(
            HeightCurve curve, int mapWidth, int mapHeight, TiledBuffer<float>? previousValues = null, TiledBuffer<byte>? previousCoverage = null)
        {
            int width = mapWidth + 1;
            int height = mapHeight + 1;
            bool road = curve.Profile == CurveProfile.Road;
            TiledBuffer<float>.Builder values = TiledBuffer<float>.Create(width, height).ToBuilder();
            TiledBuffer<byte>.Builder? coverage = road ? TiledBuffer<byte>.Create(width, height).ToBuilder() : null;
            IReadOnlyList<CurvePoint> line = Spline(curve);
            float half = Math.Max(0, curve.Width) / 2;
            float falloff = Math.Max(0, curve.Falloff);
            bool cliff = curve.Profile == CurveProfile.Cliff;
            float reach = (cliff ? Math.Max(0, curve.Width) : half) + falloff;
            if (line.Count >= 2 && reach > 0)
            {
                float minX = line.Min(p => p.X) - reach, maxX = line.Max(p => p.X) + reach;
                float minZ = line.Min(p => p.Z) - reach, maxZ = line.Max(p => p.Z) + reach;
                int x0 = Math.Clamp((int)MathF.Floor(minX), 0, width - 1), x1 = Math.Clamp((int)MathF.Ceiling(maxX), 0, width - 1);
                int z0 = Math.Clamp((int)MathF.Floor(minZ), 0, height - 1), z1 = Math.Clamp((int)MathF.Ceiling(maxZ), 0, height - 1);
                int columns = x1 - x0 + 1;
                int rows = z1 - z0 + 1;
                float[] distance = new float[columns * rows];
                float[] heights = new float[columns * rows];
                float[] side = new float[columns * rows];
                Array.Fill(distance, float.PositiveInfinity);
                for (int s = 0; s + 1 < line.Count; s++)
                {
                    Nearest(line[s], line[s + 1], reach, x0, z0, x1, z1, columns, distance, heights, side);
                }
                for (int row = 0; row < rows; row++)
                {
                    for (int column = 0; column < columns; column++)
                    {
                        int i = row * columns + column;
                        float d = distance[i];
                        if (d >= reach)
                        {
                            continue;
                        }
                        float across = Across(d, half, falloff);
                        float h = heights[i];
                        int x = x0 + column;
                        int z = z0 + row;
                        switch (curve.Profile)
                        {
                            case CurveProfile.Ridge:
                                values[x, z] = h * across;
                                break;
                            case CurveProfile.Valley:
                                values[x, z] = -h * across;
                                break;
                            case CurveProfile.Road:
                                values[x, z] = h;
                                coverage![x, z] = (byte)MathF.Round(across * 255);
                                break;
                            case CurveProfile.Cliff:
                                values[x, z] = h * Cliff(side[i] * d, Math.Max(0, curve.Width), falloff);
                                break;
                        }
                    }
                }
            }
            return (Reuse(values.ToBuffer(), previousValues), coverage is null ? null : Reuse(coverage.ToBuffer(), previousCoverage));
        }

        /// <summary>
        /// How much of a profile there is at a distance from the curve: all of it within half the
        /// width, then smoothly less over the falloff.
        /// </summary>
        private static float Across(float distance, float half, float falloff)
        {
            if (distance <= half)
            {
                return 1;
            }
            if (falloff <= 0)
            {
                return 0;
            }
            float t = Math.Clamp(1 - (distance - half) / falloff, 0, 1);
            return t * t * (3 - 2 * t);
        }

        /// <summary>
        /// A cliff at a signed distance (positive on the left): nothing on the right, a drop of an
        /// ogrid at the curve, all of it over the width on the left, fading back over the falloff.
        /// </summary>
        private static float Cliff(float signed, float width, float falloff)
        {
            if (signed <= -0.5f)
            {
                return 0;
            }
            if (signed < 0.5f)
            {
                float t = signed + 0.5f;
                return t * t * (3 - 2 * t);
            }
            return Across(signed, width, falloff);
        }

        private static void Nearest(CurvePoint a, CurvePoint b, float reach, int minX, int minZ, int maxX, int maxZ, int columns, float[] distance, float[] heights, float[] side)
        {
            Vector2 from = new Vector2(a.X, a.Z);
            Vector2 along = new Vector2(b.X, b.Z) - from;
            float lengthSquared = along.LengthSquared();
            int x0 = Math.Max(minX, (int)MathF.Floor(Math.Min(a.X, b.X) - reach));
            int x1 = Math.Min(maxX, (int)MathF.Ceiling(Math.Max(a.X, b.X) + reach));
            int z0 = Math.Max(minZ, (int)MathF.Floor(Math.Min(a.Z, b.Z) - reach));
            int z1 = Math.Min(maxZ, (int)MathF.Ceiling(Math.Max(a.Z, b.Z) + reach));
            for (int z = z0; z <= z1; z++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    Vector2 to = new Vector2(x, z) - from;
                    float t = lengthSquared > 0 ? Math.Clamp(Vector2.Dot(to, along) / lengthSquared, 0, 1) : 0;
                    float d = (to - along * t).Length();
                    int i = (z - minZ) * columns + (x - minX);
                    if (d < distance[i])
                    {
                        distance[i] = d;
                        heights[i] = a.Height + (b.Height - a.Height) * t;
                        // the left of the direction of travel in the top view (x right, z down)
                        float cross = along.X * to.Y - along.Y * to.X;
                        side[i] = cross < 0 ? 1 : -1;
                    }
                }
            }
        }

        /// <summary>
        /// The buffer with the tiles that hold the same values as the previous one's taken from it.
        /// </summary>
        private static TiledBuffer<T> Reuse<T>(TiledBuffer<T> made, TiledBuffer<T>? previous) where T : unmanaged, IEquatable<T>
        {
            if (previous is null || previous.Width != made.Width || previous.Height != made.Height)
            {
                return made;
            }
            TiledBuffer<T>.Builder builder = previous.ToBuilder();
            for (int tileY = 0; tileY < made.TilesY; tileY++)
            {
                for (int tileX = 0; tileX < made.TilesX; tileX++)
                {
                    ReadOnlySpan<T> tile = made.Tile(tileX, tileY);
                    if (!made.SharesTile(previous, tileX, tileY) && !HeightLayers.SameTile(made, tileX, tileY, tile, previous.Tile(tileX, tileY)))
                    {
                        builder.SetTile(tileX, tileY, tile);
                    }
                }
            }
            return builder.ToBuffer();
        }

        private static CurvePoint Mirror(CurvePoint about, CurvePoint point) =>
            new CurvePoint(2 * about.X - point.X, 2 * about.Z - point.Z, point.Height);

        private static float CatmullRom(float p0, float p1, float p2, float p3, float t) =>
            0.5f * (2 * p1 + (p2 - p0) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (3 * p1 - p0 - 3 * p2 + p3) * t * t * t);
    }
}
