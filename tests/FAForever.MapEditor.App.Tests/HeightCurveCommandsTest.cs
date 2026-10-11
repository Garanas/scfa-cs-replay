using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Automation;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The curve commands' new settings (user story T-4, wave 17): points as objects with their own
    /// values, the curve's shape, noise, variation, road mode and fill through <c>terrain.addCurve</c>
    /// and <c>terrain.setCurve</c>; reversing, a point, an even grade and a snap, each one step of
    /// undo; what <c>state.heightLayers</c> says about them.
    /// </summary>
    [TestClass]
    public class HeightCurveCommandsTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private static async Task<MainViewModel> Open()
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            await model.OpenAsync(HardFfa);
            return model;
        }

        private static async Task<CommandRun> Run(MainViewModel model, string id, JsonObject? arguments = null)
        {
            CommandRun run = await model.Commands.RunAsync(id, arguments ?? [], interactive: false);
            Assert.IsTrue(run.Ran, $"{id}: {run.Why} {run.Error}");
            return run;
        }

        private static HeightCurve Curve(MainViewModel model, string name) => HeightLayerEdits.CurveOf(model.Map!.Document, name);

        [TestMethod]
        public async Task AddCurveTakesEveryNewSettingAndPointsAsObjects()
        {
            MainViewModel model = await Open();
            CommandRun run = await Run(model, "terrain.addCurve", new JsonObject
            {
                ["name"] = "crest",
                ["profile"] = "ridge",
                ["height"] = 12,
                ["points"] = new JsonArray(new JsonArray(40, 40), new JsonObject { ["x"] = 90, ["z"] = 60, ["width"] = 14, ["strength"] = 0.5, ["sharp"] = true }, new JsonArray(140, 50, 8)),
                ["falloffRight"] = 20, ["shape"] = "terraced", ["terraceSteps"] = 4, ["cliffFace"] = 3, ["camber"] = 1,
                ["noiseAmplitude"] = 2, ["noiseScale"] = 9, ["noiseSeed"] = 5,
                ["heightVariation"] = new JsonArray(-1, 0), ["widthVariation"] = new JsonArray(0, 3), ["variationScale"] = 24,
            });
            Assert.AreEqual("crest", run.Result);
            HeightCurve curve = Curve(model, "crest");
            Assert.AreEqual(new CurvePoint(40, 40, 12), curve.Points[0]);
            Assert.AreEqual(new CurvePoint(90, 60, 12, Width: 14, Strength: 0.5f, Sharp: true), curve.Points[1], "an object's values are the point's own");
            Assert.AreEqual(8, curve.Points[2].Height);
            Assert.AreEqual(20, curve.FalloffRight);
            Assert.AreEqual(CurveFalloffShape.Terraced, curve.FalloffShape);
            Assert.AreEqual(4, curve.TerraceSteps);
            Assert.AreEqual(3, curve.CliffFace);
            Assert.AreEqual(1, curve.Camber);
            Assert.AreEqual((2f, 9f, 5), (curve.NoiseAmplitude, curve.NoiseScale, curve.NoiseSeed));
            Assert.AreEqual(new CurveRange(-1, 0), curve.HeightVariation);
            Assert.AreEqual(new CurveRange(0, 3), curve.WidthVariation);
            Assert.AreEqual(24, curve.VariationScale);

            JsonObject state = HeightLayersState.Describe(model);
            JsonObject described = state["layers"]!.AsArray().Single(layer => (string?)layer!["name"] == "crest")!["curve"]!.AsObject();
            Assert.AreEqual("Terraced", (string?)described["shape"]);
            Assert.AreEqual(1, described["nodes"]!.AsArray().Count, "the point with its own values");
            Assert.AreEqual(1, (int)described["nodes"]![0]!["index"]!);
            Assert.AreEqual(2, described["grades"]!.AsArray().Count, "a grade between each two points");
            Assert.AreEqual(0, (float)described["grades"]![0]!["rise"]!, 1e-3);

            await Assert.ThrowsExceptionAsync<ArgumentException>(() => model.Commands.RunAsync("terrain.addCurve", new JsonObject
            {
                ["points"] = new JsonArray(new JsonArray(40, 40), new JsonArray(80, 40)), ["shape"] = "wobbly",
            }, interactive: false), "a shape it does not know");
        }

        [TestMethod]
        public async Task SetCurveChangesTheNewSettingsAsOneStepAndClosedCurvesFill()
        {
            MainViewModel model = await Open();
            await Run(model, "terrain.addCurve", new JsonObject
            {
                ["name"] = "mesa", ["profile"] = "cliff", ["height"] = 20, ["closed"] = true,
                ["points"] = new JsonArray(new JsonArray(60, 60), new JsonArray(140, 60), new JsonArray(140, 140), new JsonArray(60, 140)),
            });
            Assert.IsTrue(Curve(model, "mesa").Fills, "a new closed curve fills");
            float inside = model.Map!.Document.HeightStack.Layers.Single(layer => layer.Name == "mesa").Values![100, 100];
            Assert.AreEqual(20, inside, 1e-3, "a closed cliff raises its inside");
            await Run(model, "terrain.setCurve", new JsonObject { ["layer"] = "mesa", ["lineOnly"] = true });
            Assert.AreEqual(0, model.Map.Document.HeightStack.Layers.Single(layer => layer.Name == "mesa").Values![100, 100], 1e-3, "line only");

            int steps = model.Map.Session.History.Count;
            await Run(model, "terrain.setCurve", new JsonObject { ["layer"] = "mesa", ["noiseAmplitude"] = 1 });
            await Run(model, "terrain.setCurve", new JsonObject { ["layer"] = "mesa", ["noiseAmplitude"] = 3 });
            Assert.AreEqual(steps + 1, model.Map.Session.History.Count, "a setting dragged is one step");
            Assert.AreEqual(3, Curve(model, "mesa").NoiseAmplitude);

            await Run(model, "terrain.setCurve", new JsonObject { ["layer"] = "mesa", ["profile"] = "road", ["roadMode"] = "cutOnly" });
            Layer road = model.Map.Document.HeightStack.Layers.Single(layer => layer.Name == "mesa");
            Assert.AreEqual(LayerCombine.Minimum, road.Combine, "cut only combines by minimum");
            await Run(model, "terrain.setCurve", new JsonObject { ["layer"] = "mesa", ["falloffRight"] = 5, ["shape"] = "easeOut" });
            Assert.AreEqual(5, Curve(model, "mesa").FalloffRight);
            await Run(model, "terrain.setCurve", new JsonObject { ["layer"] = "mesa", ["falloffRight"] = -1, ["shape"] = "default" });
            Assert.IsNull(Curve(model, "mesa").FalloffRight, "negative: the same as the falloff");
            Assert.IsNull(Curve(model, "mesa").FalloffShape, "default: the profile's");
        }

        [TestMethod]
        public async Task ARiverWithoutHeightsLiesBelowTheWater()
        {
            MainViewModel model = await Open();
            Water water = model.Map!.Document.Water;
            float baseHeight = water.HasWater ? water.Elevation : 0;
            await Run(model, "terrain.addCurve", new JsonObject
            {
                ["name"] = "river", ["profile"] = "river", ["height"] = 4,
                ["points"] = new JsonArray(new JsonArray(40, 120), new JsonArray(200, 130)),
            });
            HeightCurve river = Curve(model, "river");
            Assert.AreEqual(baseHeight - 4, river.Points[0].Height, 1e-3, "the bed below the water's elevation");
            StringAssert.Contains(model.Status, "below");
            Layer layer = model.Map.Document.HeightStack.Layers.Single(layer => layer.Name == "river");
            Assert.AreEqual(LayerCombine.Minimum, layer.Combine, "a river cuts");
            Assert.AreEqual(CurveFalloffShape.EaseOut, HeightCurves.ShapeOf(river));
        }

        [TestMethod]
        public async Task ReversePointGradeAndSnapAreOneStepEach()
        {
            MainViewModel model = await Open();
            await Run(model, "terrain.addCurve", new JsonObject
            {
                ["name"] = "ramp", ["profile"] = "road",
                ["points"] = new JsonArray(new JsonArray(40, 100, 10), new JsonArray(80, 100, 10), new JsonArray(120, 100, 10), new JsonArray(160, 100, 40)),
            });
            int steps = model.Map!.Session.History.Count;

            await Run(model, "terrain.evenGrade", new JsonObject { ["layer"] = "ramp", ["from"] = 0, ["to"] = 3 });
            HeightCurve graded = Curve(model, "ramp");
            Assert.AreEqual(20, graded.Points[1].Height, 0.5, "a third of the way up");
            Assert.AreEqual(30, graded.Points[2].Height, 0.5);
            Assert.AreEqual(steps + 1, model.Map.Session.History.Count);

            await Run(model, "terrain.setCurvePoint", new JsonObject { ["layer"] = "ramp", ["index"] = 2, ["width"] = 16, ["strength"] = 0.25, ["sharp"] = true });
            Assert.AreEqual(new CurvePoint(120, 100, graded.Points[2].Height, Width: 16, Strength: 0.25f, Sharp: true), Curve(model, "ramp").Points[2]);
            await Run(model, "terrain.setCurvePoint", new JsonObject { ["layer"] = "ramp", ["index"] = 2, ["clear"] = "width" });
            Assert.IsNull(Curve(model, "ramp").Points[2].Width, "cleared: the curve's again");
            Assert.AreEqual(0.25f, Curve(model, "ramp").Points[2].Strength);

            // the tool's points keep their own values when it sets the points again
            model.SelectTool("terrain.curve");
            model.Stroke([(200, 100)]);
            Assert.AreEqual(5, Curve(model, "ramp").Points.Length);
            Assert.AreEqual(0.25f, Curve(model, "ramp").Points[2].Strength, "a point added by the tool leaves the others' values");

            int beforeReverse = model.Map.Session.History.Count;
            await Run(model, "terrain.reverseCurve", new JsonObject { ["layer"] = "ramp" });
            Assert.AreEqual(new CurvePoint(40, 100, 10), Curve(model, "ramp").Points[^1] with { Height = 10 });
            Assert.AreEqual(beforeReverse + 1, model.Map.Session.History.Count);

            float ground = model.Map.Document.HeightStack.Layers[0].Values![40, 100];
            await Run(model, "terrain.snapCurve", new JsonObject { ["layer"] = "ramp", ["points"] = new JsonArray(4) });
            Assert.AreEqual(ground, Curve(model, "ramp").Points[4].Height, 0.05, "the point at (40, 100) on the base below");
            await Run(model, "terrain.snapCurve", new JsonObject { ["layer"] = "ramp", ["points"] = "all" });
            int snapped = model.Map.Session.History.Count;
            await Run(model, "terrain.snapCurve", new JsonObject { ["layer"] = "ramp" });
            Assert.AreEqual(snapped, model.Map.Session.History.Count, "the ends on the ground already: no step");

            model.Undo();
            model.Undo();
            Assert.AreEqual(200, Curve(model, "ramp").Points[0].X, 1e-3, "reversed still");
            model.Undo();
            Assert.AreEqual(40, Curve(model, "ramp").Points[0].X, 1e-3, "each edit one step");
        }
    }
}
