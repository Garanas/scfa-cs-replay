using System.Numerics;
using System.Text.Json;
using FAForever.FileFormats.Map;

namespace FAForever.MapEditor.Project.Workspaces
{
    /// <summary>
    /// A group of a real map chosen to become a units preset (<see cref="UnitTemplates"/>).
    /// </summary>
    /// <param name="Name">The preset's name, e.g. <c>Aeon base, small</c>.</param>
    /// <param name="Map">The map folder's name, e.g. <c>X1CA_002</c>, found under the folders scanned;
    /// with the folders above it (<c>Supreme Commander/maps/SCMP_012</c>) when two folders hold a map of that name.</param>
    /// <param name="Army">The army whose group it is.</param>
    /// <param name="Path">The group's path below the army's root group, joined with <c>/</c>; empty for the whole army.</param>
    /// <param name="Role">The army role it gets (<see cref="ArmyRoles"/>).</param>
    /// <param name="Wrecks">Whether it is placed as wrecks.</param>
    /// <param name="Leave">Groups to leave out, by name or by role (<c>Engineers</c>), anywhere below the group.</param>
    /// <param name="Area">Only the units inside this rectangle of the map (x0, z0, x1, z1 in ogrids).</param>
    /// <param name="Blueprints">Only units of these blueprints.</param>
    /// <param name="Radius">Only the units within this many ogrids of the middle of all of them (the
    /// median of their places), to leave out a base's far outposts.</param>
    /// <param name="CivilianOnly">Only civilian units (<see cref="UnitGroupScan.IsCivilianUnit"/>) and none
    /// of the civilian wall segments (<see cref="UnitGroupScan.IsCivilianWall"/>): a town without its walls
    /// and defences.</param>
    /// <param name="Without">Units of these blueprints left out.</param>
    public sealed record UnitTemplatePick(
        string Name,
        string Map,
        string Army,
        string Path,
        string Role,
        bool Wrecks = false,
        IReadOnlyList<string>? Leave = null,
        IReadOnlyList<float>? Area = null,
        IReadOnlyList<string>? Blueprints = null,
        float? Radius = null,
        bool CivilianOnly = false,
        IReadOnlyList<string>? Without = null);

    /// <summary>
    /// Makes units presets from groups of real maps (<c>faf-map dev unit-templates</c>): the units of
    /// a group's subtree around their middle, sorted into the base's role groups (<c>Defense</c>,
    /// <c>Factories</c>, <c>Energy</c>, <c>Mass</c>, ...) when they were in one, the difficulty
    /// levels of a campaign base (<c>_D1</c> inside <c>_D2</c> inside <c>_D3</c>) merged.
    /// </summary>
    public static class UnitTemplates
    {
        /// <summary>
        /// The roles in the order a template lists their groups.
        /// </summary>
        public static IReadOnlyList<string> RoleOrder { get; } = ["Factories", "Energy", "Mass", "Defense", "AA", "Shields", "Torpedo", "Artillery", "Walls", "Misc", "Engineers"];

        /// <summary>
        /// The name a role group gets in a template: the campaign's several names for one role made one.
        /// </summary>
        public static string CanonicalRole(string name) => name.ToLowerInvariant() switch
        {
            "defense" or "defenses" or "defence" or "defences" or "turrets" or "towers" => "Defense",
            "factories" or "factory" or "production" => "Factories",
            "energy" or "hydro" or "storage" => "Energy",
            "mass" => "Mass",
            "engineers" => "Engineers",
            "shields" => "Shields",
            "aa" => "AA",
            "torpedo" or "torpedoes" => "Torpedo",
            "artillery" => "Artillery",
            "walls" => "Walls",
            _ => "Misc",
        };

        /// <summary>
        /// Reads a JSON file of picks (an array of <see cref="UnitTemplatePick"/>, camel case).
        /// </summary>
        /// <exception cref="MapProjectException">The file cannot be read.</exception>
        public static IReadOnlyList<UnitTemplatePick> ReadPicks(string file) => ProjectJson.Read<UnitTemplatePick[]>(file);

        /// <summary>
        /// Makes the preset of each pick from the maps under the folders.
        /// </summary>
        /// <returns>The presets, in the order of the picks; and what went wrong with a pick, which is left out.</returns>
        public static (IReadOnlyList<Preset> Presets, IReadOnlyList<string> Problems) Make(IReadOnlyList<UnitTemplatePick> picks, IEnumerable<string> folders)
        {
            string[] mapFolders = [.. folders.SelectMany(UnitGroupScan.MapFolders).Select(folder => System.IO.Path.GetFullPath(folder).Replace('\\', '/'))];
            Dictionary<string, MapSave> saves = new Dictionary<string, MapSave>(StringComparer.OrdinalIgnoreCase);
            List<Preset> presets = [];
            List<string> problems = [];
            foreach (UnitTemplatePick pick in picks)
            {
                try
                {
                    if (!saves.TryGetValue(pick.Map, out MapSave? save))
                    {
                        string[] found = [.. mapFolders.Where(folder => folder.EndsWith("/" + pick.Map.Replace('\\', '/').Trim('/'), StringComparison.OrdinalIgnoreCase))];
                        string folder = found.Length switch
                        {
                            0 => throw new ArgumentException($"no map {pick.Map} under the folders"),
                            1 => found[0],
                            _ => throw new ArgumentException($"several maps are called {pick.Map}: {string.Join(", ", found)}; name the folder above it too"),
                        };
                        save = saves[pick.Map] = UnitGroupScan.Read(folder).Save;
                    }
                    presets.Add(Preset.Of(UnitPresets.Kind, pick.Name, Make(pick, save)));
                }
                catch (Exception exception) when (exception is ArgumentException or IOException or FormatException or KeyNotFoundException)
                {
                    problems.Add($"{pick.Name}: {exception.Message}");
                }
            }
            return (presets, problems);
        }

