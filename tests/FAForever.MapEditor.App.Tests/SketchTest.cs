using System.Numerics;
using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Automation;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The Sketch mode (user story N-1): the pencil and eraser, the sheets and their commands, the
    /// overlay, the state, and the sketch kept in the project; each operation is a command and one step of undo.
    /// </summary>
    [TestClass]
    public sealed class SketchTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "sketch-projects");

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

        private static async Task<MainViewModel> Open()
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            await model.OpenAsync(HardFfa);
            model.SelectMode("sketch");
            return model;
        }

        private static async Task<CommandRun> Run(MainViewModel model, string id, JsonObject? arguments = null) =>
            await model.Commands.RunAsync(id, arguments ?? [], interactive: false);

        private static JsonArray Points(params (float X, float Z)[] points) => new JsonArray([.. points.Select(point => (JsonNode)new JsonArray(point.X, point.Z))]);

        private static Sketch Sketch(MainViewModel model) => model.Map!.Document.Sketch;

        [TestMethod]
        public async Task TheSketchModeIsOnTheRailWithItsToolsBuiltAndItsKey()
        {
            MainViewModel model = await Open();

            Assert.AreEqual("sketch", model.SelectedMode!.Definition.Id);
            Assert.AreEqual("F10", model.SelectedMode.Definition.Gesture);
            Assert.IsInstanceOfType<PencilTool>(model.ActiveTool!.Tool, "the pencil is the first tool");
            Assert.IsFalse(model.Tools.Single(tool => tool.Definition.Id == "sketch.pencil").Definition.IsIncomplete);
            Assert.IsFalse(model.Tools.Single(tool => tool.Definition.Id == "sketch.eraser").Definition.IsIncomplete);
            Assert.IsTrue(model.Tools.Single(tool => tool.Definition.Id == "sketch.symbol").Definition.IsIncomplete, "symbols are wave 24");
            Assert.AreEqual("sketch.mode", model.Commands.Find("sketch.mode")!.Id);
            Assert.AreEqual("Ctrl+Shift+K", model.Commands.Find("view.overlay.sketch")!.Gesture);
            Assert.IsTrue(model.ShowSketch, "the sketch shows in every mode from the start");
        }

        [TestMethod]
        public async Task AStrokeIsOneStepAndTheFirstOneMakesTheFirstSheet()
        {
            MainViewModel model = await Open();
            int steps = model.History.Count;

            CommandRun drawn = await Run(model, "sketch.draw", new JsonObject { ["points"] = Points((100, 100), (140, 100), (140, 140)), ["colour"] = "blue", ["width"] = "thick", ["mirror"] = false });

            Assert.AreEqual(1, drawn.Result, "one stroke without the mirror");
            Assert.AreEqual(steps + 1, model.History.Count, "one step of undo");
            Assert.AreEqual("Draw a stroke in the sketch", model.History.Last().Description);
            SketchSheet sheet = Sketch(model).Sheets.Single();
            Assert.AreEqual("Sheet 1", sheet.Name);
            SketchStroke stroke = sheet.Strokes.Single();
            Assert.AreEqual(SketchColour.Blue, stroke.Colour);
            Assert.AreEqual(SketchWidth.Thick, stroke.Width);
            Assert.AreEqual(3, stroke.Points.Length);
            Assert.AreEqual("Sheet 1", model.Sketch.Chosen, "the sheet it made is the one drawn into");
            Assert.AreEqual(1, model.Sketch.Items.Single().Strokes);

            Assert.IsTrue(model.Undo());
            Assert.IsTrue(Sketch(model).IsEmpty);
            Assert.IsNull(model.Sketch.Chosen);
            Assert.IsTrue(model.Redo());
            Assert.AreEqual(1, Sketch(model).Sheets.Single().Strokes.Length);
            Assert.AreEqual("Sheet 1", model.Sketch.Chosen);
        }

        [TestMethod]
        public async Task TheMouseDrawsWithThePencilAndOneDragIsOneStroke()
        {
            MainViewModel model = await Open();
            PencilTool pencil = model.Tool<PencilTool>();
            await Run(model, "sketch.setPencil", new JsonObject { ["colour"] = 3, ["width"] = "thin", ["mirror"] = false });
            Assert.AreEqual(SketchColour.Green, pencil.Colour, "3 is green");
            int steps = model.History.Count;

            model.Stroke([(50, 50), (60, 52), (80, 60), (120, 90)]);

            Assert.AreEqual(steps + 1, model.History.Count);
            SketchStroke stroke = Sketch(model).Sheets.Single().Strokes.Single();
            Assert.AreEqual(SketchColour.Green, stroke.Colour);
            Assert.AreEqual(SketchWidth.Thin, stroke.Width);
            Assert.AreEqual(new Vector2(50, 50), stroke.Points[0]);
            Assert.AreEqual(new Vector2(120, 90), stroke.Points[^1]);
            Assert.AreEqual(0, model.Sketch.Pending.Count, "the line shown while the mouse is down is part of the sketch now");

            model.Stroke([(200, 200)]);
            Assert.AreEqual(1, Sketch(model).Sheets.Single().Strokes[1].Points.Length, "a click is a dot");
            Assert.AreEqual("Draw a stroke in the sketch", model.History.Last().Description);
        }

        [TestMethod]
        public async Task TheLineIsShownWhileTheMouseIsDownAndEscapeThrowsItAway()
        {
            MainViewModel model = await Open();
            PencilTool pencil = model.Tool<PencilTool>();
            await Run(model, "sketch.setPencil", new JsonObject { ["mirror"] = false });

            pencil.Press(10, 10);
            pencil.Drag(40, 40);
            Assert.AreEqual(1, model.Sketch.Pending.Count);
            Assert.AreEqual(2, model.Sketch.Pending[0].Points.Length);
            Assert.IsTrue(pencil.IsDrawing);
            Assert.IsTrue(Sketch(model).IsEmpty, "not yet part of the map");

            Assert.IsTrue(pencil.Key("Escape", ToolModifiers.None));
            Assert.AreEqual(0, model.Sketch.Pending.Count);
            pencil.Release();
            Assert.IsTrue(Sketch(model).IsEmpty, "a stroke given up draws nothing");
        }

        [TestMethod]
        public async Task ShiftDrawsAStraightLineAndSmoothThinsAWigglyOne()
        {
            MainViewModel model = await Open();
            PencilTool pencil = model.Tool<PencilTool>();
            await Run(model, "sketch.setPencil", new JsonObject { ["mirror"] = false, ["smooth"] = false });

            pencil.Press(10, 10, new ToolInput(ToolModifiers.Shift, 0.5f));
            pencil.Drag(30, 40, new ToolInput(ToolModifiers.Shift, 0.5f));
            pencil.Drag(80, 20, new ToolInput(ToolModifiers.Shift, 0.5f));
            pencil.Release();
            SketchStroke straight = Sketch(model).Sheets.Single().Strokes.Single();
            CollectionAssert.AreEqual(new[] { new Vector2(10, 10), new Vector2(80, 20) }, straight.Points.ToArray(), "from where it began to where it ended");

            await Run(model, "sketch.setPencil", new JsonObject { ["smooth"] = true });
            JsonArray wiggle = new JsonArray();
            for (int i = 0; i <= 100; i++)
            {
                wiggle.Add(new JsonArray(100 + i, 100 + (float)Math.Sin(i / 20.0) * 8 + (i % 2 == 0 ? 0.15f : -0.15f)));
            }
            CommandRun smooth = await Run(model, "sketch.draw", new JsonObject { ["points"] = wiggle, ["mirror"] = false });
            Assert.AreEqual(1, smooth.Result);
            Assert.IsTrue(Sketch(model).Sheets.Single().Strokes[1].Points.Length < 20, "smoothed and thinned to few places");

            CommandRun exact = await Run(model, "sketch.draw", new JsonObject { ["points"] = Points((1, 1), (2, 5), (9, 5)), ["smooth"] = false, ["mirror"] = false });
            Assert.AreEqual(3, Sketch(model).Sheets.Single().Strokes[2].Points.Length, "an exact polyline stays as it is");
            Assert.AreEqual(1, exact.Result);
        }

        [TestMethod]
        public async Task TheSymmetryRepeatsAStrokeWhileMirrorIsOn()
        {
            MainViewModel model = await Open();
            await Run(model, "select.symmetry", new JsonObject { ["kind"] = "rotational", ["count"] = 2 });
            Assert.IsTrue(model.Symmetry.IsMirroring);
            Assert.IsTrue(model.Tool<PencilTool>().Mirror, "on by default");

            CommandRun both = await Run(model, "sketch.draw", new JsonObject { ["points"] = Points((20, 30), (60, 30)) });
            Assert.AreEqual(2, both.Result);
            SketchStroke image = Sketch(model).Sheets.Single().Strokes[1];
            float width = model.Map!.Document.Width;
            float height = model.Map.Document.Height;
            Assert.AreEqual(width - 20, image.Points[0].X, 1e-3f, "turned half a circle around the middle");
            Assert.AreEqual(height - 30, image.Points[0].Y, 1e-3f);
            Assert.AreEqual(1, model.History.Count(step => step.Description == "Draw a stroke in the sketch"), "both in one step");

            CommandRun alone = await Run(model, "sketch.draw", new JsonObject { ["points"] = Points((20, 60), (60, 60)), ["mirror"] = false });
            Assert.AreEqual(1, alone.Result);

            await Run(model, "sketch.setPencil", new JsonObject { ["mirror"] = false });
            model.Stroke([(30, 100), (70, 100)]);
            Assert.AreEqual(4, Sketch(model).Sheets.Single().Strokes.Length, "the pencil's own Mirror off: one stroke, no image");
        }

        [TestMethod]
        public async Task TheKeysPickTheColourAndTheWidth()
        {
            MainViewModel model = await Open();
            PencilTool pencil = model.Tool<PencilTool>();

            Assert.IsTrue(model.ToolKey("D4", ToolModifiers.None));
            Assert.AreEqual(SketchColour.Blue, pencil.Colour);
            Assert.IsTrue(model.ToolKey("NumPad6", ToolModifiers.None));
            Assert.AreEqual(SketchColour.Black, pencil.Colour);
            Assert.IsFalse(model.ToolKey("D7", ToolModifiers.None), "there are six");
            Assert.IsFalse(model.ToolKey("D2", ToolModifiers.Control), "a digit with control is a shortcut");
            Assert.IsTrue(pencil.Colours.Single(choice => choice.Colour == SketchColour.Black).IsOn);
            Assert.IsFalse(pencil.Colours.Single(choice => choice.Colour == SketchColour.Blue).IsOn);

            Assert.AreEqual(SketchWidth.Medium, pencil.Width);
            Assert.IsTrue(model.ToolKey("OemCloseBrackets", ToolModifiers.None));
            Assert.AreEqual(SketchWidth.Thick, pencil.Width);
            model.ToolKey("OemCloseBrackets", ToolModifiers.None);
            Assert.AreEqual(SketchWidth.Thick, pencil.Width, "the thickest stays");
            model.ToolKey("OemOpenBrackets", ToolModifiers.None);
            model.ToolKey("OemOpenBrackets", ToolModifiers.None);
            Assert.AreEqual(SketchWidth.Thin, pencil.Width);
            Assert.IsTrue(pencil.Widths.Single(choice => choice.Width == SketchWidth.Thin).IsOn);
            StringAssert.Contains(model.Status, "thin");
        }

        [TestMethod]
        public async Task SheetsAreAddedRenamedDuplicatedShownClearedAndDeletedEachOneStepOfUndo()
        {
            MainViewModel model = await Open();

            Assert.AreEqual("Layout A", (await Run(model, "sketch.addSheet", new JsonObject { ["name"] = "Layout A" })).Result);
            Assert.AreEqual("Layout A", model.Sketch.Chosen, "a sheet added is chosen");
            await Run(model, "sketch.draw", new JsonObject { ["points"] = Points((10, 10), (50, 50)), ["mirror"] = false });
            Assert.AreEqual("Sheet 1", (await Run(model, "sketch.addSheet")).Result);
            Assert.AreEqual("Sheet 1", model.Sketch.Chosen);
            await Run(model, "sketch.draw", new JsonObject { ["points"] = Points((10, 90), (50, 90)), ["mirror"] = false });
            Assert.AreEqual(1, Sketch(model).Find("Sheet 1")!.Strokes.Length, "drawn into the chosen sheet");
            Assert.AreEqual(1, Sketch(model).Find("Layout A")!.Strokes.Length);

            await Run(model, "sketch.chooseSheet", new JsonObject { ["sheet"] = "layout a" });
            Assert.AreEqual("Layout A", model.Sketch.Chosen, "without regard to case, and no step of undo");
            int steps = model.History.Count;

            await Run(model, "sketch.renameSheet", new JsonObject { ["name"] = "Plan" });
            Assert.AreEqual("Plan", model.Sketch.Chosen, "the chosen sheet keeps being chosen when renamed");
            Assert.AreEqual(steps + 1, model.History.Count);
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "sketch.renameSheet", new JsonObject { ["name"] = "sheet 1" }));
            Assert.AreEqual(steps + 1, model.History.Count, "a refused command leaves no step");

            Assert.AreEqual("Plan copy", (await Run(model, "sketch.duplicateSheet")).Result);
            Assert.AreEqual("Plan copy", model.Sketch.Chosen);
            Assert.AreEqual(1, Sketch(model).Find("Plan copy")!.Strokes.Length);
            CollectionAssert.AreEqual(new[] { "Plan", "Plan copy", "Sheet 1" }, Sketch(model).Sheets.Select(sheet => sheet.Name).ToArray(), "right after the original");

            await Run(model, "sketch.showSheet", new JsonObject { ["sheet"] = "Plan", ["on"] = false });
            Assert.IsFalse(Sketch(model).Find("Plan")!.Shown);
            Assert.IsFalse(model.Sketch.Items.Single(item => item.Name == "Plan").Shown);
            await Run(model, "sketch.showSheet", new JsonObject { ["sheet"] = "Plan" });
            Assert.IsTrue(Sketch(model).Find("Plan")!.Shown, "left out, the other of what it is");

            await Run(model, "sketch.clearSheet", new JsonObject { ["sheet"] = "Plan copy" });
            Assert.IsTrue(Sketch(model).Find("Plan copy")!.Strokes.IsEmpty);
            await Run(model, "sketch.deleteSheet");
            Assert.IsNull(Sketch(model).Find("Plan copy"));
            Assert.AreEqual("Plan", model.Sketch.Chosen, "the first is chosen when the chosen one goes");

            Assert.IsTrue(model.Undo());
            Assert.IsNotNull(Sketch(model).Find("Plan copy"), "undo brings the sheet back");
            Assert.AreEqual(2, model.Sketch.Items.Count(item => item.Name.StartsWith("Plan", StringComparison.Ordinal)), "and the list follows");
        }

        [TestMethod]
        public async Task TheLegendAndTheOpacityAreEditsOfTheChosenSheet()
        {
            MainViewModel model = await Open();
            await Run(model, "sketch.addSheet", new JsonObject { ["name"] = "Layout A" });

            await Run(model, "sketch.setLegend", new JsonObject { ["colour"] = "red", ["text"] = "spawns" });
            await Run(model, "sketch.setLegend", new JsonObject { ["colour"] = 4, ["text"] = "water" });
            SketchSheet sheet = Sketch(model).Sheets.Single();
            Assert.AreEqual("spawns", sheet.LegendOf(SketchColour.Red));
            Assert.AreEqual("water", sheet.LegendOf(SketchColour.Blue), "4 is blue");
            Assert.AreEqual("spawns", model.Sketch.Legend[0].Text, "the inspector's field follows");
            Assert.AreEqual("water", model.Sketch.Legend[3].Text);
            IReadOnlyList<(string Sheet, IReadOnlyList<(SketchColour Colour, string Text)> Entries)> keys = model.Sketch.Keys(model.Map!.Document);
            Assert.AreEqual(1, keys.Count);
            CollectionAssert.AreEqual(new[] { (SketchColour.Red, "spawns"), (SketchColour.Blue, "water") }, keys[0].Entries.ToArray());

            // the field types a text: it is the command, one step of undo
            int steps = model.History.Count;
            model.Sketch.Legend[1].Text = "mass";
            Assert.AreEqual("mass", Sketch(model).Sheets.Single().LegendOf(SketchColour.Yellow));
            Assert.AreEqual(steps + 1, model.History.Count);

            await Run(model, "sketch.showSheet", new JsonObject { ["on"] = false });
            Assert.AreEqual(0, model.Sketch.Keys(model.Map.Document).Count, "a hidden sheet's legend is no key");

            steps = model.History.Count;
            await Run(model, "sketch.setOpacity", new JsonObject { ["opacity"] = 0.2 });
            await Run(model, "sketch.setOpacity", new JsonObject { ["opacity"] = 0.3 });
            await Run(model, "sketch.setOpacity", new JsonObject { ["opacity"] = 0.35 });
            Assert.AreEqual(0.35f, Sketch(model).Sheets.Single().Opacity);
            Assert.AreEqual(steps + 1, model.History.Count, "a slider dragged is one step");
            Assert.AreEqual(35, model.Sketch.ChosenOpacity);
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "sketch.setOpacity", new JsonObject { ["opacity"] = 1.5 }));

            // the field of the name
            model.Sketch.ChosenName = "Layout B";
            Assert.AreEqual("Layout B", Sketch(model).Sheets.Single().Name);
            await Task.Yield();
            model.Sketch.ChosenName = "";
            Assert.AreEqual("Layout B", Sketch(model).Sheets.Single().Name, "an empty name is refused");
            Assert.AreEqual("Layout B", model.Sketch.ChosenName, "and the field says the sheet's own again");
        }

        [TestMethod]
        public async Task TheEraserRubsOutWhereItPassesAndOneDragIsOneStep()
        {
            MainViewModel model = await Open();
            await Run(model, "sketch.draw", new JsonObject { ["points"] = Points((20, 100), (180, 100)), ["mirror"] = false });
            await Run(model, "sketch.draw", new JsonObject { ["points"] = Points((20, 140), (180, 140)), ["mirror"] = false });
            await Run(model, "sketch.setEraser", new JsonObject { ["radius"] = 3, ["mirror"] = false });
            Assert.AreEqual("sketch.eraser", model.ActiveTool!.Definition.Id, "setting it up chooses it");
            int steps = model.History.Count;

            model.Stroke([(100, 90), (100, 106), (60, 106), (60, 90)]);

            SketchSheet sheet = Sketch(model).Sheets.Single();
            Assert.AreEqual(steps + 1, model.History.Count, "the whole drag is one step");
            StringAssert.Contains(model.History.Last().Description, "Erase");
            Assert.AreEqual(4, sheet.Strokes.Length, "the first stroke is cut twice into three, the second stays");
            Assert.AreEqual(1, sheet.Strokes.Count(stroke => stroke.Points[0].Y == 140));
            Assert.IsTrue(model.Undo());
            Assert.AreEqual(2, Sketch(model).Sheets.Single().Strokes.Length);
        }

        [TestMethod]
        public async Task WithAltTheEraserRemovesWholeStrokesAndItsCommandDoesToo()
        {
            MainViewModel model = await Open();
            await Run(model, "sketch.draw", new JsonObject { ["points"] = Points((20, 100), (180, 100)), ["mirror"] = false });
            await Run(model, "sketch.draw", new JsonObject { ["points"] = Points((20, 140), (180, 140)), ["mirror"] = false });
            await Run(model, "sketch.setEraser", new JsonObject { ["radius"] = 5, ["mirror"] = false });
            EraserTool eraser = model.Tool<EraserTool>();
            Assert.IsTrue(eraser.UsesAlt, "the view leaves an alt click to it");

            eraser.Press(100, 102, new ToolInput(ToolModifiers.Alt, 0.5f));
            eraser.Release();
            Assert.AreEqual(1, Sketch(model).Sheets.Single().Strokes.Length, "the touched stroke goes entirely");
            Assert.AreEqual(140, Sketch(model).Sheets.Single().Strokes[0].Points[0].Y);

            CommandRun erased = await Run(model, "sketch.erase", new JsonObject { ["points"] = Points((100, 138)), ["whole"] = true });
            Assert.AreEqual(1, erased.Result);
            Assert.IsTrue(Sketch(model).Sheets.Single().Strokes.IsEmpty);
            int steps = model.History.Count;
            CommandRun nothing = await Run(model, "sketch.erase", new JsonObject { ["points"] = Points((100, 138)) });
            Assert.AreEqual(0, nothing.Result);
            Assert.AreEqual(steps, model.History.Count, "nothing touched, no step");
        }

        [TestMethod]
        public async Task TheEraserIsMirroredAndCancelledByEscape()
        {
            MainViewModel model = await Open();
            await Run(model, "select.symmetry", new JsonObject { ["kind"] = "rotational", ["count"] = 2 });
            await Run(model, "sketch.draw", new JsonObject { ["points"] = Points((40, 50), (90, 50)) });
            Assert.AreEqual(2, Sketch(model).Sheets.Single().Strokes.Length);
            EraserTool eraser = model.Tool<EraserTool>();
            await Run(model, "sketch.setEraser", new JsonObject { ["radius"] = 4 });
            Assert.IsTrue(eraser.Mirrors);

            model.Stroke([(65, 50)]);
            Assert.AreEqual(4, Sketch(model).Sheets.Single().Strokes.Length, "both strokes were cut");

            int steps = model.History.Count;
            eraser.Press(45, 50);
            Assert.IsNotNull(model.StrokeInProgress);
            Assert.IsTrue(eraser.Key("Escape", ToolModifiers.None));
            Assert.IsNull(model.StrokeInProgress);
            Assert.AreEqual(steps, model.History.Count);
            Assert.AreEqual(4, Sketch(model).Sheets.Single().Strokes.Length, "thrown away");
        }

        [TestMethod]
        public async Task TheOverlayHidesTheSketchAndDrawingShowsItAgain()
        {
            MainViewModel model = await Open();
            await Run(model, "sketch.draw", new JsonObject { ["points"] = Points((20, 100), (80, 100)), ["mirror"] = false });

            await Run(model, "view.overlay.sketch", new JsonObject { ["on"] = false });
            Assert.IsFalse(model.ShowSketch);
            Assert.IsFalse(model.Overlays.Single(overlay => overlay.Id == "sketch").IsOn);

            Assert.AreEqual(1, Sketch(model).StrokeCount, "hiding the overlay keeps the strokes");

            await Run(model, "sketch.draw", new JsonObject { ["points"] = Points((20, 120), (80, 120)), ["mirror"] = false });
            Assert.IsTrue(model.ShowSketch, "a stroke drawn shows the sketch");
            await Run(model, "view.overlay.sketch");
            Assert.IsFalse(model.ShowSketch, "toggles without an argument");
        }


        [TestMethod]
        public async Task TheSketchModeIsTheOneThatDrawsFully()
        {
            MainViewModel model = await Open();
            Assert.IsTrue(model.InSketchMode);
            model.SelectMode("terrain");
            Assert.IsFalse(model.InSketchMode);
        }

        [TestMethod]
        public async Task TheSketchIsKeptInTheProjectAndComesBack()
        {
            MainViewModel model = await Open();
            await Run(model, "sketch.addSheet", new JsonObject { ["name"] = "Layout A" });
            await Run(model, "sketch.draw", new JsonObject { ["points"] = Points((20, 100), (80, 120)), ["colour"] = "yellow", ["mirror"] = false });
            await Run(model, "sketch.setLegend", new JsonObject { ["colour"] = "yellow", ["text"] = "mass" });
            string folder = Path.Combine(Root, Guid.NewGuid().ToString("N"));
            await model.SaveAsAsync(folder);
            Assert.IsTrue(File.Exists(Path.Combine(folder, "sketch", "sketch.json")));

            MainViewModel again = new MainViewModel(new MainViewModelTest.Answers());
            await again.OpenAsync(folder);

            SketchSheet sheet = again.Map!.Document.Sketch.Sheets.Single();
            Assert.AreEqual("Layout A", sheet.Name);
            Assert.AreEqual(SketchColour.Yellow, sheet.Strokes.Single().Colour);
            Assert.AreEqual("mass", sheet.LegendOf(SketchColour.Yellow));
            Assert.AreEqual("Layout A", again.Sketch.Chosen);
            Assert.IsFalse(again.Map.Session.IsModified, "opening a project with a sketch is no edit");

            // the export knows nothing of it
            string exported = await model.ExportAsync(Path.Combine(Root, Guid.NewGuid().ToString("N")));
            Assert.IsFalse(Directory.EnumerateFiles(exported, "*", SearchOption.AllDirectories).Any(file => Path.GetRelativePath(exported, file).Contains("sketch", StringComparison.OrdinalIgnoreCase)));
        }

        [TestMethod]
        public async Task AnotherMapHasItsOwnSketch()
        {
            MainViewModel model = await Open();
            await Run(model, "sketch.draw", new JsonObject { ["points"] = Points((20, 100), (80, 100)) });
            Assert.AreEqual(1, model.Sketch.Items.Count);

            await model.OpenAsync(HardFfa);

            Assert.AreEqual(0, model.Sketch.Items.Count, "the sketch is the map's own");
            Assert.IsNull(model.Sketch.Chosen);
        }

        [TestMethod]
        public async Task EveryCommandOfTheSketchNamesItsStory()
        {
            MainViewModel model = await Open();
            string[] ids = [.. model.Commands.All.Where(command => command.Id.StartsWith("sketch.", StringComparison.Ordinal) && !command.Id.EndsWith(".mode", StringComparison.Ordinal)
                    && !command.Id.StartsWith("sketch.pencil", StringComparison.Ordinal) && !command.Id.StartsWith("sketch.eraser", StringComparison.Ordinal)
                    && !command.Id.StartsWith("sketch.symbol", StringComparison.Ordinal) && !command.Id.StartsWith("sketch.label", StringComparison.Ordinal)
                    && !command.Id.StartsWith("sketch.select", StringComparison.Ordinal) && !command.Id.StartsWith("sketch.image", StringComparison.Ordinal))
                .Select(command => command.Id).Order()];

            CollectionAssert.AreEqual(new[]
            {
                "sketch.addSheet", "sketch.chooseSheet", "sketch.clearSheet", "sketch.deleteSheet", "sketch.draw", "sketch.duplicateSheet", "sketch.erase",
                "sketch.renameSheet", "sketch.setEraser", "sketch.setLegend", "sketch.setOpacity", "sketch.setPencil", "sketch.showSheet",
            }, ids);
            Assert.IsTrue(ids.All(id => model.Commands.Find(id)!.Story == "N-1"));
        }
    }
}
