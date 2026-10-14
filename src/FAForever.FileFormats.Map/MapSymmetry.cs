using System.Numerics;

namespace FAForever.FileFormats.Map
{
    /// <summary>
    /// The ways a map can be symmetric, as map makers build them for fair games. A symmetry is fair
    /// when it maps the playable area onto itself: a rectangle has the point symmetry and the two
    /// mirrors along its edges, and both mirrors at once; a square also the quarter turn, the
    /// diagonals, both diagonals and the eight-way symmetry (<see cref="Kaleidoscope"/> of four turns).
    /// The turns for 3 and 5 to 8 players suit maps whose play lies inside a circle.
    /// </summary>
    public enum MapSymmetryKind
    {
        /// <summary>Turned around the centre: half a circle for two players (the point symmetry), a third for three, and so on.</summary>
        Rotational,

        /// <summary>Mirrored from left to right.</summary>
        MirroredLeftRight,

        /// <summary>Mirrored from top to bottom.</summary>
        MirroredTopBottom,

        /// <summary>Mirrored on the diagonal from the top left to the bottom right corner: the top right half onto the bottom left.</summary>
        MirroredDiagonal,

        /// <summary>Mirrored on the diagonal from the top right to the bottom left corner: the top left half onto the bottom right.</summary>
        MirroredOtherDiagonal,

        /// <summary>Mirrored from left to right and from top to bottom: four parts, one per corner.</summary>
        MirroredBothWays,

        /// <summary>Mirrored on both diagonals: four parts, one per edge (a square map).</summary>
        MirroredBothDiagonals,

        /// <summary>
        /// Turned around the centre and mirrored: each of the count's turns is mirrored once more, so
        /// twice as many parts as turns (four turns: the eight-way symmetry of a square). The first
        /// mirror runs from the top to the bottom through the centre, as <see cref="MirroredLeftRight"/>'s.
        /// </summary>
        Kaleidoscope,
    }

    /// <summary>
    /// The rectangle a symmetry maps onto itself, in ogrids: the whole map, or the playable area
    /// when that lies off the map's centre.
    /// </summary>
    public readonly record struct MapSymmetryArea(float X0, float Z0, float X1, float Z1)
    {
        /// <summary>The whole map of that size.</summary>
        public static MapSymmetryArea Whole(float width, float height) => new MapSymmetryArea(0, 0, width, height);

        public float Width => X1 - X0;

        public float Height => Z1 - Z0;

        public Vector2 Middle => new Vector2((X0 + X1) / 2, (Z0 + Z1) / 2);

        /// <summary>Whether it is square, which the diagonals and the turns other than half a circle need.</summary>
        public bool IsSquare => MathF.Abs(Width - Height) < 1e-3f;
    }

    /// <summary>
    /// A symmetry found in a map's markers: its kind and count (see <see cref="MapSymmetry.Transforms(MapSymmetryKind, int, MapSymmetryArea)"/>).
    /// </summary>
    public readonly record struct MapSymmetryMatch(MapSymmetryKind Kind, int Count)
    {
        /// <summary>How many parts it divides the map into.</summary>
        public int Parts => MapSymmetry.PartsOf(Kind, Count);
    }

