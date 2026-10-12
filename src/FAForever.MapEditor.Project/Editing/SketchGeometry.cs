using System.Collections.Immutable;
using System.Numerics;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// The geometry of the sketch's strokes (user story N-1): thinning and smoothing a line drawn by
    /// hand, and where an eraser's path crosses a line. Places are in ogrids.
    /// </summary>
    public static class SketchGeometry
    {
        /// <summary>
        /// A piece of line shorter than this, in ogrids, is no line: what an eraser leaves of it goes.
        /// </summary>
        public const float Shortest = 0.05f;

        #region Drawing

        /// <summary>
        /// The places a hand drew, thinned: a place closer than <paramref name="spacing"/> to the one
        /// before it is dropped (the last place always stays, so the line ends where the mouse did).
        /// </summary>
        public static ImmutableArray<Vector2> Thin(IReadOnlyList<Vector2> points, float spacing)
        {
            if (points.Count <= 1)
            {
                return [.. points];
            }
            ImmutableArray<Vector2>.Builder kept = ImmutableArray.CreateBuilder<Vector2>();
            kept.Add(points[0]);
            for (int i = 1; i < points.Count - 1; i++)
            {
                if (Vector2.DistanceSquared(points[i], kept[^1]) >= spacing * spacing)
                {
                    kept.Add(points[i]);
                }
            }
            Vector2 last = points[^1];
            if (Vector2.DistanceSquared(last, kept[^1]) > 1e-6f)
            {
                kept.Add(last);
            }
            else if (kept.Count == 1)
            {
                // a click: one dot
                return [kept[0]];
            }
            return kept.ToImmutable();
        }

        /// <summary>
        /// A hand-drawn line smoothed and thinned to few places: the places averaged with their
        /// neighbours twice (the ends stay), then every place dropped that a straight line past it
        /// would not miss by more than <paramref name="tolerance"/> (Douglas and Peucker).
        /// </summary>
        /// <param name="tolerance">How far the result may stray from the smoothed line, in ogrids;
        /// the drawing view passes about a pixel.</param>
        public static ImmutableArray<Vector2> Smooth(IReadOnlyList<Vector2> points, float tolerance)
        {
            if (points.Count <= 2)
            {
                return [.. points];
            }
            Vector2[] line = [.. points];
            for (int pass = 0; pass < 2; pass++)
            {
                Vector2[] averaged = new Vector2[line.Length];
                averaged[0] = line[0];
                averaged[^1] = line[^1];
                for (int i = 1; i < line.Length - 1; i++)
                {
                    averaged[i] = (line[i - 1] + 2 * line[i] + line[i + 1]) / 4;
                }
                line = averaged;
            }
            bool[] keep = new bool[line.Length];
            keep[0] = keep[^1] = true;
            Simplify(line, 0, line.Length - 1, tolerance, keep);
            ImmutableArray<Vector2>.Builder result = ImmutableArray.CreateBuilder<Vector2>();
            for (int i = 0; i < line.Length; i++)
            {
                if (keep[i])
                {
                    result.Add(line[i]);
                }
            }
            return result.ToImmutable();
        }

        private static void Simplify(Vector2[] line, int first, int last, float tolerance, bool[] keep)
        {
            // an explicit stack: a long line must not run the call stack out
            Stack<(int First, int Last)> work = new Stack<(int, int)>();
            work.Push((first, last));
            while (work.Count > 0)
            {
                (int from, int to) = work.Pop();
                float far = -1;
                int at = -1;
                for (int i = from + 1; i < to; i++)
                {
                    float distance = DistanceToSegment(line[i], line[from], line[to]);
                    if (distance > far)
                    {
                        far = distance;
                        at = i;
                    }
                }
                if (at >= 0 && far > tolerance)
                {
                    keep[at] = true;
                    work.Push((from, at));
                    work.Push((at, to));
                }
            }
        }

        /// <summary>
        /// A straight line from the first place to the last: what a stroke with Shift held draws.
        /// </summary>
        public static ImmutableArray<Vector2> Straight(IReadOnlyList<Vector2> points) =>
            points.Count <= 1 ? [.. points] : [points[0], points[^1]];

        /// <summary>
        /// The parts of a line that lie on the map (0 to <paramref name="width"/> by 0 to
        /// <paramref name="height"/> ogrids): where the line leaves the map it is cut at the edge, and a
        /// part that comes back is a line of its own, never joined across the excursion. A line wholly
        /// on the map is returned as it is.
        /// </summary>
        /// <returns>The pieces in order; none when the whole line lies beyond the map.</returns>
        public static IReadOnlyList<ImmutableArray<Vector2>> ClipToMap(ImmutableArray<Vector2> line, float width, float height)
        {
            bool Inside(Vector2 p) => p.X >= 0 && p.X <= width && p.Y >= 0 && p.Y <= height;
            if (line.All(Inside))
            {
                return line.IsDefaultOrEmpty ? [] : [line];
            }
            List<ImmutableArray<Vector2>> pieces = [];
            List<Vector2> current = [];
            void End()
            {
                if (current.Count > 0)
                {
                    pieces.Add([.. current]);
                    current.Clear();
                }
            }
            for (int i = 0; i + 1 < line.Length; i++)
            {
                Vector2 a = line[i];
                Vector2 b = line[i + 1];
                if (!Liang(a, b, width, height, out float t0, out float t1))
                {
                    End();
                    continue;
                }
                Vector2 from = a + (b - a) * t0;
                Vector2 to = a + (b - a) * t1;
                if (t0 > 0 || current.Count == 0 || Vector2.DistanceSquared(current[^1], from) > 1e-8f)
                {
                    End();
                    current.Add(from);
                }
                current.Add(to);
                if (t1 < 1)
                {
                    End();
                }
            }
            End();
            if (line.Length == 1 && Inside(line[0]))
            {
                pieces.Add(line);
            }
            return pieces;
        }

        // the part of the segment a-b inside the rectangle, as t from 0 to 1 along it (Liang and Barsky)
        private static bool Liang(Vector2 a, Vector2 b, float width, float height, out float t0, out float t1)
        {
            t0 = 0;
            t1 = 1;
            Vector2 d = b - a;
            float[] p = [-d.X, d.X, -d.Y, d.Y];
            float[] q = [a.X, width - a.X, a.Y, height - a.Y];
            for (int i = 0; i < 4; i++)
            {
                if (p[i] == 0)
                {
                    if (q[i] < 0)
                    {
                        return false;
                    }
                    continue;
                }
                float t = q[i] / p[i];
                if (p[i] < 0)
                {
                    t0 = Math.Max(t0, t);
                }
                else
                {
                    t1 = Math.Min(t1, t);
                }
            }
            return t0 <= t1;
        }

        #endregion

        #region Erasing
        /// <summary>
        /// What is left of a line after an eraser of a radius passed along a path (a capsule: the
        /// line between two places, <paramref name="radius"/> to both sides), cut where it crossed.
        /// </summary>
        /// <returns>The pieces of line left, none when nothing is; <paramref name="touched"/> says
        /// whether the eraser reached the line at all (then the pieces differ from it).</returns>
        public static IReadOnlyList<ImmutableArray<Vector2>> Cut(ImmutableArray<Vector2> line, Vector2 from, Vector2 to, float radius, out bool touched)
        {
            touched = false;
            if (line.Length == 1)
            {
                if (DistanceToSegment(line[0], from, to) <= radius)
                {
                    touched = true;
                    return [];
                }
                return [line];
            }

            List<ImmutableArray<Vector2>> pieces = [];
            List<Vector2> current = [];
            void Close()
            {
                if (current.Count >= 2 && Length(current) >= Shortest)
                {
                    pieces.Add([.. current]);
                }
                current.Clear();
            }

            for (int i = 0; i + 1 < line.Length; i++)
            {
                Vector2 p = line[i];
                Vector2 q = line[i + 1];
                if (!Crosses(p, q, from, to, radius, out float t0, out float t1))
                {
                    if (current.Count == 0)
                    {
                        current.Add(p);
                    }
                    current.Add(q);
                    continue;
                }
                touched = true;
                if (t0 > 1e-4f)
                {
                    if (current.Count == 0)
                    {
                        current.Add(p);
                    }
                    current.Add(Vector2.Lerp(p, q, t0));
                }
                Close();
                if (t1 < 1 - 1e-4f)
                {
                    current.Add(Vector2.Lerp(p, q, t1));
                    current.Add(q);
                }
            }
            Close();
            if (!touched)
            {
                return [line];
            }
            return pieces;
        }

        /// <summary>
        /// Whether an eraser's path touches a line anywhere.
        /// </summary>
        public static bool Touches(ImmutableArray<Vector2> line, Vector2 from, Vector2 to, float radius)
        {
            if (line.Length == 1)
            {
                return DistanceToSegment(line[0], from, to) <= radius;
            }
            for (int i = 0; i + 1 < line.Length; i++)
            {
                if (Crosses(line[i], line[i + 1], from, to, radius, out _, out _))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Where the segment <paramref name="p"/> to <paramref name="q"/> lies within <paramref name="radius"/>
        /// of the segment <paramref name="a"/> to <paramref name="b"/>, as the parameters of the first
        /// segment from 0 to 1. The set of places within a radius of a segment is convex (a capsule), so
        /// it meets the line in one stretch: the union of its parts (a rectangle and two discs).
        /// </summary>
        public static bool Crosses(Vector2 p, Vector2 q, Vector2 a, Vector2 b, float radius, out float t0, out float t1)
        {
            float first = float.MaxValue;
            float last = float.MinValue;
            t0 = 0;
            t1 = 0;
            Vector2 d = q - p;
            float length2 = d.LengthSquared();
            if (length2 < 1e-12f)
            {
                if (DistanceToSegment(p, a, b) <= radius)
                {
                    t0 = 0;
                    t1 = 1;
                    return true;
                }
                return false;
            }

            void Take(float low, float high)
            {
                low = Math.Max(low, 0);
                high = Math.Min(high, 1);
                if (low <= high)
                {
                    first = Math.Min(first, low);
                    last = Math.Max(last, high);
                }
            }

            foreach (Vector2 centre in (ReadOnlySpan<Vector2>)[a, b])
            {
                Vector2 m = p - centre;
                float qb = 2 * Vector2.Dot(d, m);
                float qc = m.LengthSquared() - radius * radius;
                float discriminant = qb * qb - 4 * length2 * qc;
                if (discriminant >= 0)
                {
                    float root = MathF.Sqrt(discriminant);
                    Take((-qb - root) / (2 * length2), (-qb + root) / (2 * length2));
                }
            }

            Vector2 ab = b - a;
            float abLength = ab.Length();
            if (abLength > 1e-6f)
            {
                Vector2 along = ab / abLength;
                Vector2 across = new Vector2(-along.Y, along.X);
                Vector2 start = p - a;
                // u runs along the eraser's path from 0 to its length, v across it within the radius
                float low = 0;
                float high = 1;
                if (Slab(Vector2.Dot(start, along), Vector2.Dot(d, along), 0, abLength, ref low, ref high)
                    && Slab(Vector2.Dot(start, across), Vector2.Dot(d, across), -radius, radius, ref low, ref high))
                {
                    Take(low, high);
                }
            }
            t0 = first;
            t1 = last;
            return first <= last;
        }

        // clips the parameter range of start + t * step to where it lies between minimum and maximum
        private static bool Slab(float start, float step, float minimum, float maximum, ref float low, ref float high)
        {
            if (MathF.Abs(step) < 1e-9f)
            {
                return start >= minimum && start <= maximum;
            }
            float a = (minimum - start) / step;
            float b = (maximum - start) / step;
            low = Math.Max(low, Math.Min(a, b));
            high = Math.Min(high, Math.Max(a, b));
            return low <= high;
        }

        #endregion

        /// <summary>
        /// How far a place lies from a segment.
        /// </summary>
        public static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float length2 = ab.LengthSquared();
            if (length2 < 1e-12f)
            {
                return Vector2.Distance(point, a);
            }
            float t = Math.Clamp(Vector2.Dot(point - a, ab) / length2, 0, 1);
            return Vector2.Distance(point, a + ab * t);
        }

        /// <summary>
        /// The length of a line through places.
        /// </summary>
        public static float Length(IReadOnlyList<Vector2> line)
        {
            float length = 0;
            for (int i = 1; i < line.Count; i++)
            {
                length += Vector2.Distance(line[i - 1], line[i]);
            }
            return length;
        }
    }
}
