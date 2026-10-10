using System.Text.Json.Nodes;
using FAForever.MapEditor.App.ViewModels;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The status line says what the last operation did, in words that read alike everywhere.
    /// </summary>
    [TestClass]
    public sealed class StatusLineTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private static async Task<MainViewModel> Open()
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            model.Assets.Use(TestAssets.Catalog());
            await model.OpenAsync(HardFfa);
            return model;
        }

        [TestMethod]
        public async Task AStrokeAModeAndASelectionSayWhatTheyDid()
        {
            MainViewModel model = await Open();
            model.Status = "Something from before";
            model.SelectTool("terrain.sculpt");
            model.Stroke([(50, 50), (60, 60)]);
            StringAssert.StartsWith(model.Status, "Done: ", "a stroke says it was done");

            await model.Commands.RunAsync("markers.mode", null, interactive: false);
            StringAssert.StartsWith(model.Status, "Markers mode: ");

            await model.Commands.RunAsync("select.all", null, interactive: false);
            StringAssert.StartsWith(model.Status, "Selected ");
            await model.Commands.RunAsync("select.invert", null, interactive: false);
            Assert.AreEqual("Selected nothing", model.Status);
        }

        [TestMethod]
        public async Task PlacingWithTheSymmetrySaysHowManyImagesItMade()
        {
            MainViewModel model = await Open();
            await model.Commands.RunAsync("select.symmetry", new JsonObject { ["kind"] = "rotational", ["count"] = 4 }, interactive: false);
            await model.Commands.RunAsync("props.place", new JsonObject { ["key"] = "env/evergreen/props/rocks/rock01_prop.bp", ["x"] = 40, ["z"] = 30 }, interactive: false);
            StringAssert.EndsWith(model.Status, " and 3 mirrored");
            Assert.AreEqual("1 unit", MainViewModel.Units(1));
            Assert.AreEqual("2 units", MainViewModel.Units(2));
        }
    }
}