    /// <summary>
    /// How one part of a symmetric map lies in another: a turn or a mirror of the top view, on the
    /// map's x and z (in ogrids; z grows downwards in the top view, y is up and stays as it is).
    /// </summary>
    /// <remarks>
    /// A point becomes <c>x' = Xx x + Xz z + OffsetX</c>, <c>z' = Zx x + Zz z + OffsetZ</c>.
    /// Orientations follow the convention of <see cref="Matrix4x4.CreateRotationY(float)"/>: a yaw
    /// of <c>θ</c> faces the direction <c>(sin θ, cos θ)</c> on (x, z), and a matrix's rows are
    /// where an object's own x, y and z axes point (<c>v * M</c>), its z axis being where it faces.
    /// </remarks>
    public readonly record struct MapSymmetryTransform(float Xx, float Xz, float Zx, float Zz, float OffsetX, float OffsetZ)
    {
        public static MapSymmetryTransform Identity { get; } = new MapSymmetryTransform(1, 0, 0, 1, 0, 0);

        /// <summary>
        /// A turn by <paramref name="angle"/> radians around (<paramref name="x"/>, <paramref name="z"/>),
        /// clockwise in the top view.
        /// </summary>
        public static MapSymmetryTransform Rotation(double angle, float x, float z)
        {
            (double sin, double cos) = Math.SinCos(angle);
            // exact at quarter turns, so that a point in the middle of an ogrid stays there
            float s = (float)Math.Round(sin, 12);
            float c = (float)Math.Round(cos, 12);
            return new MapSymmetryTransform(c, -s, s, c, x - c * x + s * z, z - s * x - c * z);
        }

        /// <summary>
        /// A mirror on the line through (<paramref name="x"/>, <paramref name="z"/>) that runs
        /// <paramref name="angle"/> radians clockwise from straight up in the top view (0: from the top
        /// to the bottom, so left and right change places).
        /// </summary>
        public static MapSymmetryTransform Mirror(double angle, float x, float z)
        {
            (double sin, double cos) = Math.SinCos(angle);
            // the line's direction (sin, -cos): straight up is -z
            double dx = sin;
            double dz = -cos;
            float xx = (float)Math.Round(2 * dx * dx - 1, 12);
            float xz = (float)Math.Round(2 * dx * dz, 12);
            float zz = (float)Math.Round(2 * dz * dz - 1, 12);
            return new MapSymmetryTransform(xx, xz, xz, zz, x - xx * x - xz * z, z - xz * x - zz * z);
        }

        /// <summary>
        /// This transform, then <paramref name="next"/>: a point goes where <paramref name="next"/>
        /// takes this one's image.
        /// </summary>
        public MapSymmetryTransform Then(MapSymmetryTransform next) => new MapSymmetryTransform(
            next.Xx * Xx + next.Xz * Zx,
            next.Xx * Xz + next.Xz * Zz,
            next.Zx * Xx + next.Zz * Zx,
            next.Zx * Xz + next.Zz * Zz,
            next.Xx * OffsetX + next.Xz * OffsetZ + next.OffsetX,
            next.Zx * OffsetX + next.Zz * OffsetZ + next.OffsetZ);

        /// <summary>
        /// Whether it mirrors: what it maps is seen from the other side.
        /// </summary>
        public bool IsMirror => Xx * Zz - Xz * Zx < 0;

        /// <summary>
        /// The transform that undoes this one: where a point of this part came from.
        /// </summary>
        public MapSymmetryTransform Inverse()
        {
            float determinant = Xx * Zz - Xz * Zx;
            float xx = Zz / determinant;
            float xz = -Xz / determinant;
            float zx = -Zx / determinant;
            float zz = Xx / determinant;
            return new MapSymmetryTransform(xx, xz, zx, zz, -(xx * OffsetX + xz * OffsetZ), -(zx * OffsetX + zz * OffsetZ));
        }

        /// <summary>
        /// The angle in radians, clockwise in the top view, of the turn this transform makes; for a
        /// mirror, the turn that follows a flip of x (<c>x → -x</c>): the linear part of every
        /// transform is the turn of <see cref="Rotation"/> by this angle, after that flip when it
        /// <see cref="IsMirror"/>.
        /// </summary>
        public float TurnAngle => IsMirror ? MathF.Atan2(-Zx, -Xx) : MathF.Atan2(Zx, Xx);

        public Vector2 Apply(Vector2 point) => new Vector2(Xx * point.X + Xz * point.Y + OffsetX, Zx * point.X + Zz * point.Y + OffsetZ);

        /// <summary>
        /// A position, its height kept.
        /// </summary>
        public Vector3 Apply(Vector3 position)
        {
            Vector2 at = Apply(new Vector2(position.X, position.Z));
            return new Vector3(at.X, position.Y, at.Y);
        }

        /// <summary>
        /// A direction on (x, z): turned or mirrored, not moved.
        /// </summary>
        public Vector2 ApplyDirection(Vector2 direction) => new Vector2(Xx * direction.X + Xz * direction.Y, Zx * direction.X + Zz * direction.Y);

        /// <summary>
        /// The yaw (radians, about the up axis) of the image of something facing
        /// <paramref name="yaw"/>, from -π to π.
        /// </summary>
        public float ApplyYaw(float yaw)
        {
            (float sin, float cos) = MathF.SinCos(yaw);
            Vector2 facing = ApplyDirection(new Vector2(sin, cos));
            return MathF.Atan2(facing.X, facing.Y);
        }

        /// <summary>
        /// The orientation of the image of something oriented by <paramref name="rotation"/> (rows:
        /// its own axes; the translation is left as it is). It faces where the image of its facing
        /// points, and stays a rotation: a mirror turns the object's own x axis around rather than
        /// mirror the object, which a prop or unit cannot be.
        /// </summary>
        public Matrix4x4 Apply(Matrix4x4 rotation)
        {
            Vector3 x = Axis(new Vector3(rotation.M11, rotation.M12, rotation.M13));
            Vector3 y = Axis(new Vector3(rotation.M21, rotation.M22, rotation.M23));
            Vector3 z = Axis(new Vector3(rotation.M31, rotation.M32, rotation.M33));
            if (IsMirror)
            {
                x = -x;
            }
            return new Matrix4x4(
                x.X, x.Y, x.Z, rotation.M14,
                y.X, y.Y, y.Z, rotation.M24,
                z.X, z.Y, z.Z, rotation.M34,
                rotation.M41, rotation.M42, rotation.M43, rotation.M44);
        }

        private Vector3 Axis(Vector3 axis)
        {
            Vector2 turned = ApplyDirection(new Vector2(axis.X, axis.Z));
            return new Vector3(turned.X, axis.Y, turned.Y);
        }
    }

