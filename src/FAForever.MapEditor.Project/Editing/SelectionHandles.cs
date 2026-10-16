using System.Numerics;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// What a handle of the selection does when dragged.
    /// </summary>
    public enum HandleKind
    {
        None,

        /// <summary>
        /// The square in the middle: moves the selection with the mouse.
        /// </summary>
        Move,

        /// <summary>
        /// The arrow along x: moves the selection along x only.
        /// </summary>
        MoveX,

        /// <summary>
        /// The arrow along z: moves the selection along z only.
        /// </summary>
        MoveZ,

        /// <summary>
        /// The ring: turns the selection about the up axis.
        /// </summary>
        Turn,

        /// <summary>
        /// A corner or side of the selection's box: scales it.
        /// </summary>
        Scale,
    }

    /// <summary>
    /// One handle of the selection. A scale handle names its side of the box in the box's own axes:
    /// <paramref name="Sx"/> and <paramref name="Sz"/> are each -1, 0 or 1, a corner has both.
    /// </summary>
    public readonly record struct HandlePart(HandleKind Kind, int Sx = 0, int Sz = 0)
    {
        public bool IsNone => Kind == HandleKind.None;

        /// <summary>
        /// Whether it is a corner of the box, which scales both sides alike.
        /// </summary>
        public bool IsCorner => Kind == HandleKind.Scale && Sx != 0 && Sz != 0;

        /// <summary>
        /// For automation and the log: <c>move</c>, <c>moveX</c>, <c>turn</c>, <c>scale 1,-1</c>.
        /// </summary>
        public override string ToString() => Kind switch
        {
            HandleKind.None => "none",
            HandleKind.Scale => $"scale {Sx},{Sz}",
            _ => char.ToLowerInvariant(Kind.ToString()[0]) + Kind.ToString()[1..],
        };
    }

    /// <summary>
    /// Which way a move may go: anywhere, or along one axis of the map (holding X or Z).
    /// </summary>
    public enum HandleAxis
    {
        Both,
        X,
        Z,
    }

    /// <summary>
    /// The box the handles of a selection sit on (user story F-11): its centre (the middle of the
    /// things' bounds, at their mean height), its turn about the up axis and half its size along its
    /// own axes, and which handles the selection has.
    /// </summary>
    /// <param name="Heading">The box's turn from +x towards +z in radians: a selection of decals that
    /// all face one way has their heading, so its sides are theirs; else 0.</param>
    /// <param name="Turns">Whether the ring shows: something turns, or several things orbit the centre.</param>
    /// <param name="Scales">Whether scale handles show: props, decals or areas are selected.</param>
    /// <param name="ScalesSides">Whether the sides scale on their own (decals and areas have sides;
    /// props scale alike along every axis, so their box has corners only).</param>
    public sealed record SelectionFrame(Vector3 Centre, float Heading, Vector2 HalfSize, bool Turns, bool Scales, bool ScalesSides)
    {
        /// <summary>
        /// The centre on the map, without its height.
        /// </summary>
        public Vector2 Middle => new Vector2(Centre.X, Centre.Z);

        /// <summary>
        /// The box of the things, or null when none of them exists.
        /// </summary>
        public static SelectionFrame? Of(MapDocument document, IEnumerable<ObjectRef> things)
        {
            List<(ObjectRef Thing, object Found)> found = [];
            foreach (ObjectRef thing in things)
            {
                if (document.Find(thing) is object item)
                {
                    found.Add((thing, item));
                }
            }
            if (found.Count == 0)
            {
                return null;
            }
            // decals that all face one way give the box their heading
            float heading = 0;
            if (found.All(item => item.Found is Decal) && found.Select(item => ((Decal)item.Found).Rotation.Y).ToArray() is float[] headings
                && headings.All(h => MathF.Abs(MathF.IEEERemainder(h - headings[0], 2 * MathF.PI)) < 1e-4f))
            {
                heading = headings[0];
            }
            (float sin, float cos) = MathF.SinCos(heading);
            Vector2 low = new Vector2(float.MaxValue);
            Vector2 high = new Vector2(float.MinValue);
            float height = 0;
            foreach ((ObjectRef thing, object _) in found)
            {
                Vector2[] outline = document.OutlineOf(thing);
                Vector3 place = document.PlaceOf(thing)!.Value;
                height += place.Y;
                IEnumerable<Vector2> points = outline.Length > 0 ? outline : [new Vector2(place.X, place.Z)];
                foreach (Vector2 point in points)
                {
                    // in the box's own axes
                    Vector2 local = new Vector2(point.X * cos + point.Y * sin, -point.X * sin + point.Y * cos);
                    low = Vector2.Min(low, local);
                    high = Vector2.Max(high, local);
                }
            }
            Vector2 middle = (low + high) / 2;
            Vector2 centre = new Vector2(middle.X * cos - middle.Y * sin, middle.X * sin + middle.Y * cos);
            bool scales = found.Any(item => item.Found is Prop or Decal or Area);
            bool sides = found.Any(item => item.Found is Decal or Area);
            bool turns = found.Count > 1 || found.Any(item => item.Found is not Area);
            return new SelectionFrame(new Vector3(centre.X, height / found.Count, centre.Y), heading, (high - low) / 2, turns, scales, sides);
        }

        /// <summary>
        /// A place of the map in the box's own axes, from its centre.
        /// </summary>
        public Vector2 ToLocal(Vector2 place)
        {
            (float sin, float cos) = MathF.SinCos(Heading);
            Vector2 d = place - Middle;
            return new Vector2(d.X * cos + d.Y * sin, -d.X * sin + d.Y * cos);
        }

        /// <summary>
        /// A point in the box's own axes as a place of the map: the reverse of <see cref="ToLocal"/>.
        /// </summary>
        public Vector2 FromLocal(Vector2 local)
        {
            (float sin, float cos) = MathF.SinCos(Heading);
            return Middle + new Vector2(local.X * cos - local.Y * sin, local.X * sin + local.Y * cos);
        }
    }

    /// <summary>
    /// Where the handles of a selection lie on the map for a view, so that they keep their size on
    /// screen: the sizes are pixels, made ogrids with how large a pixel is at the centre. The views
    /// draw them and the select tool finds the one under the mouse with the same layout.
    /// </summary>
    public sealed class HandleLayout
    {
        /// <summary>
        /// Half the side of the move square, in pixels.
        /// </summary>
        public const float MoveHalf = 6;

        /// <summary>
        /// Half the side of a scale square, in pixels.
        /// </summary>
        public const float ScaleHalf = 5;

        /// <summary>
        /// How far from a handle, in pixels, the mouse still takes it.
        /// </summary>
        public const float Reach = 6;

        // the box is at least this large, so the handles never lie on each other
        private const float SmallestHalf = 36;
        private const float RingGap = 18;
        private const float SmallestRing = 60;
        private const float ArrowStart = 10;
        private const float LongestArrow = 80;
        private const int RingSegments = 64;

        private HandleLayout(SelectionFrame frame, float ogridsPerPixel)
        {
            Frame = frame;
            OgridsPerPixel = ogridsPerPixel;
            Vector2 half = Vector2.Max(frame.HalfSize, new Vector2(SmallestHalf * ogridsPerPixel));
            Half = half;
            RingRadius = MathF.Max(half.Length() + RingGap * ogridsPerPixel, SmallestRing * ogridsPerPixel);
            float arrow = Math.Clamp(MathF.Min(half.X, half.Y) - 10 * ogridsPerPixel, 28 * ogridsPerPixel, LongestArrow * ogridsPerPixel);
            ArrowX = (At(frame.Middle + new Vector2(ArrowStart * ogridsPerPixel, 0)), At(frame.Middle + new Vector2(arrow, 0)));
            // z along the top view's up, north
            ArrowZ = (At(frame.Middle - new Vector2(0, ArrowStart * ogridsPerPixel)), At(frame.Middle - new Vector2(0, arrow)));
            List<(HandlePart, Vector3)> scales = [];
            if (frame.Scales)
            {
                foreach ((int sx, int sz) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
                {
                    scales.Add((new HandlePart(HandleKind.Scale, sx, sz), At(frame.FromLocal(new Vector2(sx * half.X, sz * half.Y)))));
                }
                if (frame.ScalesSides)
                {
                    foreach ((int sx, int sz) in new[] { (0, -1), (1, 0), (0, 1), (-1, 0) })
                    {
                        scales.Add((new HandlePart(HandleKind.Scale, sx, sz), At(frame.FromLocal(new Vector2(sx * half.X, sz * half.Y)))));
                    }
                }
            }
            Scales = scales;
        }

        public SelectionFrame Frame { get; }

        public float OgridsPerPixel { get; }

        /// <summary>
        /// Half the box's size in ogrids, at least <see cref="SmallestHalf"/> pixels.
        /// </summary>
        public Vector2 Half { get; }

        public Vector3 Centre => Frame.Centre;

        /// <summary>
        /// The ring's radius in ogrids.
        /// </summary>
        public float RingRadius { get; }

        /// <summary>
        /// The arrow along x: where it starts and its tip.
        /// </summary>
        public (Vector3 From, Vector3 To) ArrowX { get; }

        /// <summary>
        /// The arrow along z (towards -z, up in the top view).
        /// </summary>
        public (Vector3 From, Vector3 To) ArrowZ { get; }

        /// <summary>
        /// The scale squares with their places: the corners, then the sides.
        /// </summary>
        public IReadOnlyList<(HandlePart Part, Vector3 Place)> Scales { get; }

        /// <summary>
        /// The corners of the box, in order around it.
        /// </summary>
        public Vector3[] Box => [.. new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) }.Select(c => At(Frame.FromLocal(new Vector2(c.Item1 * Half.X, c.Item2 * Half.Y))))];

        /// <summary>
        /// Points around the ring, the first not repeated at the end.
        /// </summary>
        public Vector3[] Ring()
        {
            Vector3[] points = new Vector3[RingSegments];
            for (int i = 0; i < RingSegments; i++)
            {
                (float sin, float cos) = MathF.SinCos(2 * MathF.PI * i / RingSegments);
                points[i] = At(Frame.Middle + new Vector2(cos, sin) * RingRadius);
            }
            return points;
        }

        public static HandleLayout Of(SelectionFrame frame, float ogridsPerPixel) => new HandleLayout(frame, MathF.Max(ogridsPerPixel, 1e-6f));

        /// <summary>
        /// The handle under the mouse, or none: the move square first, then the arrows, the scale
        /// squares and the ring, each within <see cref="Reach"/> pixels of what is drawn.
        /// </summary>
        /// <param name="mouse">The mouse in the view's pixels.</param>
        /// <param name="toScreen">Where a place shows in the view's pixels; null behind the camera.</param>
        public HandlePart HitTest(Vector2 mouse, Func<Vector3, Vector2?> toScreen)
        {
            if (toScreen(Centre) is Vector2 centre && Square(mouse, centre, MoveHalf + 2))
            {
                return new HandlePart(HandleKind.Move);
            }
            if (Near(mouse, ArrowX, toScreen))
            {
                return new HandlePart(HandleKind.MoveX);
            }
            if (Near(mouse, ArrowZ, toScreen))
            {
                return new HandlePart(HandleKind.MoveZ);
            }
            foreach ((HandlePart part, Vector3 place) in Scales)
            {
                if (toScreen(place) is Vector2 at && Square(mouse, at, ScaleHalf + 2))
                {
                    return part;
                }
            }
            if (Frame.Turns)
            {
                Vector3[] ring = Ring();
                for (int i = 0; i < ring.Length; i++)
                {
                    if (Near(mouse, (ring[i], ring[(i + 1) % ring.Length]), toScreen))
                    {
                        return new HandlePart(HandleKind.Turn);
                    }
                }
            }
            return default;
        }

        private Vector3 At(Vector2 place) => new Vector3(place.X, Frame.Centre.Y, place.Y);

        private static bool Square(Vector2 mouse, Vector2 at, float half) =>
            MathF.Abs(mouse.X - at.X) <= half && MathF.Abs(mouse.Y - at.Y) <= half;

        private static bool Near(Vector2 mouse, (Vector3 From, Vector3 To) segment, Func<Vector3, Vector2?> toScreen) =>
            toScreen(segment.From) is Vector2 a && toScreen(segment.To) is Vector2 b && HandleMaths.DistanceToSegment(mouse, a, b) <= Reach;
    }

    /// <summary>
    /// What dragging a handle does, in numbers: how far a move goes (along one axis, snapped to a
    /// grid), how far a turn turns (snapped to steps), how much a scale scales.
    /// </summary>
    public static class HandleMaths
    {
        /// <summary>
        /// The offset a move by <paramref name="delta"/> gives: along one axis only when one is
        /// chosen, and with a snap step above 0 such that the centre lands on that grid.
        /// </summary>
        /// <param name="centre">Where the moved thing's centre was before the move.</param>
        public static Vector2 MoveDelta(Vector2 centre, Vector2 delta, HandleAxis axis, float snap)
        {
            Vector2 moved = axis switch
            {
                HandleAxis.X => new Vector2(delta.X, 0),
                HandleAxis.Z => new Vector2(0, delta.Y),
                _ => delta,
            };
            if (snap > 0)
            {
                float x = axis == HandleAxis.Z ? 0 : MathF.Round((centre.X + moved.X) / snap) * snap - centre.X;
                float z = axis == HandleAxis.X ? 0 : MathF.Round((centre.Y + moved.Y) / snap) * snap - centre.Y;
                moved = new Vector2(x, z);
            }
            return moved;
        }

        /// <summary>
        /// How far the mouse turned about a centre from one place to another, from +x towards +z
        /// (clockwise in the top view), in radians between -pi and pi; rounded to steps of
        /// <paramref name="snap"/> radians when above 0.
        /// </summary>
        public static float TurnAngle(Vector2 centre, Vector2 from, Vector2 to, float snap)
        {
            Vector2 a = from - centre;
            Vector2 b = to - centre;
            if (a.LengthSquared() < 1e-12f || b.LengthSquared() < 1e-12f)
            {
                return 0;
            }
            float angle = MathF.Atan2(a.X * b.Y - a.Y * b.X, a.X * b.X + a.Y * b.Y);
            return snap > 0 ? MathF.Round(angle / snap) * snap : angle;
        }

        /// <summary>
        /// How much dragging a scale handle from one place to another scales, along the box's own
        /// axes (x, z): a side by how much further from the centre the mouse is along that side's
        /// axis, the other axis staying; a corner both alike, by the distance from the centre. With
        /// <paramref name="snap"/> above 0 the factor is rounded to its steps; it is never below a hundredth.
        /// </summary>
        public static Vector2 ScaleFactors(SelectionFrame frame, HandlePart part, Vector2 from, Vector2 to, float snap)
        {
            Vector2 a = frame.ToLocal(from);
            Vector2 b = frame.ToLocal(to);
            float Factor(float start, float now) => MathF.Abs(start) < 1e-6f ? 1 : now / start;
            float Snapped(float factor)
            {
                factor = snap > 0 ? MathF.Round(factor / snap) * snap : factor;
                return MathF.Max(factor, 0.01f);
            }
            if (part.IsCorner)
            {
                // along the corner's diagonal
                Vector2 diagonal = Vector2.Normalize(new Vector2(part.Sx, part.Sz));
                float factor = Snapped(Factor(Vector2.Dot(a, diagonal), Vector2.Dot(b, diagonal)));
                return new Vector2(factor, factor);
            }
            if (part.Sx != 0)
            {
                return new Vector2(Snapped(Factor(a.X, b.X)), 1);
            }
            if (part.Sz != 0)
            {
                return new Vector2(1, Snapped(Factor(a.Y, b.Y)));
            }
            return Vector2.One;
        }

        /// <summary>
        /// Where a ray meets the level plane at a height, as a place (x, z); null when it runs
        /// along the plane or away from it.
        /// </summary>
        public static Vector2? PlaneHit(Vector3 origin, Vector3 direction, float height)
        {
            if (MathF.Abs(direction.Y) < 1e-6f)
            {
                return null;
            }
            float t = (height - origin.Y) / direction.Y;
            if (t <= 0)
            {
                return null;
            }
            Vector3 hit = origin + direction * t;
            return new Vector2(hit.X, hit.Z);
        }

        /// <summary>
        /// The distance from a point to a segment.
        /// </summary>
        public static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float length = ab.LengthSquared();
            float t = length < 1e-12f ? 0 : Math.Clamp(Vector2.Dot(point - a, ab) / length, 0, 1);
            return Vector2.Distance(point, a + ab * t);
        }
    }
}
