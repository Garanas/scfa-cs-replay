using System.Collections.Immutable;
using System.Numerics;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// Edits of the armies' trees of unit groups (user stories U-2, U-3, U-4): add, rename, remove
    /// and move groups, move units between armies and groups, and copy a group's units as a
    /// template (a preset) to place again elsewhere, for any army.
    /// </summary>
    /// <remarks>
    /// A group is named by its army and its path: the names of the groups from below the army's
    /// root group down (<c>INITIAL</c>, <c>INITIAL/Wrecks</c>); the empty path is the root group
    /// itself. Group names are unique among their siblings. The game creates the units of each
    /// army's group <c>INITIAL</c> (with the groups in it) when a skirmish starts; the other groups
    /// are for the map's script.
    /// </remarks>
    public static class UnitGroupEdits
    {
        /// <summary>
        /// The group whose units the game creates at the start, under each army's root group.
        /// </summary>
        public const string InitialGroup = "INITIAL";

        /// <summary>
        /// <c>INITIAL/Wrecks</c> as its names; null or empty for the root group.
        /// </summary>
        public static ImmutableArray<string> ParsePath(string? path) =>
            string.IsNullOrWhiteSpace(path) ? [] : [.. path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

        /// <summary>
        /// A group's path as text: <c>INITIAL/Wrecks</c>; empty for the root group.
        /// </summary>
        public static string PathText(IEnumerable<string> path) => string.Join('/', path);

        /// <summary>
        /// The group of an army at a path, or null when the army or the group does not exist.
        /// </summary>
        public static UnitGroup? FindGroup(this MapDocument document, string army, IReadOnlyList<string> path)
        {
            UnitGroup? group = document.Armies.FirstOrDefault(a => a.Name == army)?.Units;
            for (int i = 0; i < path.Count && group is not null; i++)
            {
                group = group.Groups.FirstOrDefault(child => child.Name == path[i]);
            }
            return group;
        }

        /// <summary>
        /// The units of a group and of the groups in it.
        /// </summary>
        public static IEnumerable<PlacedUnit> UnitsIn(this MapDocument document, string army, IReadOnlyList<string> path) =>
            document.AllUnits().Where(unit => unit.Army == army && unit.Group.Length >= path.Count && unit.Group.Take(path.Count).SequenceEqual(path));

        /// <summary>
        /// How many units a group holds, with the groups in it.
        /// </summary>
        public static int CountUnits(UnitGroup group) => group.Units.Length + group.Groups.Sum(CountUnits);

        /// <summary>
        /// Adds an empty group into a group.
        /// </summary>
        /// <param name="name">Null or empty for <c>GROUP_1</c>; a name a sibling has gets the next free number.</param>
        /// <param name="added">The new group's path.</param>
        /// <exception cref="KeyNotFoundException">The army or the group to add to does not exist.</exception>
        /// <exception cref="ArgumentException">The name holds a <c>/</c>.</exception>
        public static MapDocument AddGroup(this MapDocument document, string army, IReadOnlyList<string> parent, string? name, out ImmutableArray<string> added)
        {
            UnitGroup into = RequireGroup(document, army, parent);
            string wanted = CheckGroupName(string.IsNullOrWhiteSpace(name) ? "GROUP_1" : name);
            string free = DocumentEdits.FreeName(into.Groups.Select(group => group.Name), wanted);
            added = [.. parent, free];
            return document.ChangeGroup(army, parent, group => group with { Groups = group.Groups.Add(new UnitGroup { Name = free }) });
        }

        /// <exception cref="KeyNotFoundException">The army or the group does not exist.</exception>
        /// <exception cref="ArgumentException">It is the root group, a sibling has the name, or the name holds a <c>/</c>.</exception>
        public static MapDocument RenameGroup(this MapDocument document, string army, IReadOnlyList<string> path, string name)
        {
            RequireGroup(document, army, path);
            if (path.Count == 0)
            {
                throw new ArgumentException($"The root group of {army} keeps its name");
            }
            string newName = CheckGroupName(name);
            if (newName == path[^1])
            {
                return document;
            }
            UnitGroup parent = document.FindGroup(army, [.. path.Take(path.Count - 1)])!;
            if (parent.Groups.Any(group => group.Name == newName))
            {
                throw new ArgumentException($"The group {PathText(path.Take(path.Count - 1))} of {army} has a group {newName} already");
            }
            return document.ChangeGroup(army, path, group => group with { Name = newName });
        }

        /// <summary>
        /// Removes a group. With <paramref name="keepUnits"/> its units and groups go to the group
        /// above it (a group whose name is taken there gets the next free number); without, they
        /// are removed with it.
        /// </summary>
        /// <exception cref="KeyNotFoundException">The army or the group does not exist.</exception>
        /// <exception cref="ArgumentException">It is the root group.</exception>
        public static MapDocument RemoveGroup(this MapDocument document, string army, IReadOnlyList<string> path, bool keepUnits)
        {
            UnitGroup removed = RequireGroup(document, army, path);
            if (path.Count == 0)
            {
                throw new ArgumentException($"The root group of {army} cannot be removed; remove the army");
            }
            ImmutableArray<string> parent = [.. path.Take(path.Count - 1)];
            return document.ChangeGroup(army, parent, group =>
            {
                UnitGroup without = group with { Groups = group.Groups.RemoveAll(child => child.Name == removed.Name) };
                return keepUnits ? Merge(without, removed.Units, removed.Groups) : without;
            });
        }

        /// <summary>
        /// Moves a group (with its units and groups) into another group, of the same or another
        /// army; a name taken there gets the next free number.
        /// </summary>
        /// <param name="moved">The group's new path.</param>
        /// <exception cref="KeyNotFoundException">A group or army does not exist.</exception>
        /// <exception cref="ArgumentException">It is a root group, or the group would go into itself.</exception>
        public static MapDocument MoveGroup(this MapDocument document, string army, IReadOnlyList<string> path, string toArmy, IReadOnlyList<string> toParent, out ImmutableArray<string> moved)
        {
            UnitGroup group = RequireGroup(document, army, path);
            RequireGroup(document, toArmy, toParent);
            if (path.Count == 0)
            {
                throw new ArgumentException($"The root group of {army} stays where it is");
            }
            if (army == toArmy && toParent.Count >= path.Count && toParent.Take(path.Count).SequenceEqual(path))
            {
                throw new ArgumentException($"The group {PathText(path)} cannot go into itself");
            }
            if (army == toArmy && toParent.SequenceEqual(path.Take(path.Count - 1)))
            {
                moved = [.. path];
                return document;
            }
            document = document.RemoveGroup(army, path, keepUnits: false);
            UnitGroup into = document.FindGroup(toArmy, toParent)!;
            string name = DocumentEdits.FreeName(into.Groups.Select(g => g.Name), group.Name);
            moved = [.. toParent, name];
            return document.ChangeGroup(toArmy, toParent, g => g with { Groups = g.Groups.Add(group with { Name = name }) });
        }

        /// <summary>
        /// Moves units into a group of an army, made when it does not exist, in the order given;
        /// a unit that is there already stays, and a name that is no unit is left out.
        /// </summary>
        /// <returns>The document, and how many units moved.</returns>
        /// <exception cref="KeyNotFoundException">No army has the name.</exception>
        public static MapDocument MoveUnits(this MapDocument document, IEnumerable<string> names, string army, IReadOnlyList<string> group, out int count)
        {
            if (document.Armies.All(a => a.Name != army))
            {
                throw new KeyNotFoundException($"The map has no army {army}");
            }
            count = 0;
            foreach (string name in names.Distinct(StringComparer.Ordinal).ToArray())
            {
                if (document.FindUnit(name) is not PlacedUnit unit || (unit.Army == army && unit.Group.SequenceEqual(group)))
                {
                    continue;
                }
                document = document.RemoveUnit(name).AddUnit(army, group, unit.Unit);
                count++;
            }
            return document;
        }

        /// <summary>
        /// A template of units to place again (a preset, user story U-4): a group named
        /// <paramref name="name"/> with the units, their places relative to the middle of them (x
        /// and z, the middle rounded to a whole ogrid) and their heights above the terrain (y), in the groups they are in below the
        /// deepest group that holds them all. Names are kept, to be replaced when placed.
        /// </summary>
        /// <exception cref="ArgumentException">None of the names is a unit.</exception>
        public static UnitGroup CaptureUnits(this MapDocument document, IEnumerable<string> names, string name) => document.CaptureUnits(names, name, null);

        /// <summary>
        /// A template of units as <see cref="CaptureUnits(MapDocument, IEnumerable{string}, string)"/>
        /// makes it, around a middle of the caller's (a selection preset's, a whole ogrid), or their
        /// own when it is null.
        /// </summary>
        /// <exception cref="ArgumentException">None of the names is a unit.</exception>
        public static UnitGroup CaptureUnits(this MapDocument document, IEnumerable<string> names, string name, Vector2? around)
        {
            PlacedUnit[] units = [.. names.Distinct(StringComparer.Ordinal).Select(document.FindUnit).OfType<PlacedUnit>()];
            if (units.Length == 0)
            {
                throw new ArgumentException("Choose units to save: none of them is on the map");
            }
            Heightmap heights = document.Terrain.Heightmap;
            // the middle on a whole ogrid, so structures stay on the build grid wherever it is placed
            float cx = around?.X ?? MathF.Round(units.Average(u => u.Unit.Position.X));
            float cz = around?.Y ?? MathF.Round(units.Average(u => u.Unit.Position.Z));

            // the deepest group that holds them all, in the army of the first
            ImmutableArray<string> common = units[0].Group;
            foreach (PlacedUnit unit in units.Skip(1))
            {
                int same = 0;
                while (same < common.Length && same < unit.Group.Length && common[same] == unit.Group[same] && unit.Army == units[0].Army)
                {
                    same++;
                }
                common = common.Take(same).ToImmutableArray();
            }

            UnitGroup template = new UnitGroup { Name = name };
            foreach (PlacedUnit unit in units)
            {
                Vector3 p = unit.Unit.Position;
                Unit relative = unit.Unit with { Position = new Vector3(p.X - cx, p.Y - heights.HeightAt(p.X, p.Z), p.Z - cz) };
                ImmutableArray<string> below = unit.Army == units[0].Army ? [.. unit.Group.Skip(common.Length)] : [];
                template = AddInto(template, below, 0, relative);
            }
            return template;
        }

        /// <summary>
        /// Places a template of units (<see cref="CaptureUnits"/>) as a new group in a group of an
        /// army, its middle at a place and turned about it; each unit stands as high above the
        /// terrain as it did, and gets a free name.
        /// </summary>
        /// <param name="radians">The turn, clockwise in the top view.</param>
        /// <param name="placed">The units' names, in the order of the template.</param>
        /// <param name="group">The new group's path: the template's name, made free among its siblings.</param>
        /// <exception cref="KeyNotFoundException">The army or the group to place into does not exist.</exception>
        public static MapDocument PlaceUnits(this MapDocument document, UnitGroup template, string army, IReadOnlyList<string> parent, float x, float z, float radians,
            out ImmutableArray<string> placed, out ImmutableArray<string> group)
        {
            UnitGroup into = RequireGroup(document, army, parent);
            string name = DocumentEdits.FreeName(into.Groups.Select(g => g.Name), CheckGroupName(template.Name.Length == 0 ? "GROUP_1" : template.Name));
            group = [.. parent, name];
            Heightmap heights = document.Terrain.Heightmap;
            (float sin, float cos) = MathF.SinCos(radians);
            int next = NextUnitNumber(document);
            ImmutableArray<string>.Builder names = ImmutableArray.CreateBuilder<string>();
            UnitGroup Place(UnitGroup source, string groupName)
            {
                ImmutableArray<Unit>.Builder units = ImmutableArray.CreateBuilder<Unit>(source.Units.Length);
                foreach (Unit unit in source.Units)
                {
                    Vector3 o = unit.Position;
                    float ux = x + o.X * cos - o.Z * sin;
                    float uz = z + o.X * sin + o.Z * cos;
                    string unitName = $"UNIT_{next++}";
                    names.Add(unitName);
                    units.Add(unit with
                    {
                        Name = unitName,
                        Position = new Vector3(ux, heights.HeightAt(ux, uz) + o.Y, uz),
                        Orientation = unit.Orientation with { Y = Wrap(unit.Orientation.Y + radians) },
                    });
                }
                return new UnitGroup { Name = groupName, Units = units.MoveToImmutable(), Groups = [.. source.Groups.Select(g => Place(g, g.Name))] };
            }
            UnitGroup made = Place(template, name);
            placed = names.ToImmutable();
            return document.ChangeGroup(army, parent, g => g with { Groups = g.Groups.Add(made) });
        }

        /// <summary>
        /// The document with the group at a path of an army changed.
        /// </summary>
        /// <exception cref="KeyNotFoundException">The army or the group does not exist.</exception>
        private static MapDocument ChangeGroup(this MapDocument document, string army, IReadOnlyList<string> path, Func<UnitGroup, UnitGroup> change)
        {
            UnitGroup Change(UnitGroup group, int depth)
            {
                if (depth == path.Count)
                {
                    return change(group);
                }
                for (int i = 0; i < group.Groups.Length; i++)
                {
                    if (group.Groups[i].Name == path[depth])
                    {
                        return group with { Groups = group.Groups.SetItem(i, Change(group.Groups[i], depth + 1)) };
                    }
                }
                throw new KeyNotFoundException($"The army {army} has no group {PathText(path)}");
            }
            return document with { Armies = document.Armies.Update(a => a.Name == army, a => a with { Units = Change(a.Units, 0) }, $"army {army}") };
        }

        /// <summary>
        /// A group with units and groups added to it; a group whose name is taken gets the next free number.
        /// </summary>
        private static UnitGroup Merge(UnitGroup group, ImmutableArray<Unit> units, ImmutableArray<UnitGroup> groups)
        {
            ImmutableArray<UnitGroup> children = group.Groups;
            foreach (UnitGroup child in groups)
            {
                children = children.Add(child with { Name = DocumentEdits.FreeName(children.Select(c => c.Name), child.Name) });
            }
            return group with { Units = group.Units.AddRange(units), Groups = children };
        }

        private static UnitGroup AddInto(UnitGroup group, IReadOnlyList<string> path, int depth, Unit unit)
        {
            if (depth == path.Count)
            {
                return group with { Units = group.Units.Add(unit) };
            }
            for (int i = 0; i < group.Groups.Length; i++)
            {
                if (group.Groups[i].Name == path[depth])
                {
                    return group with { Groups = group.Groups.SetItem(i, AddInto(group.Groups[i], path, depth + 1, unit)) };
                }
            }
            return group with { Groups = group.Groups.Add(AddInto(new UnitGroup { Name = path[depth] }, path, depth + 1, unit)) };
        }

        private static UnitGroup RequireGroup(MapDocument document, string army, IReadOnlyList<string> path)
        {
            if (document.Armies.All(a => a.Name != army))
            {
                throw new KeyNotFoundException($"The map has no army {army}");
            }
            return document.FindGroup(army, path) ?? throw new KeyNotFoundException($"The army {army} has no group {PathText(path)}");
        }

        private static string CheckGroupName(string name)
        {
            string trimmed = name.Trim();
            if (trimmed.Length == 0 || trimmed.Contains('/'))
            {
                throw new ArgumentException($"'{name}' cannot be a group's name: it may not be empty or hold a /");
            }
            return trimmed;
        }

        /// <summary>
        /// The number of the next <c>UNIT_n</c> (<see cref="DocumentEdits.NextUnitName"/>).
        /// </summary>
        private static int NextUnitNumber(MapDocument document) => int.Parse(document.NextUnitName().AsSpan("UNIT_".Length), System.Globalization.CultureInfo.InvariantCulture);

        private static float Wrap(float radians)
        {
            float wrapped = MathF.IEEERemainder(radians, 2 * MathF.PI);
            return wrapped <= -MathF.PI ? wrapped + 2 * MathF.PI : wrapped;
        }
    }

    /// <summary>
    /// Where structures go: the game's build grid. A structure an odd number of ogrids wide sits
    /// on a half ogrid (its centre at 10.5), an even one on a whole ogrid; a structure turns in
    /// quarter turns, which swap its width and depth.
    /// </summary>
    public static class BuildGrid
    {
        /// <summary>
        /// The place a structure's centre snaps to.
        /// </summary>
        /// <param name="sizeX">Its footprint along x when not turned, in ogrids (rounded, at least 1).</param>
        /// <param name="radians">Its heading; an odd number of quarter turns swaps the sizes.</param>
        public static (float X, float Z) Snap(float x, float z, double sizeX, double sizeZ, float radians)
        {
            int quarters = (int)MathF.Round(radians / (MathF.PI / 2));
            int sx = Math.Max(1, (int)Math.Round(sizeX));
            int sz = Math.Max(1, (int)Math.Round(sizeZ));
            if (quarters % 2 != 0)
            {
                (sx, sz) = (sz, sx);
            }
            return (Axis(x, sx), Axis(z, sz));
        }

        /// <summary>
        /// A heading in whole quarter turns, the nearest to the one given.
        /// </summary>
        public static float SnapHeading(float radians) => MathF.Round(radians / (MathF.PI / 2)) * (MathF.PI / 2);

        private static float Axis(float value, int size) => size % 2 == 1 ? MathF.Floor(value) + 0.5f : MathF.Round(value);
    }
}
