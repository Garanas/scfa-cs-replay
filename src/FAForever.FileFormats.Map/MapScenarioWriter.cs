using FAForever.FileFormats.Lua;

namespace FAForever.FileFormats.Map
{
    /// <summary>
    /// Writes a map's <c>_scenario.lua</c>, the reverse of <see cref="MapScenarioParser"/>: what it
    /// writes parses back to the same scenario.
    /// </summary>
    /// <remarks>
    /// The typed fields of <see cref="MapScenario"/> are the source; the keys it has no field for
    /// (<c>norushoffsetX_ARMY_1</c>, <c>author</c>, a configuration's <c>factions</c>, ...) come from
    /// <see cref="MapScenario.Raw"/> as they are.
    /// </remarks>
    public static class MapScenarioWriter
    {
        private static readonly HashSet<string> ScenarioFields =
        [
            "name", "description", "preview", "map_version", "AdaptiveMap", "type", "starts", "size",
            "reclaim", "map", "save", "script", "norushradius", "Configurations",
        ];

        private static readonly HashSet<string> ConfigurationFields = ["teams", "customprops"];

        public static string Write(MapScenario scenario)
        {
            LuaSourceWriter w = new LuaSourceWriter();
            w.Line("version = 3 -- Lua Version. Dont touch this");
            w.Open("ScenarioInfo = {");

            w.Field("name", LuaSourceWriter.Literal(scenario.Name));
            OptionalString(w, "description", scenario.Description);
            OptionalString(w, "preview", scenario.Preview);
            if (scenario.MapVersion is int version)
            {
                w.Field("map_version", LuaSourceWriter.Literal((double)version));
            }
            if (scenario.AdaptiveMap is bool adaptive)
            {
                w.Field("AdaptiveMap", LuaSourceWriter.Literal(adaptive));
            }
            OptionalString(w, "type", scenario.Type);
            if (scenario.Starts is bool starts)
            {
                w.Field("starts", LuaSourceWriter.Literal(starts));
            }
            w.Field("size", $"{{ {LuaSourceWriter.Literal((double)scenario.Size.Width)}, {LuaSourceWriter.Literal((double)scenario.Size.Height)} }}");
            if (scenario.Reclaim is (double mass, double energy))
            {
                w.Field("reclaim", $"{{ {LuaSourceWriter.Literal(mass)}, {LuaSourceWriter.Literal(energy)} }}");
            }
            OptionalString(w, "map", scenario.Map);
            OptionalString(w, "save", scenario.Save);
            OptionalString(w, "script", scenario.Script);
            if (scenario.NoRushRadius is double radius)
            {
                w.Field("norushradius", LuaSourceWriter.Literal(radius));
            }

            foreach ((string key, LuaData value) in scenario.Raw.Value)
            {
                if (!ScenarioFields.Contains(key))
                {
                    w.Field(key, value);
                }
            }

            w.OpenField("Configurations");
            foreach (MapConfiguration configuration in scenario.Configurations)
            {
                WriteConfiguration(w, configuration, RawConfiguration(scenario, configuration.Name));
            }
            w.Close();

            w.Close("}");
            return w.ToString();
        }

        private static void WriteConfiguration(LuaSourceWriter w, MapConfiguration configuration, LuaData.Table? raw)
        {
            w.OpenField(configuration.Name);

            w.OpenField("teams");
            foreach (MapTeam team in configuration.Teams)
            {
                w.Open("{");
                w.Field("name", LuaSourceWriter.Literal(team.Name));
                w.Field("armies", $"{{ {string.Join(", ", team.Armies.Select(LuaSourceWriter.Literal))} }}");
                w.Close();
            }
            w.Close();

            w.OpenField("customprops");
            foreach ((string key, string value) in configuration.CustomProperties)
            {
                w.Field(key, LuaSourceWriter.Literal(value));
            }
            w.Close();

            foreach ((string key, LuaData value) in raw?.Value ?? [])
            {
                if (!ConfigurationFields.Contains(key))
                {
                    w.Field(key, value);
                }
            }

            w.Close();
        }

        private static LuaData.Table? RawConfiguration(MapScenario scenario, string name) =>
            scenario.Raw.TryGetTableValue("Configurations", out LuaData.Table? configurations)
            && configurations!.TryGetTableValue(name, out LuaData.Table? configuration)
                ? configuration
                : null;

        private static void OptionalString(LuaSourceWriter w, string key, string? value)
        {
            if (value is not null)
            {
                w.Field(key, LuaSourceWriter.Literal(value));
            }
        }
    }
}
