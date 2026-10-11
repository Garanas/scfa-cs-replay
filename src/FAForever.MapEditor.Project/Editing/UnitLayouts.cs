using System.Collections.Immutable;
using System.Numerics;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// Where one unit of a layout goes: its centre in ogrids and its heading in radians, clockwise
    /// from +x in the top view (the unit's <c>Orientation.Y</c>).
    /// </summary>
    public readonly record struct UnitPlace(float X, float Z, float Heading);

    /// <summary>
    /// The places and headings of many units placed at once (user story U-7): a line, a filled or
    /// hollow rectangle, a ring around something, and a scatter of random places. Pure maths: the
    /// caller puts the units on the terrain and into a group (<see cref="DocumentEdits.AddUnits"/>).
    /// </summary>
    /// <remarks>
    /// A structure stands on the build grid (<see cref="BuildGrid.Snap"/>) and turns in quarter turns
    /// (<see cref="BuildGrid.SnapHeading"/>): in a line and a rectangle structures butt together at
    /// their footprint (a wall becomes one wall), whatever spacing is given; mobile units stand the
    /// spacing apart and face any way. A footprint is a unit's size along x and z when it is not
    /// turned (<see cref="Extent"/> gives it turned); a unit without one counts as 1 by 1.
    /// </remarks>
    public static class UnitLayouts
    {
        /// <summary>
        /// The least spacing of mobile units, in ogrids.
        /// </summary>
        public const float MinimumSpacing = 0.25f;

        /// <summary>
        /// A quarter of a right angle: the steps a line keeps to with shift held.
        /// </summary>
        public const float EighthTurn = MathF.PI / 4;

        /// <summary>
        /// A footprint's reach along x and z when turned by a heading: an odd number of quarter turns
        /// swaps its sides, as the build grid does. At least 1 by 1.
        /// </summary>
        public static (float X, float Z) Extent((double SizeX, double SizeZ)? footprint, float radians)
        {
            (double sizeX, double sizeZ) = footprint ?? (1, 1);
            float x = MathF.Max(1, (float)sizeX);
            float z = MathF.Max(1, (float)sizeZ);
            int quarters = (int)MathF.Round(radians / (MathF.PI / 2));
            return quarters % 2 == 0 ? (x, z) : (z, x);
        }

        /// <summary>
        /// The end of a line from a place, moved so the line's angle is a whole number of steps (an
        /// eighth of a turn with shift), its length kept.
        /// </summary>
        public static Vector2 Constrain(Vector2 from, Vector2 to, float stepRadians)
        {
            Vector2 d = to - from;
            float length = d.Length();
            if (length < 1e-4f || stepRadians <= 0)
            {
                return to;
            }
            float angle = MathF.Round(MathF.Atan2(d.Y, d.X) / stepRadians) * stepRadians;
            (float sin, float cos) = MathF.SinCos(angle);
            return from + new Vector2(cos, sin) * length;
        }

        /// <summary>
        /// Units along a line from one place to another: structures butting together at their
        /// footprint along the line (each on the build grid; two that land on the same place are one),
        /// mobile units a spacing apart from the first place on; the first at the start, the last
        /// within the line. Each faces along the line (structures in the nearest quarter turn) or the
        /// heading given.
        /// </summary>
        /// <param name="spacing">Between the centres of mobile units, in ogrids (at least <see cref="MinimumSpacing"/>).</param>
        /// <param name="heading">The heading of every unit when it does not face along the line, in radians.</param>
        public static ImmutableArray<UnitPlace> Line(Vector2 from, Vector2 to, (double SizeX, double SizeZ)? footprint, float spacing, bool faceAlong, float heading, bool isStructure)
        {
            Vector2 d = to - from;
            float length = d.Length();
            Vector2 direction = length < 1e-4f ? Vector2.UnitX : d / length;
            float along = MathF.Atan2(direction.Y, direction.X);
            ImmutableArray<UnitPlace>.Builder places = ImmutableArray.CreateBuilder<UnitPlace>();
            if (isStructure)
            {
                float turn = BuildGrid.SnapHeading(faceAlong ? along : heading);
                (float extentX, float extentZ) = Extent(footprint, turn);
                // how far the footprint reaches along the line: its whole side along an axis, more between
                float step = MathF.Abs(direction.X) * extentX + MathF.Abs(direction.Y) * extentZ;
                int count = 1 + (int)MathF.Floor(length / step + 1e-3f);
                (float sizeX, float sizeZ) = ((float)(footprint?.SizeX ?? 1), (float)(footprint?.SizeZ ?? 1));
                UnitPlace? last = null;
                for (int i = 0; i < count; i++)
                {
                    Vector2 at = from + direction * (i * step);
                    (float x, float z) = BuildGrid.Snap(at.X, at.Y, sizeX, sizeZ, turn);
                    UnitPlace place = new UnitPlace(x, z, turn);
                    if (last is UnitPlace previous && previous.X == x && previous.Z == z)
                    {
                        continue;
                    }
                    places.Add(place);
                    last = place;
                }
            }
            else
            {
                float turn = faceAlong ? along : heading;
                float step = MathF.Max(MinimumSpacing, spacing);
                int count = 1 + (int)MathF.Floor(length / step + 1e-3f);
                for (int i = 0; i < count; i++)
                {
                    Vector2 at = from + direction * (i * step);
                    places.Add(new UnitPlace(at.X, at.Y, turn));
                }
            }
            return places.ToImmutable();
        }

        /// <summary>
        /// Units filling the rectangle between two corners (any order), all facing the heading given:
        /// structures in rows of their footprint from the corner nearest the map's origin (the
        /// rectangle rounded to whole ogrids, at least one unit each way), mobile units on a grid of
        /// the spacing from that corner. Hollow, only the border stays: a wall around a yard.
        /// </summary>
        public static ImmutableArray<UnitPlace> Rectangle(Vector2 cornerA, Vector2 cornerB, (double SizeX, double SizeZ)? footprint, bool hollow, float spacing, float heading, bool isStructure)
        {
            float xMin = MathF.Min(cornerA.X, cornerB.X);
            float xMax = MathF.Max(cornerA.X, cornerB.X);
            float zMin = MathF.Min(cornerA.Y, cornerB.Y);
            float zMax = MathF.Max(cornerA.Y, cornerB.Y);
            if (isStructure)
            {
                float turn = BuildGrid.SnapHeading(heading);
                (float extentX, float extentZ) = Extent(footprint, turn);
                float x0 = MathF.Round(xMin);
                float z0 = MathF.Round(zMin);
                int columns = Math.Max(1, (int)MathF.Round((MathF.Round(xMax) - x0) / extentX));
                int rows = Math.Max(1, (int)MathF.Round((MathF.Round(zMax) - z0) / extentZ));
                return Grid(columns, rows, hollow, (i, j) => new UnitPlace(x0 + extentX / 2 + i * extentX, z0 + extentZ / 2 + j * extentZ, turn));
            }
            else
            {
                float step = MathF.Max(MinimumSpacing, spacing);
                int columns = 1 + (int)MathF.Floor((xMax - xMin) / step + 1e-3f);
                int rows = 1 + (int)MathF.Floor((zMax - zMin) / step + 1e-3f);
                return Grid(columns, rows, hollow, (i, j) => new UnitPlace(xMin + i * step, zMin + j * step, heading));
            }
        }

        /// <summary>
        /// Units around a rectangle (the footprint bounds of what they go around), a distance from
        /// it: structures as a hollow rectangle whose inner edge lies that far out (each on the build
        /// grid), mobile units spread evenly along the rectangle that far out, about the spacing apart.
        /// All face the heading given.
        /// </summary>
        /// <param name="bounds">The left, top, right and bottom of what the ring goes around, in ogrids.</param>
        /// <param name="distance">Between the bounds and the ring's units, in ogrids (0 or more).</param>
        public static ImmutableArray<UnitPlace> Ring((float X0, float Z0, float X1, float Z1) bounds, (double SizeX, double SizeZ)? footprint, float distance, float spacing, float heading, bool isStructure)
        {
            float gap = MathF.Max(0, distance);
            float x0 = MathF.Min(bounds.X0, bounds.X1) - gap;
            float z0 = MathF.Min(bounds.Z0, bounds.Z1) - gap;
            float x1 = MathF.Max(bounds.X0, bounds.X1) + gap;
            float z1 = MathF.Max(bounds.Z0, bounds.Z1) + gap;
            if (isStructure)
            {
                float turn = BuildGrid.SnapHeading(heading);
                (float extentX, float extentZ) = Extent(footprint, turn);
                return Rectangle(new Vector2(x0 - extentX, z0 - extentZ), new Vector2(x1 + extentX, z1 + extentZ), footprint, hollow: true, spacing, turn, isStructure: true);
            }
            float step = MathF.Max(MinimumSpacing, spacing);
            int columns = Math.Max(2, 1 + (int)MathF.Round((x1 - x0) / step));
            int rows = Math.Max(2, 1 + (int)MathF.Round((z1 - z0) / step));
            float stepX = (x1 - x0) / (columns - 1);
            float stepZ = (z1 - z0) / (rows - 1);
            return Grid(columns, rows, hollow: true, (i, j) => new UnitPlace(x0 + i * stepX, z0 + j * stepZ, heading));
        }

        /// <summary>
        /// Random places within a circle, each facing a random way, none closer than the least
        /// distance to another or to the places given (what a brush stroke placed before): as many as
        /// asked when they fit, fewer when the circle is full. The same seed gives the same places.
        /// </summary>
        /// <param name="existing">Places to keep the least distance from; null for none.</param>
        public static ImmutableArray<UnitPlace> Scatter(Vector2 centre, float radius, int count, float leastDistance, int seed, IReadOnlyList<Vector2>? existing = null)
        {
            Random random = new Random(seed);
            List<Vector2> kept = existing is null ? [] : [.. existing];
            float least = MathF.Max(0, leastDistance);
            float leastSquared = least * least;
            ImmutableArray<UnitPlace>.Builder places = ImmutableArray.CreateBuilder<UnitPlace>();
            int tries = Math.Max(0, count) * 30;
            while (places.Count < count && tries-- > 0)
            {
                float r = MathF.Max(0, radius) * MathF.Sqrt(random.NextSingle());
                float angle = random.NextSingle() * 2 * MathF.PI;
                Vector2 at = centre + new Vector2(r * MathF.Cos(angle), r * MathF.Sin(angle));
                bool tooClose = false;
                foreach (Vector2 other in kept)
                {
                    if ((other - at).LengthSquared() < leastSquared)
                    {
                        tooClose = true;
                        break;
                    }
                }
                if (tooClose)
                {
                    continue;
                }
                kept.Add(at);
                places.Add(new UnitPlace(at.X, at.Y, random.NextSingle() * 2 * MathF.PI - MathF.PI));
            }
            return places.ToImmutable();
        }

        private static ImmutableArray<UnitPlace> Grid(int columns, int rows, bool hollow, Func<int, int, UnitPlace> at)
        {
            ImmutableArray<UnitPlace>.Builder places = ImmutableArray.CreateBuilder<UnitPlace>();
            for (int j = 0; j < rows; j++)
            {
                for (int i = 0; i < columns; i++)
                {
                    if (!hollow || i == 0 || i == columns - 1 || j == 0 || j == rows - 1)
                    {
                        places.Add(at(i, j));
                    }
                }
            }
            return places.ToImmutable();
        }
    }
}