    /// <summary>
    /// Finds the symmetry of a map's markers, and mirrors places and orientations by a symmetry.
    /// </summary>
    public static class MapSymmetry
    {
        /// <summary>
        /// The most players a rotational symmetry is made for, and the most turns of a <see cref="MapSymmetryKind.Kaleidoscope"/>.
        /// </summary>
        public const int MaximumCount = 8;

        /// <summary>
        /// The fewest turns of a <see cref="MapSymmetryKind.Kaleidoscope"/> (two is <see cref="MapSymmetryKind.MirroredBothWays"/>).
        /// </summary>
        public const int KaleidoscopeMinimumCount = 2;

        /// <summary>
        /// Whether the kind takes a count: the turns of a rotational symmetry or a kaleidoscope.
        /// </summary>
        public static bool HasCount(MapSymmetryKind kind) => kind is MapSymmetryKind.Rotational or MapSymmetryKind.Kaleidoscope;

        /// <summary>
        /// How many parts a symmetry divides a map into.
        /// </summary>
        public static int PartsOf(MapSymmetryKind kind, int count) => kind switch
        {
            MapSymmetryKind.Rotational => count,
            MapSymmetryKind.Kaleidoscope => 2 * count,
            MapSymmetryKind.MirroredBothWays or MapSymmetryKind.MirroredBothDiagonals => 4,
            _ => 2,
        };

        /// <summary>
        /// The symmetry with the most parts under which every position has a counterpart within the
        /// tolerance, or null when there is none. Pass the start positions and the resource markers:
        /// they decide whether a map is fair. The diagonals and the turns other than half a circle are
        /// only looked for on a square area.
        /// </summary>
        public static MapSymmetryMatch? Detect(MapSymmetryArea area, IReadOnlyList<Vector3> positions, float tolerance = 2)
        {
            if (positions.Count == 0)
            {
                return null;
            }
            foreach (MapSymmetryMatch candidate in Candidates(area.IsSquare))
            {
                IReadOnlyList<MapSymmetryTransform> transforms = Transforms(candidate.Kind, candidate.Count, area);
                if (transforms.Skip(1).All(transform => positions.All(position => HasCounterpart(transform.Apply(new Vector2(position.X, position.Z)), positions, tolerance))))
                {
                    return candidate;
                }
            }
            return null;
        }

