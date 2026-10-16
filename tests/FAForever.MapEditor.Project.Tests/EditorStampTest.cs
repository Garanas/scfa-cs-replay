using System.Text;
using FAForever.FileFormats.Lua;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Files.Serialized;

namespace FAForever.MapEditor.Project.Tests
{
    [TestClass]
    public class EditorStampTest
    {
        private static readonly EditorStamp Stamp = new EditorStamp(EditorStamp.EditorName, "1.0.0", "0cb76c2", 6, new DateTimeOffset(2026, 10, 10, 18, 20, 0, TimeSpan.Zero));

        [TestMethod]
        public void NamesTheEditorInTheScenario()
        {
            MapDocument document = MapImporter.ImportFolder(ProjectFolders.HardFfa);

            IReadOnlyDictionary<string, byte[]> files = MapExporter.ToBytes(document, Stamp);

            string scenario = Encoding.Latin1.GetString(files["HardFFA_scenario.lua"]);
            StringAssert.StartsWith(scenario, "-- Made with the FAF Map Editor 1.0.0 (0cb76c2) on 2026-10-10\nversion = 3");
            LuaData.Table editor = (LuaData.Table)MapScenarioParser.Parse(scenario).Raw.Value["map_editor"];
            Assert.AreEqual("FAF Map Editor", ((LuaData.String)editor.Value["name"]).Value);
            Assert.AreEqual("1.0.0", ((LuaData.String)editor.Value["version"]).Value);
            Assert.AreEqual("0cb76c2", ((LuaData.String)editor.Value["commit"]).Value);
            Assert.AreEqual(6, ((LuaData.Number)editor.Value["project_format"]).Value);
            Assert.AreEqual("2026-10-10T18:20:00Z", ((LuaData.String)editor.Value["exported"]).Value);
        }

        [TestMethod]
        public void TheStampChangesNothingTheGameReads()
        {
            MapDocument document = MapImporter.ImportFolder(ProjectFolders.HardFfa);
            string plain = Encoding.Latin1.GetString(MapExporter.ToBytes(document)["HardFFA_scenario.lua"]);
            string stamped = Encoding.Latin1.GetString(MapExporter.ToBytes(document, Stamp)["HardFFA_scenario.lua"]);

            Assert.IsFalse(plain.Contains("map_editor"), "no stamp unless given: two exports give the same bytes");
            MapScenario parsed = MapScenarioParser.Parse(stamped);
            Dictionary<string, LuaData> withoutStamp = new Dictionary<string, LuaData>(parsed.Raw.Value);
            Assert.IsTrue(withoutStamp.Remove("map_editor"));
            Assert.AreEqual(GameContent.Scenario(MapScenarioParser.Parse(plain)), GameContent.Scenario(parsed with { Raw = new LuaData.Table(withoutStamp) }), "the stamp is the only difference");
            // imported again, the stamp is left out: the next export writes its own
            string folder = MapExporter.WriteFolder(document, ProjectFolders.New("stamped"), Stamp);
            MapDocument imported = MapImporter.ImportFolder(folder);
            Assert.IsFalse(Encoding.Latin1.GetString(MapExporter.ToBytes(imported)["HardFFA_scenario.lua"]).Contains("map_editor"));
        }

        [TestMethod]
        public void LeavesAnUnknownCommitOut()
        {
            EditorStamp unknown = Stamp with { Commit = null };

            Assert.IsFalse(unknown.ToLua().Value.ContainsKey("commit"));
            Assert.AreEqual("-- Made with the FAF Map Editor 1.0.0 (commit unknown) on 2026-10-10", unknown.Comment);
        }

        [TestMethod]
        public void NowIsThisBuildToTheSecond()
        {
            EditorStamp now = EditorStamp.Now();

            Assert.AreEqual(EditorBuild.Version, now.Version);
            Assert.IsFalse(now.Version.Contains('+'), now.Version);
            Assert.AreEqual(EditorBuild.ShortCommit, now.Commit);
            Assert.AreEqual(MapProjectDto.CurrentFormatVersion, now.ProjectFormat);
            Assert.AreEqual(0, now.Exported.Ticks % TimeSpan.TicksPerSecond);
            Assert.AreEqual(TimeSpan.Zero, now.Exported.Offset);
        }

        [TestMethod]
        public void TheEditorsExportIsStamped()
        {
            EditedMap map = EditedMap.ImportMap(ProjectFolders.HardFfa);

            string folder = map.Export(ProjectFolders.New("edited"));

            StringAssert.StartsWith(File.ReadAllText(Path.Combine(folder, "HardFFA_scenario.lua")), "-- Made with the FAF Map Editor ");
        }
    }
}
