using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Automation;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// Cliff faces that block walkability (user story T-4, D3) through the commands: new cliffs block,
    /// <c>blocksWalkability</c> turns it off as one step of undo, walkability goes out of date, and the
    /// Walkability tool's state counts the face's ogrids.
    /// </summary>
    [TestClass]
    public class CliffWalkabilityTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private static HeightCurve Curve(MainViewModel model, string name) => HeightLayerEdits.CurveOf(model.Map!.Document, name);

        private static async Task Run(MainViewModel model, string id, JsonObject arguments)
        {
            CommandRun run = await model.Commands.RunAsync(id, arguments, interactive: false);
            Assert.IsTrue(run.Ran, $"{id}: {run.Why} {run.Error}");
        }

        [TestMethod]
        public async Task NewCliffsBlockAndTheSettingIsOneStep()
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            await model.OpenAsync(HardFfa);
            await Run(model, "terrain.addCurve", new JsonObject
            {
                ["name"] = "edge", ["profile"] = "cliff", ["height"] = 6, ["cliffFace"] = 2,
                ["points"] = new JsonArray(new JsonArray(60, 60), new JsonArray(120, 70), new JsonArray(180, 60)),
            });
            await Run(model, "terrain.addCurve", new JsonObject
            {
                ["name"] = "crest", ["profile"] = "ridge", ["points"] = new JsonArray(new JsonArray(40, 200), new JsonArray(120, 200)),
            });
            Assert.IsTrue(Curve(model, "edge").BlocksWalkability, "a new cliff blocks");
            Assert.IsFalse(Curve(model, "crest").BlocksWalkability);
            int faces = CliffFaces.Count(model.Map!.Document);
            Assert.IsTrue(faces > 100, $"the face's ogrids: {faces}");
            Assert.AreEqual(faces, (int)model.Tool<WalkabilityMarkTool>().Describe()["curveFaces"]!);
            JsonObject described = HeightLayersState.Describe(model)["layers"]!.AsArray().Single(layer => (string?)layer!["name"] == "edge")!["curve"]!.AsObject();
            Assert.IsTrue((bool)described["blocksWalkability"]!);

            await model.GenerateAsync(WalkabilityGenerator.GeneratorId);
            await Run(model, "terrain.setCurve", new JsonObject { ["layer"] = "edge", ["blocksWalkability"] = false });
            Assert.IsFalse(Curve(model, "edge").BlocksWalkability);
            Assert.AreEqual(0, CliffFaces.Count(model.Map!.Document));
            CollectionAssert.Contains(model.Generation.Results.ResultOf(WalkabilityGenerator.GeneratorId)!.ChangedInputs(model.Map!.Document).ToList(), "cliff faces",
                "walkability is out of date");

            model.Undo();
            Assert.IsTrue(Curve(model, "edge").BlocksWalkability, "one step of undo");

            // a ridge made a cliff is a new cliff
            await Run(model, "terrain.setCurve", new JsonObject { ["layer"] = "crest", ["profile"] = "cliff" });
            Assert.IsTrue(Curve(model, "crest").BlocksWalkability);
            await Run(model, "terrain.addCurve", new JsonObject
            {
                ["name"] = "soft", ["profile"] = "cliff", ["blocksWalkability"] = false, ["points"] = new JsonArray(new JsonArray(40, 300), new JsonArray(120, 300)),
            });
            Assert.IsFalse(Curve(model, "soft").BlocksWalkability, "asked otherwise");
        }
    }
}
