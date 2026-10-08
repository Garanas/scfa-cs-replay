using System.Globalization;
using FAForever.FileFormats.Lua;

namespace FAForever.FileFormats.Map
{
    /// <summary>
    /// Writes a map's <c>_save.lua</c>, the reverse of <see cref="MapSaveParser"/>: what it writes
    /// parses back to the same markers, areas, chains and armies.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The layout follows the save files of the FAF map editor: the fields of markers and areas in
    /// the wrappers of <c>lua/dataInit.lua</c> (<see cref="MapLuaFunctions.Wrap"/>), the armies with
    /// their unit groups in <c>GROUP { ... }</c>.
    /// </para>
    /// <para>
    /// The typed fields are the source: a marker's type, position and orientation come from
    /// <see cref="MapMarker"/>, its other fields (<c>amount</c>, <c>adjacentTo</c>, ...) from its
    /// <see cref="MapMarker.Raw"/>; likewise for the armies. The platoons, orders and platoon
    /// builders of co-op missions are not kept: they are written empty. The editor's counters
    /// (<c>next_unit_id</c>, ...) are kept from <see cref="MapSave.Raw"/> and raised past the names
    /// in use, so that an editor never hands out a name twice.
    /// </para>
    /// </remarks>
    public static class MapSaveWriter
    {
        private static readonly HashSet<string> MarkerFields = ["type", "position", "orientation"];

        private static readonly HashSet<string> ArmyFields =
            ["personality", "plans", "color", "faction", "Economy", "Alliances", "Units", "PlatoonBuilders"];

        public static string Write(MapSave save)
        {
            LuaSourceWriter w = new LuaSourceWriter();
            Banner(w, "Automatically generated code (do not edit)");
            Banner(w, "Scenario");
            w.Open("Scenario = {");

            w.Field("next_area_id", LuaSourceWriter.Literal(NextId(save, "next_area_id", save.Areas.Select(area => area.Name), "AREA_")));

            Banner(w, "Props");
            w.Field("Props", "{}");

            Banner(w, "Areas");
            w.OpenField("Areas");
            foreach (MapArea area in save.Areas)
            {
                w.OpenField(area.Name);
                w.Field("rectangle", $"RECTANGLE( {L(area.X0)}, {L(area.Z0)}, {L(area.X1)}, {L(area.Z1)} )");
                w.Close();
            }
            w.Close();

            Banner(w, "Markers");
            w.OpenField("MasterChain").OpenField("_MASTERCHAIN_").OpenField("Markers");
            foreach (MapMarker marker in save.Markers)
            {
                w.OpenField(marker.Name);
                foreach ((string key, LuaData value) in marker.Raw.Value)
                {
                    if (!MarkerFields.Contains(key))
                    {
                        w.Field(key, value, MapLuaFunctions.Wrap);
                    }
                }
                w.Field("type", $"STRING( {LuaSourceWriter.Literal(marker.Type)} )");
                w.Field("orientation", MapLuaFunctions.Vector3(marker.Orientation));
                w.Field("position", MapLuaFunctions.Vector3(marker.Position));
                w.Close();
            }
            w.Close().Close().Close();

            w.OpenField("Chains");
            foreach (MapChain chain in save.Chains)
            {
                w.OpenField(chain.Name).OpenField("Markers");
                foreach (string name in chain.Markers)
                {
                    w.Line(LuaSourceWriter.Literal(name) + ",");
                }
                w.Close().Close();
            }
            w.Close();

            w.Field("next_queue_id", LuaSourceWriter.Literal(NextId(save, "next_queue_id", [], "")));
            Banner(w, "Orders");
            w.Field("Orders", "{}");
            w.Field("next_platoon_id", LuaSourceWriter.Literal(NextId(save, "next_platoon_id", [], "")));
            Banner(w, "Platoons");
            w.Field("Platoons", "{}");

            List<MapUnitGroup> groups = save.Armies.SelectMany(army => AllGroups(army.Units)).ToList();
            w.Field("next_army_id", LuaSourceWriter.Literal(NextId(save, "next_army_id", save.Armies.Select(army => army.Name), "ARMY_", save.Armies.Count + 1)));
            w.Field("next_group_id", LuaSourceWriter.Literal(NextId(save, "next_group_id", [], "", groups.Count + 1)));
            w.Field("next_unit_id", LuaSourceWriter.Literal(NextId(save, "next_unit_id", groups.SelectMany(group => group.Units).Select(unit => unit.Name), "UNIT_")));

            Banner(w, "Armies");
            w.OpenField("Armies");
            foreach (MapArmy army in save.Armies)
            {
                Banner(w, army.Name);
                WriteArmy(w, army);
            }
            w.Close();

            w.Close("}");
            return w.ToString();
        }