        /// <summary>
        /// The preset of a pick from its map's save.
        /// </summary>
        /// <exception cref="ArgumentException">The army or group is missing, or nothing is left of it.</exception>
        public static UnitPresetSettings Make(UnitTemplatePick pick, MapSave save)
        {
            MapArmy army = save.Armies.FirstOrDefault(a => a.Name == pick.Army) ?? throw new ArgumentException($"{pick.Map} has no army {pick.Army}");
            MapUnitGroup group = army.Units;
            foreach (string part in pick.Path.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                group = group.Groups.FirstOrDefault(g => g.Name == part) ?? throw new ArgumentException($"{pick.Map}'s army {pick.Army} has no group {pick.Path}");
            }
            HashSet<string> leave = new HashSet<string>(pick.Leave ?? [], StringComparer.OrdinalIgnoreCase);
            HashSet<string>? blueprints = pick.Blueprints is null ? null : new HashSet<string>(pick.Blueprints, StringComparer.OrdinalIgnoreCase);
            HashSet<string> without = new HashSet<string>(pick.Without ?? [], StringComparer.OrdinalIgnoreCase);
            List<(string? Role, MapUnit Unit)> units = [];
            Gather(group, null, true);
            void Gather(MapUnitGroup g, string? role, bool top)
            {
                if (!top)
                {
                    string bare = UnitGroupScan.PatternOf(g.Name);
                    bool isRole = UnitGroupScan.RoleNames.Contains(bare);
                    if (leave.Contains(g.Name) || leave.Contains(bare) || (isRole && leave.Contains(CanonicalRole(bare))))
                    {
                        return;
                    }
                    if (isRole)
                    {
                        role ??= CanonicalRole(bare);
                    }
                }
                foreach (MapUnit unit in g.Units)
                {
                    if (Inside(unit.Position) && Kept(unit.BlueprintId))
                    {
                        units.Add((role, unit));
                    }
                }
                foreach (MapUnitGroup child in g.Groups)
                {
                    Gather(child, role, false);
                }
            }
            bool Kept(string blueprint) =>
                (blueprints is null || blueprints.Contains(blueprint))
                && !without.Contains(blueprint)
                && (!pick.CivilianOnly || (UnitGroupScan.IsCivilianUnit(blueprint) && !UnitGroupScan.IsCivilianWall(blueprint)));
            bool Inside(Vector3 p) => pick.Area is not { Count: 4 } a || (p.X >= a[0] && p.Z >= a[1] && p.X <= a[2] && p.Z <= a[3]);

            // the same unit in two difficulty levels counts once
            units = [.. units.DistinctBy(u => (u.Unit.BlueprintId.ToLowerInvariant(), MathF.Round(u.Unit.Position.X * 2), MathF.Round(u.Unit.Position.Z * 2)))];
            if (pick.Radius is float radius && units.Count > 0)
            {
                Vector2 median = new Vector2(Median(units.Select(u => u.Unit.Position.X)), Median(units.Select(u => u.Unit.Position.Z)));
                units = [.. units.Where(u => Vector2.Distance(new Vector2(u.Unit.Position.X, u.Unit.Position.Z), median) <= radius)];
            }
            if (units.Count == 0)
            {
                throw new ArgumentException($"{pick.Map}'s group {pick.Path} of {pick.Army} has no units left");
            }
            float cx = MathF.Round((units.Min(u => u.Unit.Position.X) + units.Max(u => u.Unit.Position.X)) / 2);
            float cz = MathF.Round((units.Min(u => u.Unit.Position.Z) + units.Max(u => u.Unit.Position.Z)) / 2);
            UnitPresetUnit Relative(MapUnit unit) => new UnitPresetUnit(unit.BlueprintId.ToLowerInvariant(), unit.Position.X - cx, 0, unit.Position.Z - cz, Heading(unit.Orientation.Y));

            UnitPresetUnit[] loose = [.. units.Where(u => u.Role is null).Select(u => Relative(u.Unit))];
            UnitPresetGroup[] roles = [.. units.Where(u => u.Role is not null)
                .GroupBy(u => u.Role!)
                .OrderBy(g => RoleOrder.IndexOf(g.Key) is int index and >= 0 ? index : RoleOrder.Count)
                .Select(g => new UnitPresetGroup(g.Key, [.. g.Select(u => Relative(u.Unit))], []))];
            string source = pick.Path.Length == 0 ? $"{pick.Map}, army {pick.Army}" : $"{pick.Map}, army {pick.Army}, group {pick.Path}";
            return new UnitPresetSettings(new UnitPresetGroup(pick.Name, loose, roles), units.Count, pick.Role, pick.Wrecks, source);
        }

        private static float Median(IEnumerable<float> values)
        {
            float[] sorted = [.. values.Order()];
            return sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
        }

        /// <summary>
        /// A heading within (-π, π], rounded to a thousandth of a radian so the files stay short.
        /// </summary>
        private static float Heading(float radians)
        {
            float wrapped = MathF.IEEERemainder(radians, MathF.Tau);
            return MathF.Round(wrapped, 3);
        }

        /// <summary>
        /// Writes presets as the workspace keeps them (<c>&lt;folder&gt;/&lt;name&gt;.json</c>).
        /// </summary>
        /// <returns>The files.</returns>
        public static IReadOnlyList<string> Write(IEnumerable<Preset> presets, string folder) =>
            [.. presets.Select(preset => new PresetLibrary(folder).Save(preset))];

        private static int IndexOf(this IReadOnlyList<string> list, string value)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == value)
                {
                    return i;
                }
            }
            return -1;
        }
    }
}
