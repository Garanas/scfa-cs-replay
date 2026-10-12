using System.Numerics;
using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Automation;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The sketch's symbols, labels and the Select tool (user story N-4): each operation a command and one step
    /// of undo, the tools' mouse and keys, the eraser with Alt, and what the state says.
    /// </summary>
    [TestClass]
    public sealed class SketchMarksTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private static async Task<MainViewModel> Open()
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            await model.OpenAsync(HardFfa);
            model.SelectMode("sketch");
            return model;
        }

        private static async Task<CommandRun> Run(MainViewModel model, string id, JsonObject? arguments = null) =>
            await model.Commands.RunAsync(id, arguments ?? [], interactive: false);

        // a command that is refused: it did not run, and says why
        private static async Task<CommandRun> Try(MainViewModel model, string id, JsonObject? arguments = null) =>
            await model.Commands.TryRunAsync(id, arguments ?? [], interactive: false);

        private static SketchSheet Sheet(MainViewModel model) => model.Map!.Document.Sketch.Sheets.Single();

        private static JsonArray Points(params (float X, float Z)[] points) => new JsonArray([.. points.Select(point => (JsonNode)new JsonArray(point.X, point.Z))]);

        [TestMethod]
        public async Task ASymbolIsStampedByACommandAsOneStepAndMirroredBySymmetry()
        {
            MainViewModel model = await Open();

            CommandRun one = await Run(model, "sketch.symbol", new JsonObject { ["kind"] = "mass", ["x"] = 60, ["z"] = 70, ["colour"] = "green", ["size"] = "large", ["mirror"] = false });
            Assert.IsTrue(one.Ran, one.Why);
            Assert.AreEqual(1, one.Result);
            SketchSymbol mass = Sheet(model).Symbols.Single();
            Assert.AreEqual(SketchSymbolKind.Mass, mass.Kind);
            Assert.AreEqual(new Vector2(60, 70), mass.Place);
            Assert.AreEqual(SketchColour.Green, mass.Colour);
            Assert.AreEqual(SketchLook.SymbolOgrids(SketchSize.Large), mass.Size);
            Assert.IsTrue(model.ShowSketch);

            await Run(model, "select.symmetry", new JsonObject { ["kind"] = "rotational", ["count"] = 2 });
            CommandRun both = await Run(model, "sketch.symbol", new JsonObject { ["kind"] = "arrow", ["x"] = 20, ["z"] = 30, ["turn"] = 90 });
            Assert.AreEqual(2, both.Result);
            SketchSymbol image = Sheet(model).Symbols[2];
            Assert.AreEqual(model.Map!.Document.Width - 20, image.Place.X, 1e-3f);
            Assert.AreEqual(270f, image.Turn, 0.01f, "the image points the other way");
            Assert.AreEqual(2, model.History.Count(step => step.Description == "Stamp a symbol in the sketch"), "one step for each command, the symbol and its image together");

            Assert.IsTrue(model.Undo());
            Assert.AreEqual(1, Sheet(model).Symbols.Length);

            CommandRun wrong = await Try(model, "sketch.symbol", new JsonObject { ["kind"] = "star", ["x"] = 1, ["z"] = 1 });
            Assert.IsFalse(wrong.Ran);
            StringAssert.Contains(wrong.Why, "spawn, mass, hydrocarbon, arrow, cross, question");
        }

        [TestMethod]
        public async Task ALabelIsWrittenOnceAndItsTextChangedEachOneStep()
        {
            MainViewModel model = await Open();
            await Run(model, "select.symmetry", new JsonObject { ["kind"] = "rotational", ["count"] = 2 });

            CommandRun written = await Run(model, "sketch.label", new JsonObject { ["text"] = "spawn 1", ["x"] = 40, ["z"] = 50, ["colour"] = 4, ["size"] = 20 });
            Assert.IsTrue(written.Ran, written.Why);
            SketchLabel label = Sheet(model).Labels.Single();
            Assert.AreEqual("spawn 1", label.Text);
            Assert.AreEqual(SketchColour.Blue, label.Colour);
            Assert.AreEqual(20f, label.Size);
            Assert.AreEqual(1, Sheet(model).Labels.Length, "written once, not mirrored");

            CommandRun edited = await Run(model, "sketch.editLabel", new JsonObject { ["label"] = 0, ["text"] = "start 1" });
            Assert.IsTrue(edited.Ran, edited.Why);
            Assert.AreEqual("start 1", Sheet(model).Labels.Single().Text);
            Assert.IsTrue(model.Undo());
            Assert.AreEqual("spawn 1", Sheet(model).Labels.Single().Text);

            Assert.IsFalse((await Try(model, "sketch.label", new JsonObject { ["text"] = "  ", ["x"] = 1, ["z"] = 1 })).Ran);
            Assert.IsFalse((await Try(model, "sketch.label", new JsonObject { ["text"] = "x", ["x"] = 1, ["z"] = 1, ["size"] = 0 })).Ran);
        }

        [TestMethod]
        public async Task TheToolsAreSetUpByCommandsWhichChooseThem()
        {
            MainViewModel model = await Open();

            await Run(model, "sketch.setSymbol", new JsonObject { ["kind"] = "hydrocarbon", ["colour"] = "yellow", ["size"] = "small", ["mirror"] = false });
            SymbolTool symbol = model.Tool<SymbolTool>();
            Assert.AreEqual("sketch.symbol", model.ActiveTool!.Definition.Id);
            Assert.AreEqual(SketchSymbolKind.Hydrocarbon, symbol.Kind);
            Assert.AreEqual(SketchColour.Yellow, symbol.Colour);
            Assert.AreEqual(SketchLook.SymbolOgrids(SketchSize.Small), symbol.Ogrids);
            Assert.IsFalse(symbol.Mirror);
            Assert.IsTrue(symbol.Kinds[(int)SketchSymbolKind.Hydrocarbon].IsOn && !symbol.Kinds[0].IsOn);
            Assert.IsTrue(symbol.Sizes[(int)SketchSize.Small].IsOn);

            await Run(model, "sketch.setSymbol", new JsonObject { ["size"] = 33.5 });
            Assert.AreEqual(33.5f, symbol.Ogrids, "a number of ogrids");
            Assert.IsFalse((await Try(model, "sketch.setSymbol", new JsonObject { ["size"] = 999 })).Ran);

            await Run(model, "sketch.setLabel", new JsonObject { ["colour"] = "white", ["size"] = "large" });
            LabelTool label = model.Tool<LabelTool>();
            Assert.AreEqual("sketch.label", model.ActiveTool!.Definition.Id);
            Assert.AreEqual(SketchColour.White, label.Colour);
            Assert.AreEqual(SketchLook.LabelOgrids(SketchSize.Large), label.Ogrids);

            // a click on the tool's choice runs the command
            symbol.Colours[(int)SketchColour.Blue].ChooseCommand.Execute(null);
            Assert.AreEqual(SketchColour.Blue, symbol.Colour);
        }

        [TestMethod]
        public async Task TheSymbolToolStampsOnReleaseAndAnArrowFollowsTheDrag()
        {
            MainViewModel model = await Open();
            await Run(model, "sketch.setSymbol", new JsonObject { ["kind"] = "arrow", ["mirror"] = false });
            SymbolTool tool = model.Tool<SymbolTool>();

            tool.Press(100, 100);
            Assert.AreEqual(1, model.Sketch.PendingSymbols.Count, "shown under the mouse");
            Assert.IsTrue(model.Map!.Document.Sketch.IsEmpty, "not yet part of the map");
            tool.Drag(100, 140);
            Assert.AreEqual(90f, model.Sketch.PendingSymbols[0].Turn, 0.01f, "pointing towards +z");
            tool.Release();
            Dispatcher_RunJobs();

            Assert.AreEqual(0, model.Sketch.PendingSymbols.Count);
            SketchSymbol arrow = Sheet(model).Symbols.Single();
            Assert.AreEqual(new Vector2(100, 100), arrow.Place);
            Assert.AreEqual(90f, arrow.Turn, 0.1f);

            // a click without a drag takes the Turn in the row
            await Run(model, "sketch.setSymbol", new JsonObject { ["turn"] = 45 });
            tool.Press(200, 200);
            tool.Release();
            Assert.AreEqual(45f, Sheet(model).Symbols[1].Turn, 0.1f);

            tool.Press(10, 10);
            Assert.IsTrue(tool.Key("Escape", ToolModifiers.None));
            tool.Release();
            Assert.AreEqual(2, Sheet(model).Symbols.Length, "a symbol given up stamps nothing");
        }

        private static void Dispatcher_RunJobs() => Avalonia.Threading.Dispatcher.UIThread.RunJobs();


        [TestMethod]
        public async Task TheLabelToolOpensATextBoxEnterKeepsAndEscapeDrops()
        {
            MainViewModel model = await Open();
            await Run(model, "sketch.setLabel", new JsonObject { ["colour"] = "green" });
            LabelTool tool = model.Tool<LabelTool>();

            tool.Press(80, 90);
            Assert.IsTrue(model.Sketch.IsWriting);
            Assert.AreEqual(new Vector2(80, 90), model.Sketch.Draft!.Place);
            model.Sketch.LabelText = "ramp";
            await model.Sketch.Keep();
            Assert.IsFalse(model.Sketch.IsWriting);
            SketchLabel label = Sheet(model).Labels.Single();
            Assert.AreEqual("ramp", label.Text);
            Assert.AreEqual(new Vector2(80, 90), label.Place);
            Assert.AreEqual(SketchColour.Green, label.Colour);

            tool.Press(10, 10);
            model.Sketch.LabelText = "never";
            Assert.IsTrue(tool.Key("Escape", ToolModifiers.None));
            Assert.IsFalse(model.Sketch.IsWriting);
            Assert.AreEqual(1, Sheet(model).Labels.Length);

            tool.Press(10, 10);
            await model.Sketch.Keep();
            Assert.AreEqual(1, Sheet(model).Labels.Length, "an empty label is dropped");

            tool.Press(10, 10);
            model.Sketch.LabelText = "first";
            tool.Press(30, 30);
            Assert.IsTrue(model.Sketch.IsWriting, "a click elsewhere begins another");
            await Task.Delay(50);
            Assert.AreEqual(2, Sheet(model).Labels.Length, "and keeps the one that was being written");

            await Run(model, "tool.sketch.pencil");
            Assert.IsFalse(model.Sketch.IsWriting, "another tool drops what was being written");
        }

        private static async Task<MainViewModel> WithSomething()
        {
            MainViewModel model = await Open();
            await Run(model, "sketch.draw", new JsonObject { ["points"] = Points((10, 10), (60, 10)), ["mirror"] = false, ["colour"] = "red" });
            await Run(model, "sketch.draw", new JsonObject { ["points"] = Points((10, 50), (60, 50)), ["mirror"] = false, ["colour"] = "red" });
            await Run(model, "sketch.symbol", new JsonObject { ["kind"] = "cross", ["x"] = 100, ["z"] = 100, ["mirror"] = false, ["colour"] = "blue", ["size"] = 10 });
            await Run(model, "sketch.label", new JsonObject { ["text"] = "ramp", ["x"] = 150, ["z"] = 150, ["colour"] = "green", ["size"] = 16 });
            return model;
        }

        [TestMethod]
        public async Task TheSelectToolClicksBoxesAndShiftAddsAndTheCommandsDoTheSame()
        {
            MainViewModel model = await WithSomething();
            await Run(model, "tool.sketch.select");
            SketchSelectTool tool = model.Tool<SketchSelectTool>();
            ToolInput input = new ToolInput(ToolModifiers.None, 0.5f);
            ToolInput shift = new ToolInput(ToolModifiers.Shift, 0.5f);

            tool.Press(30, 11, input);
            tool.Release();
            CollectionAssert.AreEqual(new[] { new SketchItem(SketchItemKind.Stroke, 0) }, tool.Selected.ToArray());

            tool.Press(100, 100, shift);
            tool.Release();
            Assert.AreEqual(2, tool.Selected.Length, "Shift adds");
            tool.Press(100, 100, shift);
            tool.Release();
            Assert.AreEqual(1, tool.Selected.Length, "Shift on a selected item takes it away");

            tool.Press(0, 0, input);
            tool.Drag(70, 70, input);
            Assert.IsNotNull(tool.DraggedBox);
            tool.Release();
            Assert.AreEqual(2, tool.Selected.Length, "a box takes the two strokes it crosses");
            Assert.IsNull(tool.DraggedBox);

            tool.Press(300, 300, input);
            tool.Release();
            Assert.AreEqual(0, tool.Selected.Length, "a click on nothing selects nothing");

            CommandRun all = await Run(model, "sketch.select", new JsonObject { ["all"] = true });
            Assert.AreEqual(4, all.Result);
            CommandRun box = await Run(model, "sketch.select", new JsonObject { ["box"] = new JsonArray(90, 90, 160, 160) });
            Assert.AreEqual(2, box.Result, "the symbol and the label");
            CommandRun at = await Run(model, "sketch.select", new JsonObject { ["at"] = new JsonArray(100, 100), ["add"] = true });
            Assert.AreEqual(2, at.Result, "already selected");
            Assert.AreEqual(0, (await Run(model, "sketch.select")).Result, "no arguments selects nothing");
        }

        [TestMethod]
        public async Task ADragMovesTheSelectionAsOneStepOfUndoAndEscapeThrowsItAway()
        {
            MainViewModel model = await WithSomething();
            await Run(model, "sketch.select", new JsonObject { ["all"] = true });
            SketchSelectTool tool = model.Tool<SketchSelectTool>();
            ToolInput input = new ToolInput(ToolModifiers.None, 0.5f);
            int steps = model.History.Count;

            tool.Press(100, 100, input);
            tool.Drag(110, 105, input);
            tool.Drag(120, 130, input);
            tool.Release();

            SketchSheet sheet = Sheet(model);
            Assert.AreEqual(new Vector2(30, 40), sheet.Strokes[0].Points[0]);
            Assert.AreEqual(new Vector2(120, 130), sheet.Symbols[0].Place);
            Assert.AreEqual(new Vector2(170, 180), sheet.Labels[0].Place);
            Assert.AreEqual(steps + 1, model.History.Count, "the whole drag is one step");
            Assert.IsTrue(model.Undo());
            Assert.AreEqual(new Vector2(10, 10), Sheet(model).Strokes[0].Points[0]);
            Assert.AreEqual(4, tool.Selected.Length, "the selection stays");

            tool.Press(100, 100, input);
            tool.Drag(150, 150, input);
            Assert.IsTrue(tool.Key("Escape", ToolModifiers.None));
            tool.Release();
            Assert.AreEqual(new Vector2(100, 100), Sheet(model).Symbols[0].Place, "Escape gives the move up");

            CommandRun moved = await Run(model, "sketch.move", new JsonObject { ["dx"] = 5, ["dz"] = -5 });
            Assert.AreEqual(4, moved.Result);
            Assert.AreEqual(new Vector2(105, 95), Sheet(model).Symbols[0].Place);
        }

        [TestMethod]
        public async Task TheSelectionIsRecolouredResizedAndDeletedEachOneStep()
        {
            MainViewModel model = await WithSomething();
            await Run(model, "sketch.select", new JsonObject { ["box"] = new JsonArray(90, 90, 160, 160) });
            SketchSelectTool tool = model.Tool<SketchSelectTool>();

            Assert.IsTrue(tool.Key("D3", ToolModifiers.None), "a colour key recolours");
            await Task.Delay(50);
            Assert.AreEqual(SketchColour.Green, Sheet(model).Symbols[0].Colour);
            Assert.AreEqual(SketchColour.Green, Sheet(model).Labels[0].Colour);
            Assert.AreEqual(SketchColour.Red, Sheet(model).Strokes[0].Colour, "what was not selected stays");
            tool.Colours[(int)SketchColour.Black].ChooseCommand.Execute(null);
            await Task.Delay(50);
            Assert.AreEqual(SketchColour.Black, Sheet(model).Symbols[0].Colour, "the row's swatch too");

            CommandRun resized = await Run(model, "sketch.resize", new JsonObject { ["size"] = "small" });
            Assert.AreEqual(2, resized.Result);
            Assert.AreEqual(SketchLook.SymbolOgrids(SketchSize.Small), Sheet(model).Symbols[0].Size);
            Assert.AreEqual(SketchLook.LabelOgrids(SketchSize.Small), Sheet(model).Labels[0].Size, "a label takes the label's size");

            CommandRun deleted = await Run(model, "sketch.deleteSelected");
            Assert.AreEqual(2, deleted.Result);
            Assert.IsTrue(Sheet(model).Symbols.IsEmpty && Sheet(model).Labels.IsEmpty);
            Assert.AreEqual(2, Sheet(model).Strokes.Length);
            Assert.AreEqual(0, tool.Selected.Length);
            Assert.IsFalse((await Try(model, "sketch.deleteSelected")).Ran, "nothing is selected");
            Assert.IsTrue(model.Undo());
            Assert.AreEqual(1, Sheet(model).Symbols.Length);
        }

        [TestMethod]
        public async Task ADoubleClickOnALabelEditsItsTextAndEnterKeepsIt()
        {
            MainViewModel model = await WithSomething();
            await Run(model, "tool.sketch.select");
            SketchSelectTool tool = model.Tool<SketchSelectTool>();

            tool.Press(150, 150, new ToolInput(ToolModifiers.None, 0.5f, Clicks: 2));
            tool.Release();
            Assert.IsTrue(model.Sketch.IsWriting);
            Assert.AreEqual("ramp", model.Sketch.LabelText);
            Assert.AreEqual(0, model.Sketch.Draft!.Index);
            model.Sketch.LabelText = "ramp up";
            await model.Sketch.Keep();
            Assert.AreEqual("ramp up", Sheet(model).Labels.Single().Text);
        }

        [TestMethod]
        public async Task WithAltTheEraserTakesSymbolsAndLabelsToo()
        {
            MainViewModel model = await WithSomething();
            await Run(model, "sketch.setEraser", new JsonObject { ["radius"] = 3, ["mirror"] = false });

            CommandRun rubbed = await Run(model, "sketch.erase", new JsonObject { ["points"] = Points((90, 100), (110, 100)) });
            Assert.AreEqual(0, rubbed.Result, "rubbing leaves the symbol");
            CommandRun whole = await Run(model, "sketch.erase", new JsonObject { ["points"] = Points((90, 100), (110, 100)), ["whole"] = true });
            Assert.AreEqual(1, whole.Result);
            Assert.IsTrue(Sheet(model).Symbols.IsEmpty);

            EraserTool eraser = model.Tool<EraserTool>();
            eraser.Press(150, 150, new ToolInput(ToolModifiers.Alt, 0.5f));
            eraser.Release();
            Assert.IsTrue(Sheet(model).Labels.IsEmpty, "the mouse with Alt takes the label");
            Assert.AreEqual(2, Sheet(model).Strokes.Length);
        }

        [TestMethod]
        public async Task ASheetCountsItsStrokesSymbolsAndLabels()
        {
            MainViewModel model = await WithSomething();

            SketchSheetItem item = model.Sketch.Items.Single();
            Assert.AreEqual("2 strokes · 1 symbol · 1 label", item.Summary);
            Assert.AreEqual("4", item.CountText);
            Assert.AreEqual("Sheet 1: 2 strokes, 1 symbol, 1 label", item.Label);
            Assert.AreEqual("2 strokes · 1 symbol · 1 label", model.Sketch.ChosenCount);

            await Run(model, "sketch.select", new JsonObject { ["all"] = true });
            await Run(model, "sketch.clearSheet");
            Assert.AreEqual("0 strokes", model.Sketch.Items.Single().Summary);
        }

        [TestMethod]
        public async Task TheOtherModesNoteCountsSymbolsAndLabelsToo()
        {
            MainViewModel model = await Open();
            await Run(model, "sketch.symbol", new JsonObject { ["x"] = 10, ["z"] = 10 });

            model.SelectMode("terrain");
            StringAssert.Contains(model.SketchNote, "Sheet 1");
        }

        [TestMethod]
        public async Task TheSketchWithSymbolsAndLabelsIsKeptInTheProject()
        {
            MainViewModel model = await WithSomething();
            string folder = Path.Combine(AppContext.BaseDirectory, "marks-projects", Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(folder);
            try
            {
                FAForever.MapEditor.Project.MapProjectStore.Save(model.Map!.Document, folder);
                FAForever.MapEditor.Project.Files.InMemory.MapDocument loaded = FAForever.MapEditor.Project.MapProjectStore.Load(folder);
                Assert.AreEqual(1, loaded.Sketch.Sheets[0].Symbols.Length);
                Assert.AreEqual("ramp", loaded.Sketch.Sheets[0].Labels[0].Text);
            }
            finally
            {
                foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }
                Directory.Delete(folder, recursive: true);
            }
        }
    }
}
