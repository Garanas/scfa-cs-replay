using System.Globalization;
using System.Text;
using FAForever.FileFormats.Lua;
using FAForever.FileFormats.Map;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// What the game reads from a save or scenario, as text, to compare a map with its export: the
    /// markers by name with the fields that differ per marker, without the decoration the export
    /// derives from the type (colour, prop, hint, ...), and without the orders and platoons of unit
    /// groups, which the editor does not keep.
    /// </summary>
    internal static class GameContent
    {
        private static readonly HashSet<string> Decoration = ["color", "prop", "editorIcon", "hint", "graph", "resource", "size", "amount"];

        public static string Save(MapSave save)
        {
            StringBuilder b = new StringBuilder();
            foreach (MapMarker marker in save.Markers.OrderBy(m => m.Name, StringComparer.Ordinal))
            {
                b.AppendLine($"marker {marker.Name} | {marker.Type} | {V(marker.Position)} | {V(marker.Orientation)} | {Fields(marker.Raw, new HashSet<string>([.. MapMarker.TypedKeys, .. Decoration]))}");
            }
            foreach (MapArea area in save.Areas)
            {
                b.AppendLine($"area {area.Name} | {N(area.X0)} {N(area.Z0)} {N(area.X1)} {N(area.Z1)}");
            }
            foreach (MapChain chain in save.Chains)
            {
                b.AppendLine($"chain {chain.Name} | {string.Join(" ", chain.Markers)}");
            }
            foreach (MapArmy army in save.Armies)
            {
                // a missing colour or faction is the first, as the export writes it
                b.AppendLine($"army {army.Name} | {army.Personality} | {army.Plans} | {army.Color ?? 0} | {army.Faction ?? 0} | {army.Economy}");
                b.AppendLine($"  alliances {string.Join(" ", army.Alliances.OrderBy(a => a.Key, StringComparer.Ordinal).Select(a => $"{a.Key}={a.Value}"))}");
                Group(b, army.Units, "  ");
            }
            return b.ToString();
        }

        public static string Scenario(MapScenario scenario)
        {
            StringBuilder b = new StringBuilder();
            b.AppendLine($"{scenario.Name} | {scenario.Description} | {scenario.Type} | {scenario.Starts} | {scenario.Preview} | {scenario.Size}");
            b.AppendLine($"{scenario.Map} | {scenario.Save} | {scenario.Script} | {scenario.MapVersion} | {scenario.NoRushRadius} | {scenario.Reclaim} | {scenario.AdaptiveMap}");
            b.AppendLine($"rest {Fields(scenario.Raw, MapScenario.TypedKeys)}");
            foreach (MapConfiguration configuration in scenario.Configurations)
            {
                b.AppendLine($"configuration {configuration.Name}");
                foreach (MapTeam team in configuration.Teams)
                {
                    b.AppendLine($"  team {team.Name} | {string.Join(" ", team.Armies)}");
                }
                b.AppendLine($"  customprops {string.Join(" ", configuration.CustomProperties.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}={p.Value}"))}");
                b.AppendLine($"  rest {Fields(configuration.Raw, MapConfiguration.TypedKeys)}");
            }
            return b.ToString();
        }

        private static void Group(StringBuilder b, MapUnitGroup group, string indent)
        {
            b.AppendLine($"{indent}group {group.Name}");
            foreach (MapUnitGroup inner in group.Groups)
            {
                Group(b, inner, indent + "  ");
            }
            foreach (MapUnit unit in group.Units)
            {
                b.AppendLine($"{indent}  unit {unit.Name} | {unit.BlueprintId} | {V(unit.Position)} | {V(unit.Orientation)}");
            }
        }

        /// <summary>
        /// The entries of a table but the left out ones, by key.
        /// </summary>
        /// <remarks>
        /// A path node without links has an empty <c>adjacentTo</c> in most maps and none in a few:
        /// both mean no links, and the export writes the empty one.
        /// </remarks>
        private static string Fields(LuaData.Table table, IReadOnlySet<string> leftOut) =>
            LuaSourceWriter.Format(new LuaData.Table(table.Value
                .Where(entry => !leftOut.Contains(entry.Key))
                .Where(entry => !(entry.Key == "adjacentTo" && entry.Value is LuaData.String { Value: "" }))
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .ToDictionary()));

        /// <summary>
        /// A vector with -0 as 0, which the editor does not tell apart.
        /// </summary>
        private static string V(System.Numerics.Vector3 v)
        {
            v += System.Numerics.Vector3.Zero;
            return $"{v.X:R} {v.Y:R} {v.Z:R}";
        }

        private static string N(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    }
}
