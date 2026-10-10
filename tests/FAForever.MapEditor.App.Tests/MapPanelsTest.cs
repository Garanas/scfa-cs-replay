using System.Numerics;
using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The panels of the map and its atmosphere (user stories M-1, M-2, M-5, M-6, W-1, L-1, L-2,
    /// S-2, T-3, V-5): their fields and buttons change the map through commands, as steps of undo.
    /// </summary>
    [TestClass]
    public class MapPanelsTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "map-panels");

        [ClassCleanup]
        public static void Cleanup()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private static string NewFolder() => Path.Combine(Root, Guid.NewGuid().ToString("N"));

        private static async Task<MainViewModel> Open()
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            await model.OpenAsync(HardFfa);
            return model;
        }

        private static T Setting<T>(SettingGroup group, string label) where T : Setting => group.Settings.OfType<T>().Single(setting => setting.Label == label);

        private static Task<CommandRun> Run(MainViewModel model, string id, JsonObject? arguments = null) => model.Commands.RunAsync(id, arguments ?? [], interactive: false);

        [TestMethod]
        public async Task TheInformationFieldsEditTheMapOneStepAFieldTypedAgain()
        {
            MainViewModel model = await Open();
            TextSetting name = Setting<TextSetting>(model.MapInformation.Settings, "Name");
            Assert.AreEqual("HardFFA", name.Text);
            int steps = model.History.Count;

            name.Text = "Hard FFA";
            name.Text = "Hard FFA 2";
            Assert.AreEqual("Hard FFA 2", model.Map!.Document.Scenario.Name);
            Assert.AreEqual(steps + 1, model.History.Count, "typing in one field again is one step");

            NumberSetting radius = Setting<NumberSetting>(model.MapInformation.Settings, "No-rush radius");
            radius.Value = 70;
            radius.Value = 80;
            Setting<BoolSetting>(model.MapInformation.Settings, "Adaptive map").IsOn = true;
            Setting<ChoiceSetting>(model.MapInformation.Settings, "Type").Chosen = "campaign_coop";
            Assert.AreEqual(80, model.Map.Document.Scenario.NoRushRadius);
            Assert.IsTrue(model.Map.Document.Scenario.AdaptiveMap);
            Assert.AreEqual(ScenarioType.CampaignCoop, model.Map.Document.Scenario.Type);
            Assert.AreEqual(steps + 4, model.History.Count);

            radius.Text = "a lot";
            Assert.AreEqual("80", radius.Text, "a typo shows the value again");
            Assert.AreEqual(steps + 4, model.History.Count);

            model.Undo();
            model.Undo();
            Assert.AreEqual(ScenarioType.Skirmish, model.Map.Document.Scenario.Type);
            Assert.AreEqual("skirmish", Setting<ChoiceSetting>(model.MapInformation.Settings, "Type").Chosen, "the field follows the undo");

            await Run(model, "map.information", new JsonObject { ["version"] = 7, ["reclaimMass"] = 1200 });
            Assert.AreEqual(7, model.Map.Document.Version);
            Assert.AreEqual(1200, model.Map.Document.Scenario.Reclaim!.Mass);
            Assert.AreEqual("hardffa.v0007", model.MapInformation.Folder.ToLowerInvariant());
            Assert.IsNotNull(model.MapInformation.Preview);
            StringAssert.StartsWith(model.MapInformation.Size, "5 km, 256 by 256");
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "map.information", new JsonObject { ["type"] = "moon" }));

            await Run(model, "map.information");
            Assert.AreEqual("map", model.SelectedMode!.Definition.Id, "without arguments it shows the Map panel");
        }

        [TestMethod]
        public async Task TheStepOfASecondAreaNamesIt()
        {
            MainViewModel model = await Open();
            await Run(model, "map.addArea");
            Assert.AreEqual("AREA_2", (string)(await Run(model, "map.addArea")).Result!);
            Assert.AreEqual("Add the area AREA_2", model.Map!.Session.UndoDescription, "the step names the area added");
        }

        [TestMethod]
        public async Task AreasAreAddedRenamedResizedAndRemoved()
        {
            MainViewModel model = await Open();
            Assert.IsTrue(model.AreaList.IsEmpty, "HardFFA has no areas");

            string name = (string)(await Run(model, "map.addArea")).Result!;
            Assert.AreEqual("AREA_1", name);
            Assert.AreEqual(1, model.Map!.Document.Markers.Areas.Length);
            Assert.IsTrue(model.AreaList.Items[0].IsPlayable, "the first area is the playable area");
            Assert.AreEqual("map.areas", model.ActiveTool!.Definition.Id, "the areas tool, whose handles resize it");
            CollectionAssert.AreEqual(new[] { ObjectRef.Area(name) }, model.Selection.Items.ToArray());
            Area added = model.Map.Document.Markers.Areas[^1];
            Assert.AreEqual(96, added.X0, "a square a quarter of the map in its middle");
            Assert.AreEqual(160, added.X1);

            // the handle at the bottom right corner, dragged 10 ogrids right and 6 down: one step
            int steps = model.History.Count;
            model.Stroke([(160, 160), (165, 163), (170, 166)]);
            Area resized = model.Map.Document.Markers.Areas[^1];
            Assert.AreEqual((96.0, 96.0, 170.0, 166.0), (resized.X0, resized.Z0, resized.X1, resized.Z1));
            Assert.AreEqual(steps + 1, model.History.Count);
            Assert.AreEqual(8, model.Tool<AreaTool>().Marks!.Handles.Count, "the selected area's handles");

            // inside it: moved
            model.Stroke([(130, 130), (140, 130)]);
            Assert.AreEqual(106, model.Map.Document.Markers.Areas[^1].X0);

            // on empty ground: a new area drawn
            model.Selection.Clear();
            model.Stroke([(10, 200), (30, 220), (40, 240)]);
            Area drawn = model.Map.Document.Markers.Areas[^1];
            Assert.AreEqual((10.0, 200.0, 40.0, 240.0), (drawn.X0, drawn.Z0, drawn.X1, drawn.Z1));

            AreaItem item = model.AreaList.Items.Single(area => area.Area.Name == name);
            model.Selection.Set([ObjectRef.Area(name)]);
            item.Name = "BASE";
            Assert.IsTrue(model.Map.Document.Markers.Areas.Any(area => area.Name == "BASE"));
            CollectionAssert.AreEqual(new[] { ObjectRef.Area("BASE") }, model.Selection.Items.ToArray(), "it stays selected under its new name");
            AreaSelection inspector = model.Selection.Inspector.Sections.OfType<AreaSelection>().Single();
            Assert.IsTrue(inspector.IsSingle);
            Setting<NumberSetting>(inspector.Fields, "Left").Text = "100";
            Assert.AreEqual(100, model.Map.Document.Markers.Areas.Single(area => area.Name == "BASE").X0);

            await Run(model, "map.removeArea", new JsonObject { ["name"] = "BASE" });
            Assert.IsFalse(model.Map.Document.Markers.Areas.Any(area => area.Name == "BASE"));
            await Run(model, "map.addArea", new JsonObject { ["name"] = "OTHER", ["x0"] = 1, ["z0"] = 1, ["x1"] = 9, ["z1"] = 9 });
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "map.setArea", new JsonObject { ["name"] = drawn.Name, ["rename"] = "OTHER" }), "names are unique");
        }

        [TestMethod]
        public async Task OpensTheScriptAndListsTheMapsFiles()
        {
            MainViewModel model = await Open();
            Assert.IsFalse(model.Commands.CanRun(model.Commands.Find("file.openScript")!), "an imported map has no project");
            Assert.IsFalse(model.ScriptFiles.IsProject);
            StringAssert.Contains(model.ScriptFiles.Script, "no script");

            string folder = NewFolder();
            await model.SaveAsAsync(folder);
            Assert.IsTrue(model.ScriptFiles.IsProject);
            List<string> opened = [];
            model.OpenFileRequested += opened.Add;

            string script = (string)(await Run(model, "file.openScript")).Result!;
            Assert.AreEqual(Path.Combine(model.Map!.ProjectFolder!, MapProjectLayout.Script), script);
            StringAssert.Contains(File.ReadAllText(script), "ScenarioUtils.InitializeArmies()", "a map without a script gets a skirmish map's");
            Assert.AreEqual(0, opened.Count, "without a person nothing opens");
            await model.Commands.RunAsync("file.openScript");
            CollectionAssert.AreEqual(new[] { script }, opened);
        }

        [TestMethod]
        public async Task MakesANewMap()
        {
            MainViewModel model = await Open();
            await model.Commands.RunAsync("file.new");
            Assert.IsTrue(model.NewMapPage.IsOpen, "with a person, without arguments, the page shows");
            model.NewMapPage.IsOpen = false;

            CommandRun run = await Run(model, "file.new", new JsonObject { ["name"] = "Dunes", ["size"] = 256, ["biome"] = "desert", ["water"] = false, ["players"] = 4 });

            Assert.IsTrue(run.Ran);
            Assert.AreEqual("Dunes", model.Map!.Document.Scenario.Name);
            Assert.AreEqual(256, model.Map.Document.Width);
            Assert.IsFalse(model.Map.Document.Water.HasWater);
            Assert.AreEqual(4, model.Map.Document.Markers.Spawns.Length);
            Assert.IsNull(model.Map.ProjectFolder);
            Assert.IsTrue(model.Map.Session.IsModified, "closing it asks to save it");
            Assert.AreEqual("Dunes", Setting<TextSetting>(model.MapInformation.Settings, "Name").Text);

            model.NewMapPage.Height = "high";
            Assert.IsNull(model.NewMapPage.Arguments());
            Assert.IsNotNull(model.NewMapPage.Error);
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "file.new", new JsonObject { ["size"] = 300 }));
        }

        [TestMethod]
        public async Task TheWaterFieldsAreStepsAndKeepDeepBelowTheSurface()
        {
            MainViewModel model = await Open();
            int steps = model.History.Count;
            NumberSetting surface = Setting<NumberSetting>(model.WaterSettings.Basic, "Surface");

            surface.Value = 10;
            surface.Value = 12;
            Assert.AreEqual(12, model.Map!.Document.Water.Elevation);
            Assert.AreEqual(steps + 1, model.History.Count, "a slider dragged is one step");
            Assert.IsTrue(model.Map.Document.Water.ElevationDeep <= 12, "deep stays below the surface");

            Setting<ColorSetting>(model.WaterSettings.Basic, "Surface colour").Text = "0.1, 0.5, 2";
            Assert.AreEqual(new Vector3(0.1f, 0.5f, 2), model.Map.Document.Water.SurfaceColor);
            Assert.AreEqual("#1a80ff", Setting<ColorSetting>(model.WaterSettings.Basic, "Surface colour").Swatch);
            Setting<BoolSetting>(model.WaterSettings.Basic, "Water").IsOn = !model.Map.Document.Water.HasWater;
            Setting<TextureSetting>(model.WaterSettings.Textures, "Ramp").Text = "/textures/engine/waterramp_tundra.dds";
            Assert.AreEqual("/textures/engine/waterramp_tundra.dds", model.Map.Document.Water.Ramp);

            Setting<NumberSetting>(model.WaterSettings.WaveTextures[2], "Repeat").Text = "0.25";
            Assert.AreEqual(0.25f, model.Map.Document.Water.WaveTextures[2].NormalRepeat);

            Assert.AreEqual(0, model.WaterSettings.Generators.Count);
            await model.WaterSettings.AddGeneratorCommand.ExecuteAsync(null);
            Assert.AreEqual(1, model.Map.Document.Water.WaveGenerators.Length);
            Assert.AreEqual(model.WaterSettings.Generators[0], model.WaterSettings.SelectedGenerator);
            Setting<NumberSetting>(model.WaterSettings.Generator, "X").Text = "40";
            Setting<ColorSetting>(model.WaterSettings.Generator, "Velocity").Text = "-0.01, 0, 0.02";
            Assert.AreEqual(new Vector3(40, model.Map.Document.Water.Elevation, 128), model.Map.Document.Water.WaveGenerators[0].Position);
            Assert.AreEqual(-0.01f, model.Map.Document.Water.WaveGenerators[0].Velocity.X, "a velocity may be negative");
            await model.WaterSettings.RemoveGeneratorCommand.ExecuteAsync(null);
            Assert.AreEqual(0, model.Map.Document.Water.WaveGenerators.Length);
        }

        [TestMethod]
        public async Task TheLightingTurnsByItsAnglesAndWithTheSunTool()
        {
            MainViewModel model = await Open();
            Vector3 east = SunAngles.Direction(90, 30);
            Assert.AreEqual(0.866, east.X, 0.001, "east is to the right in the top view");
            Assert.AreEqual(0.5, east.Y, 0.001);
            (double azimuth, double elevation) = SunAngles.Of(SunAngles.Direction(225, 40));
            Assert.AreEqual(225, azimuth, 0.01);
            Assert.AreEqual(40, elevation, 0.01);

            int steps = model.History.Count;
            NumberSetting height = Setting<NumberSetting>(model.LightingSettings.Sun, "Elevation");
            height.Value = 20;
            height.Value = 25;
            Assert.AreEqual(25, SunAngles.Of(model.Map!.Document.Lighting.SunDirection).Elevation, 0.01);
            Assert.AreEqual(steps + 1, model.History.Count);
            Setting<ColorSetting>(model.LightingSettings.Fog, "Fog colour").Text = "0.5 0.5 0.6";
            Assert.AreEqual(new Vector3(0.5f, 0.5f, 0.6f), model.Map.Document.Lighting.FogColor);

            // the sun tool: dragged to the middle of the right edge, the sun stands low in the east
            model.SelectTool("lighting.sun");
            steps = model.History.Count;
            model.Stroke([(150, 128), (200, 128), (256, 128)]);
            (azimuth, elevation) = SunAngles.Of(model.Map.Document.Lighting.SunDirection);
            Assert.AreEqual(90, azimuth, 0.5);
            Assert.AreEqual(SunTool.LowestElevation, elevation, 0.5);
            Assert.AreEqual(steps + 1, model.History.Count, "a drag is one step");
            Assert.AreEqual(90, Setting<NumberSetting>(model.LightingSettings.Sun, "Azimuth").Value, 0.5, "the panel follows");
        }

        [TestMethod]
        public async Task SavesAndAppliesLightingAndWaterPresets()
        {
            MainViewModel model = await Open();
            model.Presets.Use(new PresetLibrary(NewFolder()));
            model.SelectMode("lighting");
            Assert.AreEqual("lighting", model.Presets.Kind, "the Lighting panel lists the lighting presets");
            Lighting geothermal = model.Map!.Document.Lighting;
            await Run(model, "edit.saveAsPreset", new JsonObject { ["name"] = "Geothermal" });

            await Run(model, "lighting.set", new JsonObject { ["multiplier"] = 3, ["sunColor"] = new JsonArray(1, 0, 0) });
            Assert.AreEqual(3, model.Map.Document.Lighting.Multiplier);
            await Run(model, "lighting.preset", new JsonObject { ["name"] = "Geothermal" });
            Assert.AreEqual(geothermal, model.Map.Document.Lighting);
            model.Undo();
            Assert.AreEqual(3, model.Map.Document.Lighting.Multiplier, "applying a preset is a step of undo");

            model.SelectMode("water");
            Assert.AreEqual("water", model.Presets.Kind);
            await Run(model, "edit.saveAsPreset", new JsonObject { ["name"] = "Lava lake" });
            float elevation = model.Map.Document.Water.Elevation;
            await Run(model, "water.set", new JsonObject { ["surfaceColor"] = new JsonArray(0, 0, 0), ["elevation"] = elevation + 5 });
            await Run(model, "presets.apply", new JsonObject { ["kind"] = "water", ["name"] = "Lava lake" });
            Assert.AreEqual(new Vector3(1.03f, 1.03f, 0), model.Map.Document.Water.SurfaceColor);
            Assert.AreEqual(elevation + 5, model.Map.Document.Water.Elevation, "the elevations are the map's");
        }

        [TestMethod]
        public async Task LayersGetTexturesFromThePickerAndMoveWithTheirMasks()
        {
            MainViewModelTest.Answers answers = new MainViewModelTest.Answers();
            MainViewModel model = new MainViewModel(answers);
            await model.OpenAsync(HardFfa);
            model.Assets.Use(TestAssets.Catalog());
            Assert.AreEqual(10, model.LayerTextures.Items.Count);
            Assert.AreEqual("Upper layer", model.LayerTextures.Items[0].Title, "the upper layer first, as they lie on each other");
            StringAssert.StartsWith(model.LayerTextures.Summary, "4 of 8 strata used (version 56)");

            model.LayerTextures.Choose(2);
            answers.Assets.Enqueue("env/tundra/layers/tund_rock_albedo.dds");
            await model.LayerTextures.Layer.Settings.OfType<TextureSetting>().First().ChooseCommand.ExecuteAsync(null);
            Assert.AreEqual((AssetKind.Texture, "Choose the texture of the stratum 2"), (answers.AssetQuestions[^1].Kind, answers.AssetQuestions[^1].Title));
            Assert.AreEqual("/env/tundra/layers/tund_rock_albedo.dds", model.Map!.Document.Terrain.Layers[2].AlbedoPath);
            Assert.AreEqual("/env/tundra/layers/tund_rock_normal.dds", model.Map.Document.Terrain.Layers[2].NormalPath, "with its normal map");
            Assert.AreEqual("Layer 2: tund_rock_albedo", model.StrataTool.Strata[1].Name, "the strata tool's list follows");

            int steps = model.History.Count;
            NumberSetting scale = Setting<NumberSetting>(model.LayerTextures.Layer, "Albedo scale");
            scale.Text = "8";
            scale.Text = "9";
            Assert.AreEqual(9, model.Map.Document.Terrain.Layers[2].AlbedoScale);
            Assert.AreEqual(steps + 1, model.History.Count);

            int added = (int)(await Run(model, "paint.addStratum", new JsonObject { ["key"] = "env/evergreen/layers/eg_grass001_albedo.dds" })).Result!;
            Assert.AreEqual(5, added);
            Assert.AreEqual(5, model.LayerTextures.SelectedIndex, "the new stratum is chosen");

            TiledBuffer<byte> mask = model.Map.Document.Terrain.StratumMasks.Layers[1];
            model.LayerTextures.Choose(2);
            await model.LayerTextures.MoveUpCommand.ExecuteAsync(null);
            Assert.AreEqual("/env/tundra/layers/tund_rock_albedo.dds", model.Map.Document.Terrain.Layers[3].AlbedoPath);
            Assert.AreSame(mask, model.Map.Document.Terrain.StratumMasks.Layers[2], "the mask moves with it");
            Assert.AreEqual(3, model.LayerTextures.SelectedIndex);

            await model.LayerTextures.RemoveCommand.ExecuteAsync(null);
            Assert.AreEqual(4, model.Map.Document.Terrain.Layers.Count(layer => layer.IsUsed()) - 2);
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "paint.chooseTexture", new JsonObject { ["layer"] = 1, ["key"] = "env/nowhere/layers/x_albedo.dds" }));
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Run(model, "paint.chooseTexture", new JsonObject { ["layer"] = 1 }), "without a person the key is an argument");
        }

        [TestMethod]
        public async Task ImportsAndExportsTheHeightmap()
        {
            MainViewModel model = await Open();
            string folder = NewFolder();
            string exported = Path.Combine(folder, "heights.r16");

            await Run(model, "terrain.exportHeightmap", new JsonObject { ["path"] = exported });
            Assert.AreEqual(257 * 257 * 2, new FileInfo(exported).Length);

            // a heightmap of half the size, all at one height
            string small = Path.Combine(folder, "small.r16");
            byte[] bytes = new byte[129 * 129 * 2];
            for (int i = 0; i < bytes.Length; i += 2)
            {
                bytes[i + 1] = 20;
            }
            File.WriteAllBytes(small, bytes);
            int steps = model.History.Count;
            await Run(model, "terrain.importHeightmap", new JsonObject { ["path"] = small });
            Assert.AreEqual(20 * 256, model.Map!.Document.Terrain.Heightmap.Samples[200, 17]);
            Assert.AreEqual(steps + 1, model.History.Count);
            StringAssert.Contains(model.Status, "resampled from 129 by 129");

            await Run(model, "terrain.importHeightmap", new JsonObject { ["path"] = exported });
            CollectionAssert.AreNotEqual(new ushort[] { 20 * 256 }, new[] { model.Map.Document.Terrain.Heightmap.Samples[200, 17] });
        }

        [TestMethod]
        public async Task TheRulerMeasuresDistanceRiseAndSlope()
        {
            MainViewModel model = await Open();
            model.SelectTool("terrain.ruler");
            Heightmap heights = model.Map!.Document.Terrain.Heightmap;
            int steps = model.History.Count;

            model.Stroke([(20, 30), (60, 50), (80, 110)]);

            Measurement measured = model.Tool<RulerTool>().Measured!.Value;
            Assert.AreEqual(100, measured.Distance, 0.001);
            Assert.AreEqual(100 * 5 / 256.0, measured.Kilometres, 0.001);
            Assert.AreEqual(heights.HeightAt(80, 110) - heights.HeightAt(20, 30), measured.Rise, 0.001);
            Assert.AreEqual(Math.Atan2(Math.Abs(measured.Rise), 100) * 180 / Math.PI, measured.Slope, 0.001);
            StringAssert.StartsWith(model.Status, "Measured 100.0 ogrids (1.95 km)");
            Assert.AreEqual(steps, model.History.Count, "measuring changes nothing");
            Assert.AreEqual((20f, 30f, 80f, 110f), model.Tool<RulerTool>().Marks!.Line);
        }
    }
}
