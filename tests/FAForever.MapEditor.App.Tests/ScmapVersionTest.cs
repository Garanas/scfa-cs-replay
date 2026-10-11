using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The file version in the Map mode's Information (<c>map.setScmapVersion</c>): a version 56 map
    /// written as 60 gets the skybox the game gave it, as one step of undo, and the Lighting mode's
    /// Skybox entry offers the same.
    /// </summary>
    [TestClass]
    public class ScmapVersionTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private static async Task<MainViewModel> Open()
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            await model.OpenAsync(HardFfa);
            return model;
        }

        private static ChoiceSetting FileVersion(MainViewModel model) =>
            model.MapInformation.FileSettings.Settings.OfType<ChoiceSetting>().Single(setting => setting.Label == "File version");

        [TestMethod]
        public async Task TheFileVersionFieldWritesTheMapAs60WithTheGamesSkybox()
        {
            MainViewModel model = await Open();
            Assert.AreEqual(56, model.Map!.Document.Terrain.ScmapVersion);
            ChoiceSetting field = FileVersion(model);
            Assert.AreEqual("56", field.Chosen);
            CollectionAssert.AreEqual(new[] { "60", "56" }, field.Choices.ToArray());
            int steps = model.History.Count;

            field.Chosen = "60";

            Assert.AreEqual(60, model.Map.Document.Terrain.ScmapVersion);
            Assert.AreEqual(Skyboxes.GameDefault(model.Map.Document), model.Map.Document.Sky.Skybox);
            Assert.AreEqual(steps + 1, model.History.Count);
            Assert.AreEqual("Write the map as version 60", model.Map.Session.UndoDescription);
            Assert.AreEqual("60", FileVersion(model).Chosen, "the field follows");
            StringAssert.Contains(model.Status, "skybox the game gave it");
            Assert.IsTrue(model.LightingSettings.Skybox.HasSkybox);
            Assert.AreEqual("", model.LightingSettings.Skybox.VersionNote);

            model.Undo();
            Assert.AreEqual(56, model.Map.Document.Terrain.ScmapVersion);
            Assert.IsNull(model.Map.Document.Sky.Skybox);
            Assert.AreEqual("56", FileVersion(model).Chosen);
        }

        [TestMethod]
        public async Task TheSkyboxEntryOffersToWriteTheMapAs60()
        {
            MainViewModel model = await Open();
            await model.Commands.RunAsync("lighting.addSkybox", [], interactive: false);
            SkyboxPart part = model.LightingSettings.Skybox;
            Assert.IsTrue(part.CanWriteAsVersion60);
            StringAssert.Contains(part.VersionNote, "default dome");

            await part.WriteAsVersion60Command.ExecuteAsync(null);

            Assert.AreEqual(60, model.Map!.Document.Terrain.ScmapVersion);
            Assert.IsFalse(part.CanWriteAsVersion60);
            Assert.AreEqual("", part.VersionNote);
            Assert.IsNotNull(model.Map.Document.Sky.Skybox, "the skybox it had stays");
        }

        [TestMethod]
        public async Task OtherVersionsAreRefusedWithWhy()
        {
            MainViewModel model = await Open();

            CommandRun run = await model.Commands.TryRunAsync("map.setScmapVersion", new JsonObject { ["version"] = 58 }, interactive: false);

            Assert.IsFalse(run.Ran);
            StringAssert.Contains(run.Why, "60 or 56");
        }
    }
}
