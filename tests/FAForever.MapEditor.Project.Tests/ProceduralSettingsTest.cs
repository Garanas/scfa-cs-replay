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
                .WithOnExport(NormalMapGenerator.GeneratorId, false)
                with { Navigation = new NavigationSettings { Routes = RoutePairs.All } };
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
        public void TurningOffAndOnAgainIsTheDefault()
        {
            ProceduralSettings settings = ProceduralSettings.Default.WithAutoGenerate("walkability", true).WithAutoGenerate("walkability", false);

            Assert.IsTrue(settings.IsDefault);
            Assert.AreEqual(ProceduralSettings.Default, settings);
        }
    }
}
