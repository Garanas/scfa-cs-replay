using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.Project.Checks
{
    /// <summary>
    /// The ground a structure takes on the map, as a rectangle on the axes: x and z from
    /// <see cref="MinX"/> to <see cref="MaxX"/>, in ogrids.
    /// </summary>
    public readonly record struct PlacementRect(float MinX, float MinZ, float MaxX, float MaxZ)
    {
        /// <summary>
        /// How far two rectangles must reach into each other to overlap: edges that touch, or
        /// differ by rounding, do not.
        /// </summary>
        public const float Tolerance = 0.01f;

        public Vector2 Centre => new Vector2((MinX + MaxX) / 2, (MinZ + MaxZ) / 2);

        public bool Overlaps(PlacementRect other) =>
            MathF.Min(MaxX, other.MaxX) - MathF.Max(MinX, other.MinX) > Tolerance
            && MathF.Min(MaxZ, other.MaxZ) - MathF.Max(MinZ, other.MinZ) > Tolerance;

        /// <summary>
        /// The middle of the ground the two share.
        /// </summary>
        public Vector2 MiddleOfOverlap(PlacementRect other) => new Vector2(
            (MathF.Max(MinX, other.MinX) + MathF.Min(MaxX, other.MaxX)) / 2,
            (MathF.Max(MinZ, other.MinZ) + MathF.Min(MaxZ, other.MaxZ)) / 2);
    }

    /// <summary>
    /// A structure of the map that takes ground: the unit, what it is, and where (user story U-12).
    /// </summary>
    public sealed record PlacedStructure(PlacedUnit Placed, AssetEntry Entry, PlacementRect Rect)
    {
        /// <summary>
        /// <c>Land Factory UNIT_12</c>: what it is and its name on the map.
        /// </summary>
        public string Title => $"{Entry.Name} {Placed.Unit.Name}";
    }

    /// <summary>
    /// The structures of a map by place, to find those that reach into a rectangle without
    /// looking at them all: a grid of cells, each with the structures that touch it.
    /// </summary>
    public sealed class PlacementIndex
    {
        private const int Cell = 16;

        private readonly Dictionary<(int, int), List<int>> _cells = [];

        /// <summary>
        /// The structures: every structure of the map with a blueprint the catalog knows and ground
        /// to take, wrecks and mobile units left out.
        /// </summary>
        public IReadOnlyList<PlacedStructure> Structures { get; }

        public PlacementIndex(MapDocument document, AssetCatalog catalog)
        {
            List<PlacedStructure> structures = [];
            foreach (PlacedUnit placed in document.AllUnits())
            {
                if (UnitWrecks.IsWreck(placed) || catalog.Find(placed.Unit.Blueprint) is not { IsStructure: true } entry
                    || PlacementChecks.SkirtOf(entry, placed.Unit.Position.X, placed.Unit.Position.Z, placed.Unit.Orientation.Y) is not PlacementRect rect)
                {
                    continue;
                }
                structures.Add(new PlacedStructure(placed, entry, rect));
            }
            Structures = structures;
            for (int i = 0; i < structures.Count; i++)
            {
                PlacementRect rect = structures[i].Rect;
                for (int x = CellOf(rect.MinX); x <= CellOf(rect.MaxX); x++)
                {
                    for (int z = CellOf(rect.MinZ); z <= CellOf(rect.MaxZ); z++)
                    {
                        if (!_cells.TryGetValue((x, z), out List<int>? list))
                        {
                            _cells[(x, z)] = list = [];
                        }
                        list.Add(i);
                    }
                }
            }
        }

        private static int CellOf(float ogrids) => (int)MathF.Floor(ogrids / Cell);

        /// <summary>
        /// The structures whose ground overlaps <paramref name="rect"/>, each once, by their place in <see cref="Structures"/>.
        /// </summary>
        public IEnumerable<int> Overlapping(PlacementRect rect)
        {
            HashSet<int>? seen = null;
            for (int x = CellOf(rect.MinX); x <= CellOf(rect.MaxX); x++)
            {
                for (int z = CellOf(rect.MinZ); z <= CellOf(rect.MaxZ); z++)
                {
                    if (!_cells.TryGetValue((x, z), out List<int>? list))
                    {
                        continue;
                    }
                    foreach (int i in list)
                    {
                        if (Structures[i].Rect.Overlaps(rect) && (seen ??= []).Add(i))
                        {
                            yield return i;
                        }
                    }
                }
            }
        }

        private static readonly ConditionalWeakTable<MapDocument, PlacementIndex> Cache = [];

        private static readonly ConditionalWeakTable<PlacementIndex, AssetCatalog> CatalogOf = [];

        /// <summary>
        /// The index of a document, made once per document and catalog (a step of editing makes a
        /// new document, so the next ask builds it again).
        /// </summary>
        public static PlacementIndex Of(MapDocument document, AssetCatalog catalog)
        {
            if (Cache.TryGetValue(document, out PlacementIndex? index) && CatalogOf.TryGetValue(index, out AssetCatalog? made) && ReferenceEquals(made, catalog))
            {
                return index;
            }
            index = new PlacementIndex(document, catalog);
            Cache.AddOrUpdate(document, index);
            CatalogOf.AddOrUpdate(index, catalog);
            return index;
        }
    }

    /// <summary>
    /// Where a structure may stand (user story U-12): its ground against the others', against the
    /// layers its blueprint allows and its slope limit. The Problems tab's checks
    /// <c>units-overlap</c> and <c>units-ground</c> and the place tool's red preview use these
    /// functions, so they agree. Anything a blueprint does not say is not checked.
    /// </summary>
    public static class PlacementChecks
    {
        /// <summary>
        /// At most this many problems per check; one more says how many were left out.
        /// </summary>
        public const int MostProblems = 200;

        /// <summary>
        /// The two checks, for <see cref="MapChecks.All"/>.
        /// </summary>
        public static IReadOnlyList<IProblemCheck> All { get; } =
        [
            new ProblemCheck("units-overlap", "Structures that overlap", Overlaps),
            new ProblemCheck("units-ground", "Structures on ground they cannot stand on", Ground),
        ];

        #region Rectangles

        /// <summary>
        /// A rectangle from a corner of <paramref name="sizeX"/> by <paramref name="sizeZ"/> turned
        /// about its footprint's centre by <paramref name="radians"/> (clockwise in the top view; a
        /// structure stands in quarter turns, so the nearest one is used).
        /// </summary>
        private static PlacementRect Turned(double fromX, double fromZ, double sizeX, double sizeZ, float x, float z, float radians)
        {
            int quarters = ((int)MathF.Round(radians / (MathF.PI / 2)) % 4 + 4) % 4;
            (float cos, float sin) = quarters switch { 0 => (1f, 0f), 1 => (0f, 1f), 2 => (-1f, 0f), _ => (0f, -1f) };
            float ax = (float)fromX;
            float az = (float)fromZ;
            float bx = (float)(fromX + sizeX);
            float bz = (float)(fromZ + sizeZ);
            float x0 = ax * cos - az * sin;
            float z0 = ax * sin + az * cos;
            float x1 = bx * cos - bz * sin;
            float z1 = bx * sin + bz * cos;
            return new PlacementRect(x + MathF.Min(x0, x1), z + MathF.Min(z0, z1), x + MathF.Max(x0, x1), z + MathF.Max(z0, z1));
        }

        /// <summary>
        /// The ground a structure reserves at a place: its skirt, else its footprint; null for a
        /// unit whose blueprint names neither.
        /// </summary>
        /// <param name="x">Its centre, in ogrids.</param>
        /// <param name="radians">Its heading.</param>
        public static PlacementRect? SkirtOf(AssetEntry entry, float x, float z, float radians)
        {
            if (entry.Limits?.Skirt is Skirt skirt)
            {
                (double fx, double fz) = entry.Footprint ?? (skirt.SizeX, skirt.SizeZ);
                return Turned(-fx / 2 + skirt.OffsetX, -fz / 2 + skirt.OffsetZ, skirt.SizeX, skirt.SizeZ, x, z, radians);
            }
            return FootprintOf(entry, x, z, radians);
        }

        /// <summary>
        /// The footprint at a place; null for a unit whose blueprint names none.
        /// </summary>
        public static PlacementRect? FootprintOf(AssetEntry entry, float x, float z, float radians) =>
            entry.Footprint is (double sizeX, double sizeZ) ? Turned(-sizeX / 2, -sizeZ / 2, sizeX, sizeZ, x, z, radians) : null;

        #endregion

        #region Overlap

        /// <summary>
        /// The structure a new one of <paramref name="entry"/> at a place would overlap, if any.
        /// </summary>
        /// <param name="index">The map's structures (<see cref="PlacementIndex.Of"/>).</param>
        /// <param name="ignore">Names of units that do not count (the ones being moved).</param>
        public static PlacedStructure? OverlapAt(PlacementIndex index, AssetEntry entry, float x, float z, float radians, ISet<string>? ignore = null)
        {
            if (!entry.IsStructure || SkirtOf(entry, x, z, radians) is not PlacementRect rect)
            {
                return null;
            }
            foreach (int i in index.Overlapping(rect))
            {
                PlacedStructure other = index.Structures[i];
                if (ignore is null || !ignore.Contains(other.Placed.Unit.Name))
                {
                    return other;
                }
            }
            return null;
        }

        /// <summary>
        /// Two structures (wrecks and mobile units left out) whose skirts, else footprints, overlap:
        /// one warning per pair. Walls that touch edge to edge do not overlap.
        /// </summary>
        public static IEnumerable<Problem> Overlaps(ProblemContext context)
        {
            if (context.Catalog is not AssetCatalog catalog || catalog.Entries.Count == 0)
            {
                yield break;
            }
            PlacementIndex index = PlacementIndex.Of(context.Document, catalog);
            int found = 0;
            for (int i = 0; i < index.Structures.Count; i++)
            {
                PlacedStructure a = index.Structures[i];
                foreach (int j in index.Overlapping(a.Rect))
                {
                    if (j <= i)
                    {
                        continue;
                    }
                    if (++found > MostProblems)
                    {
                        continue;
                    }
                    PlacedStructure b = index.Structures[j];
                    Vector2 at = a.Rect.MiddleOfOverlap(b.Rect);
                    yield return new Problem("units-overlap", ProblemSeverity.Warning,
                        string.Create(CultureInfo.InvariantCulture, $"{a.Title} overlaps {b.Title} near {at.X:0}, {at.Y:0}"),
                        ObjectRef.Unit(a.Placed.Unit.Name), at, [ObjectRef.Unit(b.Placed.Unit.Name)]);
                }
            }
            if (found > MostProblems)
            {
                yield return new Problem("units-overlap", ProblemSeverity.Warning, $"{found - MostProblems} more pairs of structures overlap");
            }
        }

        #endregion

        #region Ground

        /// <summary>
        /// Why a structure may not stand where it does, or null when it may (or the blueprint says
        /// too little to tell): the layer under its centre against its <c>BuildOnLayerCaps</c>
        /// (land, or water and seabed under the water's height), the depth of the water against its
        /// least depth, and the slope across its footprint against its limit.
        /// </summary>
        /// <param name="water">The water's height, null for a map without water.</param>
        public static string? GroundIssue(Heightmap heights, float? water, AssetEntry entry, float x, float z, float radians)
        {
            if (entry.Limits is not PlacementLimits limits)
            {
                return null;
            }
            List<string> reasons = [];
            float height = heights.HeightAt(x, z);
            float depth = water is float level ? level - height : 0;
            if (limits.Layers is { } layers)
            {
                if (depth <= 0 && !layers.Contains("Land"))
                {
                    reasons.Add("it cannot be built on land");
                }
                else if (depth > 0 && !layers.Contains("Water") && !layers.Contains("Seabed"))
                {
                    reasons.Add("it cannot be built under water");
                }
            }
            if (depth > 0 && limits.MinWaterDepth is double least && depth < least)
            {
                reasons.Add(string.Create(CultureInfo.InvariantCulture, $"the water is {depth:0.#} deep, it needs {least:0.#}"));
            }
            if (limits.MaxSlope is double maxSlope && SlopeOf(heights, entry, x, z, radians) is float slope && slope > maxSlope)
            {
                reasons.Add(string.Create(CultureInfo.InvariantCulture, $"the ground rises {slope:0.##} an ogrid, it allows {maxSlope:0.##}"));
            }
            return reasons.Count == 0 ? null : string.Join("; ", reasons);
        }

        /// <summary>
        /// The steepest rise per ogrid from the centre of the footprint to one of its corners
        /// (the heightmap's height at the five points); null for a unit without a footprint.
        /// </summary>
        public static float? SlopeOf(Heightmap heights, AssetEntry entry, float x, float z, float radians)
        {
            if (FootprintOf(entry, x, z, radians) is not PlacementRect rect)
            {
                return null;
            }
            float centre = heights.HeightAt(x, z);
            float reach = MathF.Max(0.5f, MathF.Sqrt((rect.MaxX - rect.MinX) * (rect.MaxX - rect.MinX) + (rect.MaxZ - rect.MinZ) * (rect.MaxZ - rect.MinZ)) / 2);
            float steepest = 0;
            foreach ((float cx, float cz) in new[] { (rect.MinX, rect.MinZ), (rect.MaxX, rect.MinZ), (rect.MinX, rect.MaxZ), (rect.MaxX, rect.MaxZ) })
            {
                steepest = MathF.Max(steepest, MathF.Abs(heights.HeightAt(cx, cz) - centre) / reach);
            }
            return steepest;
        }

        /// <summary>
        /// Structures on the wrong layer or too steep ground (<see cref="GroundIssue"/>): one
        /// warning per structure. Wrecks and mobile units are never checked.
        /// </summary>
        public static IEnumerable<Problem> Ground(ProblemContext context)
        {
            if (context.Catalog is not AssetCatalog catalog || catalog.Entries.Count == 0)
            {
                yield break;
            }
            MapDocument document = context.Document;
            Heightmap heights = document.Terrain.Heightmap;
            float? water = document.Water.HasWater ? document.Water.Elevation : null;
            int found = 0;
            foreach (PlacedStructure structure in PlacementIndex.Of(document, catalog).Structures)
            {
                Unit unit = structure.Placed.Unit;
                if (GroundIssue(heights, water, structure.Entry, unit.Position.X, unit.Position.Z, unit.Orientation.Y) is not string why)
                {
                    continue;
                }
                if (++found > MostProblems)
                {
                    continue;
                }
                yield return new Problem("units-ground", ProblemSeverity.Warning,
                    string.Create(CultureInfo.InvariantCulture, $"{structure.Title} at {unit.Position.X:0}, {unit.Position.Z:0}: {why}"),
                    ObjectRef.Unit(unit.Name), new Vector2(unit.Position.X, unit.Position.Z));
            }
            if (found > MostProblems)
            {
                yield return new Problem("units-ground", ProblemSeverity.Warning, $"{found - MostProblems} more structures stand on ground they cannot");
            }
        }

        #endregion
    }
}
