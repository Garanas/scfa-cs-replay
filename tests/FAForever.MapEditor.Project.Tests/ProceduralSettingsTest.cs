using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;

namespace FAForever.MapEditor.Project.Tests
{
    [TestClass]
    public class ProceduralSettingsTest
    {
        [TestMethod]
        public void TheProjectKeepsThem()
        {
            MapDocument document = MapImporter.ImportFolder(ProjectFolders.HardFfa);
            ProceduralSettings settings = document.Procedural
                .WithAutoGenerate(WalkabilityGenerator.GeneratorId, true)
                .WithOnExport(NormalMapGenerator.GeneratorId, false);
            string project = ProjectFolders.New("procedural");

            MapProjectStore.Save(document with { Procedural = settings }, project);
            MapDocument loaded = MapProjectStore.Load(project);

            Assert.AreEqual(settings, loaded.Procedural);
            Assert.IsTrue(loaded.Procedural.RegeneratesOnItsOwn("walkability"));
            Assert.IsFalse(loaded.Procedural.GeneratesOnExport("normalMap"));
            Assert.IsTrue(loaded.Procedural.GeneratesOnExport("preview"));
        }

        [TestMethod]
        public void TheDefaultsWriteNothing()
        {
            string project = ProjectFolders.New("procedural-default");

            MapProjectStore.Save(MapImporter.ImportFolder(ProjectFolders.HardFfa), project);

            Assert.IsFalse(File.ReadAllText(Path.Combine(project, MapProjectLayout.Map)).Contains("procedural", StringComparison.OrdinalIgnoreCase));
            Assert.AreEqual(ProceduralSettings.Default, MapProjectStore.Load(project).Procedural);
        }

        [TestMethod]
        public void AnOlderProjectWithRoutesStillOpens()
        {
            string project = ProjectFolders.New("procedural-routes");
            MapProjectStore.Save(MapImporter.ImportFolder(ProjectFolders.HardFfa) with { Procedural = ProceduralSettings.Default.WithAutoGenerate("walkability", true) }, project);
            string file = Path.Combine(project, MapProjectLayout.Map);
            string json = File.ReadAllText(file);
            File.WriteAllText(file, json.Replace("\"autoGenerate\"", "\"routes\": \"All\", \"autoGenerate\"", StringComparison.Ordinal));
            StringAssert.Contains(File.ReadAllText(file), "\"routes\"");

            MapDocument loaded = MapProjectStore.Load(project);

            Assert.IsTrue(loaded.Procedural.RegeneratesOnItsOwn("walkability"));
        }

        [TestMethod]
        public void TurningOffAndOnAgainIsTheDefault()
        {
            ProceduralSettings settings = ProceduralSettings.Default.WithAutoGenerate("walkability", true).WithAutoGenerate("walkability", false);

            Assert.IsTrue(settings.IsDefault);
            Assert.AreEqual(ProceduralSettings.Default, settings);
        }
    }
}
