using System.Numerics;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// A place on a curve's spline (<see cref="HeightCurves.Samples"/>) with every value the curve
    /// has there: its nodes' values blended, the variation along the curve added.
    /// </summary>
    /// <param name="X">In ogrids.</param>
    /// <param name="Z">In ogrids.</param>
    /// <param name="Height">The height (a road's or river bed's) or rise there, in game units.</param>
    /// <param name="Width">The width at full height there, in ogrids.</param>
    /// <param name="FalloffLeft">The fade on the left there, in ogrids.</param>
    /// <param name="FalloffRight">The fade on the right there, in ogrids.</param>
    /// <param name="Strength">How much of the profile there is there, 0 to 1.</param>
    /// <param name="Distance">How far along the spline from the first point, in ogrids.</param>
    /// <param name="Node">The index of the curve's point at this place, -1 between points.</param>
    public readonly record struct CurveSample(float X, float Z, float Height, float Width, float FalloffLeft, float FalloffRight, float Strength,
        float Distance, int Node);

    /// <summary>
    /// The grade of a curve between two of its points (<see cref="HeightCurves.Grades"/>).
    /// </summary>
    /// <param name="From">The first point's index.</param>
    /// <param name="To">The next point's index (0 for a closed curve's last part).</param>
    /// <param name="Rise">The second point's height less the first's, in game units.</param>
    /// <param name="Run">The length of the spline between them, in ogrids.</param>
    /// <param name="Percent">Rise over run (a game unit is an ogrid), in percent.</param>
    /// <param name="Degrees">The same as an angle.</param>
    /// <param name="SteepestPercent">The steepest grade between them, rising in the direction of travel (negative: falling), in percent.</param>
    /// <param name="SteepestX">Where it is steepest, in ogrids.</param>
    /// <param name="SteepestZ">Where it is steepest, in ogrids.</param>
    public sealed record CurveGrade(int From, int To, float Rise, float Run, float Percent, float Degrees, float SteepestPercent, float SteepestX, float SteepestZ);

    /// <summary>
    /// Curves that shape the terrain along them (user story T-4): the spline through a curve's
    /// points (<see cref="Samples"/>), and the values of its layer made from it
    /// (<see cref="Rasterize"/>). A curve is a height layer (<see cref="CurveSource"/>), so editing
    /// the curve makes its values again and the heightmap follows.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The path is a Catmull-Rom spline through the points; at a sharp point the neighbour across it
    /// is the point itself, so the path makes a corner there. Heights follow a monotone cubic over the
    /// points (Fritsch and Carlson): no height between two points lies outside theirs. A point's own
    /// width, falloffs and strength blend into the next point's by a smoothstep.
    /// </para>
    /// <para>
    /// Across the curve a profile is full over the width and fades over the falloff on each side by
    /// the curve's shape (<see cref="Fade"/>); a cliff has a face of its own width at the line. Edge
    /// noise moves the distance to the line before the fade; variation along the curve moves the
    /// width and height by the distance along it. A closed curve that fills counts its inside as on
    /// the line. The images of a symmetric curve are drawn from the curve's own samples, turned or
    /// mirrored, and read the edge noise at the original place, so every part of the map is alike.
    /// </para>
    /// </remarks>
    public static class HeightCurves
    {
        /// <summary>
        /// About how far apart the spline is sampled, in ogrids: a corner's distance.
        /// </summary>
        private const float Step = 1f;

        /// <summary>
        /// How a new curve layer of a profile combines: a road replaces the terrain where it lies (by
        /// its coverage), a river cuts (minimum), the others add their rise or sink.
        /// </summary>
        public static LayerCombine CombineOf(CurveProfile profile) => profile switch
        {
            CurveProfile.Road => LayerCombine.Replace,
            CurveProfile.River => LayerCombine.Minimum,
            _ => LayerCombine.Add,
        };

        /// <summary>
        /// How a curve's layer combines: as its profile does (<see cref="CombineOf(CurveProfile)"/>),
        /// a road by its <see cref="HeightCurve.RoadMode"/> (replace, minimum, maximum).
        /// </summary>
        public static LayerCombine CombineOf(HeightCurve curve) => curve.Profile == CurveProfile.Road
            ? curve.RoadMode switch
            {
                CurveRoadMode.CutOnly => LayerCombine.Minimum,
                CurveRoadMode.FillOnly => LayerCombine.Maximum,
                _ => LayerCombine.Replace,
            }
            : CombineOf(curve.Profile);

        /// <summary>
        /// Whether a profile's points hold heights (a road's, a river bed's) rather than a rise or
        /// sink; its layer has coverage.
        /// </summary>
        public static bool IsAbsolute(CurveProfile profile) => profile is CurveProfile.Road or CurveProfile.River;

        /// <summary>
        /// The shape of a curve's fade: its own, else its profile's (a river's banks ease out).
        /// </summary>
        public static CurveFalloffShape ShapeOf(HeightCurve curve) =>
            curve.FalloffShape ?? (curve.Profile == CurveProfile.River ? CurveFalloffShape.EaseOut : CurveFalloffShape.Smooth);

        /// <summary>
        /// A fade's shape: how much of a profile there is a fraction <paramref name="t"/> of the way
        /// from the outer edge (0) to where it is full (1).
        /// </summary>
        public static float Fade(float t, CurveFalloffShape shape, int steps = 3)
        {
            t = Math.Clamp(t, 0, 1);
            switch (shape)
            {
                case CurveFalloffShape.Linear:
                    return t;
                case CurveFalloffShape.EaseIn:
                    return t * t;
                case CurveFalloffShape.EaseOut:
                    return 1 - (1 - t) * (1 - t);
                case CurveFalloffShape.Terraced:
                {
                    steps = Math.Max(1, steps);
                    float k = MathF.Floor(t * steps);
                    float r = Math.Clamp((t * steps - k - 0.7f) / 0.3f, 0, 1);
                    return Math.Min(1, (k + r * r * (3 - 2 * r)) / steps);
                }
                default:
                    return t * t * (3 - 2 * t);
            }
        }

        /// <summary>
        /// The spline through the points, as places about an ogrid apart with the curve's values
        /// there (<see cref="CurveSample"/>); the points themselves are on it. A closed curve's last
        /// sample is its first point again.
        /// </summary>
        public static IReadOnlyList<CurveSample> Samples(HeightCurve curve)
        {
            var points = curve.Points;
            int count = points.Length;
            if (count == 0)
            {
                return [];
            }
            float falloffRight = curve.FalloffRight ?? curve.Falloff;
            CurveSample At(CurvePoint point, int node) => new CurveSample(point.X, point.Z, point.Height, point.Width ?? curve.Width,
                point.FalloffLeft ?? curve.Falloff, point.FalloffRight ?? falloffRight, Math.Clamp(point.Strength ?? 1, 0, 1), 0, node);
            List<CurveSample> line = [At(points[0], 0)];
            if (count >= 2)
            {
                int segments = curve.Closed ? count : count - 1;
                float[] tangents = HeightTangents(points, curve.Closed);
                for (int s = 0; s < segments; s++)
                {
                    CurvePoint p1 = points[s];
                    CurvePoint p2 = points[(s + 1) % count];
                    // at a sharp point the neighbour across it is the point itself: a corner
                    CurvePoint p0 = p1.Sharp ? p1 : s > 0 ? points[s - 1] : curve.Closed ? points[count - 1] : Mirror(p1, p2);
                    CurvePoint p3 = p2.Sharp ? p2 : s + 2 < count ? points[s + 2] : curve.Closed ? points[(s + 2) % count] : Mirror(p2, p1);
                    CurveSample a = At(p1, s);
                    CurveSample b = At(p2, (s + 1) % count);
                    float length = MathF.Sqrt((p2.X - p1.X) * (p2.X - p1.X) + (p2.Z - p1.Z) * (p2.Z - p1.Z));
                    int steps = Math.Max(1, (int)MathF.Ceiling(length / Step));
                    for (int i = 1; i <= steps; i++)
                    {
                        float t = i / (float)steps;
                        float blend = t * t * (3 - 2 * t);
                        line.Add(new CurveSample(
                            CatmullRom(p0.X, p1.X, p2.X, p3.X, t),
                            CatmullRom(p0.Z, p1.Z, p2.Z, p3.Z, t),
                            // points of one height keep it exactly
                            i == steps || p1.Height == p2.Height ? p2.Height : Hermite(p1.Height, p2.Height, tangents[s], tangents[(s + 1) % count], t),
                            a.Width + (b.Width - a.Width) * blend,
                            a.FalloffLeft + (b.FalloffLeft - a.FalloffLeft) * blend,
                            a.FalloffRight + (b.FalloffRight - a.FalloffRight) * blend,
                            a.Strength + (b.Strength - a.Strength) * blend,
                            0,
                            i == steps ? (s + 1) % count : -1));
                    }
                }
            }
            float distance = 0;
            bool varies = !curve.HeightVariation.IsNone || !curve.WidthVariation.IsNone;
            float scale = Math.Max(0.01f, curve.VariationScale);
            for (int i = 0; i < line.Count; i++)
            {
                if (i > 0)
                {
                    distance += MathF.Sqrt((line[i].X - line[i - 1].X) * (line[i].X - line[i - 1].X) + (line[i].Z - line[i - 1].Z) * (line[i].Z - line[i - 1].Z));
                }
                CurveSample sample = line[i] with { Distance = distance };
                if (varies)
                {
                    // two rows of one-dimensional noise over the distance along the curve, one per value
                    float along = distance / scale;
                    sample = sample with
                    {
                        Width = Math.Max(0, sample.Width + curve.WidthVariation.At(Noise01(along, 0.5f, curve.NoiseSeed))),
                        Height = sample.Height + curve.HeightVariation.At(Noise01(along, 7.5f, curve.NoiseSeed)),
                    };
                }
                line[i] = sample;
            }
            return line;
        }

        /// <summary>
        /// The spline through the points (<see cref="Samples"/>) as points about an ogrid apart, each
        /// with the width, falloffs and strength the curve has there.
        /// </summary>
        public static IReadOnlyList<CurvePoint> Spline(HeightCurve curve) =>
            [.. Samples(curve).Select(sample => new CurvePoint(sample.X, sample.Z, sample.Height, sample.Width, sample.FalloffLeft, sample.FalloffRight, sample.Strength))];

        /// <summary>
        /// The grade between each two points one after the other (a closed curve's last back to the
        /// first): the rise, the run along the spline, the grade and where it is steepest. For a road
        /// or river the heights are the ground's; for the others the rise's.
        /// </summary>
        public static IReadOnlyList<CurveGrade> Grades(HeightCurve curve)
        {
            IReadOnlyList<CurveSample> samples = Samples(curve);
            List<int> nodes = [.. Enumerable.Range(0, samples.Count).Where(i => samples[i].Node >= 0)];
            List<CurveGrade> grades = [];
            for (int n = 0; n + 1 < nodes.Count; n++)
            {
                CurveSample a = samples[nodes[n]];
                CurveSample b = samples[nodes[n + 1]];
                float rise = curve.Points[b.Node].Height - curve.Points[a.Node].Height;
                float run = b.Distance - a.Distance;
                float steepest = 0;
                float steepestX = (a.X + b.X) / 2;
                float steepestZ = (a.Z + b.Z) / 2;
                for (int i = nodes[n]; i < nodes[n + 1]; i++)
                {
                    float step = samples[i + 1].Distance - samples[i].Distance;
                    if (step <= 1e-4f)
                    {
                        continue;
                    }
                    float grade = (samples[i + 1].Height - samples[i].Height) / step * 100;
                    if (MathF.Abs(grade) > MathF.Abs(steepest))
                    {
                        steepest = grade;
                        steepestX = (samples[i].X + samples[i + 1].X) / 2;
                        steepestZ = (samples[i].Z + samples[i + 1].Z) / 2;
                    }
                }
                grades.Add(new CurveGrade(a.Node, b.Node, rise, run, run > 0 ? rise / run * 100 : 0,
                    MathF.Atan2(rise, run) * 180 / MathF.PI, steepest, steepestX, steepestZ));
            }
            return grades;
        }

        /// <summary>
        /// The curve running the other way: its points in the other order and, for every profile but
        /// the cliff, the left and right falloffs (the curve's and each point's) swapped, so it keeps
        /// its look; a cliff's high side flips (its fade stays the left's). The camber turns round too.
        /// Variation along the curve reads the distance from the new first point.
        /// </summary>
        public static HeightCurve Reversed(HeightCurve curve)
        {
            if (curve.Profile == CurveProfile.Cliff)
            {
                return curve with { Points = [.. curve.Points.Reverse()], Camber = -curve.Camber };
            }
            float right = curve.FalloffRight ?? curve.Falloff;
            return curve with
            {
                Points = [.. curve.Points.Reverse().Select(point => point with { FalloffLeft = point.FalloffRight, FalloffRight = point.FalloffLeft })],
                Falloff = right,
                FalloffRight = right == curve.Falloff ? null : curve.Falloff,
                Camber = -curve.Camber,
            };
        }

        /// <summary>
        /// The curve with an even grade from point <paramref name="from"/> to point
        /// <paramref name="to"/>: the points between them get heights on a straight line between
        /// theirs, by the distance along the spline (the smooth heights between points then make it
        /// a ramp).
        /// </summary>
        /// <exception cref="ArgumentException">A point is not the curve's, or they are the same.</exception>
        public static HeightCurve EvenGrade(HeightCurve curve, int from, int to)
        {
            int count = curve.Points.Length;
            if (from < 0 || from >= count || to < 0 || to >= count)
            {
                throw new ArgumentException($"The curve has points 0 to {count - 1}", nameof(from));
            }
            if (from == to)
            {
                throw new ArgumentException("Choose two different points to even out the grade between", nameof(to));
            }
            (from, to) = (Math.Min(from, to), Math.Max(from, to));
            float[] distances = new float[count];
            foreach (CurveSample sample in Samples(curve with { HeightVariation = CurveRange.None, WidthVariation = CurveRange.None }))
            {
                // a closed curve's first point comes again at the end: keep its first distance
                if (sample.Node > 0 || (sample.Node == 0 && sample.Distance == 0))
                {
                    distances[sample.Node] = sample.Distance;
                }
            }
            float h0 = curve.Points[from].Height;
            float h1 = curve.Points[to].Height;
            float run = distances[to] - distances[from];
            var points = curve.Points.Select((point, i) => i <= from || i >= to || run <= 0
                ? point
                : point with { Height = h0 + (h1 - h0) * (distances[i] - distances[from]) / run });
            return curve with { Points = [.. points] };
        }

        /// <summary>
        /// The values (and, for a road or river, the coverage) of a curve's layer on a map of that
        /// size; with <paramref name="previousValues"/> and <paramref name="previousCoverage"/> the
        /// tiles that come out the same are those buffers' own, so only the tiles the change touched
        /// are added up again.
        /// </summary>
        public static (TiledBuffer<float> Values, TiledBuffer<byte>? Coverage) Rasterize(
            HeightCurve curve, int mapWidth, int mapHeight, TiledBuffer<float>? previousValues = null, TiledBuffer<byte>? previousCoverage = null,
            HeightCurve? previousCurve = null)
        {
            int width = mapWidth + 1;
            int height = mapHeight + 1;
            TiledBuffer<float>.Builder values = TiledBuffer<float>.Create(width, height).ToBuilder();
            TiledBuffer<byte>.Builder? coverage = IsAbsolute(curve.Profile) ? TiledBuffer<byte>.Create(width, height).ToBuilder() : null;
            IReadOnlyList<CurveSample> samples = Samples(curve);
            foreach (PartLine part in PartLines(curve, samples, mapWidth, mapHeight))
            {
                Draw(curve, part, width, height, values, coverage);
            }
            // the tiles that can differ lie where either curve reaches; without the previous curve, anywhere
            (int X0, int Z0, int X1, int Z1)? tiles = previousCurve is null ? null : Union(TilesOf(curve, width, height), TilesOf(previousCurve, width, height));
            return (Reuse(values.ToBuffer(), previousValues, tiles), coverage is null ? null : Reuse(coverage.ToBuffer(), previousCoverage, tiles));
        }

        /// <summary>
        /// The curve and, when it is drawn by a symmetry (<see cref="HeightCurve.Symmetry"/>), its images
        /// in the other parts of a map of that size: the curve first. An image in a mirrored part runs
        /// the other way, so that a cliff's high side (and a point's left falloff) is the image of the curve's.
        /// </summary>
        public static IReadOnlyList<HeightCurve> Parts(HeightCurve curve, int mapWidth, int mapHeight)
        {
            if (curve.Symmetry is not SymmetrySetting symmetry)
            {
                return [curve];
            }
            List<HeightCurve> parts = [curve with { Symmetry = null }];
            foreach (MapSymmetryTransform transform in symmetry.Transforms(mapWidth, mapHeight).Skip(1))
            {
                IEnumerable<CurvePoint> points = curve.Points.Select(point =>
                {
                    Vector2 image = transform.Apply(new Vector2(point.X, point.Z));
                    return point with { X = image.X, Z = image.Y };
                });
                parts.Add(curve with { Symmetry = null, Points = [.. transform.IsMirror ? points.Reverse() : points] });
            }
            return parts;
        }

        /// <summary>
        /// The least half width of a face's band of ogrids: an ogrid's half diagonal and a little, so
        /// that the ogrids under a face of any width join by their sides and corners into a wall
        /// units cannot slip through.
        /// </summary>
        private const float FaceBand = 0.75f;

        /// <summary>
        /// The ogrids under a cliff's face (user story T-4, D3): those whose centre lies within half
        /// the face's width (<see cref="HeightCurve.CliffFace"/>, at least <see cref="FaceBand"/>) of
        /// the drop, the edge noise moving the drop as it moves the terrain, where the cliff rises;
        /// for a closed cliff that fills, its rim. The images of a curve drawn by a symmetry are
        /// included. Each ogrid once, as its index row by row (<c>z * mapWidth + x</c>), in order;
        /// empty for a curve that is no cliff. Whether the face blocks walkability
        /// (<see cref="HeightCurve.BlocksWalkability"/>) is the caller's (<see cref="CliffFaces"/>).
        /// </summary>
        public static int[] FaceCells(HeightCurve curve, int mapWidth, int mapHeight)
        {
            if (curve.Profile != CurveProfile.Cliff || curve.Points.Length < 2 || mapWidth <= 0 || mapHeight <= 0)
            {
                return [];
            }
            float half = Math.Max(FaceBand, Math.Max(0, curve.CliffFace) / 2);
            float reach = half + MathF.Abs(curve.NoiseAmplitude);
            bool fills = curve.Fills;
            HashSet<int> cells = [];
            foreach (PartLine part in PartLines(curve, Samples(curve), mapWidth, mapHeight))
            {
                FaceCellsOf(curve, part, half, reach, fills, mapWidth, mapHeight, cells);
            }
            int[] sorted = [.. cells];
            Array.Sort(sorted);
            return sorted;
        }

        /// <summary>
        /// Adds the ogrids under one part's face to <paramref name="cells"/>.
        /// </summary>
        private static void FaceCellsOf(HeightCurve curve, PartLine part, float half, float reach, bool fills, int mapWidth, int mapHeight, HashSet<int> cells)
        {
            CurveSample[] line = part.Line;
            if (line.Length < 2)
            {
                return;
            }
            bool outsideIsLeft = fills && FillOf(line).OutsideIsLeft;
            // per ogrid near the line: the nearest segment's distance, side and place along it
            Dictionary<int, (float Distance, int Segment, float Along, bool Left)> near = [];
            for (int s = 0; s + 1 < line.Length; s++)
            {
                CurveSample a = line[s], b = line[s + 1];
                Vector2 from = new Vector2(a.X, a.Z);
                Vector2 along = new Vector2(b.X, b.Z) - from;
                float lengthSquared = along.LengthSquared();
                int x0 = Math.Max(0, (int)MathF.Floor(Math.Min(a.X, b.X) - reach - 0.5f));
                int x1 = Math.Min(mapWidth - 1, (int)MathF.Ceiling(Math.Max(a.X, b.X) + reach));
                int z0 = Math.Max(0, (int)MathF.Floor(Math.Min(a.Z, b.Z) - reach - 0.5f));
                int z1 = Math.Min(mapHeight - 1, (int)MathF.Ceiling(Math.Max(a.Z, b.Z) + reach));
                for (int z = z0; z <= z1; z++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        // the ogrid's centre
                        Vector2 to = new Vector2(x + 0.5f, z + 0.5f) - from;
                        float t = lengthSquared > 0 ? Math.Clamp(Vector2.Dot(to, along) / lengthSquared, 0, 1) : 0;
                        float d = (to - along * t).Length();
                        if (d > reach)
                        {
                            continue;
                        }
                        int index = z * mapWidth + x;
                        if (!near.TryGetValue(index, out var best) || d < best.Distance)
                        {
                            // the left of the direction of travel in the top view (x right, z down), as Nearest
                            near[index] = (d, s, t, along.X * to.Y - along.Y * to.X < 0);
                        }
                    }
                }
            }
            float scale = Math.Max(0.01f, curve.NoiseScale);
            foreach ((int index, (float d, int segment, float t, bool left)) in near)
            {
                CurveSample here = Lerp(line[segment], line[segment + 1], t);
                if (here.Height * here.Strength == 0)
                {
                    // no drop here
                    continue;
                }
                int x = index % mapWidth, z = index / mapWidth;
                float noise = 0;
                if (curve.NoiseAmplitude != 0)
                {
                    // as ValueAt: an image reads the noise at the original place
                    Vector2 at = part.NoisePlace is MapSymmetryTransform place ? place.Apply(new Vector2(x + 0.5f, z + 0.5f)) : new Vector2(x + 0.5f, z + 0.5f);
                    noise = curve.NoiseAmplitude * HeightNoise.At(at.X / scale, at.Y / scale, curve.NoiseSeed);
                }
                // the signed distance to the drop as the terrain has it: a fill's inside, an open cliff's left, positive
                float signed = fills ? (left != outsideIsLeft ? d : -d) : left ? d : -d;
                if (MathF.Abs(signed + (fills ? -noise : noise)) <= half)
                {
                    cells.Add(index);
                }
            }
        }

        /// <summary>
        /// One part's line: the curve's samples where that part has them, and where the part reads
        /// the edge noise (null: in place; else the original place of its own).
        /// </summary>
        private sealed record PartLine(CurveSample[] Line, MapSymmetryTransform? NoisePlace);

        /// <summary>
        /// The lines of the curve and its images: the curve's own samples turned or mirrored (an
        /// image in a mirrored part running the other way), so each place of an image has the
        /// variation of the place it is the image of.
        /// </summary>
        private static IEnumerable<PartLine> PartLines(HeightCurve curve, IReadOnlyList<CurveSample> samples, int mapWidth, int mapHeight)
        {
            yield return new PartLine([.. samples], null);
            if (curve.Symmetry is not SymmetrySetting symmetry)
            {
                yield break;
            }
            foreach (MapSymmetryTransform transform in symmetry.Transforms(mapWidth, mapHeight).Skip(1))
            {
                CurveSample[] line = [.. samples.Select(sample =>
                {
                    Vector2 image = transform.Apply(new Vector2(sample.X, sample.Z));
                    return sample with { X = image.X, Z = image.Y };
                })];
                if (transform.IsMirror)
                {
                    Array.Reverse(line);
                }
                yield return new PartLine(line, transform.Inverse());
            }
        }

        /// <summary>
        /// How far from a sample its curve can shape the terrain, in ogrids.
        /// </summary>
        private static float ReachOf(HeightCurve curve, CurveSample sample)
        {
            float noise = MathF.Abs(curve.NoiseAmplitude);
            float fall = Math.Max(0, Math.Max(sample.FalloffLeft, sample.FalloffRight));
            if (curve.Profile == CurveProfile.Cliff)
            {
                float face = Math.Max(0, curve.CliffFace) / 2;
                return (curve.Fills ? 0 : Math.Max(Math.Max(0, sample.Width), face) + fall) + face + noise;
            }
            return Math.Max(0, sample.Width) / 2 + fall + noise;
        }

        /// <summary>
        /// Draws one part of a curve into the layer's values: where parts overlap, the one that
        /// shapes the terrain most (a road or river: the one that covers most).
        /// </summary>
        private static void Draw(HeightCurve curve, PartLine part, int width, int height, TiledBuffer<float>.Builder values, TiledBuffer<byte>.Builder? coverage)
        {
            CurveSample[] line = part.Line;
            if (line.Length < 2)
            {
                return;
            }
            bool fills = curve.Fills;
            float[] reaches = [.. line.Select(sample => ReachOf(curve, sample))];
            float maxReach = reaches.Max();
            if (maxReach <= 0 && !fills)
            {
                return;
            }
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            for (int i = 0; i < line.Length; i++)
            {
                minX = Math.Min(minX, line[i].X - reaches[i]);
                maxX = Math.Max(maxX, line[i].X + reaches[i]);
                minZ = Math.Min(minZ, line[i].Z - reaches[i]);
                maxZ = Math.Max(maxZ, line[i].Z + reaches[i]);
            }
            if (maxX < 0 || maxZ < 0 || minX > width - 1 || minZ > height - 1)
            {
                return;
            }
            int x0 = Math.Clamp((int)MathF.Floor(minX), 0, width - 1), x1 = Math.Clamp((int)MathF.Ceiling(maxX), 0, width - 1);
            int z0 = Math.Clamp((int)MathF.Floor(minZ), 0, height - 1), z1 = Math.Clamp((int)MathF.Ceiling(maxZ), 0, height - 1);
            int columns = x1 - x0 + 1;
            int rows = z1 - z0 + 1;
            Nearness near = new Nearness(columns * rows);
            // rows in bands, each band with the parts of the line that reach it; the rows apart on the pool
            const int band = 16;
            List<int>[] bands = [.. Enumerable.Range(0, (rows + band - 1) / band).Select(_ => new List<int>())];
            float[] segmentReach = new float[line.Length - 1];
            for (int s = 0; s + 1 < line.Length; s++)
            {
                segmentReach[s] = Math.Max(reaches[s], reaches[s + 1]);
                int from = Math.Max(0, (int)MathF.Floor(Math.Min(line[s].Z, line[s + 1].Z) - segmentReach[s]) - z0) / band;
                int to = Math.Min(rows - 1, (int)MathF.Ceiling(Math.Max(line[s].Z, line[s + 1].Z) + segmentReach[s]) - z0) / band;
                for (int b = from; b <= to; b++)
                {
                    bands[b].Add(s);
                }
            }
            bool[]? inside = fills ? new bool[columns * rows] : null;
            Parallel.For(0, rows, row =>
            {
                foreach (int s in bands[row / band])
                {
                    Nearest(line[s], line[s + 1], segmentReach[s], s, z0 + row, x0, z0, x1, columns, near);
                }
                if (inside is not null)
                {
                    Inside(line, z0 + row, x0, x1, inside.AsSpan(row * columns, columns));
                }
            });

            // the values of this part, NaN where it does nothing
            float[] made = new float[columns * rows];
            byte[] covered = new byte[columns * rows];
            Fill fill = fills ? FillOf(line) : default;
            CurveFalloffShape shape = ShapeOf(curve);
            Parallel.For(0, rows, row =>
            {
                for (int column = 0; column < columns; column++)
                {
                    int i = row * columns + column;
                    (made[i], covered[i]) = ValueAt(curve, shape, line, part.NoisePlace, near, i, x0 + column, z0 + row, inside?[i] == true, fill);
                }
            });

            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    int i = row * columns + column;
                    float value = made[i];
                    if (float.IsNaN(value))
                    {
                        continue;
                    }
                    int x = x0 + column;
                    int z = z0 + row;
                    if (coverage is not null)
                    {
                        if (covered[i] >= coverage[x, z])
                        {
                            values[x, z] = value;
                            coverage[x, z] = covered[i];
                        }
                    }
                    else if (MathF.Abs(value) >= MathF.Abs(values[x, z]))
                    {
                        values[x, z] = value;
                    }
                }
            }
        }

        /// <summary>
        /// For each corner near a part's line: the distance to the nearest segment, which segment,
        /// how far along it and on which side (1 left, -1 right in the top view).
        /// </summary>
        private sealed class Nearness
        {
            public readonly float[] Distance;
            public readonly int[] Segment;
            public readonly float[] Along;
            public readonly sbyte[] Side;

            public Nearness(int count)
            {
                Distance = new float[count];
                Segment = new int[count];
                Along = new float[count];
                Side = new sbyte[count];
                Array.Fill(Distance, float.PositiveInfinity);
                Array.Fill(Segment, -1);
            }
        }

        /// <summary>
        /// What a fill uses for its inside and rim: one height and strength (the means along the
        /// line) and which side of the line the outside lies on.
        /// </summary>
        private readonly record struct Fill(float Height, float Strength, bool OutsideIsLeft);

        private static Fill FillOf(CurveSample[] line)
        {
            // the last sample is the first again
            int count = line.Length - 1;
            float height = 0, strength = 0, area = 0;
            for (int i = 0; i < count; i++)
            {
                height += line[i].Height;
                strength += line[i].Strength;
                area += line[i].X * line[i + 1].Z - line[i + 1].X * line[i].Z;
            }
            // the left of travel has a negative cross product (x right, z down); the inside is on the left when the area is negative
            return new Fill(height / count, strength / count, OutsideIsLeft: area > 0);
        }

        /// <summary>
        /// Marks the corners of one row inside a closed line (even and odd crossings).
        /// </summary>
        private static void Inside(CurveSample[] line, int z, int minX, int maxX, Span<bool> inside)
        {
            List<float> crossings = [];
            for (int s = 0; s + 1 < line.Length; s++)
            {
                CurveSample a = line[s], b = line[s + 1];
                if ((a.Z <= z) != (b.Z <= z))
                {
                    crossings.Add(a.X + (z - a.Z) / (b.Z - a.Z) * (b.X - a.X));
                }
            }
            crossings.Sort();
            for (int q = 0; q + 1 < crossings.Count; q += 2)
            {
                int from = Math.Max(minX, (int)MathF.Ceiling(crossings[q]));
                int to = Math.Min(maxX, (int)MathF.Floor(crossings[q + 1]));
                for (int x = from; x <= to; x++)
                {
                    inside[x - minX] = true;
                }
            }
        }

        /// <summary>
        /// A curve's value (and coverage) at a corner, NaN where it does nothing.
        /// </summary>
        private static (float Value, byte Coverage) ValueAt(HeightCurve curve, CurveFalloffShape shape, CurveSample[] line, MapSymmetryTransform? noisePlace,
            Nearness near, int i, int x, int z, bool inside, Fill fill)
        {
            int k = near.Segment[i];
            if (k < 0 && !inside)
            {
                return (float.NaN, 0);
            }
            float d = near.Distance[i];
            if (!inside && d >= ReachOf(curve, Lerp(line[k], line[k + 1], near.Along[i])))
            {
                return (float.NaN, 0);
            }
            float noise = 0;
            if (curve.NoiseAmplitude != 0)
            {
                // an image reads the noise at the original place, so every part of the map gets the same
                Vector2 at = noisePlace is MapSymmetryTransform place ? place.Apply(new Vector2(x, z)) : new Vector2(x, z);
                float scale = Math.Max(0.01f, curve.NoiseScale);
                noise = curve.NoiseAmplitude * HeightNoise.At(at.X / scale, at.Y / scale, curve.NoiseSeed);
            }
            CurveSample here = k >= 0 ? Lerp(line[k], line[k + 1], near.Along[i]) : line[0];
            bool left = k >= 0 && near.Side[i] > 0;
            int steps = curve.TerraceSteps;
            float face = Math.Max(0, curve.CliffFace);
            bool absolute = IsAbsolute(curve.Profile);
            float f;
            float value;
            if (curve.Fills)
            {
                // the inside is on the curve; the outside fades over the falloff on its side
                float signed = (inside ? -(k < 0 ? float.PositiveInfinity : d) : d) + noise;
                if (curve.Profile == CurveProfile.Cliff)
                {
                    f = CliffFace(-signed, face, shape, steps);
                }
                else
                {
                    float fall = fill.OutsideIsLeft ? here.FalloffLeft : here.FalloffRight;
                    f = signed <= 0 ? 1 : Across(signed, Math.Max(0, here.Width) / 2, fall, shape, steps);
                }
                value = absolute ? fill.Height : fill.Height * fill.Strength * f * (curve.Profile == CurveProfile.Valley ? -1 : 1);
                return (value, absolute ? ToByte(f * fill.Strength) : (byte)0);
            }
            float w = Math.Max(0, here.Width);
            if (curve.Profile == CurveProfile.Cliff)
            {
                f = Cliff((left ? d : -d) + noise, w, here.FalloffLeft, face, shape, steps);
                return (here.Height * here.Strength * f, 0);
            }
            float distance = Math.Max(0, d + noise);
            f = Across(distance, w / 2, left ? here.FalloffLeft : here.FalloffRight, shape, steps);
            if (absolute)
            {
                float across = left ? d : -d;
                float camber = curve.Camber == 0 ? 0 : curve.Camber * (w > 0 ? Math.Clamp(across / w, -0.5f, 0.5f) : 0.5f * MathF.Sign(across));
                return (here.Height + camber, ToByte(f * here.Strength));
            }
            value = here.Height * here.Strength * f;
            return (curve.Profile == CurveProfile.Valley ? -value : value, 0);
        }

        private static byte ToByte(float fraction) => (byte)MathF.Round(Math.Clamp(fraction, 0, 1) * 255);

        private static CurveSample Lerp(CurveSample a, CurveSample b, float t) => new CurveSample(
            a.X + (b.X - a.X) * t, a.Z + (b.Z - a.Z) * t, a.Height + (b.Height - a.Height) * t, a.Width + (b.Width - a.Width) * t,
            a.FalloffLeft + (b.FalloffLeft - a.FalloffLeft) * t, a.FalloffRight + (b.FalloffRight - a.FalloffRight) * t,
            a.Strength + (b.Strength - a.Strength) * t, a.Distance + (b.Distance - a.Distance) * t, -1);

        /// <summary>
        /// How much of a profile there is at a distance from the curve: all of it within half the
        /// width, then less over the falloff by the shape.
        /// </summary>
        private static float Across(float distance, float half, float falloff, CurveFalloffShape shape, int steps)
        {
            if (distance <= half)
            {
                return 1;
            }
            if (falloff <= 0)
            {
                return 0;
            }
            return Fade(1 - (distance - half) / falloff, shape, steps);
        }

        /// <summary>
        /// A cliff's face at a signed distance (positive on the high side): nothing beyond half the
        /// face on the low side, rising by the shape over the face, all of it beyond.
        /// </summary>
        private static float CliffFace(float signed, float face, CurveFalloffShape shape, int steps)
        {
            float half = face / 2;
            if (signed <= -half)
            {
                return 0;
            }
            return signed < half ? Fade((signed + half) / face, shape, steps) : 1;
        }

        /// <summary>
        /// A cliff at a signed distance (positive on the left): nothing on the right, a drop of the
        /// face's width at the curve, all of it over the width on the left, fading back over the falloff.
        /// </summary>
        private static float Cliff(float signed, float width, float falloff, float face, CurveFalloffShape shape, int steps)
        {
            float half = face / 2;
            if (signed < half)
            {
                return CliffFace(signed, face, shape, steps);
            }
            return Across(signed, Math.Max(width, half), falloff, shape, steps);
        }

        /// <summary>
        /// Keeps, for the corners of one row near a segment of the line, the segment nearest to them.
        /// </summary>
        private static void Nearest(CurveSample a, CurveSample b, float reach, int segment, int z, int minX, int minZ, int maxX, int columns, Nearness near)
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
            for (int x = x0; x <= x1; x++)
            {
                Vector2 to = new Vector2(x, z) - from;
                float t = lengthSquared > 0 ? Math.Clamp(Vector2.Dot(to, along) / lengthSquared, 0, 1) : 0;
                float d = (to - along * t).Length();
                int i = (z - minZ) * columns + (x - minX);
                if (d < near.Distance[i])
                {
                    near.Distance[i] = d;
                    near.Segment[i] = segment;
                    near.Along[i] = t;
                    // the left of the direction of travel in the top view (x right, z down)
                    float cross = along.X * to.Y - along.Y * to.X;
                    near.Side[i] = (sbyte)(cross < 0 ? 1 : -1);
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
            const int size = TiledBuffer<float>.TileSize;
            (int X0, int Z0, int X1, int Z1) tiles = (0, 0, -1, -1);
            int Clamp(float value, int limit) => Math.Clamp((int)value, 0, limit - 1) / size;
            foreach (PartLine part in PartLines(curve, Samples(curve), width - 1, height - 1))
            {
                CurveSample[] line = part.Line;
                if (line.Length < 2)
                {
                    continue;
                }
                float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
                foreach (CurveSample sample in line)
                {
                    float reach = ReachOf(curve, sample) + 1;
                    minX = Math.Min(minX, sample.X - reach);
                    maxX = Math.Max(maxX, sample.X + reach);
                    minZ = Math.Min(minZ, sample.Z - reach);
                    maxZ = Math.Max(maxZ, sample.Z + reach);
                }
                tiles = Union(tiles, (Clamp(minX, width), Clamp(minZ, height), Clamp(maxX + 1, width), Clamp(maxZ + 1, height)));
            }
            return tiles;
        }

        private static (int X0, int Z0, int X1, int Z1) Union((int X0, int Z0, int X1, int Z1) a, (int X0, int Z0, int X1, int Z1) b) =>
            a.X1 < a.X0 ? b : b.X1 < b.X0 ? a : (Math.Min(a.X0, b.X0), Math.Min(a.Z0, b.Z0), Math.Max(a.X1, b.X1), Math.Max(a.Z1, b.Z1));

        /// <summary>
        /// Noise from 0 to 1 along a row of <see cref="HeightNoise"/>.
        /// </summary>
        private static float Noise01(float along, float row, int seed) => Math.Clamp((HeightNoise.At(along, row, seed) + 1) / 2, 0, 1);

        /// <summary>
        /// The tangents of the heights at the points (Fritsch and Carlson): zero where the heights
        /// turn, limited so that no height between two points lies outside theirs.
        /// </summary>
        private static float[] HeightTangents(IReadOnlyList<CurvePoint> points, bool closed)
        {
            int count = points.Count;
            int segments = closed ? count : count - 1;
            float[] d = new float[segments];
            for (int k = 0; k < segments; k++)
            {
                d[k] = points[(k + 1) % count].Height - points[k].Height;
            }
            float[] m = new float[count];
            for (int k = 0; k < count; k++)
            {
                float? left = k > 0 ? d[k - 1] : closed ? d[segments - 1] : null;
                float? right = k < segments ? d[k] : closed ? d[0] : null;
                m[k] = left is null ? right ?? 0
                    : right is null ? left.Value
                    : left.Value * right.Value <= 0 ? 0
                    : (left.Value + right.Value) / 2;
            }
            for (int k = 0; k < segments; k++)
            {
                int next = (k + 1) % count;
                if (d[k] == 0)
                {
                    m[k] = 0;
                    m[next] = 0;
                    continue;
                }
                float a = m[k] / d[k];
                float b = m[next] / d[k];
                float s = a * a + b * b;
                if (s > 9)
                {
                    float tau = 3 / MathF.Sqrt(s);
                    m[k] = tau * a * d[k];
                    m[next] = tau * b * d[k];
                }
            }
            return m;
        }

        private static float Hermite(float h0, float h1, float m0, float m1, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return (2 * t3 - 3 * t2 + 1) * h0 + (t3 - 2 * t2 + t) * m0 + (-2 * t3 + 3 * t2) * h1 + (t3 - t2) * m1;
        }

        private static CurvePoint Mirror(CurvePoint about, CurvePoint point) =>
            new CurvePoint(2 * about.X - point.X, 2 * about.Z - point.Z, point.Height);

        private static float CatmullRom(float p0, float p1, float p2, float p3, float t) =>
            0.5f * (2 * p1 + (p2 - p0) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (3 * p1 - p0 - 3 * p2 + p3) * t * t * t);
    }
}
