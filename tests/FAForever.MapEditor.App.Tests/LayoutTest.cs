using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The window's layout (user story V-4): the views side by side, panels hidden, shown and
    /// resized, remembered in the user's settings; and the toolbar of commands (user story F-1).
    /// </summary>
    [TestClass]
    public sealed class LayoutTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private static string SettingsFile() => Path.Combine(AppContext.BaseDirectory, "layout-settings", Guid.NewGuid().ToString("N"), "settings.json");

        private static async Task<(MainWindow Window, MainViewModel Model)> Open()
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            await model.OpenAsync(HardFfa);
            MainWindow window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            return (window, model);
        }

        private static Control Named(MainWindow window, string label) => window.GetVisualDescendants().OfType<Control>()
            .First(control => Avalonia.Automation.AutomationProperties.GetName(control) == label);

        [TestMethod]
        public async Task PanelsAreShownHiddenAndResizedByTheirCommands()
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());

            Assert.IsTrue((await model.Commands.RunAsync("view.inspector", null, interactive: false)).Ran);
            Assert.IsFalse(model.Layout.ShowInspector, "without on it turns the panel the other way");
            await model.Commands.RunAsync("view.togglePanel", new JsonObject { ["panel"] = "dock", ["on"] = false }, interactive: false);
            Assert.IsFalse(model.Layout.ShowDock);
            await model.Commands.RunAsync("view.togglePanel", new JsonObject { ["panel"] = "Dock", ["on"] = true }, interactive: false);
            Assert.IsTrue(model.Layout.ShowDock);
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => model.Commands.RunAsync("view.togglePanel", new JsonObject { ["panel"] = "sidebar" }, interactive: false));

            await model.Commands.RunAsync("view.resizePanel", new JsonObject { ["panel"] = "modePanel", ["size"] = 400 }, interactive: false);
            Assert.AreEqual(400, model.Layout.ModePanelWidth);
            model.Layout.Resize(LayoutPanel.Dock, 5);
            Assert.AreEqual(LayoutViewModel.MinimumPanelSize, model.Layout.DockHeight, "a panel keeps a size one can grab");

            await model.Commands.RunAsync("view.resetLayout", null, interactive: false);
            Assert.IsTrue(model.Layout.ShowInspector);
            Assert.IsNull(model.Layout.ModePanelWidth);
            Assert.IsNull(model.Layout.DockHeight);
        }

        [TestMethod]
        public async Task TheLayoutIsRememberedInTheUsersSettings()
        {
            string file = SettingsFile();
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            model.Layout.Load(new UserSettings(file));
            model.Layout.Toggle(LayoutPanel.ModePanel, false);
            model.Layout.Resize(LayoutPanel.Inspector, 360);
            model.Layout.Split(0.3);
            await model.Commands.RunAsync("view.sideBySide", null, interactive: false);
            model.Layout.SetToolbar(["file.save", "file.commit"]);

            MainViewModel again = new MainViewModel(new MainViewModelTest.Answers());
            again.Layout.Load(new UserSettings(file));

            Assert.IsFalse(again.Layout.ShowModePanel);
            Assert.AreEqual(360, again.Layout.InspectorWidth);
            Assert.AreEqual(0.3, again.Layout.SideBySideSplit!.Value, 1e-9);
            Assert.AreEqual(MapViewTab.SideBySide, again.Layout.RememberedView);
            CollectionAssert.AreEqual(new[] { "file.save", "file.commit" }, again.Layout.Toolbar.Select(item => item.Id).ToArray());
        }

        [TestMethod]
        public async Task TheToolbarIsMadeFromTheRegistry()
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            CollectionAssert.AreEqual(LayoutViewModel.DefaultToolbar.ToArray(), model.Layout.Toolbar.Select(item => item.Id).ToArray());
            ToolbarItem undo = model.Layout.Toolbar.Single(item => item.Id == "edit.undo");
            Assert.IsTrue(undo.HasIcon);
            Assert.IsFalse(undo.CanRun, "nothing to undo without a map");

            await model.OpenAsync(HardFfa);
            model.EditMap("Rename", document => document with { Scenario = document.Scenario with { Name = "Other" } });
            Assert.IsTrue(undo.CanRun, "the button follows whether its command can run");

            _ = (await model.Commands.RunAsync("view.setToolbar", new JsonObject { ["commands"] = new JsonArray("file.save", "no.such", "generate.stale") }, interactive: false)).Result as JsonNode;
            CollectionAssert.AreEqual(new[] { "file.save", "generate.stale" }, model.Layout.Toolbar.Select(item => item.Id).ToArray(), "an unknown id is left out");
            Assert.IsFalse(model.Layout.Toolbar.Single(item => item.Id == "generate.stale").HasIcon, "a command without an icon shows its title");

            await model.Commands.RunAsync("view.addToToolbar", new JsonObject { ["id"] = "edit.undo" }, interactive: false);
            await model.Commands.RunAsync("view.removeFromToolbar", new JsonObject { ["id"] = "file.save" }, interactive: false);
            CollectionAssert.AreEqual(new[] { "generate.stale", "edit.undo" }, model.Layout.Toolbar.Select(item => item.Id).ToArray());
            await model.Commands.RunAsync("view.setToolbar", null, interactive: false);
            CollectionAssert.AreEqual(LayoutViewModel.DefaultToolbar.ToArray(), model.Layout.Toolbar.Select(item => item.Id).ToArray());
        }

        [TestMethod]
        public Task SideBySideShowsBothViewsInTheirOwnHalf() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            Assert.IsFalse(window.View3D.IsVisible);

            TabControl views = window.GetVisualDescendants().OfType<TabControl>().Single(tabs => Avalonia.Automation.AutomationProperties.GetName(tabs) == "View");
            Control tab = views.GetVisualDescendants().OfType<TabItem>().Single(item => (string?)item.Header == "Side by side");
            Point at = tab.TranslatePoint(new Point(tab.Bounds.Width / 2, tab.Bounds.Height / 2), window)!.Value;
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.AreEqual(MapViewTab.SideBySide, model.View);
            Assert.IsTrue(window.MapView.IsEffectivelyVisible);
            Assert.IsTrue(window.View3D.IsEffectivelyVisible);
            double topLeft = window.MapView.TranslatePoint(default, window)!.Value.X;
            double threeDLeft = window.View3D.TranslatePoint(default, window)!.Value.X;
            Assert.IsTrue(threeDLeft >= topLeft + window.MapView.Bounds.Width, "the 3D view lies right of the top view");
            Assert.AreEqual(window.MapView.Bounds.Width, window.View3D.Bounds.Width, 8, "each takes half");

            // each view takes its own mouse: the wheel over the top view zooms it, not the 3D camera
            float distance = model.Camera.Now.Distance;
            double zoom = window.MapView.Zoom;
            Point overTop = window.MapView.TranslatePoint(new Point(window.MapView.Bounds.Width / 2, window.MapView.Bounds.Height / 2), window)!.Value;
            window.MouseMove(overTop);
            window.MouseWheel(overTop, new Vector(0, 1));
            Dispatcher.UIThread.RunJobs();
            Assert.AreNotEqual(zoom, window.MapView.Zoom);
            Assert.AreEqual(distance, model.Camera.Now.Distance);

            model.View = MapViewTab.ThreeD;
            Dispatcher.UIThread.RunJobs();
            Assert.IsFalse(window.MapView.IsVisible);
            Assert.IsTrue(window.View3D.Bounds.Width > 2 * window.MapView.Bounds.Width - 8 || window.View3D.Bounds.Width > 600, "alone, the 3D view takes the whole width");
        });

        [TestMethod]
        public Task BackFromSideBySideTheTopViewShowsTheWholeMapAgain() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            double whole = window.MapView.Zoom;

            model.View = MapViewTab.SideBySide;
            Dispatcher.UIThread.RunJobs();
            Assert.IsTrue(window.MapView.Zoom < whole, "half as wide, the whole map smaller");
            model.View = MapViewTab.Top;
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(whole, window.MapView.Zoom, 1e-6, "the whole map as large as before");

            // zoomed in by the person, a panel hidden keeps the zoom and the place in the middle
            window.MapView.Show(4, 100, 80);
            model.Layout.Toggle(LayoutPanel.Inspector, false);
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(4, window.MapView.Zoom, 1e-6);
            Assert.AreEqual(100, window.MapView.Centre.X, 0.5);
            Assert.AreEqual(80, window.MapView.Centre.Y, 0.5);
        });

        [TestMethod]
        public Task HidingAPanelGivesTheViewsItsRoom() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            double width = window.MapView.Bounds.Width;
            double height = window.MapView.Bounds.Height;

            model.Layout.Toggle(LayoutPanel.Inspector, false);
            model.Layout.Toggle(LayoutPanel.Dock, false);
            Dispatcher.UIThread.RunJobs();
            Assert.IsTrue(window.MapView.Bounds.Width > width + 250, "the inspector's width goes to the views");
            Assert.IsTrue(window.MapView.Bounds.Height > height + 150, "and the dock's height");

            model.Layout.Reset();
            model.Layout.Resize(LayoutPanel.ModePanel, 400);
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(width - 120, window.MapView.Bounds.Width, 2, "the mode panel is 120 wider than its own 280");
        });

        [TestMethod]
        public Task DraggingASplitterResizesThePanelAndRemembersIt() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            Control splitter = Named(window, "Resize the inspector");
            double width = window.MapView.Bounds.Width;
            Point from = splitter.TranslatePoint(new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2), window)!.Value;
            window.MouseMove(from);
            window.MouseDown(from, MouseButton.Left);
            for (int step = 1; step <= 5; step++)
            {
                window.MouseMove(from - new Point(20 * step, 0));
                Dispatcher.UIThread.RunJobs();
            }
            window.MouseUp(from - new Point(100, 0), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.AreEqual(400, model.Layout.InspectorWidth!.Value, 2, "dragged 100 to the left from its 300");
            Assert.AreEqual(width - 100, window.MapView.Bounds.Width, 2);
        });

        [TestMethod]
        public Task AToolbarButtonRunsItsCommand() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            model.EditMap("Rename", document => document with { Scenario = document.Scenario with { Name = "Other" } });
            Dispatcher.UIThread.RunJobs();
            Button undo = (Button)Named(window, "Undo");
            Assert.IsTrue(undo.IsEffectivelyEnabled);
            Point at = undo.TranslatePoint(new Point(undo.Bounds.Width / 2, undo.Bounds.Height / 2), window)!.Value;
            window.MouseMove(at);
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();

            Assert.AreNotEqual("Other", model.Map!.Document.Scenario.Name, "the button undid the edit");
            Dispatcher.UIThread.RunJobs();
            Assert.IsFalse(undo.IsEffectivelyEnabled, "nothing left to undo");

            model.Layout.Toggle(LayoutPanel.Toolbar, false);
            Dispatcher.UIThread.RunJobs();
            Assert.IsFalse(undo.IsEffectivelyVisible);
        });
    }
}
