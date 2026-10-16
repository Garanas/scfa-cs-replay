using System.Collections.Immutable;
using System.Numerics;
using System.Text;
using FAForever.FileFormats.Lua;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// The files of an adaptive map (user story R-5): options, tables and script after the
    /// convention of FAF's adaptive maps.
    /// </summary>
    [TestClass]
    public class AdaptiveScriptTest
    {
        /// <summary>
        /// A new map of four players with a deposit for each of the first two armies, a shared one,
        /// and civilians by option and by the number of players.
        /// </summary>
        internal static MapDocument Adaptive()
        {
            MapDocument document = NewMap.Create(new NewMapSettings { Name = "Adapt", Size = 256, Players = 4 });
            Vector3 At(float x, float z) => new Vector3(x, document.Terrain.Heightmap.HeightAt(x, z), z);
            document = document
                .AddMarker(new ResourceMarker { Id = 0, Name = "Mass 01", Kind = ResourceKind.Mass, Position = At(40.5f, 40.5f), SpawnWithArmy = "ARMY_1" })
                .AddMarker(new ResourceMarker { Id = 0, Name = "Mass 02", Kind = ResourceKind.Mass, Position = At(200.5f, 40.5f), SpawnWithArmy = "ARMY_2" })
                .AddMarker(new ResourceMarker { Id = 0, Name = "Mass 03", Kind = ResourceKind.Mass, Position = At(128.5f, 128.5f) })
                .AddMarker(new ResourceMarker { Id = 0, Name = "Hydrocarbon 01", Kind = ResourceKind.Hydrocarbon, Position = At(60.5f, 60.5f), SpawnWithArmy = "ARMY_1" });
            document = document.AddOptionalGroup("ARMY_17", "Civilian_Base", 2, out string base2)
                .AddOptionalGroup("ARMY_17", "Civilian_Base", 3, out string base3)
                .AddOptionalGroup("ARMY_17", "Wreckage", 2, out string wrecks)
                .AddOptionalGroup("ARMY_17", AdaptiveScript.PlayersOption, 4, out string crowd);
            Unit Unit(float x) => new Unit { Name = "", Blueprint = "uec1101", Position = At(x, 100) };
            document = document.AddUnit("ARMY_17", [base2], Unit(100) with { Name = "UNIT_1" })
                .AddUnit("ARMY_17", [base3], Unit(110) with { Name = "UNIT_2" })
                .AddUnit("ARMY_17", [wrecks], Unit(120) with { Name = "UNIT_3" })
                .AddUnit("ARMY_17", [crowd], Unit(130) with { Name = "UNIT_4" });
            return document;
        }

        private static IReadOnlyDictionary<string, LuaData> Run(string lua) => LuaSourceParser.Execute(lua, new Dictionary<string, LuaFunction>());

        [TestMethod]
        public void TheExportWritesTheOptionsTablesAndScriptOfAnAdaptiveMap()
        {
            MapDocument document = Adaptive();
            Assert.IsTrue(AdaptiveScript.Adapts(document));
            Assert.IsTrue(AdaptiveScript.Writes(document), "a new map's script is the editor's to replace");

            IReadOnlyDictionary<string, byte[]> files = MapExporter.ToBytes(document);
            string options = Encoding.UTF8.GetString(files["adapt_options.lua"]);
            string tables = Encoding.UTF8.GetString(files["adapt_tables.lua"]);
            string script = Encoding.UTF8.GetString(files["adapt_script.lua"]);

            LuaData.Table list = (LuaData.Table)Run(options)["options"];
            LuaData.Table[] entries = [.. list.Value.Values.OfType<LuaData.Table>()];
            CollectionAssert.AreEqual(new[] { "dynamic_spawn", "optional_civilian_base", "optional_wreckage" },
                entries.Select(entry => entry.TryGetStringValue("key", out string? key) ? key : null).ToArray(), "a lobby option per option of the groups; the players' groups are none");
            entries[1].TryGetTableValue("values", out LuaData.Table? values);
            Assert.AreEqual(3, values!.Value.Count, "disabled, then up to group 2 and 3");

            IReadOnlyDictionary<string, LuaData> read = Run(tables);
            Assert.AreEqual(4, ((LuaData.Number)read["maxPlayerOnMap"]).Value);
            LuaData.Table mexes = (LuaData.Table)read["spwnMexArmy"];
            LuaData.Table[] rows = [.. mexes.Value.Values.OfType<LuaData.Table>()];
            Assert.AreEqual(4, rows.Length, "a row per playable army");
            Assert.AreEqual("01", ((LuaData.String)rows[0].Value.Values.Single()).Value, "the number of ARMY_1's mass deposit");
            Assert.AreEqual(0, rows[2].Value.Count);
            Assert.AreEqual("01", ((LuaData.String)((LuaData.Table)read["spwnHydroArmy"]).Value.Values.OfType<LuaData.Table>().First().Value.Values.Single()).Value);

            StringAssert.StartsWith(script, AdaptiveScript.Header);
            StringAssert.Contains(script, "import('/maps/adapt.v0001/adapt_tables.lua')");
            StringAssert.Contains(script, "if optional_civilian_base >= 3 then");
            StringAssert.Contains(script, "ScenarioUtils.CreateArmyGroup('ARMY_17', 'Optional_Wreckage_2', true)", "wreckage as wrecks");
            StringAssert.Contains(script, "if players >= 4 then");
        }

        [TestMethod]
        public void AScriptOfTheMapMakersOwnIsKept()
        {
            MapDocument document = Adaptive() with { Script = [.. Encoding.UTF8.GetBytes("-- mine\nfunction OnPopulate() end\n")] };
            Assert.IsFalse(AdaptiveScript.Writes(document));
            IReadOnlyDictionary<string, byte[]> files = MapExporter.ToBytes(document);
            Assert.IsFalse(files.ContainsKey("adapt_options.lua"));
            StringAssert.StartsWith(Encoding.UTF8.GetString(files["adapt_script.lua"]), "-- mine");

            MapDocument plain = NewMap.Create(new NewMapSettings { Name = "Plain", Size = 256 });
            Assert.IsFalse(AdaptiveScript.Adapts(plain));
            Assert.IsFalse(MapExporter.ToBytes(plain).ContainsKey("plain_options.lua"), "a map that does not adapt gets no options");
        }

        [TestMethod]
        public void AScriptTheExportWroteIsWrittenAgainAfterAnImport()
        {
            string output = ProjectFolders.New("adaptive");
            string folder = MapExporter.WriteFolder(Adaptive(), output);
            MapDocument imported = MapImporter.ImportFolder(folder);
            Assert.IsTrue(AdaptiveScript.Writes(imported), "its script is the editor's");

            MapDocument changed = imported.AddOptionalGroup("ARMY_17", "Jamming", null, out string jamming)
                .AddUnit("ARMY_17", [jamming], new Unit { Name = "UNIT_9", Blueprint = "uec1101", Position = new Vector3(50, 30, 50) });
            IReadOnlyDictionary<string, byte[]> files = MapExporter.ToBytes(changed);
            StringAssert.Contains(Encoding.UTF8.GetString(files["adapt_options.lua"]), "optional_jamming", "options and tables follow the map");
            StringAssert.Contains(Encoding.UTF8.GetString(files["adapt_script.lua"]), "if optional_jamming >= 2 then");
        }

        [TestMethod]
        public void ThePlayableAreaTheScenarioNamesIsSetByTheScript()
        {
            MapDocument document = Adaptive();
            document = document.AddArea(new Area { Name = "AREA_2", X0 = 10, Z0 = 10, X1 = 200, Z1 = 200 }, out Area area).SetPlayableArea(area.Name);
            string script = Encoding.UTF8.GetString(MapExporter.ToBytes(document)["adapt_script.lua"]);
            StringAssert.Contains(script, "ScenarioFramework.SetPlayableArea('AREA_2'");
        }
    }
}