        private static void WriteArmy(LuaSourceWriter w, MapArmy army)
        {
            w.OpenField(army.Name);
            if (army.Personality is not null)
            {
                w.Field("personality", LuaSourceWriter.Literal(army.Personality));
            }
            if (army.Plans is not null)
            {
                w.Field("plans", LuaSourceWriter.Literal(army.Plans));
            }
            if (army.Color is int color)
            {
                w.Field("color", L((double)color));
            }
            if (army.Faction is int faction)
            {
                w.Field("faction", L((double)faction));
            }
            if (army.Economy is MapEconomy economy)
            {
                w.OpenField("Economy");
                w.Field("mass", L(economy.Mass));
                w.Field("energy", L(economy.Energy));
                w.Close();
            }
            w.OpenField("Alliances");
            foreach ((string other, string stance) in army.Alliances)
            {
                w.Field(other, LuaSourceWriter.Literal(stance));
            }
            w.Close();
            foreach ((string key, LuaData value) in army.Raw.Value)
            {
                if (!ArmyFields.Contains(key))
                {
                    w.Field(key, value);
                }
            }
            WriteGroup(w, army.Units);
            w.OpenField("PlatoonBuilders");
            w.Field("next_platoon_builder_id", "'1'");
            w.Field("Builders", "{}");
            w.Close();
            w.Close();
        }

        private static void WriteGroup(LuaSourceWriter w, MapUnitGroup group)
        {
            w.OpenField(group.Name, "GROUP ");
            WriteOrders(w, group.Orders, group.Platoon);
            w.OpenField("Units");
            foreach (MapUnitGroup inner in group.Groups)
            {
                WriteGroup(w, inner);
            }
            foreach (MapUnit unit in group.Units)
            {
                w.OpenField(unit.Name);
                w.Field("type", LuaSourceWriter.Literal(unit.BlueprintId));
                WriteOrders(w, unit.Orders, unit.Platoon);
                w.Field("Position", $"{{ {L(unit.Position.X)}, {L(unit.Position.Y)}, {L(unit.Position.Z)} }}");
                w.Field("Orientation", $"{{ {L(unit.Orientation.X)}, {L(unit.Orientation.Y)}, {L(unit.Orientation.Z)} }}");
                w.Close();
            }
            w.Close();
            w.Close();
        }

        private static void WriteOrders(LuaSourceWriter w, string? orders, string? platoon)
        {
            if (orders is not null)
            {
                w.Field("orders", LuaSourceWriter.Literal(orders));
            }
            if (platoon is not null)
            {
                w.Field("platoon", LuaSourceWriter.Literal(platoon));
            }
        }

        private static IEnumerable<MapUnitGroup> AllGroups(MapUnitGroup group) =>
            group.Groups.SelectMany(AllGroups).Prepend(group);

        /// <summary>
        /// A counter of the editor: the value in the file, raised past every name of the form
        /// <paramref name="prefix"/> and a number (<c>UNIT_302</c>) and past <paramref name="least"/>.
        /// </summary>
        private static string NextId(MapSave save, string key, IEnumerable<string> names, string prefix, int least = 1)
        {
            int next = least;
            if (save.Raw.TryGetStringValue(key, out string? text) && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int stored))
            {
                next = Math.Max(next, stored);
            }
            if (prefix.Length > 0)
            {
                foreach (string name in names)
                {
                    if (name.StartsWith(prefix, StringComparison.Ordinal)
                        && int.TryParse(name.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int number))
                    {
                        next = Math.Max(next, number + 1);
                    }
                }
            }
            return next.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// A comment in the boxed style of the map editors.
        /// </summary>
        private static void Banner(LuaSourceWriter w, string title)
        {
            const int Width = 75;
            title = title.Replace("]]", "] ]"); // would end the comment
            w.Line($"--[[{new string(' ', Width)}]]--");
            w.Line($"--[[  {title.PadRight(Width - 2)}]]--");
            w.Line($"--[[{new string(' ', Width)}]]--");
        }

        private static string L(double value) => LuaSourceWriter.Literal(value);

        private static string L(float value) => LuaSourceWriter.Literal(value);
    }
}
