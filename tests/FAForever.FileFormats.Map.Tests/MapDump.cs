using System.Globalization;
using System.Text;
using FAForever.FileFormats.Lua;
using FAForever.FileFormats.Map;

namespace FAForever.FileFormats.Map.Tests
{
    /// <summary>
    /// The parsed content of a save or scenario as text, to compare two of them: the records hold
    /// lists and tables, which compare by reference. The fields of <c>Raw</c> that have a typed
    /// property are left out, so a dump shows each value once.
    /// </summary>
    internal static class MapDump
    {
        public static string Save(MapSave save)
        {
            StringBuilder b = new StringBuilder();
            foreach (MapMarker marker in save.Markers)
            {
                b.AppendLine($"marker {marker.Name} | {marker.Type} | {V(marker.Position)} | {V(marker.Orientation)} | {Rest(marker.Raw, "type", "position", "orientation")}");
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
                b.AppendLine($"army {army.Name} | {army.Personality} | {army.Plans} | {army.Color} | {army.Faction} | {army.Economy}");
                b.AppendLine($"  alliances {string.Join(" ", army.Alliances.Select(a => $"{a.Key}={a.Value}"))}");
                b.AppendLine($"  rest {Rest(army.Raw, "personality", "plans", "color", "faction", "Economy", "Alliances", "Units", "PlatoonBuilders")}");
                Group(b, army.Units, "  ");
            }
            return b.ToString();
        }

        public static string Scenario(MapScenario scenario)
        {
            StringBuilder b = new StringBuilder();
            b.AppendLine($"{scenario.Name} | {scenario.Description} | {scenario.Type} | {scenario.Starts} | {scenario.Preview} | {scenario.Size}");
            b.AppendLine($"{scenario.Map} | {scenario.Save} | {scenario.Script} | {scenario.MapVersion} | {scenario.NoRushRadius} | {scenario.Reclaim} | {scenario.AdaptiveMap}");
            b.AppendLine($"rest {Rest(scenario.Raw, "name", "description", "preview", "map_version", "AdaptiveMap", "type", "starts", "size", "reclaim", "map", "save", "script", "norushradius", "Configurations")}");
            foreach (MapConfiguration configuration in scenario.Configurations)
            {
                b.AppendLine($"configuration {configuration.Name}");
                foreach (MapTeam team in configuration.Teams)
                {
                    b.AppendLine($"  team {team.Name} | {string.Join(" ", team.Armies)}");
                }
                b.AppendLine($"  customprops {string.Join(" ", configuration.CustomProperties.Select(p => $"{p.Key}={p.Value}"))}");
                if (scenario.Raw.TryGetTableValue("Configurations", out LuaData.Table? all) && all!.TryGetTableValue(configuration.Name, out LuaData.Table? raw))
                {
                    b.AppendLine($"  rest {Rest(raw!, "teams", "customprops")}");
                }
            }
            return b.ToString();
        }

        private static void Group(StringBuilder b, MapUnitGroup group, string indent)
        {
            b.AppendLine($"{indent}group {group.Name} | {group.Orders} | {group.Platoon}");
            foreach (MapUnitGroup inner in group.Groups)
            {
                Group(b, inner, indent + "  ");
            }
            foreach (MapUnit unit in group.Units)
            {
                b.AppendLine($"{indent}  unit {unit.Name} | {unit.BlueprintId} | {V(unit.Position)} | {V(unit.Orientation)} | {unit.Orders} | {unit.Platoon}");
            }
        }

        private static string Rest(LuaData.Table table, params string[] typed) =>
            LuaSourceWriter.Format(new LuaData.Table(table.Value.Where(entry => !typed.Contains(entry.Key)).ToDictionary()));

        private static string V(System.Numerics.Vector3 v) => $"{v.X:R} {v.Y:R} {v.Z:R}";

        private static string N(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    }
}