        /// <summary>
        /// <see cref="Detect(MapSymmetryArea, IReadOnlyList{Vector3}, float)"/> on the whole map.
        /// </summary>
        public static MapSymmetryMatch? Detect(int width, int height, IReadOnlyList<Vector3> positions, float tolerance = 2) =>
            Detect(MapSymmetryArea.Whole(width, height), positions, tolerance);

        /// <summary>
        /// The symmetries <see cref="Detect(MapSymmetryArea, IReadOnlyList{Vector3}, float)"/> tries, those with the most parts first.
        /// </summary>
        private static IEnumerable<MapSymmetryMatch> Candidates(bool square)
        {
            List<MapSymmetryMatch> candidates = [];
            if (square)
            {
                for (int count = MaximumCount; count >= 3; count--)
                {
                    candidates.Add(new MapSymmetryMatch(MapSymmetryKind.Kaleidoscope, count));
                }
                for (int count = MaximumCount; count >= 3; count--)
                {
                    candidates.Add(new MapSymmetryMatch(MapSymmetryKind.Rotational, count));
                }
            }
            candidates.Add(new MapSymmetryMatch(MapSymmetryKind.MirroredBothWays, 2));
            if (square)
            {
                candidates.Add(new MapSymmetryMatch(MapSymmetryKind.MirroredBothDiagonals, 2));
            }
            candidates.Add(new MapSymmetryMatch(MapSymmetryKind.Rotational, 2));
            candidates.Add(new MapSymmetryMatch(MapSymmetryKind.MirroredLeftRight, 2));
            candidates.Add(new MapSymmetryMatch(MapSymmetryKind.MirroredTopBottom, 2));
            if (square)
            {
                candidates.Add(new MapSymmetryMatch(MapSymmetryKind.MirroredDiagonal, 2));
                candidates.Add(new MapSymmetryMatch(MapSymmetryKind.MirroredOtherDiagonal, 2));
            }
            // stable: of the same number of parts, the order above
            return candidates.OrderByDescending(candidate => candidate.Parts);
        }

        /// <summary>
        /// The parts a symmetry divides a map into, each the image of the first: for a rotational
        /// symmetry the first transform is the identity and the others turn around the middle by a
        /// <paramref name="count"/>th of a circle each, clockwise in the top view; a mirror has the
        /// identity and the mirror; both mirrors the identity, the mirror on the first line, half a
        /// turn and the mirror on the second; a kaleidoscope each turn followed by the turn after a
        /// mirror on its first line.
        /// </summary>
        /// <param name="count">The players of a rotational symmetry, 2 to <see cref="MaximumCount"/>,
        /// or the turns of a kaleidoscope, 2 to <see cref="MaximumCount"/>; a mirror leaves it out.</param>
        /// <exception cref="ArgumentOutOfRangeException">A count outside those.</exception>
        public static IReadOnlyList<MapSymmetryTransform> Transforms(MapSymmetryKind kind, int count, MapSymmetryArea area)
        {
            Vector2 middle = area.Middle;
            switch (kind)
            {
                case MapSymmetryKind.Rotational:
                    if (count < 2 || count > MaximumCount)
                    {
                        throw new ArgumentOutOfRangeException(nameof(count), count, $"A rotational symmetry is for 2 to {MaximumCount} players");
                    }
                    MapSymmetryTransform[] turns = new MapSymmetryTransform[count];
                    for (int i = 0; i < count; i++)
                    {
                        turns[i] = MapSymmetryTransform.Rotation(2 * Math.PI * i / count, middle.X, middle.Y);
                    }
                    return turns;
                case MapSymmetryKind.MirroredBothWays:
                    return Dihedral(2, 0, middle);
                case MapSymmetryKind.MirroredBothDiagonals:
                    return Dihedral(2, Math.PI / 4, middle);
                case MapSymmetryKind.Kaleidoscope:
                    if (count < KaleidoscopeMinimumCount || count > MaximumCount)
                    {
                        throw new ArgumentOutOfRangeException(nameof(count), count, $"A kaleidoscope has {KaleidoscopeMinimumCount} to {MaximumCount} turns");
                    }
                    return Dihedral(count, 0, middle);
                default:
                    return [MapSymmetryTransform.Identity, Mirror(kind, area)];
            }
        }

