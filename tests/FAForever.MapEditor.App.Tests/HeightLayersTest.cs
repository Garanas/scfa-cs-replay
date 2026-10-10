using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// Height layers and curves in the editor (user stories T-5, T-4): the panel's list and the
    /// inspector's fields run commands, the sculpt tool writes into the chosen layer, the curve tool
    /// draws and edits curves, every change one step of undo.
    /// </summary>
    [TestClass]
    public class HeightLayersTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "height-layers");

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

        private static Task<CommandRun> Run(MainViewModel model, string id, JsonObject? arguments = null) => model.Commands.RunAsync(id, arguments ?? [], interactive: false);

        private static T Setting<T>(SettingGroup group, string label) where T : Setting => group.Settings.OfType<T>().Single(setting => setting.Label == label);

        private static float HeightAt(MainViewModel model, int x, int z) => model.Map!.Document.Terrain.Heightmap.GetHeight(x, z);

        [TestMethod]
        public async Task TheSculptToolWritesIntoTheChosenLayer()
        {
            MainViewModel model = await Open();
            Assert.IsTrue(model.HeightStack.IsEmpty);
            MapDocument opened = model.Map!.Document;

            CommandRun added = await Run(model, "terrain.addLayer");
            Assert.IsTrue(added.Ran);
            CollectionAssert.AreEqual(new[] { "Sculpt", "Base" }, model.HeightStack.Items.Select(item => item.Name).ToArray(), "the highest first");
            Assert.AreEqual("Sculpt", model.HeightStack.Chosen);
            Assert.AreEqual("Sculpt", model.Map.Session.SculptLayer, "the chosen layer takes the sculpt tool's work");
            Assert.AreSame(opened.Terrain.Heightmap.Samples, model.Map.Document.Terrain.Heightmap.Samples, "a new layer changes no height");

            float before = HeightAt(model, 100, 100);
            TiledBuffer<float> baseValues = model.Map.Document.HeightLayers[0].Values;
            model.SelectTool("terrain.sculpt");
            SculptTool sculpt = model.Tool<SculptTool>();
            sculpt.Mode = HeightBrushMode.Raise;
            sculpt.Radius = 10;
            sculpt.Strength = 2;
            int steps = model.History.Count;
            model.Stroke([(100, 100), (104, 100)]);
            Assert.AreEqual(steps + 1, model.History.Count, "one step");
            Assert.IsTrue(HeightAt(model, 100, 100) > before + 1, "the terrain rose");
            Assert.AreSame(baseValues, model.Map.Document.HeightLayers[0].Values, "the base is untouched");
            Assert.IsTrue(model.Map.Document.HeightLayers[1].Values[100, 100] > 1, "the sculpting layer holds the change");

            // hidden, it counts for nothing and takes no stroke
            await Run(model, "terrain.setLayer", new JsonObject { ["visible"] = false });
            Assert.AreEqual(before, HeightAt(model, 100, 100), 1e-3);
            steps = model.History.Count;
            model.Stroke([(100, 100)]);
            Assert.AreEqual(steps, model.History.Count);
            StringAssert.Contains(model.Status, "hidden");
            model.Undo();

            await Run(model, "terrain.bake");
            Assert.IsTrue(model.Map.Document.HeightLayers.IsEmpty);
            Assert.IsTrue(model.HeightStack.IsEmpty);
            Assert.IsTrue(HeightAt(model, 100, 100) > before + 1, "the sum stays");
            model.Undo();
            Assert.AreEqual(2, model.HeightStack.Items.Count, "undo brings the layers back");
        }

        [TestMethod]
        public async Task TheInspectorsFieldsChangeTheChosenLayer()
        {
            MainViewModel model = await Open();
            await Run(model, "paint.addMask", new JsonObject { ["name"] = "rivers" });
            await Run(model, "terrain.addLayer");
            HeightLayersViewModel stack = model.HeightStack;
            int steps = model.History.Count;

            NumberSetting strength = Setting<NumberSetting>(stack.Layer, "Strength");
            strength.Value = 0.6;
            strength.Value = 0.5;
            Assert.AreEqual(0.5f, model.Map!.Document.HeightLayers[1].Strength);
            Assert.AreEqual(steps + 1, model.History.Count, "a slider dragged is one step");

            Setting<ChoiceSetting>(stack.Layer, "Combine").Chosen = "Maximum";
            Assert.AreEqual(HeightCombine.Maximum, model.Map.Document.HeightLayers[1].Combine);
            ChoiceSetting mask = Setting<ChoiceSetting>(stack.Layer, "Mask");
            CollectionAssert.Contains(mask.Choices.ToArray(), "mask rivers");
            mask.Chosen = "mask rivers";
            Assert.AreEqual(HeightLayers.MaskKey("rivers"), model.Map.Document.HeightLayers[1].Mask);
            mask.Chosen = HeightLayersViewModel.Everywhere;
            Assert.IsNull(model.Map.Document.HeightLayers[1].Mask);

            Setting<TextSetting>(stack.Layer, "Name").Text = "Hills";
            Assert.AreEqual("Hills", stack.Chosen, "the renamed layer stays chosen");
            Assert.AreEqual("Hills", model.Map.Session.SculptLayer);

            stack.Items.Single(item => item.Name == "Hills").IsLocked = true;
            Assert.IsTrue(model.Map.Document.HeightLayers[1].Locked);
            model.SelectTool("terrain.sculpt");
            int before = model.History.Count;
            model.Stroke([(50, 50)]);
            Assert.AreEqual(before, model.History.Count, "a locked layer takes no stroke");
            StringAssert.Contains(model.Status, "locked");

            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "terrain.setLayer", new JsonObject { ["layer"] = "Base", ["strength"] = 0.5 }), "the base works at full strength");
        }

        [TestMethod]
        public async Task LayersAreOrderedRemovedAndReadFromFiles()
        {
            MainViewModel model = await Open();
            Directory.CreateDirectory(Root);
            string file = Path.Combine(Root, $"{Guid.NewGuid():N}.r16");
            int size = 65;
            byte[] bytes = new byte[size * size * 2];
            for (int i = 0; i < size * size; i++)
            {
                ushort sample = 60 * 128;
                bytes[i * 2] = (byte)sample;
                bytes[i * 2 + 1] = (byte)(sample >> 8);
            }
            File.WriteAllBytes(file, bytes);

            await Run(model, "terrain.addLayer");
            CommandRun imported = await Run(model, "terrain.addLayer", new JsonObject { ["kind"] = "imported", ["path"] = file, ["name"] = "Gaea" });
            Assert.IsTrue(imported.Ran);
            Assert.AreEqual(60, HeightAt(model, 30, 30), 1e-3, "an image replaces the heights below it");
            CollectionAssert.AreEqual(new[] { "Gaea", "Sculpt", "Base" }, model.HeightStack.Items.Select(item => item.Name).ToArray());
            Assert.IsTrue(model.HeightStack.IsImported);
            StringAssert.Contains(model.HeightStack.ChosenFacts, file);

            await Run(model, "terrain.moveLayer", new JsonObject { ["layer"] = "Gaea", ["by"] = -1 });
            CollectionAssert.AreEqual(new[] { "Sculpt", "Gaea", "Base" }, model.HeightStack.Items.Select(item => item.Name).ToArray());
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "terrain.moveLayer", new JsonObject { ["layer"] = "Base", ["by"] = 1 }), "the base stays the lowest");

            for (int i = 0; i < size * size; i++)
            {
                bytes[i * 2 + 1] = 40;
            }
            File.WriteAllBytes(file, bytes);
            await Run(model, "terrain.reloadLayer", new JsonObject { ["layer"] = "Gaea" });
            Assert.AreEqual(40 * 256 / 128f, HeightAt(model, 30, 30), 0.1, "read again");

            await Run(model, "terrain.removeLayer", new JsonObject { ["layer"] = "Gaea" });
            CollectionAssert.AreEqual(new[] { "Sculpt", "Base" }, model.HeightStack.Items.Select(item => item.Name).ToArray());
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "terrain.removeLayer", new JsonObject { ["layer"] = "Base" }));
        }

        [TestMethod]
        public async Task GeneratedResultsGoOutOfDateWhenALayerChanges()
        {
            MainViewModel model = await Open();
            await Run(model, "terrain.addLayer");
            await Run(model, "generate.normalMap");
            Assert.IsFalse(model.Generation.Stale.Any(state => state.Generator.Id == "normalMap"));
            await Run(model, "terrain.addCurve", new JsonObject { ["points"] = new JsonArray(new JsonArray(40, 40), new JsonArray(120, 60)), ["height"] = 20 });
            Assert.IsTrue(model.Generation.Stale.Any(state => state.Generator.Id == "normalMap"), "the heightmap changed with the curve");
        }

        [TestMethod]
        public async Task TheCurveToolDrawsAndEditsACurve()
        {
            MainViewModel model = await Open();
            model.SelectTool("terrain.curve");
            CurveTool tool = model.Tool<CurveTool>();
            tool.Profile = CurveProfile.Ridge;
            tool.Height = 15;
            float before = HeightAt(model, 60, 100);
            float there = HeightAt(model, 140, 150);

            model.Stroke([(40, 100)]);
            Assert.AreEqual(1, model.Map!.Document.HeightLayers.Count(layer => layer.Kind == HeightLayerKind.Curve), "the first click starts a curve layer");
            Assert.AreEqual("Ridge", model.HeightStack.Chosen);
            model.Stroke([(80, 100)]);
            model.Stroke([(120, 110)]);
            HeightCurve curve = model.Map.Document.HeightLayers[^1].Curve!;
            Assert.AreEqual(3, curve.Points.Length, "each click adds a point");
            Assert.AreEqual(15, model.Map.Document.HeightLayers[^1].Values[60, 100], 0.01, "the ridge's layer");
            Assert.AreEqual(before + 15, HeightAt(model, 60, 100), 0.2, "the ridge rises along it");
            Assert.AreEqual(3, tool.Marks!.Handles.Count);
            Assert.IsTrue(tool.Marks.Path!.Count > 3, "the view draws the spline");

            // dragging a point moves it, one step; the terrain follows
            int steps = model.History.Count;
            model.Stroke([(120, 110), (130, 130), (140, 150)]);
            Assert.AreEqual(steps + 1, model.History.Count);
            Assert.AreEqual(new CurvePoint(140, 150, 15), model.Map.Document.HeightLayers[^1].Curve!.Points[2]);
            Assert.AreEqual(there + 15, HeightAt(model, 140, 150), 0.2);

            // control and a click removes a point
            tool.Press(80, 100, new ToolInput(ToolModifiers.Control, 0.5f));
            tool.Release();
            Assert.AreEqual(2, model.Map.Document.HeightLayers[^1].Curve!.Points.Length);

            // the inspector changes the curve
            Setting<NumberSetting>(model.HeightStack.Curve, "Height").Text = "5";
            Assert.AreEqual(5, model.Map.Document.HeightLayers[^1].Curve!.Points[0].Height);
            Setting<ChoiceSetting>(model.HeightStack.Curve, "Profile").Chosen = "Valley";
            Assert.AreEqual(CurveProfile.Valley, model.Map.Document.HeightLayers[^1].Curve!.Profile);

            // Escape: the next click starts another curve
            Assert.IsTrue(tool.Key("Escape", ToolModifiers.None));
            model.Stroke([(200, 200)]);
            Assert.AreEqual(2, model.Map.Document.HeightLayers.Count(layer => layer.Kind == HeightLayerKind.Curve));
        }
    }
}
