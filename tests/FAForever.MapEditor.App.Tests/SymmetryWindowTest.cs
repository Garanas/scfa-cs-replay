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
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// Symmetry applied to edits in the real window (user stories F-12, S-1): a stroke and a click
    /// with the mouse in the top view, and the "Mirror edits" check box beside the choice.
    /// </summary>
    [TestClass]
    public sealed class SymmetryWindowTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private static async Task<(MainWindow Window, MainViewModel Model)> Open()
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            model.Assets.Use(TestAssets.Catalog());
            await model.OpenAsync(HardFfa);
            MainWindow window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
            window.Show();
            model.View = MapViewTab.Top;
            Dispatcher.UIThread.RunJobs();
            await model.Commands.RunAsync("select.symmetry", new JsonObject { ["kind"] = "rotational", ["count"] = 4 }, interactive: false);
            Dispatcher.UIThread.RunJobs();
            return (window, model);
        }

        private static Point At(MainWindow window, double x, double z) =>
            window.MapView.TranslatePoint(window.MapView.MapToControl(x, z), window)!.Value;

        [TestMethod]
        public Task AStrokeWithTheMouseRaisesEveryPart() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            MapDocument opened = model.ShownDocument!;
            model.SelectTool("terrain.sculpt");
            await model.Commands.RunAsync("brush.set", new JsonObject { ["mode"] = "raise", ["radius"] = 8, ["strength"] = 4 }, interactive: false);

            Point from = At(window, 60, 40);
            window.MouseMove(from);
            window.MouseDown(from, MouseButton.Left);
            window.MouseMove(At(window, 64, 40));
            window.MouseUp(At(window, 64, 40), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.AreEqual(2, model.History.Count, "opened, and one stroke");
            // a quarter turn takes (62, 40) to (216, 62)
            float before = opened.Terrain.Heightmap.HeightAt(216, 62);
            Assert.IsTrue(model.ShownDocument!.Terrain.Heightmap.HeightAt(216, 62) > before + 2, "raised in the next part");
            window.Close();
        });

        [TestMethod]
        public Task AClickPlacesADepositInEveryPartAndTheCheckBoxTurnsMirroringOff() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            model.SelectTool("markers.mass");
            int masses = model.ShownDocument!.Markers.Resources.Length;

            Point at = At(window, 100.5, 40.5);
            window.MouseMove(at);
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            await Task.Yield();
            Dispatcher.UIThread.RunJobs();

            Assert.AreEqual(masses + 4, model.ShownDocument!.Markers.Resources.Length);
            Assert.AreEqual(4, model.Selection.Count, "selected together");

            CheckBox mirror = window.GetVisualDescendants().OfType<CheckBox>().Single(box => box.Content as string == "Mirror edits");
            Assert.IsTrue(mirror.IsVisible && mirror.IsChecked == true);
            mirror.IsChecked = false;
            Assert.IsFalse(model.Symmetry.IsMirroring);

            at = At(window, 60.5, 120.5);
            window.MouseMove(at);
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            await Task.Yield();
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(masses + 5, model.ShownDocument!.Markers.Resources.Length, "one, the guides only");
            window.Close();
        });
    }
}