        /// <summary>
        /// <see cref="Transforms(MapSymmetryKind, int, MapSymmetryArea)"/> around the whole map.
        /// </summary>
        public static IReadOnlyList<MapSymmetryTransform> Transforms(MapSymmetryKind kind, int count, float width, float height) =>
            Transforms(kind, count, MapSymmetryArea.Whole(width, height));

        /// <summary>
        /// The turns by a <paramref name="count"/>th of a circle, each followed by its image in the
        /// mirror on the line at <paramref name="angle"/>.
        /// </summary>
        private static MapSymmetryTransform[] Dihedral(int count, double angle, Vector2 middle)
        {
            MapSymmetryTransform mirror = MapSymmetryTransform.Mirror(angle, middle.X, middle.Y);
            MapSymmetryTransform[] parts = new MapSymmetryTransform[2 * count];
            for (int i = 0; i < count; i++)
            {
                MapSymmetryTransform turn = MapSymmetryTransform.Rotation(2 * Math.PI * i / count, middle.X, middle.Y);
                parts[2 * i] = turn;
                parts[2 * i + 1] = Exact(mirror.Then(turn));
            }
            return parts;
        }

        /// <summary>
        /// A transform made of others with its linear part rounded, so that quarter turns and the
        /// mirrors of a square stay exact (and equal to <see cref="Transforms(MapSymmetryKind, int, MapSymmetryArea)"/>'s
        /// mirrors: a negative zero becomes zero).
        /// </summary>
        private static MapSymmetryTransform Exact(MapSymmetryTransform transform)
        {
            static float Round(float value) => (MathF.Abs(value - MathF.Round(value)) < 1e-6f ? MathF.Round(value) : value) + 0f;
            return new MapSymmetryTransform(Round(transform.Xx), Round(transform.Xz), Round(transform.Zx), Round(transform.Zz), transform.OffsetX + 0f, transform.OffsetZ + 0f);
        }

        /// <summary>
        /// Where a point (x, z in ogrids) lies in every part of the map, itself first.
        /// </summary>
        public static Vector2[] Images(MapSymmetryKind kind, int count, float width, float height, Vector2 point) =>
            [.. Transforms(kind, count, width, height).Select(transform => transform.Apply(point))];

        /// <summary>
        /// The lines between the parts of a symmetry, for guides in a view, as pairs of points
        /// (x, z in ogrids) inside the area: a mirror's axis; a rotational symmetry's ray from the
        /// middle to the edge per part, the first straight up (towards z = 0), the others turned as
        /// the parts are; both mirrors' and a kaleidoscope's lines through the middle, one per mirror.
        /// The point symmetry has none: any line through its middle parts it alike, so its guide is
        /// that point (<see cref="Point(MapSymmetryKind, int, MapSymmetryArea)"/>).
        /// </summary>
        public static (Vector2 From, Vector2 To)[] Guides(MapSymmetryKind kind, int count, MapSymmetryArea area)
        {
            Vector2 middle = area.Middle;
            switch (kind)
            {
                case MapSymmetryKind.Rotational when count == 2:
                    return [];
                case MapSymmetryKind.MirroredLeftRight:
                    return [(new Vector2(middle.X, area.Z0), new Vector2(middle.X, area.Z1))];
                case MapSymmetryKind.MirroredTopBottom:
                    return [(new Vector2(area.X0, middle.Y), new Vector2(area.X1, middle.Y))];
                case MapSymmetryKind.MirroredDiagonal:
                    float side = Math.Min(area.Width, area.Height);
                    return [(new Vector2(area.X0, area.Z0), new Vector2(area.X0 + side, area.Z0 + side))];
                case MapSymmetryKind.MirroredOtherDiagonal:
                    return [(new Vector2(area.X1, area.Z0), new Vector2(area.X0, area.Z1))];
                case MapSymmetryKind.MirroredBothWays:
                    return Lines(2, 0, area);
                case MapSymmetryKind.MirroredBothDiagonals:
                    return Lines(2, Math.PI / 4, area);
                case MapSymmetryKind.Kaleidoscope:
                    return Lines(count, 0, area);
            }
            return [.. Transforms(kind, count, area).Select(transform =>
            {
                Vector2 direction = transform.ApplyDirection(new Vector2(0, -1));
                return (middle, middle + direction * ToEdge(middle, direction, area));
            })];
        }

