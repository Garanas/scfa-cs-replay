using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// What the Sketch mode shows after another map is opened, an image is renamed or a stroke is under way:
    /// the panels follow the model, and nothing of the map that was left stays chosen.
    /// </summary>
    [TestClass]
    public sealed class SketchConsistencyTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "sketch-consistency");

        [ClassCleanup]
        public static void Cleanup()
        {
            if (Directory.Exists(Root))
            {
                foreach (string file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }
                Directory.Delete(Root, recursive: true);
            }
        }

        private static async Task<MainViewModel> Open(string folder = HardFfa)
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            await model.OpenAsync(folder);
            model.SelectMode("sketch");
            return model;
        }

        private static Task<CommandRun> Run(MainViewModel model, string id, JsonObject? arguments = null) =>
            model.Commands.RunAsync(id, arguments ?? [], interactive: false);

        [TestMethod]
        public async Task OpeningAnotherMapChoosesItsFirstSheetNotTheSheetOfTheSameNameChosenBefore()
        {
            MainViewModel first = await Open();
            await Run(first, "sketch.addSheet", new JsonObject { ["name"] = "One" });
            await Run(first, "sketch.addSheet", new JsonObject { ["name"] = "Two" });
            string folder = Path.Combine(Root, Guid.NewGuid().ToString("N"));
            await first.SaveAsAsync(folder);

            MainViewModel model = await Open();
            await Run(model, "sketch.addSheet", new JsonObject { ["name"] = "One" });
            await Run(model, "sketch.addSheet", new JsonObject { ["name"] = "Two" });
            Assert.AreEqual("Two", model.Sketch.Chosen);
            SketchSelectTool select = model.Tool<SketchSelectTool>();
            await Run(model, "sketch.draw", new JsonObject { ["points"] = new JsonArray(new JsonArray(20, 100), new JsonArray(80, 120)), ["mirror"] = false });
            select.Select("Two", model.ShownDocument!.Sketch.Find("Two")!.Strokes.Select((_, i) => new SketchItem(SketchItemKind.Stroke, i)), add: false);
            Assert.IsTrue(select.HasSelection);

            await model.OpenAsync(folder);

            Assert.AreEqual("One", model.Sketch.Chosen, "the first sheet of the map that was opened");
            Assert.IsTrue(model.Sketch.Items.Single(item => item.Name == "One").IsChosen);
            Assert.IsFalse(model.Sketch.Items.Single(item => item.Name == "Two").IsChosen);
            Assert.IsFalse(select.HasSelection, "a selection belongs to the map it was made in");
        }

        [TestMethod]
        public async Task OpeningAnotherMapDropsALabelBeingWrittenAndTheChosenImage()
        {
            MainViewModel model = await Open();
            await Run(model, "images.project", new JsonObject { ["path"] = ProjectedImagesTest.Picture() });
            Assert.AreEqual("Draft", model.Images.ChosenName);
            await model.Sketch.BeginLabel(new SketchLabelDraft(new System.Numerics.Vector2(50, 50), new System.Numerics.Vector2(10, 10), Project.Files.InMemory.SketchColour.Red, 2, "", null), "half");
            Assert.IsTrue(model.Sketch.IsWriting);

            await model.OpenAsync(HardFfa);

            Assert.IsNull(model.Images.ChosenName);
            Assert.IsTrue(model.Images.IsEmpty);
            Assert.IsFalse(model.Images.HasChosen);
            Assert.IsFalse(model.Sketch.IsWriting);
            Assert.AreEqual("", model.Sketch.LabelText);
        }

        [TestMethod]
        public async Task ProjectingAnImageShowsTheImagesAsADrawnStrokeShowsTheSketch()
        {
            MainViewModel model = await Open();
            model.ShowImages = false;
            Assert.IsFalse(model.ShowImages);

            await Run(model, "images.project", new JsonObject { ["path"] = ProjectedImagesTest.Picture() });

            Assert.IsTrue(model.ShowImages);
        }

        [TestMethod]
        public async Task TheEraserAndTheSelectToolLeaveAHiddenSketchAloneAndSayWhy()
        {
            MainViewModel model = await Open();
            await Run(model, "sketch.draw", new JsonObject { ["points"] = new JsonArray(new JsonArray(20, 100), new JsonArray(80, 100)), ["mirror"] = false });
            model.ShowSketch = false;
            model.Status = "";

            model.Tool<EraserTool>().Press(50, 100);
            Assert.AreEqual(1, model.ShownDocument!.Sketch.StrokeCount, "what cannot be seen is not erased");
            StringAssert.Contains(model.Status, "sketch is hidden");

            model.Status = "";
            model.Tool<SketchSelectTool>().Press(50, 100);
            Assert.IsFalse(model.Tool<SketchSelectTool>().HasSelection);
            StringAssert.Contains(model.Status, "sketch is hidden");

            model.ShowSketch = true;
            model.Tool<SketchSelectTool>().Press(50, 100);
            Assert.IsTrue(model.Tool<SketchSelectTool>().HasSelection);
        }

        [TestMethod]
        public async Task TheImageToolSaysWhyItDoesNothingWhileTheImagesAreHidden()
        {
            MainViewModel model = await Open();
            await Run(model, "images.project", new JsonObject { ["path"] = ProjectedImagesTest.Picture() });
            model.ShowImages = false;
            model.Status = "";

            model.Tool<ImageTool>().Press(128, 128);

            StringAssert.Contains(model.Status, "hidden");
        }
    }
}
