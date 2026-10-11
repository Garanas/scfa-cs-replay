using System.Text.Json.Nodes;
using Avalonia.Threading;
using FAForever.MapEditor.App.Automation;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The height curve in the window (user story T-4, wave 18): the inspector's groups, the table of
    /// points with its fields, grades, chosen points and buttons, the curve tool's height by hand and
    /// the lines it shows.
    /// </summary>
    [TestClass]
    public sealed class CurveWindowTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private static async Task<(MainWindow Window, MainViewModel Model)> Open()
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers { SaveChoice = SaveChoice.Discard });
            await model.OpenAsync(HardFfa);
            MainWindow window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
            window.Show();
            model.SelectMode("terrain");
            Dispatcher.UIThread.RunJobs();
            return (window, model);
        }

        private static async Task Run(MainViewModel model, string id, JsonObject? arguments = null)
        {
            CommandRun run = await model.Commands.RunAsync(id, arguments ?? [], interactive: false);
            Assert.IsTrue(run.Ran, $"{id}: {run.Why} {run.Error}");
            Dispatcher.UIThread.RunJobs();
        }

        private static HeightCurve Curve(MainViewModel model, string name) => HeightLayerEdits.CurveOf(model.Map!.Document, name);

        private static async Task Ramp(MainViewModel model, string profile = "road")
        {
            await Run(model, "terrain.addCurve", new JsonObject
            {
                ["name"] = "ramp", ["profile"] = profile, ["width"] = 6, ["falloff"] = 8,
                ["points"] = new JsonArray(new JsonArray(40, 100, 10), new JsonArray(80, 100, 10), new JsonArray(120, 100, 10), new JsonArray(160, 100, 40)),
            });
        }

        [TestMethod]
        public Task TheTableShowsAPointPerRowAndEditsRunTheCommands() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            await Ramp(model);
            CurveNodesViewModel nodes = model.HeightStack.Nodes;
            Assert.AreEqual(4, nodes.Rows.Count);
            Assert.AreEqual("80", nodes.Rows[1].X);
            Assert.AreEqual("10", nodes.Rows[1].Height);
            Assert.AreEqual("", nodes.Rows[1].Width, "no width of its own: empty, the curve's as a hint");
            Assert.AreEqual("6", nodes.Rows[1].WidthHint);

            // a field typed is one step of undo and a command
            int steps = model.Map!.Session.History.Count;
            nodes.Rows[1].Height = "12.5";
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(12.5f, Curve(model, "ramp").Points[1].Height);
            Assert.AreEqual(steps + 1, model.Map.Session.History.Count);
            nodes.Rows[1].Width = "14";
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(14f, Curve(model, "ramp").Points[1].Width);
            Assert.AreEqual("14", nodes.Rows[1].Width, "the row follows the curve");

            // an emptied field gives the value back to the curve
            nodes.Rows[1].Width = "";
            Dispatcher.UIThread.RunJobs();
            Assert.IsNull(Curve(model, "ramp").Points[1].Width);
            nodes.Rows[2].Sharp = true;
            Dispatcher.UIThread.RunJobs();
            Assert.IsTrue(Curve(model, "ramp").Points[2].Sharp);

            // text that is no number leaves the point as it is
            int before = model.Map.Session.History.Count;
            nodes.Rows[1].Height = "high";
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(before, model.Map.Session.History.Count);
            Assert.AreEqual("12.5", nodes.Rows[1].Height, "shows the value again");

            // the grade between rows, marked above the limit
            Assert.IsFalse(string.IsNullOrEmpty(nodes.Rows[0].Grade));
            Assert.IsTrue(string.IsNullOrEmpty(nodes.Rows[3].Grade), "no stretch after the last point of an open curve");
            nodes.GradeLimit = 5;
            Assert.IsTrue(nodes.Rows[2].IsSteep, "30 units over 40 ogrids");
            nodes.GradeLimit = 500;
            Assert.IsFalse(nodes.Rows[2].IsSteep);
            window.Close();
        });

        [TestMethod]
        public Task ChosenPointsFollowTheTableTheCommandAndTheButtons() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            await Ramp(model);
            CurveNodesViewModel nodes = model.HeightStack.Nodes;
            Assert.AreEqual(0, nodes.Chosen.Count);
            Assert.IsTrue(nodes.EvenGradeCommand.CanExecute(null), "without a choice: the curve's ends");
            Assert.IsFalse(nodes.SnapChosenCommand.CanExecute(null), "nothing chosen to snap");

            await Run(model, "terrain.chooseCurvePoints", new JsonObject { ["points"] = new JsonArray(1, 2) });
            CollectionAssert.AreEqual(new[] { 1, 2 }, nodes.Chosen.ToArray());
            Assert.IsTrue(nodes.Rows[1].IsChosen && nodes.Rows[2].IsChosen && !nodes.Rows[0].IsChosen);
            Assert.IsFalse(nodes.EvenGradeCommand.CanExecute(null), "two neighbours have nothing between them");
            Assert.IsTrue(nodes.SnapChosenCommand.CanExecute(null));
            await Run(model, "terrain.chooseCurvePoints", new JsonObject { ["points"] = new JsonArray(0, 3) });
            Assert.AreEqual("Even the grade (points 0 to 3)", nodes.EvenTitle);

            int steps = model.Map!.Session.History.Count;
            nodes.EvenGradeCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(20, Curve(model, "ramp").Points[1].Height, 0.5);
            Assert.AreEqual(steps + 1, model.Map.Session.History.Count);

            nodes.ReverseCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(160, Curve(model, "ramp").Points[0].X, 1e-3);
            Assert.AreEqual(steps + 2, model.Map.Session.History.Count);

            nodes.SnapAllCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(steps + 3, model.Map.Session.History.Count);
            window.Close();
        });

        [TestMethod]
        public Task SnapIsOffForARidgeWithTheReason() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            await Ramp(model, "ridge");
            CurveNodesViewModel nodes = model.HeightStack.Nodes;
            Assert.IsFalse(nodes.CanSnap);
            Assert.IsFalse(nodes.SnapAllCommand.CanExecute(null));
            StringAssert.Contains(nodes.SnapReason, "Roads and rivers only");
            window.Close();
        });

        [TestMethod]
        public Task AFillHasOneHeightAndTheGroupsShowWhatTheProfileUses() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            await Run(model, "terrain.addCurve", new JsonObject
            {
                ["name"] = "pond", ["profile"] = "valley", ["closed"] = true,
                ["points"] = new JsonArray(new JsonArray(60, 60), new JsonArray(100, 60), new JsonArray(100, 100), new JsonArray(60, 100)),
            });
            HeightLayersViewModel stack = model.HeightStack;
            Assert.IsFalse(stack.Nodes.Rows[0].HeightEditable, "a fill has one height");
            CurveSettingGroup Group(string title) => stack.CurveGroups.Single(group => group.Group.Title == title);
            Assert.IsTrue(Group("Line only").IsShown, "closed curves can be line only");
            Assert.IsFalse(Group("Cliff").IsShown);
            Assert.IsFalse(Group("Road").IsShown);
            Assert.IsTrue(Group("Falloff").IsShown);
            Assert.IsFalse(Group("Fade back").IsShown);

            await Run(model, "terrain.setCurve", new JsonObject { ["closed"] = false, ["profile"] = "cliff" });
            Assert.IsFalse(Group("Line only").IsShown);
            Assert.IsTrue(Group("Cliff").IsShown, "a cliff has a face");
            Assert.IsTrue(Group("Fade back").IsShown);
            Assert.IsFalse(Group("Falloff").IsShown, "a cliff's falloff is the fade back");
            Assert.IsTrue(stack.Nodes.Rows[0].HeightEditable);

            await Run(model, "terrain.setCurve", new JsonObject { ["profile"] = "road" });
            Assert.IsTrue(Group("Road").IsShown);
            Assert.IsTrue(Group("Camber").IsShown);
            window.Close();
        });

        [TestMethod]
        public Task TheMirroredBoxFollowsTheCurveAndFalloffsAndVariationAreFields() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            await Run(model, "select.symmetry", new JsonObject { ["kind"] = "rotational", ["count"] = 2 });
            await Ramp(model);
            HeightLayersViewModel stack = model.HeightStack;
            BoolSetting Bool(string label) => stack.CurveGroups.SelectMany(group => group.Group.Settings).OfType<BoolSetting>().Single(setting => setting.Label == label);
            NumberSetting Number(string label, string title) => stack.CurveGroups.Single(group => group.Group.Title == title).Group.Settings.OfType<NumberSetting>().Single(setting => setting.Label == label);

            await Run(model, "terrain.setCurve", new JsonObject { ["mirrored"] = true });
            Assert.IsTrue(Bool("Mirrored").IsOn, "the box shows what a command set");
            Assert.IsNotNull(Curve(model, "ramp").Symmetry);

            // the right falloff has its own field once the sides differ
            Assert.IsTrue(Bool("Same on both sides").IsOn);
            Bool("Same on both sides").IsOn = false;
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(8f, Curve(model, "ramp").FalloffRight, "starts as the left's");
            Assert.IsFalse(Bool("Same on both sides").IsOn);
            Number("Right falloff", "Right falloff").Text = "3";
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(3f, Curve(model, "ramp").FalloffRight);

            // the points share a height: the variation's range is shown as heights, typing one end keeps the other
            await Run(model, "terrain.setCurve", new JsonObject { ["height"] = 4 });
            Number("Height from", "Variation along the curve").Text = "4";
            Number("Height to", "Variation along the curve").Text = "5";
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(0f, Curve(model, "ramp").HeightVariation.Min, 1e-3, "an offset from the points' height of 4");
            Assert.AreEqual(1f, Curve(model, "ramp").HeightVariation.Max, 1e-3);
            Assert.AreEqual(5, Number("Height to", "Variation along the curve").Value, 1e-3, "shown as the absolute height");
            window.Close();
        });

        [TestMethod]
        public Task TheToolChoosesAPointAndSetsItsHeightByHandInOneStep() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            await Ramp(model, "ridge");
            model.SelectTool("terrain.curve");
            CurveTool tool = model.Tool<CurveTool>();
            ToolInput input = new ToolInput(ToolModifiers.Shift, 0.5f);

            // pressing a point chooses it
            tool.Press(80, 100, input with { Modifiers = ToolModifiers.None });
            tool.Release();
            CollectionAssert.AreEqual(new[] { 1 }, model.HeightStack.Nodes.Chosen.ToArray());

            // shift and a drag up on the screen raises it; the status line says the height; one step
            int steps = model.Map!.Session.History.Count;
            float start = Curve(model, "ramp").Points[1].Height;
            tool.Press(80, 100, input);
            tool.Drag(80, 90, input);
            tool.Drag(80, 80, input);
            StringAssert.Contains(model.Status, "height");
            tool.Release();
            Dispatcher.UIThread.RunJobs();
            CurvePoint raised = Curve(model, "ramp").Points[1];
            Assert.AreEqual(start + 10, raised.Height, 0.2, "20 ogrids up: 40 pixels at half an ogrid, half a unit each");
            Assert.AreEqual(80, raised.X, 1e-3, "the place stays");
            Assert.AreEqual(100, raised.Z, 1e-3);
            Assert.AreEqual(steps + 1, model.Map.Session.History.Count, "one undo step for the drag");

            // + and - change the chosen point's height (shift: finer, control: coarser)
            Assert.IsTrue(tool.Key("OemPlus", ToolModifiers.None));
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(raised.Height + 1, Curve(model, "ramp").Points[1].Height, 1e-3);
            Assert.IsTrue(tool.Key("OemMinus", ToolModifiers.Control));
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(raised.Height - 4, Curve(model, "ramp").Points[1].Height, 1e-3);
            window.Close();
        });

        [TestMethod]
        public Task ANewPointsHeightComesFromTheOptionAndAFillRefusesTheGesture() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            await Ramp(model, "road");
            model.SelectTool("terrain.curve");
            CurveTool tool = model.Tool<CurveTool>();
            ToolInput input = new ToolInput(ToolModifiers.None, 0.5f);
            Assert.AreEqual("Automatic", tool.PointHeightChoice);

            // automatic on a road: the ground under the click; typed: the number
            tool.Press(200, 120, input);
            tool.Release();
            Dispatcher.UIThread.RunJobs();
            float ground = model.ShownHeightmap!.HeightAt(200, 120);
            Assert.AreEqual(5, Curve(model, "ramp").Points.Length);
            Assert.AreNotEqual(40f, Curve(model, "ramp").Points[4].Height, "not the last point's");
            Assert.AreEqual(ground, Curve(model, "ramp").Points[4].Height, 1.5, "the ground");
            await Run(model, "terrain.setCurveTool", new JsonObject { ["pointHeight"] = "Typed", ["typedHeight"] = 33 });
            Assert.IsTrue(tool.IsTypedHeight);
            tool.Press(210, 140, input);
            tool.Release();
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(33f, Curve(model, "ramp").Points[5].Height);
            await Run(model, "terrain.setCurveTool", new JsonObject { ["pointHeight"] = "Last" });
            tool.Press(220, 150, input);
            tool.Release();
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(33f, Curve(model, "ramp").Points[6].Height, "the last point's");

            // a fill has one height: the gesture and the keys say so
            await Run(model, "terrain.addCurve", new JsonObject
            {
                ["name"] = "pond", ["profile"] = "valley", ["closed"] = true,
                ["points"] = new JsonArray(new JsonArray(60, 40), new JsonArray(100, 40), new JsonArray(100, 70)),
            });
            int steps = model.Map!.Session.History.Count;
            Assert.IsTrue(tool.Key("OemPlus", ToolModifiers.None));
            StringAssert.Contains(model.Status, "one height");
            Assert.AreEqual(steps, model.Map.Session.History.Count);
            window.Close();
        });

        [TestMethod]
        public Task TheMarksShowTheReachChosenPointsAndCorners() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            await Run(model, "select.symmetry", new JsonObject { ["kind"] = "rotational", ["count"] = 2 });
            await Ramp(model);
            await Run(model, "terrain.setCurve", new JsonObject { ["mirrored"] = true });
            await Run(model, "terrain.setCurvePoint", new JsonObject { ["index"] = 2, ["sharp"] = true });
            await Run(model, "terrain.chooseCurvePoints", new JsonObject { ["points"] = new JsonArray(1) });
            model.SelectTool("terrain.curve");
            CurveTool tool = model.Tool<CurveTool>();
            ToolMarks marks = tool.Marks!;
            Assert.AreEqual(4, marks.Handles.Count);
            CollectionAssert.AreEqual(new[] { 1 }, marks.ChosenHandles!.ToArray());
            CollectionAssert.AreEqual(new[] { 2 }, marks.CornerHandles!.ToArray());
            Assert.AreEqual(4 + 4, marks.Outlines!.Count, "a road's two edges and two fades, and the same for its image");
            Assert.AreEqual(4, marks.Outlines.Count(outline => outline.Dotted), "the fades are dotted");
            Assert.AreEqual(4, marks.Outlines.Count(outline => outline.Thin), "the image's are thinner");
            Assert.AreSame(marks, tool.Marks, "made once for the same curve");

            tool.ShowReach = false;
            Assert.IsNull(tool.Marks!.Outlines);
            Assert.AreEqual(true, (await StateOf(window, model))["heightLayers"]!["curveTool"]!["chosenPoints"] is JsonArray { Count: 1 });
            window.Close();
        });

        private static async Task<JsonObject> StateOf(MainWindow window, MainViewModel model)
        {
            AutomationServer server = new AutomationServer(window, model, port: 0);
            return (JsonObject)(await server.HandleAsync("state", new JsonObject()))!;
        }
    }
}
