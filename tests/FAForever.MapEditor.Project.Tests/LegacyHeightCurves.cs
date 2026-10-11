using System.Numerics;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// The curves' values as they were made before wave 17 (the library's code of then): the
    /// reference that curves of one height keep their exact output, and the time a curve took.
    /// </summary>
    internal static class LegacyHeightCurves
    {
        /// <summary>
        /// About how far apart the spline is sampled, in ogrids: a corner's distance.
        /// </summary>
        private const float Step = 1f;

        /// <summary>
        /// How a new curve layer of a profile combines: a road replaces the terrain where it lies (by
        /// its coverage), the others add their rise or sink.
        /// </summary>
        public static LayerCombine CombineOf(CurveProfile profile) => profile == CurveProfile.Road ? LayerCombine.Replace : LayerCombine.Add;

        /// <summary>
        /// The spline through the points (Catmull-Rom), as a line of places with their heights,
        /// about an ogrid apart; the points themselves are on it.
        /// </summary>
        public static IReadOnlyList<CurvePoint> LegacySpline(HeightCurve curve)
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
            HeightCurve curve, int mapWidth, int mapHeight, TiledBuffer<float>? previousValues = null, TiledBuffer<byte>? previousCoverage = null,
            HeightCurve? previousCurve = null)
        {
            int width = mapWidth + 1;
            int height = mapHeight + 1;
            bool road = curve.Profile == CurveProfile.Road;
            TiledBuffer<float>.Builder values = TiledBuffer<float>.Create(width, height).ToBuilder();
            TiledBuffer<byte>.Builder? coverage = road ? TiledBuffer<byte>.Create(width, height).ToBuilder() : null;
            foreach (HeightCurve part in Parts(curve, mapWidth, mapHeight))
            {
                Draw(part, width, height, values, coverage);
            }
            // the tiles that can differ lie where either curve reaches; without the previous curve, anywhere
            (int X0, int Z0, int X1, int Z1)? tiles = previousCurve is null ? null : Union(TilesOf(curve, width, height), TilesOf(previousCurve, width, height));
            return (Reuse(values.ToBuffer(), previousValues, tiles), coverage is null ? null : Reuse(coverage.ToBuffer(), previousCoverage, tiles));
        }

        /// <summary>
        /// The curve and, when it is drawn by a symmetry (<see cref="HeightCurve.Symmetry"/>), its images
        /// in the other parts of a map of that size: the curve first. An image in a mirrored part runs
        /// the other way, so that a cliff's high side is the image of the curve's.
        /// </summary>
        public static IReadOnlyList<HeightCurve> Parts(HeightCurve curve, int mapWidth, int mapHeight)
        {
            if (curve.Symmetry is not SymmetrySetting symmetry)
            {
                return [curve];
            }
            List<HeightCurve> parts = [curve with { Symmetry = null }];
            foreach (MapSymmetryTransform transform in MapSymmetry.Transforms(symmetry.Kind, symmetry.Count, mapWidth, mapHeight).Skip(1))
            {
                IEnumerable<CurvePoint> points = curve.Points.Select(point =>
                {
                    Vector2 image = transform.Apply(new Vector2(point.X, point.Z));
                    return new CurvePoint(image.X, image.Y, point.Height);
                });
                parts.Add(curve with { Symmetry = null, Points = [.. transform.IsMirror ? points.Reverse() : points] });
            }
            return parts;
        }

        /// <summary>
        /// Draws one part of a curve into the layer's values: where parts overlap, the one that
        /// shapes the terrain most (a road: the one that covers most).
        /// </summary>
        private static void Draw(HeightCurve curve, int width, int height, TiledBuffer<float>.Builder values, TiledBuffer<byte>.Builder? coverage)
        {
            IReadOnlyList<CurvePoint> line = LegacySpline(curve);
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
                // rows in bands, each band with the parts of the line that reach it; the rows apart on the pool
                const int band = 16;
                List<int>[] bands = [.. Enumerable.Range(0, (rows + band - 1) / band).Select(_ => new List<int>())];
                for (int s = 0; s + 1 < line.Count; s++)
                {
                    int from = Math.Max(0, (int)MathF.Floor(Math.Min(line[s].Z, line[s + 1].Z) - reach) - z0) / band;
                    int to = Math.Min(rows - 1, (int)MathF.Ceiling(Math.Max(line[s].Z, line[s + 1].Z) + reach) - z0) / band;
                    for (int b = from; b <= to; b++)
                    {
                        bands[b].Add(s);
                    }
                }
                Parallel.For(0, rows, row =>
                {
                    foreach (int s in bands[row / band])
                    {
                        Nearest(line[s], line[s + 1], reach, z0 + row, x0, z0, x1, columns, distance, heights, side);
                    }
                });
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
                        float value = curve.Profile switch
                        {
                            CurveProfile.Ridge => h * across,
                            CurveProfile.Valley => -h * across,
                            CurveProfile.Road => h,
                            _ => h * Cliff(side[i] * d, Math.Max(0, curve.Width), falloff),
                        };
                        if (coverage is not null)
                        {
                            byte covered = (byte)MathF.Round(across * 255);
                            if (covered >= coverage[x, z])
                            {
                                values[x, z] = value;
                                coverage[x, z] = covered;
                            }
                        }
                        else if (MathF.Abs(value) >= MathF.Abs(values[x, z]))
                        {
                            values[x, z] = value;
                        }
                    }
                }
            }
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

        /// <summary>
        /// Keeps, for the corners of one row near a part of the line, the part nearest to them.
        /// </summary>
        private static void Nearest(CurvePoint a, CurvePoint b, float reach, int z, int minX, int minZ, int maxX, int columns, float[] distance, float[] heights, float[] side)
        {
            if (z < Math.Min(a.Z, b.Z) - reach || z > Math.Max(a.Z, b.Z) + reach)
            {
                return;
            }
            Vector2 from = new Vector2(a.X, a.Z);
            Vector2 along = new Vector2(b.X, b.Z) - from;
            float lengthSquared = along.LengthSquared();
            int x0 = Math.Max(minX, (int)MathF.Floor(Math.Min(a.X, b.X) - reach));
            int x1 = Math.Min(maxX, (int)MathF.Ceiling(Math.Max(a.X, b.X) + reach));
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
        private static TiledBuffer<T> Reuse<T>(TiledBuffer<T> made, TiledBuffer<T>? previous, (int X0, int Z0, int X1, int Z1)? tiles) where T : unmanaged, IEquatable<T>
        {
            if (previous is null || previous.Width != made.Width || previous.Height != made.Height)
            {
                return made;
            }
            TiledBuffer<T>.Builder builder = previous.ToBuilder();
            (int x0, int z0, int x1, int z1) = tiles ?? (0, 0, made.TilesX - 1, made.TilesY - 1);
            for (int tileY = z0; tileY <= z1; tileY++)
            {
                for (int tileX = x0; tileX <= x1; tileX++)
                {
                    ReadOnlySpan<T> tile = made.Tile(tileX, tileY);
                    if (!made.SharesTile(previous, tileX, tileY) && !LayerStacks.SameTile(made, tileX, tileY, tile, previous.Tile(tileX, tileY)))
                    {
                        builder.SetTile(tileX, tileY, tile);
                    }
                }
            }
            return builder.ToBuffer();
        }

        /// <summary>
        /// The tiles (columns and rows, inclusive) a curve's values can reach; an empty range for a
        /// curve that reaches nothing.
        /// </summary>
        private static (int X0, int Z0, int X1, int Z1) TilesOf(HeightCurve curve, int width, int height)
        {
            if (curve.Symmetry is not null)
            {
                return Parts(curve, width - 1, height - 1).Select(part => TilesOf(part, width, height)).Aggregate(Union);
            }
            const int size = TiledBuffer<float>.TileSize;
            IReadOnlyList<CurvePoint> line = LegacySpline(curve);
            float reach = (curve.Profile == CurveProfile.Cliff ? Math.Max(0, curve.Width) : Math.Max(0, curve.Width) / 2) + Math.Max(0, curve.Falloff) + 1;
            if (line.Count < 2)
            {
                return (0, 0, -1, -1);
            }
            int Clamp(float value, int limit) => Math.Clamp((int)value, 0, limit - 1) / size;
            return (Clamp(line.Min(p => p.X) - reach, width), Clamp(line.Min(p => p.Z) - reach, height),
                Clamp(line.Max(p => p.X) + reach + 1, width), Clamp(line.Max(p => p.Z) + reach + 1, height));
        }

        private static (int X0, int Z0, int X1, int Z1) Union((int X0, int Z0, int X1, int Z1) a, (int X0, int Z0, int X1, int Z1) b) =>
            a.X1 < a.X0 ? b : b.X1 < b.X0 ? a : (Math.Min(a.X0, b.X0), Math.Min(a.Z0, b.Z0), Math.Max(a.X1, b.X1), Math.Max(a.Z1, b.Z1));

        private static CurvePoint Mirror(CurvePoint about, CurvePoint point) =>
            new CurvePoint(2 * about.X - point.X, 2 * about.Z - point.Z, point.Height);

        private static float CatmullRom(float p0, float p1, float p2, float p3, float t) =>
            0.5f * (2 * p1 + (p2 - p0) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (3 * p1 - p0 - 3 * p2 + p3) * t * t * t);
    }
}
