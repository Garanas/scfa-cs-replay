using System.Numerics;

namespace FAForever.FileFormats.Map
{
    /// <summary>
    /// The ways a map can be symmetric, as map makers build them for fair games.
    /// </summary>
    public enum MapSymmetryKind
    {
        /// <summary>Turned around the centre: half a circle for two players, a third for three, and so on (<see cref="MapSymmetry.Find"/> looks for half a circle).</summary>
        Rotational,

        /// <summary>Mirrored from left to right.</summary>
        MirroredLeftRight,

        /// <summary>Mirrored from top to bottom.</summary>
        MirroredTopBottom,

        /// <summary>Mirrored on the diagonal from the top left to the bottom right corner.</summary>
        MirroredDiagonal,

        /// <summary>Mirrored on the diagonal from the top right to the bottom left corner.</summary>
        MirroredOtherDiagonal,
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
        /// Whether it mirrors: what it maps is seen from the other side.
        /// </summary>
        public bool IsMirror => Xx * Zz - Xz * Zx < 0;

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
        /// The first symmetry under which every position has a counterpart within the tolerance,
        /// or null when there is none. Pass the start positions and the resource markers: they decide
        /// whether a map is fair.
        /// </summary>
        public static MapSymmetryKind? Find(int width, int height, IReadOnlyList<Vector3> positions, float tolerance = 2)
        {
            if (positions.Count == 0)
            {
                return null;
            }

            foreach (MapSymmetryKind kind in Enum.GetValues<MapSymmetryKind>())
            {
                // the diagonals only map a square map onto itself
                if (width != height && kind is MapSymmetryKind.MirroredDiagonal or MapSymmetryKind.MirroredOtherDiagonal)
                {
                    continue;
                }

                if (positions.All(position => HasCounterpart(Transform(kind, width, height, position), positions, tolerance)))
                {
                    return kind;
                }
            }
            return null;
        }

        /// <summary>
        /// The most players a rotational symmetry is made for.
        /// </summary>
        public const int MaximumCount = 8;

        /// <summary>
        /// The parts a symmetry divides a map into, each the image of the first: for a rotational
        /// symmetry the first transform is the identity and the others turn around the middle by a
        /// <paramref name="count"/>th of a circle each, clockwise in the top view; a mirror has the
        /// identity and the mirror.
        /// </summary>
        /// <param name="count">The players of a rotational symmetry, 2 to <see cref="MaximumCount"/>;
        /// a mirror has two parts whatever it is.</param>
        /// <exception cref="ArgumentOutOfRangeException">A rotational symmetry for fewer than 2 or more than 8 players.</exception>
        public static IReadOnlyList<MapSymmetryTransform> Transforms(MapSymmetryKind kind, int count, float width, float height)
        {
            if (kind != MapSymmetryKind.Rotational)
            {
                return [MapSymmetryTransform.Identity, Mirror(kind, width, height)];
            }
            if (count < 2 || count > MaximumCount)
            {
                throw new ArgumentOutOfRangeException(nameof(count), count, $"A rotational symmetry is for 2 to {MaximumCount} players");
            }
            MapSymmetryTransform[] transforms = new MapSymmetryTransform[count];
            for (int i = 0; i < count; i++)
            {
                transforms[i] = MapSymmetryTransform.Rotation(2 * Math.PI * i / count, width / 2, height / 2);
            }
            return transforms;
        }

        /// <summary>
        /// Where a point (x, z in ogrids) lies in every part of the map, itself first.
        /// </summary>
        public static Vector2[] Images(MapSymmetryKind kind, int count, float width, float height, Vector2 point) =>
            [.. Transforms(kind, count, width, height).Select(transform => transform.Apply(point))];

        /// <summary>
        /// The lines between the parts of a symmetry, for guides in a view, as pairs of points
        /// (x, z in ogrids) on the map: a mirror's axis; for a rotational symmetry a ray from the
        /// middle to the edge per part, the first straight up (towards z = 0), the others turned as
        /// the parts are.
        /// </summary>
        public static (Vector2 From, Vector2 To)[] Guides(MapSymmetryKind kind, int count, float width, float height)
        {
            Vector2 middle = new Vector2(width / 2, height / 2);
            switch (kind)
            {
                case MapSymmetryKind.MirroredLeftRight:
                    return [(new Vector2(middle.X, 0), new Vector2(middle.X, height))];
                case MapSymmetryKind.MirroredTopBottom:
                    return [(new Vector2(0, middle.Y), new Vector2(width, middle.Y))];
                case MapSymmetryKind.MirroredDiagonal:
                    return [(Vector2.Zero, new Vector2(Math.Min(width, height), Math.Min(width, height)))];
                case MapSymmetryKind.MirroredOtherDiagonal:
                    return [(new Vector2(width, 0), new Vector2(0, height))];
            }
            return [.. Transforms(kind, count, width, height).Select(transform =>
            {
                Vector2 direction = transform.ApplyDirection(new Vector2(0, -1));
                return (middle, middle + direction * ToEdge(middle, direction, width, height));
            })];
        }

        /// <summary>
        /// How far a ray from inside the map goes before it leaves it.
        /// </summary>
        private static float ToEdge(Vector2 from, Vector2 direction, float width, float height)
        {
            float distance = float.PositiveInfinity;
            if (direction.X > 1e-6f)
            {
                distance = Math.Min(distance, (width - from.X) / direction.X);
            }
            if (direction.X < -1e-6f)
            {
                distance = Math.Min(distance, -from.X / direction.X);
            }
            if (direction.Y > 1e-6f)
            {
                distance = Math.Min(distance, (height - from.Y) / direction.Y);
            }
            if (direction.Y < -1e-6f)
            {
                distance = Math.Min(distance, -from.Y / direction.Y);
            }
            return float.IsPositiveInfinity(distance) ? 0 : distance;
        }

        private static MapSymmetryTransform Mirror(MapSymmetryKind kind, float width, float height) => kind switch
        {
            MapSymmetryKind.MirroredLeftRight => new MapSymmetryTransform(-1, 0, 0, 1, width, 0),
            MapSymmetryKind.MirroredTopBottom => new MapSymmetryTransform(1, 0, 0, -1, 0, height),
            MapSymmetryKind.MirroredDiagonal => new MapSymmetryTransform(0, 1, 1, 0, 0, 0),
            MapSymmetryKind.MirroredOtherDiagonal => new MapSymmetryTransform(0, -1, -1, 0, width, height),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a mirror"),
        };

        private static Vector2 Transform(MapSymmetryKind kind, int width, int height, Vector3 p) => kind switch
        {
            MapSymmetryKind.Rotational => new Vector2(width - p.X, height - p.Z),
            MapSymmetryKind.MirroredLeftRight => new Vector2(width - p.X, p.Z),
            MapSymmetryKind.MirroredTopBottom => new Vector2(p.X, height - p.Z),
            MapSymmetryKind.MirroredDiagonal => new Vector2(p.Z, p.X),
            _ => new Vector2(width - p.Z, height - p.X),
        };

        private static bool HasCounterpart(Vector2 target, IReadOnlyList<Vector3> positions, float tolerance) =>
            positions.Any(other => Vector2.Distance(target, new Vector2(other.X, other.Z)) <= tolerance);
    }
}