        /// <summary>
        /// <see cref="Guides(MapSymmetryKind, int, MapSymmetryArea)"/> on the whole map.
        /// </summary>
        public static (Vector2 From, Vector2 To)[] Guides(MapSymmetryKind kind, int count, float width, float height) =>
            Guides(kind, count, MapSymmetryArea.Whole(width, height));

        /// <summary>
        /// The point the point symmetry (rotational for two) turns the map about, its guide in a view:
        /// the area's middle. Null for every other kind, whose guides are lines.
        /// </summary>
        public static Vector2? Point(MapSymmetryKind kind, int count, MapSymmetryArea area) =>
            kind == MapSymmetryKind.Rotational && count == 2 ? area.Middle : null;

        /// <summary>
        /// <see cref="Point(MapSymmetryKind, int, MapSymmetryArea)"/> on the whole map.
        /// </summary>
        public static Vector2? Point(MapSymmetryKind kind, int count, float width, float height) =>
            Point(kind, count, MapSymmetryArea.Whole(width, height));

        /// <summary>
        /// The mirrors' lines of a dihedral symmetry, through the middle from edge to edge: one per
        /// half of a <paramref name="count"/>th of a circle, the first at <paramref name="angle"/>.
        /// </summary>
        private static (Vector2 From, Vector2 To)[] Lines(int count, double angle, MapSymmetryArea area)
        {
            Vector2 middle = area.Middle;
            (Vector2, Vector2)[] lines = new (Vector2, Vector2)[count];
            for (int i = 0; i < count; i++)
            {
                (double sin, double cos) = Math.SinCos(angle + Math.PI * i / count);
                Vector2 direction = new Vector2((float)Math.Round(sin, 12), (float)Math.Round(-cos, 12));
                lines[i] = (middle - direction * ToEdge(middle, -direction, area), middle + direction * ToEdge(middle, direction, area));
            }
            return lines;
        }

        /// <summary>
        /// How far a ray from inside the area goes before it leaves it.
        /// </summary>
        private static float ToEdge(Vector2 from, Vector2 direction, MapSymmetryArea area)
        {
            float distance = float.PositiveInfinity;
            if (direction.X > 1e-6f)
            {
                distance = Math.Min(distance, (area.X1 - from.X) / direction.X);
            }
            if (direction.X < -1e-6f)
            {
                distance = Math.Min(distance, (area.X0 - from.X) / direction.X);
            }
            if (direction.Y > 1e-6f)
            {
                distance = Math.Min(distance, (area.Z1 - from.Y) / direction.Y);
            }
            if (direction.Y < -1e-6f)
            {
                distance = Math.Min(distance, (area.Z0 - from.Y) / direction.Y);
            }
            return float.IsPositiveInfinity(distance) ? 0 : distance;
        }

        private static MapSymmetryTransform Mirror(MapSymmetryKind kind, MapSymmetryArea area) => kind switch
        {
            MapSymmetryKind.MirroredLeftRight => new MapSymmetryTransform(-1, 0, 0, 1, area.X0 + area.X1, 0),
            MapSymmetryKind.MirroredTopBottom => new MapSymmetryTransform(1, 0, 0, -1, 0, area.Z0 + area.Z1),
            // from the area's top left corner at 45 degrees, its bottom right on a square
            MapSymmetryKind.MirroredDiagonal => new MapSymmetryTransform(0, 1, 1, 0, area.X0 - area.Z0, area.Z0 - area.X0),
            // from the area's top right corner to its bottom left
            MapSymmetryKind.MirroredOtherDiagonal => new MapSymmetryTransform(0, -1, -1, 0, area.X1 + area.Z0, area.Z1 + area.X0),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a mirror"),
        };

        private static bool HasCounterpart(Vector2 target, IReadOnlyList<Vector3> positions, float tolerance) =>
            positions.Any(other => Vector2.Distance(target, new Vector2(other.X, other.Z)) <= tolerance);
    }
}
