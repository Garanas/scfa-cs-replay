using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Rendering;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Generation;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// Opening another map leaves the Procedural mode's view models with nothing of the map before it.
    /// </summary>
    [TestClass]
    public sealed class ProceduralAnotherMapTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";
        private const string Theta = "assets/maps/theta_passage_-_faf_version.v0001";

        private static async Task Run(MainViewModel model, string id, JsonObject? arguments = null)
        {
            CommandRun run = await model.Commands.TryRunAsync(id, arguments ?? [], interactive: false);
            Assert.IsTrue(run.Ran, run.Why);
        }

        [TestMethod]
        public async Task AnotherMapShowsNoResultsOfTheMapBefore()
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            await model.OpenAsync(HardFfa);
            await Run(model, "generate.all");
            await Run(model, "generate.skyCube");
            await Run(model, "generate.setNavigation", new JsonObject { ["region"] = 1 });
            Assert.IsNotNull(model.Navigation.Mesh);
            Assert.IsNotNull(model.Walkability.Navigation);
            Assert.IsNotNull(model.PathNodes.Preview);
            Assert.IsNotNull(model.SkyCube.Preview, "the sky cube map was generated");
            Assert.IsNotNull(model.ProceduralList.PartOf(SkyCubeGenerator.GeneratorId)!.Picture);

            await model.OpenAsync(Theta);

            Assert.IsNull(model.Navigation.Mesh, "navigation mesh");
            Assert.IsNull(model.Navigation.ChosenRegion, "chosen region");
            Assert.AreEqual(0, model.Navigation.Regions.Count);
            Assert.IsNull(model.Walkability.Navigation, "walkability");
            Assert.IsNull(model.PathNodes.Preview, "path nodes");
            Assert.IsNull(model.SkyCube.Preview, "the sky cube map's picture");
            Assert.IsNull(model.ProceduralList.PartOf(SkyCubeGenerator.GeneratorId)!.Picture, "the entry's picture");
            Assert.AreEqual("Not made in this session", model.ProceduralList.PartOf(SkyCubeGenerator.GeneratorId)!.MadeText);
        }
    }
}
