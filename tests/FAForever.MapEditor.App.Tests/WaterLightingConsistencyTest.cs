using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Rendering;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Generation;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The Water and Lighting modes show the open map and nothing of an earlier one: the wave
    /// generator that is chosen, the sky cube map's picture.
    /// </summary>
    [TestClass]
    public class WaterLightingConsistencyTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "water-lighting-consistency");

        [ClassCleanup]
        public static void Cleanup()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private static async Task<MainViewModel> Open()
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            await model.OpenAsync(HardFfa);
            return model;
        }

        private static Task Run(MainViewModel model, string id, JsonObject? arguments = null) => model.Commands.RunAsync(id, arguments ?? [], interactive: false);

        [TestMethod]
        public async Task AddingAWaveGeneratorChoosesTheNewOneAsPlacingOneDoes()
        {
            MainViewModel model = await Open();
            await model.WaterSettings.AddGeneratorCommand.ExecuteAsync(null);
            await model.WaterSettings.AddGeneratorCommand.ExecuteAsync(null);
            await Run(model, "water.addWaveGenerator", new JsonObject { ["x"] = 20, ["z"] = 30 });

            Assert.AreEqual(3, model.WaterSettings.Generators.Count);
            Assert.AreEqual(2, model.WaterSettings.SelectedGenerator?.Index, "the last one added is the chosen one");

            await Run(model, "water.placeWaveGenerator", new JsonObject { ["x"] = 60, ["z"] = 60 });
            Assert.AreEqual(3, model.WaterSettings.SelectedGenerator?.Index, "placing one chooses it too");
        }

        [TestMethod]
        public async Task AnotherMapDoesNotKeepTheChosenWaveGeneratorsPlace()
        {
            string folder = Path.Combine(Root, Guid.NewGuid().ToString("N"));
            MainViewModel model = await Open();
            for (int i = 0; i < 3; i++)
            {
                await Run(model, "water.addWaveGenerator", new JsonObject { ["x"] = 10 + i, ["z"] = 10 });
            }
            await model.SaveAsAsync(folder);
            Assert.AreEqual(2, model.WaterSettings.SelectedGenerator?.Index);

            await model.OpenAsync(folder);

            Assert.AreEqual(3, model.WaterSettings.Generators.Count);
            Assert.AreEqual(0, model.WaterSettings.SelectedGenerator?.Index, "a map opened has none of the earlier map's choice: the first");

            // and a map without any shows none
            await model.OpenAsync(HardFfa);
            Assert.AreEqual(0, model.WaterSettings.Generators.Count);
            Assert.IsNull(model.WaterSettings.SelectedGenerator);
            Assert.IsFalse(model.WaterSettings.HasSelectedGenerator);
        }

        [TestMethod]
        public async Task AResizeKeepsTheChosenWaveGenerator()
        {
            MainViewModel model = await Open();
            for (int i = 0; i < 3; i++)
            {
                await Run(model, "water.addWaveGenerator", new JsonObject { ["x"] = 10 + i, ["z"] = 10 });
            }
            Assert.AreEqual(2, model.WaterSettings.SelectedGenerator?.Index);
            int width = model.Map!.Document.Width;
            await Run(model, "map.resize", new JsonObject { ["width"] = width * 2, ["height"] = width * 2 });
            Assert.AreEqual(2, model.WaterSettings.SelectedGenerator?.Index, "a resize keeps the map, so it keeps the choice");
        }

        [TestMethod]
        public async Task TheSkyCubeMapsPictureIsOfTheOpenMap()
        {
            MainViewModel model = await Open();
            await Run(model, "generate.setSkyCube", new JsonObject { ["resolution"] = 512, ["terrain"] = false });
            await Run(model, "generate.skyCube");
            Assert.IsInstanceOfType<TexturePicture>(model.SkyCube.Preview);

            await model.OpenAsync(HardFfa);

            Assert.IsNull(model.SkyCube.Preview, "the faces made for the map that went are not shown for this one");
            Assert.AreEqual("Not generated yet", model.SkyCube.Status);
            Assert.IsFalse(model.ProceduralList.PartOf(SkyCubeGenerator.GeneratorId)!.HasPicture);
        }
    }
}
