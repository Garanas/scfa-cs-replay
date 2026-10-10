using System.Text;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Tests
{
    [TestClass]
    public class PlayableAreaTest
    {
        [ClassCleanup]
        public static void Cleanup() => ProjectFolders.Clean();

        private const string Script = """
            local ScenarioUtils = import('/lua/sim/ScenarioUtilities.lua')
            local ScenarioFramework = import('/lua/ScenarioFramework.lua')

            function OnPopulate()
                ScenarioUtils.InitializeArmies()
                ScenarioFramework.SetPlayableArea('AREA_1' , false)
            end

            function Expand_MapExpand()
                ScenarioFramework.SetPlayableArea('AREA_4' , false)
            end
            """;

        private static MapDocument Map(string? script = null) =>
            NewMap.Create(new NewMapSettings { Size = 512 }).AddArea(new Area { Name = "INNER", X0 = 64, Z0 = 64, X1 = 448, Z1 = 448 }) with
            {
                Script = script is null ? null : [.. Encoding.Latin1.GetBytes(script)],
            };

        private static string ScriptOf(MapDocument document) => Encoding.Latin1.GetString(document.Script!.Value.AsSpan());

        [TestMethod]
        public void ReadsThePlayableAreaOfTheStart()
        {
            Assert.AreEqual("AREA_1", MapScript.PlayableAreaOf(Script));
            Assert.IsNull(MapScript.PlayableAreaOf(NewMap.DefaultScript), "the whole map");
            Assert.IsNull(MapScript.PlayableAreaOf(Script.Replace("    ScenarioFramework.SetPlayableArea('AREA_1'", "    --ScenarioFramework.SetPlayableArea('AREA_1'")),
                "a call behind a comment, and a call that grows the map later, do not count");
            Assert.AreEqual("Start", MapScript.PlayableAreaOf("function OnStart(self)\n    ScenarioFramework.SetPlayableArea(\"Start\")\nend\n"));
        }

        [TestMethod]
        public void WritesThePlayableAreaIntoTheScript()
        {
            string changed = MapScript.WithPlayableArea(Script, "INNER")!;
            Assert.AreEqual("INNER", MapScript.PlayableAreaOf(changed));
            StringAssert.Contains(changed, "SetPlayableArea('AREA_4'", "the later call stays");

            string added = MapScript.WithPlayableArea(NewMap.DefaultScript.ReplaceLineEndings("\r\n"), "INNER")!;
            StringAssert.Contains(added, "    ScenarioUtils.InitializeArmies()\r\n    ScenarioFramework.SetPlayableArea('INNER', false)\r\n");
            Assert.AreEqual("INNER", MapScript.PlayableAreaOf(added));

            Assert.IsNull(MapScript.WithPlayableArea("function OnStart()\nend\n", "INNER"), "no place for it");
        }

        [TestMethod]
        public void FallsBackToTheFirstAreaAndFollowsRenamesAndRemovals()
        {
            MapDocument document = Map();
            Assert.IsNull(document.Scenario.PlayableArea);
            Assert.AreEqual("AREA_1", document.PlayableArea!.Name, "the convention");

            document = document.SetPlayableArea("INNER");
            Assert.AreEqual("INNER", document.PlayableArea!.Name);
            Assert.ThrowsException<KeyNotFoundException>(() => document.SetPlayableArea("NONE"));

            document = document.UpdateArea("INNER", area => area with { Name = "MIDDLE" });
            Assert.AreEqual("MIDDLE", document.Scenario.PlayableArea);

            document = document.RemoveArea("MIDDLE");
            Assert.IsNull(document.Scenario.PlayableArea);
            Assert.AreEqual("AREA_1", document.PlayableArea!.Name);

            MapDocument renamed = Map().UpdateArea("AREA_1", area => area with { Name = "MAIN" });
            Assert.AreEqual("MAIN", renamed.Scenario.PlayableArea, "renaming the first area names it, so the script follows");
        }

        [TestMethod]
        public void KeepsThePlayableAreaInTheProject()
        {
            MapDocument document = Map(NewMap.DefaultScript).SetPlayableArea("INNER");
            string project = ProjectFolders.New("playable");
            MapProjectStore.Save(document, project);
            Assert.AreEqual("INNER", MapProjectStore.Load(project).Scenario.PlayableArea);

            MapProjectStore.Save(document.SetPlayableArea(null), project, document);
            Assert.IsNull(MapProjectStore.Load(project).Scenario.PlayableArea);
        }

        [TestMethod]
        public void ExportsThePlayableAreaThroughTheScriptAndImportsItAgain()
        {
            MapDocument document = Map(Script).SetPlayableArea("INNER");

            string folder = MapExporter.WriteFolder(document, ProjectFolders.New("playable-export"));
            string script = File.ReadAllText(Directory.GetFiles(folder, "*_script.lua").Single(), Encoding.Latin1);
            Assert.AreEqual("INNER", MapScript.PlayableAreaOf(script));
            MapDocument imported = MapImporter.ImportFolder(folder);
            Assert.AreEqual("INNER", imported.Scenario.PlayableArea);
            Assert.AreEqual("INNER", imported.PlayableArea!.Name);

            // the convention exports the script as it is, and reads back as the convention
            MapDocument plain = Map(Script);
            string plainFolder = MapExporter.WriteFolder(plain, ProjectFolders.New("playable-plain"));
            Assert.AreEqual(Script, File.ReadAllText(Directory.GetFiles(plainFolder, "*_script.lua").Single(), Encoding.Latin1));
            Assert.IsNull(MapImporter.ImportFolder(plainFolder).Scenario.PlayableArea);
        }

        [TestMethod]
        public void WalkabilityUsesThePlayableArea()
        {
            MapDocument document = Map().SetPlayableArea("INNER");
            Assert.AreEqual("INNER", Generation.Walkability.PlayableArea(document)!.Name);
            Assert.AreEqual("AREA_1", Generation.Walkability.PlayableArea(Map())!.Name);
        }

        [TestMethod]
        public void TellsTheNextVersion()
        {
            MapDocument document = Map();
            Assert.AreEqual(1, document.Version);
            Assert.AreEqual(2, document.NextVersion);
            Assert.AreEqual(1, (document with { Version = null }).NextVersion);
            Assert.AreEqual("new_map.v0002", (document with { Version = document.NextVersion }).Folder.ToString());
        }
    }
}
