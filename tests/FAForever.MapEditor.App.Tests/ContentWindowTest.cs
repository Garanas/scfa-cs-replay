using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;
using FAForever.MapEditor.Project.Editing;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// Content to use again in the real window (headless): dragging in the Units mode's tree (U-3).
    /// </summary>
    [TestClass]
    public sealed class ContentWindowTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private static async Task<(MainWindow Window, MainViewModel Model)> Open()
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            model.Assets.Use(UnitsTest.Catalog());
            await model.OpenAsync(HardFfa);
            MainWindow window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            return (window, model);
        }

        private static Point Middle(Control control, MainWindow window) =>
            control.TranslatePoint(new Point(Math.Min(40, control.Bounds.Width / 2), 12), window)!.Value;

        private static TreeViewItem Item(MainWindow window, Func<object?, bool> holds) =>
            window.GetVisualDescendants().OfType<TreeViewItem>().First(item => holds(item.DataContext));

        /// <summary>
        /// Presses the left mouse at one point, moves it there in steps and lets go.
        /// </summary>
        private static void Drag(MainWindow window, Point from, Point to)
        {
            window.MouseMove(from);
            window.MouseDown(from, MouseButton.Left);
            for (int step = 1; step <= 8; step++)
            {
                window.MouseMove(from + (to - from) * (step / 8.0), RawInputModifiers.LeftMouseButton);
                Dispatcher.UIThread.RunJobs();
            }
            window.MouseUp(to, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }

        [TestMethod]
        public Task AUnitAndAGroupDraggedOntoAGroupGoIntoItAsOneStepEach() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            await model.Commands.RunAsync("units.add", new JsonObject { ["blueprint"] = "uec1101", ["x"] = 50, ["z"] = 50, ["army"] = "ARMY_1" }, interactive: false);
            await model.Commands.RunAsync("units.addGroup", new JsonObject { ["army"] = "ARMY_1", ["parent"] = "INITIAL", ["name"] = "Town" }, interactive: false);
            await model.Commands.RunAsync("units.addGroup", new JsonObject { ["army"] = "ARMY_2", ["parent"] = "INITIAL", ["name"] = "Camp" }, interactive: false);
            model.SelectMode("units");
            foreach (GroupNode node in model.Armies.Tree.Where(node => node.Army is "ARMY_1" or "ARMY_2").SelectMany(node => node.SelfAndBelow()))
            {
                node.IsExpanded = true;
            }
            Dispatcher.UIThread.RunJobs();

            TreeViewItem unit = Item(window, context => context is UnitNode { Name: "UNIT_1" });
            TreeViewItem town = Item(window, context => context is GroupNode { Army: "ARMY_1", PathText: "INITIAL/Town" });
            int steps = model.History.Count;
            Drag(window, Middle(unit, window), Middle(town, window));

            CollectionAssert.AreEqual(new[] { "INITIAL", "Town" }, model.ShownDocument!.FindUnit("UNIT_1")!.Group.ToArray(), "dropped into the group");
            Assert.AreEqual(steps + 1, model.History.Count, "one step");
            StringAssert.StartsWith(model.Map!.Session.UndoDescription, "Move ", "named after what moved");
            Assert.IsFalse(window.GetVisualDescendants().OfType<TreeViewItem>().Any(item => item.Classes.Contains("unitdroptarget")), "the light goes out");

            // a group dragged onto a group of another army
            foreach (GroupNode node in model.Armies.Tree.Where(node => node.Army is "ARMY_1" or "ARMY_2").SelectMany(node => node.SelfAndBelow()))
            {
                node.IsExpanded = true;
            }
            Dispatcher.UIThread.RunJobs();
            town = Item(window, context => context is GroupNode { Army: "ARMY_1", PathText: "INITIAL/Town" });
            TreeViewItem camp = Item(window, context => context is GroupNode { Army: "ARMY_2", PathText: "INITIAL/Camp" });
            Drag(window, Middle(town, window), Middle(camp, window));
            Assert.AreEqual(("ARMY_2", "INITIAL/Camp/Town"), (model.ShownDocument!.FindUnit("UNIT_1")!.Army, UnitGroupEdits.PathText(model.ShownDocument.FindUnit("UNIT_1")!.Group)));
            Assert.AreEqual(steps + 2, model.History.Count);

            // a short move is a click: it chooses, it moves nothing
            foreach (GroupNode node in model.Armies.Tree.Where(node => node.Army == "ARMY_2").SelectMany(node => node.SelfAndBelow()))
            {
                node.IsExpanded = true;
            }
            Dispatcher.UIThread.RunJobs();
            TreeViewItem moved = Item(window, context => context is UnitNode { Name: "UNIT_1" });
            Point at = Middle(moved, window);
            Drag(window, at, at + new Point(2, 1));
            Assert.AreEqual(steps + 2, model.History.Count);
            Assert.AreEqual("unit:UNIT_1", model.Selection.Items.Single().ToString(), "choosing a unit selects it");
            window.Close();
        });
    }
}
