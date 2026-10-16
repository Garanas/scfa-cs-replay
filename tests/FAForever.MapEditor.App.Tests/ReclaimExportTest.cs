using System.Collections.Immutable;
using System.Numerics;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The export step that writes the props' reclaim into the scenario, and the Map panel's
    /// read-only line saying what it will write.
    /// </summary>
    [TestClass]
    public class ReclaimExportTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "reclaim-export");

        private static Prop PropOf(int id, string blueprint, float x) => new Prop
        {
            Id = id,
            Blueprint = blueprint,
            Position = new Vector3(x, 0, 40),
            RotationX = Vector3.UnitX,
            RotationY = Vector3.UnitY,
            RotationZ = Vector3.UnitZ,
            Scale = Vector3.One,
        };

        /// <summary>
        /// HardFFA with two rocks (20 mass each), a pine (30 energy) and a prop the test's assets lack.
        /// </summary>
        private static async Task<MainViewModel> OpenWithProps(bool assets = true)
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            if (assets)
            {
                model.Assets.Use(TestAssets.Catalog());
            }
            await model.OpenAsync(HardFfa);
            model.Map!.Session.Edit("Props", document => document with
            {
                Props =
                [
                    PropOf(1, "/env/evergreen/props/rocks/rock01_prop.bp", 20),
                    PropOf(2, "/env/evergreen/props/rocks/rock01_prop.bp", 30),
                    PropOf(3, "/env/evergreen/props/trees/pine06_prop.bp", 40),
                    PropOf(4, "/env/evergreen/props/rocks/rock99_prop.bp", 50),
                ],
                Scenario = document.Scenario with { Reclaim = new Reclaim { Mass = 1200, Energy = 5 } },
            });
            return model;
        }

        [TestMethod]
        public async Task TheExportWritesThePropsReclaimAndTheProjectKeepsItsOwn()
        {
            MainViewModel model = await OpenWithProps();
            Assert.AreEqual("Reclaim: 40 mass · 30 energy, written at export", model.MapInformation.ReclaimAtExport, "the Map panel says what the export writes");
            Assert.AreEqual("Reclaim: 40 mass · 30 energy, written at export", model.DescribeMapPanels()["information"]!["reclaimAtExport"]!.GetValue<string>());
            Assert.IsFalse(model.MapInformation.Settings.Settings.Any(setting => setting.Label.StartsWith("Reclaim", StringComparison.Ordinal)), "no typed reclaim");

            string folder = await model.ExportAsync(Path.Combine(Root, Guid.NewGuid().ToString("N")));

            ExportStepState step = model.Export.Lines.Single(line => line.Id == MainViewModel.ReclaimExportStep);
            Assert.AreEqual(ExportStepStatus.Done, step.Status);
            Assert.AreEqual("40 mass, 30 energy from 4 props; 1 prop the workspace does not know count as 0: rock99_prop", step.Note);
            Reclaim written = MapImporter.ImportFolder(folder).Scenario.Reclaim!;
            Assert.AreEqual((40.0, 30.0), (written.Mass, written.Energy), "the props' totals, not the typed ones");
            Assert.AreEqual(1200, model.Map!.Document.Scenario.Reclaim!.Mass, "the project keeps what it imported until it exports");
        }

        [TestMethod]
        public async Task AMapWithoutPropsWritesNoReclaimAndAWorkspaceWithoutAssetsKeepsTheMapsOwn()
        {
            MainViewModel model = await OpenWithProps();
            model.Map!.Session.Edit("No props", document => document with { Props = ImmutableArray<Prop>.Empty });
            Assert.AreEqual("Reclaim: none, the map has no props", model.MapInformation.ReclaimAtExport);
            string folder = await model.ExportAsync(Path.Combine(Root, Guid.NewGuid().ToString("N")));
            Assert.AreEqual("no props: no reclaim written", model.Export.Lines.Single(line => line.Id == MainViewModel.ReclaimExportStep).Note);
            Assert.IsNull(MapImporter.ImportFolder(folder).Scenario.Reclaim);

            MainViewModel bare = await OpenWithProps(assets: false);
            folder = await bare.ExportAsync(Path.Combine(Root, Guid.NewGuid().ToString("N")));
            ExportStepState skipped = bare.Export.Lines.Single(line => line.Id == MainViewModel.ReclaimExportStep);
            Assert.AreEqual(ExportStepStatus.Skipped, skipped.Status);
            StringAssert.Contains(skipped.Note, "no assets");
            Assert.AreEqual(1200, MapImporter.ImportFolder(folder).Scenario.Reclaim!.Mass, "without assets the map's own totals are kept");
        }

        [TestMethod]
        public void TheNoteNamesAFewUnknownBlueprints()
        {
            string note = MainViewModel.ReclaimNote(new ReclaimTotal(1234.4, 50, 10, 5), ["/a/one_prop.bp", "/a/two_prop.bp", "/a/three_prop.bp", "/a/four_prop.bp"]);
            Assert.AreEqual("1,234 mass, 50 energy from 10 props; 5 props the workspace does not know count as 0: one_prop, two_prop, three_prop and 1 more", note);
        }
    }
}
