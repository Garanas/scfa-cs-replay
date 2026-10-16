using System.Numerics;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// What a unit becomes when its blueprint is replaced (<see cref="UnitEdits.ReplaceUnits"/>).
    /// </summary>
    /// <param name="Blueprint">The new blueprint's id.</param>
    /// <param name="Footprint">The new blueprint's footprint in ogrids when it is a structure, which stands on the build grid; null for any other unit.</param>
    public sealed record UnitReplacement(string Blueprint, (double SizeX, double SizeZ)? Footprint);

    /// <summary>
    /// Edits of the units on the map as a crowd (user story U-11): replace their blueprints and spread
    /// them evenly. The groups' edits are <see cref="UnitGroupEdits"/>.
    /// </summary>
    public static class UnitEdits
    {
        /// <summary>
        /// Gives each unit the blueprint <paramref name="replacement"/> answers for it; its name, army,
        /// group, place and heading stay, and its height above the terrain. A unit that becomes a
        /// structure stands on the build grid (<see cref="BuildGrid.Snap"/> with its new footprint, a
        /// heading that was no structure's in quarter turns); a null answer leaves the unit as it is.
        /// </summary>
        /// <param name="changed">How many units got another blueprint.</param>
        public static MapDocument ReplaceUnits(this MapDocument document, IEnumerable<string> names, Func<PlacedUnit, UnitReplacement?> replacement, out int changed)
        {
            int count = 0;
            foreach (string name in names.Distinct(StringComparer.Ordinal))
            {
                if (document.FindUnit(name) is not PlacedUnit placed || replacement(placed) is not UnitReplacement made
                    || string.Equals(made.Blueprint, placed.Unit.Blueprint, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                Heightmap heights = document.Terrain.Heightmap;
                document = document.UpdateUnit(name, unit =>
                {
                    Vector3 p = unit.Position;
                    Vector3 o = unit.Orientation;
                    if (made.Footprint is (double sizeX, double sizeZ))
                    {
                        float heading = BuildGrid.SnapHeading(o.Y);
                        (float x, float z) = BuildGrid.Snap(p.X, p.Z, sizeX, sizeZ, heading);
                        float above = p.Y - heights.HeightAt(Math.Clamp(p.X, 0, document.Width), Math.Clamp(p.Z, 0, document.Height));
                        p = new Vector3(x, heights.HeightAt(Math.Clamp(x, 0, document.Width), Math.Clamp(z, 0, document.Height)) + above, z);
                        o = o with { Y = heading };
                    }
                    return unit with { Blueprint = made.Blueprint, Position = p, Orientation = o };
                });
                count++;
            }
            changed = count;
            return document;
        }

        /// <summary>
        /// Whether the units spread out further along x than along z (true when equal); true for none.
        /// </summary>
        public static bool LongerAlongX(MapDocument document, IEnumerable<string> names)
        {
            Vector3[] places = [.. names.Distinct(StringComparer.Ordinal).Select(document.FindUnit).OfType<PlacedUnit>().Select(unit => unit.Unit.Position)];
            return places.Length == 0 || places.Max(p => p.X) - places.Min(p => p.X) >= places.Max(p => p.Z) - places.Min(p => p.Z);
        }

        /// <summary>
        /// Spreads units evenly along an axis between the first and the last of them along it, keeping
        /// their order there; the two ends stay, and a structure between them stands on the build grid.
        /// </summary>
        /// <param name="alongX">True along x, false along z; null along the longer extent of the units (x when equal).</param>
        /// <param name="structureFootprint">A blueprint's footprint in ogrids when it is a structure; null for any other unit.</param>
        /// <param name="moved">How many units went to another place.</param>
        public static MapDocument DistributeUnits(this MapDocument document, IEnumerable<string> names, bool? alongX, Func<string, (double SizeX, double SizeZ)?> structureFootprint, out int moved)
        {
            PlacedUnit[] units = [.. names.Distinct(StringComparer.Ordinal).Select(document.FindUnit).OfType<PlacedUnit>()];
            moved = 0;
            if (units.Length < 3)
            {
                return document;
            }
            bool x = alongX ?? LongerAlongX(document, units.Select(unit => unit.Unit.Name));
            float Along(PlacedUnit unit) => x ? unit.Unit.Position.X : unit.Unit.Position.Z;
            PlacedUnit[] ordered = [.. units.OrderBy(Along).ThenBy(unit => unit.Unit.Name, StringComparer.Ordinal)];
            float first = Along(ordered[0]);
            float last = Along(ordered[^1]);
            Heightmap heights = document.Terrain.Heightmap;
            for (int i = 1; i < ordered.Length - 1; i++)
            {
                Vector3 p = ordered[i].Unit.Position;
                float to = first + (last - first) * i / (ordered.Length - 1);
                float nx = x ? to : p.X;
                float nz = x ? p.Z : to;
                if (structureFootprint(ordered[i].Unit.Blueprint) is (double sizeX, double sizeZ))
                {
                    (nx, nz) = BuildGrid.Snap(nx, nz, sizeX, sizeZ, ordered[i].Unit.Orientation.Y);
                }
                if (nx == p.X && nz == p.Z)
                {
                    continue;
                }
                float above = p.Y - heights.HeightAt(Math.Clamp(p.X, 0, document.Width), Math.Clamp(p.Z, 0, document.Height));
                float height = heights.HeightAt(Math.Clamp(nx, 0, document.Width), Math.Clamp(nz, 0, document.Height)) + above;
                document = document.UpdateUnit(ordered[i].Unit.Name, unit => unit with { Position = new Vector3(nx, height, nz) });
                moved++;
            }
            return document;
        }
    }
}
