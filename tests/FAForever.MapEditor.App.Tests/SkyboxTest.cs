using System.Numerics;
using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The Lighting panel's Skybox entry (user story L-1): the version 60 sky dome listed under Sky,
    /// added from the lighting, read from a preset, edited field by field in the inspector (the dome,
    /// sky objects, cirrus and its layers) and removed, each a step of undo.
    /// </summary>
    [TestClass]
    public class SkyboxTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "skybox");

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

        private static T Setting<T>(SettingGroup group, string label) where T : Setting => group.Settings.OfType<T>().Single(setting => setting.Label == label);

        private static Task<CommandRun> Run(MainViewModel model, string id, JsonObject? arguments = null) =>
            model.Commands.RunAsync(id, arguments ?? [], interactive: false);

        private static Task<CommandRun> TryRun(MainViewModel model, string id, JsonObject? arguments = null) =>
            model.Commands.TryRunAsync(id, arguments ?? [], interactive: false);

        private static SkyboxPart Part(MainViewModel model) => model.LightingSettings.Skybox;

        [TestMethod]
        public async Task TheLightingPanelListsItUnderSkyAndAMapWithoutOneOffersToAddIt()
        {
            MainViewModel model = await Open();
            Assert.IsNull(model.Map!.Document.Sky.Skybox, "the test map has no skybox");

            PanelPart entry = model.LightingSettings.Parts.Find(LightingSettingsViewModel.SkyboxPart)!;
            Assert.AreEqual("Skybox", entry.Title);
            Assert.AreEqual("Sky", entry.Group);
            Assert.AreSame(Part(model), entry.Content);
            Assert.AreEqual("none", entry.Detail);
            Assert.IsFalse(Part(model).HasSkybox);
            Assert.IsTrue(Part(model).AddCommand.CanExecute(null));
            Assert.IsFalse(Part(model).RemoveCommand.CanExecute(null));
            StringAssert.Contains(Part(model).Summary, "no skybox");

            int steps = model.History.Count;
            await Run(model, "lighting.addSkybox");

            Skybox skybox = model.Map.Document.Sky.Skybox!;
            Assert.IsNotNull(skybox);
            Assert.IsTrue(skybox.Planets.IsEmpty);
            Assert.AreEqual(steps + 1, model.History.Count);
            Assert.AreEqual("Add a skybox", model.Map.Session.UndoDescription);
            Assert.IsTrue(Part(model).HasSkybox);
            Assert.AreEqual("0 objects", entry.Detail);
            Assert.AreEqual(LightingSettingsViewModel.SkyboxPart, model.LightingSettings.Parts.Chosen.Id, "adding shows it in the inspector");
            Assert.AreEqual("lighting", model.SelectedMode!.Definition.Id);
            Assert.IsTrue(Setting<ColorSetting>(Part(model).Dome, "Horizon colour").IsEnabled);
            Assert.AreEqual(ColorSetting.SwatchOf(skybox.HorizonColor), Setting<ColorSetting>(Part(model).Dome, "Horizon colour").Swatch);
            Assert.IsFalse((await TryRun(model, "lighting.addSkybox")).Ran, "a second is refused");

            model.Undo();
            Assert.IsNull(model.Map.Document.Sky.Skybox);
            Assert.IsFalse(Part(model).HasSkybox, "the part follows an undo");
        }

        [TestMethod]
        public async Task ItsFieldsEditTheSkyboxOneStepAFieldTypedAgain()
        {
            MainViewModel model = await Open();
            await Run(model, "lighting.addSkybox");
            int steps = model.History.Count;

            ColorSetting horizon = Setting<ColorSetting>(Part(model).Dome, "Horizon colour");
            horizon.Text = "0.2, 0.3, 0.4";
            horizon.Text = "0.25, 0.3, 0.4";
            Assert.AreEqual(new Vector3(0.25f, 0.3f, 0.4f), model.Map!.Document.Sky.Skybox!.HorizonColor);
            Assert.AreEqual(steps + 1, model.History.Count, "typing in one field again is one step");

            Setting<NumberSetting>(Part(model).Dome, "Zenith height").Text = "300";
            Setting<ColorSetting>(Part(model).Dome, "Position").Text = "-10, 5, 256";
            Setting<TextureSetting>(Part(model).SkyObjects, "Atlas").Text = "/maps/x/env/decals/sky_albedo.dds";
            Setting<NumberSetting>(Part(model).Cirrus, "Cirrus strength").Value = 0.5;

            Skybox skybox = model.Map.Document.Sky.Skybox!;
            Assert.AreEqual(300, skybox.ZenithHeight);
            Assert.AreEqual(new Vector3(-10, 5, 256), skybox.Position, "a place may be negative");
            Assert.AreEqual("/maps/x/env/decals/sky_albedo.dds", skybox.Albedo);
            Assert.AreEqual(0.5f, skybox.CirrusMultiplier);

            Assert.IsFalse((await TryRun(model, "lighting.setSkybox", new JsonObject { ["scale"] = 0 })).Ran, "a dome has a size");
            Assert.IsFalse((await TryRun(model, "lighting.setSkybox", new JsonObject { ["subdivisionsAxis"] = 2 })).Ran);
        }

        [TestMethod]
        public async Task ItAlwaysHasFourCirrusLayersEditedOneByOne()
        {
            MainViewModel model = await Open();
            await Run(model, "lighting.addSkybox");
            Assert.AreEqual(4, model.Map!.Document.Sky.Skybox!.CirrusLayers.Length, "the game always reads four");
            Assert.AreEqual(4, Part(model).CirrusLayers.Count);
            Assert.AreEqual("Cirrus layer 4 (alpha)", Part(model).CirrusLayers[3].Title);
            Assert.IsNull(model.Commands.Find("lighting.addCirrusLayer"), "layers are not added or removed");

            Setting<NumberSetting>(Part(model).CirrusLayers[1], "Layer 2 speed").Text = "0.3";
            await Run(model, "lighting.setCirrusLayer", new JsonObject { ["index"] = 1, ["directionX"] = -1, ["frequencyY"] = 2 });
            CirrusLayer layer = model.Map.Document.Sky.Skybox!.CirrusLayers[1];
            Assert.AreEqual(0.3f, layer.Speed);
            Assert.AreEqual(-1, layer.Direction.X);
            Assert.AreEqual(2, layer.Frequency.Y);
            Assert.AreEqual("0.3", Setting<NumberSetting>(Part(model).CirrusLayers[1], "Layer 2 speed").Text, "the field shows the map's value");
            Assert.IsFalse((await TryRun(model, "lighting.setCirrusLayer", new JsonObject { ["index"] = 4, ["speed"] = 1 })).Ran);
        }

        [TestMethod]
        public async Task WithoutASkyboxItsCommandsSayWhy()
        {
            MainViewModel model = await Open();

            CommandRun run = await TryRun(model, "lighting.setSkybox", new JsonObject { ["scale"] = 100 });
            Assert.IsFalse(run.Ran);
            StringAssert.Contains(run.Why, "no skybox");
            Assert.IsFalse((await TryRun(model, "lighting.removeSkybox")).Ran);

            await Run(model, "lighting.setSkybox");
            Assert.AreEqual(LightingSettingsViewModel.SkyboxPart, model.LightingSettings.Parts.Chosen.Id, "without arguments it shows the entry");
        }

        [TestMethod]
        public async Task APresetOfTheFafMapEditorIsReadAndShownAndRemoved()
        {
            MainViewModel model = await Open();
            Directory.CreateDirectory(Root);
            string file = Path.Combine(Root, "sky.scmskybox");
            File.WriteAllText(file, """
                {"Data": {"Position": {"x": 256, "y": 0, "z": 256}, "Scale": 1171.6, "SubtractHeight": 1.25, "SubdivisionsAxis": 16,
                  "SubdivisionsHeight": 6, "HorizonHeight": -50, "ZenithHeight": 256, "HorizonColor": {"r": 0.6, "g": 0.4, "b": 0.3, "a": 1},
                  "ZenithColor": {"r": 0, "g": 0, "b": 0, "a": 1}, "DecalGlowMultiplier": 0.33, "Albedo": "", "Glow": "",
                  "Planets": [{"Position": {"x": -2500, "y": 350, "z": -5000}, "Rotation": -0.2, "Scale": {"x": 1900, "y": 1900}, "Uv": {"x": 0, "y": 0.5, "z": 0.5, "w": 0.5}},
                              {"Position": {"x": 6000, "y": 233, "z": -700}, "Rotation": 0, "Scale": {"x": 100, "y": 100}, "Uv": {"x": 0, "y": 0, "z": 0.5, "w": 0.5}}],
                  "MidRgbColor": {"r": 0, "g": 0, "b": 0, "a": 0}, "CirrusMultiplier": 0, "CirrusColor": {"r": 1, "g": 1, "b": 1, "a": 1},
                  "CirrusTexture": "", "CirrusLayers": [], "Clouds7": 0}}
                """);

            await Run(model, "lighting.importSkybox", new JsonObject { ["path"] = file });

            Assert.AreEqual(LightingSettingsViewModel.SkyboxPart, model.LightingSettings.Parts.Chosen.Id);
            Assert.AreEqual("2 objects", model.LightingSettings.Parts.Chosen.Detail);
            StringAssert.Contains(Part(model).Summary, "2 sky objects");
            Assert.AreEqual("-50", Setting<NumberSetting>(Part(model).Dome, "Horizon height").Text);
            Assert.AreEqual(4, model.Map!.Document.Sky.Skybox!.CirrusLayers.Length, "a preset without layers gets the game's four");

            await Run(model, "lighting.removeSkybox");
            Assert.IsNull(model.Map!.Document.Sky.Skybox);
            Assert.AreEqual("Remove the skybox", model.Map.Session.UndoDescription);
            Assert.IsFalse(Part(model).HasSkybox);
        }

        [TestMethod]
        public async Task AMapOfAnEarlierVersionSaysTheGameDrawsNoSkybox()
        {
            MainViewModel model = await Open();
            await Run(model, "lighting.addSkybox");

            int version = model.Map!.Document.Terrain.ScmapVersion;
            if (version < 60)
            {
                StringAssert.Contains(Part(model).VersionNote, $"version {version} map");
                StringAssert.Contains(Part(model).VersionNote, "default dome", "the game draws its own dome for such a map");
            }
            await Run(model, "map.setScmapVersion", new JsonObject { ["version"] = 60 });
            Assert.AreEqual("", Part(model).VersionNote);
        }
    }
}
