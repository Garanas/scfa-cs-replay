using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FAForever.MapEditor.App.Automation;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// Moving the panels (user story V-4): the mode's panel, the inspector, the dock and the dock's
    /// tabs to another side or into a window of their own, remembered in the user's settings, and
    /// automation reaching a floating panel; and automation's <c>expand</c>.
    /// </summary>
    [TestClass]
    public sealed class LayoutPlacesTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private static string SettingsFile() => Path.Combine(AppContext.BaseDirectory, "layout-places", Guid.NewGuid().ToString("N"), "settings.json");

        private static async Task<(MainWindow Window, MainViewModel Model, AutomationServer Automation)> Open()
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            await model.OpenAsync(HardFfa);
            MainWindow window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            return (window, model, new AutomationServer(window, model, port: 0));
        }

        private static async Task<JsonObject> Send(AutomationServer automation, string command, JsonObject? arguments = null)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            JsonObject answer = (JsonObject)await automation.HandleAsync(command, arguments ?? []);
            Dispatcher.UIThread.RunJobs();
            return answer;
        }

        [TestMethod]
        public async Task PanelsMoveAndTheLayoutRemembersWhere()
        {
            string file = SettingsFile();
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            model.Layout.Load(new UserSettings(file));

            Assert.AreEqual("left", (await model.Commands.RunAsync("view.movePanel", new JsonObject { ["panel"] = "inspector", ["place"] = "left" }, interactive: false)).Result as string);
            Assert.AreEqual(PanelPlace.Left, model.Layout.PlaceOf(MovablePanel.Inspector));
            CollectionAssert.AreEqual(new[] { MovablePanel.ModePanel, MovablePanel.Inspector }, model.Layout.PanelsAt(PanelPlace.Left).ToArray(), "one above the other, in their order");
            await model.Commands.RunAsync("view.movePanel", new JsonObject { ["panel"] = "assets", ["place"] = "right" }, interactive: false);
            CollectionAssert.DoesNotContain(model.Layout.TabsInDock.ToArray(), DockTab.Assets, "a tab moved out leaves the dock");
            await model.Commands.RunAsync("view.movePanel", new JsonObject { ["panel"] = "modePanel", ["place"] = "float" }, interactive: false);
            Assert.AreEqual(PanelPlace.Left, model.Layout.ReturnOf(MovablePanel.ModePanel), "a floating panel remembers where it lay");
            model.Layout.SetFloatingBounds(MovablePanel.ModePanel, new FloatingBounds(100, 120, 400, 600));
            CommandRun refused = await model.Commands.TryRunAsync("view.movePanel", new JsonObject { ["panel"] = "dock", ["place"] = "dock" }, interactive: false);
            Assert.IsFalse(refused.Ran, "only tabs go into the dock");
            model.Layout.Resize(LayoutPanel.Inspector, 350);
            Assert.AreEqual(350, model.Layout.ModePanelWidth, "a panel's size is its side's: the inspector lies on the left");

            MainViewModel again = new MainViewModel(new MainViewModelTest.Answers());
            again.Layout.Load(new UserSettings(file));
            Assert.AreEqual(PanelPlace.Left, again.Layout.PlaceOf(MovablePanel.Inspector));
            Assert.AreEqual(PanelPlace.Right, again.Layout.PlaceOf(MovablePanel.Assets));
            Assert.AreEqual(PanelPlace.Float, again.Layout.PlaceOf(MovablePanel.ModePanel));
            Assert.AreEqual(new FloatingBounds(100, 120, 400, 600), again.Layout.FloatingBoundsOf(MovablePanel.ModePanel));
            Assert.AreEqual(PanelPlace.Left, again.Layout.Return(MovablePanel.ModePanel), "closing its window puts it back");

            await again.Commands.RunAsync("view.resetLayout", null, interactive: false);
            foreach (MovablePanel panel in Enum.GetValues<MovablePanel>())
            {
                Assert.AreEqual(LayoutViewModel.OwnPlace(panel), again.Layout.PlaceOf(panel), $"{panel} back in its own place");
            }
            Assert.IsNull(new UserSettings(file).Layout.Places, "nothing to remember once every panel lies in its own place");
        }

        [TestMethod]
        public Task APanelMovedToAnotherSideLiesThereAndATabLeavesTheDock() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            Control inspector = window.FindControl<Control>("InspectorView")!;
            Control modePanel = window.FindControl<Control>("ModePanelView")!;
            double width = window.MapView.Bounds.Width;

            await Send(automation, "run", new JsonObject { ["id"] = "view.movePanel", ["panel"] = "inspector", ["place"] = "left" });

            Assert.AreEqual("LeftSide", ((Control)inspector.Parent!).Name);
            Assert.IsTrue(inspector.IsEffectivelyVisible);
            Point inspectorAt = inspector.TranslatePoint(default, window)!.Value;
            Point modePanelAt = modePanel.TranslatePoint(default, window)!.Value;
            Assert.AreEqual(modePanelAt.X, inspectorAt.X, 1, "beside the rail, as the mode panel");
            Assert.IsTrue(inspectorAt.Y > modePanelAt.Y + 100, "under the mode panel");
            Assert.IsTrue(window.MapView.Bounds.Width > width + 250, "the right side is empty: its width goes to the views");
            Assert.IsNotNull(UiTree.Find(window, "Resize the mode panel and the inspector"), "the left splitter says what it resizes");

            // the Assets tab to the right: its shelf there under its name, its header gone from the dock
            await Send(automation, "run", new JsonObject { ["id"] = "view.movePanel", ["panel"] = "assets", ["place"] = "right" });
            Views.Dock dock = window.FindControl<Views.Dock>("DockView")!;
            Assert.IsFalse(dock.TabItemOf(DockTab.Assets).IsVisible);
            Control shelf = window.GetVisualDescendants().OfType<AssetShelf>().Single();
            Assert.IsTrue(shelf.IsEffectivelyVisible);
            Assert.IsTrue(shelf.TranslatePoint(default, window)!.Value.X > window.MapView.TranslatePoint(default, window)!.Value.X + window.MapView.Bounds.Width - 1, "right of the views");
            await Send(automation, "run", new JsonObject { ["id"] = "view.assets" });
            Assert.AreNotEqual(DockTab.Assets, model.Dock, "the dock shows a tab it holds");

            await Send(automation, "run", new JsonObject { ["id"] = "view.movePanel", ["panel"] = "assets", ["place"] = "dock" });
            Assert.IsTrue(dock.TabItemOf(DockTab.Assets).IsVisible, "back into the dock");
            model.Dock = DockTab.Assets;
            Dispatcher.UIThread.RunJobs();
            Assert.IsTrue(shelf.IsEffectivelyVisible, "its tab shows its shelf again");

            // every panel has a context menu that moves it; a tab's header has its own
            ContextMenu menu = inspector.ContextMenu!;
            string[] headers = [.. menu.Items.OfType<MenuItem>().Select(item => (string)item.Header!)];
            CollectionAssert.IsSubsetOf(new[] { "Move to the left", "Move to the right", "Move to the bottom", "Float in a window", "Hide the inspector", "Reset layout" }, headers);
            Assert.IsFalse(menu.Items.OfType<MenuItem>().Single(item => (string)item.Header! == "Move to the left").IsEnabled, "where it lies already");
            Assert.IsTrue(dock.TabItemOf(DockTab.Console).ContextMenu!.Items.OfType<MenuItem>().Any(item => (string)item.Header! == "Float in a window"));
            menu.Items.OfType<MenuItem>().Single(item => (string)item.Header! == "Move to the right").Command!.Execute("move:inspector:right");
            await Task.Delay(20);
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(PanelPlace.Right, model.Layout.PlaceOf(MovablePanel.Inspector), "the menu runs the move");
            Assert.AreEqual(width, window.MapView.Bounds.Width, 2, "back as it was");
            window.Close();
        });

        [TestMethod]
        public Task AFloatingPanelLiesInItsOwnWindowAndAutomationReachesIt() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            Control modePanel = window.FindControl<Control>("ModePanelView")!;

            await Send(automation, "run", new JsonObject { ["id"] = "view.movePanel", ["panel"] = "modePanel", ["place"] = "float" });

            Window floating = window.FloatingWindows.Single();
            Assert.AreSame(floating, TopLevel.GetTopLevel(modePanel), "the panel lies in its own window");
            Assert.IsTrue(floating.IsVisible);
            Assert.IsFalse(window.FindControl<Control>("LeftSide")!.IsVisible, "the left side is empty");

            JsonArray ui = (JsonArray)(await Send(automation, "ui"))["ui"]!;
            JsonObject node = ui.OfType<JsonObject>().Single(each => (string?)each["type"] == "FloatingWindow");
            Assert.AreEqual("modePanel", (string?)node["name"]);
            StringAssert.Contains(node.ToJsonString(), "\"label\":\"Curve\"", "ui lists the controls of the floating window");

            // a click by label goes to the floating window
            JsonObject clicked = (JsonObject)(await Send(automation, "click", new JsonObject { ["label"] = "Curve" }))["clicked"]!;
            Assert.AreEqual("modePanel", (string?)clicked["window"]);
            Assert.AreEqual("terrain.curve", model.ActiveTool!.Definition.Id, "the click in the floating window chose the tool");
            JsonObject state = await Send(automation, "state");
            Assert.AreEqual("modePanel", (string?)((JsonArray)state["floatingWindows"]!)[0]!["panel"]);
            Assert.AreEqual("float", (string?)state["layout"]!["places"]!["modePanel"]!["place"]);
            string shot = Path.Combine(AppContext.BaseDirectory, "captures", "floating-panel.png");
            await Send(automation, "screenshot", new JsonObject { ["part"] = "panel", ["path"] = shot });
            Assert.IsTrue(File.Exists(shot), "screenshot part=panel takes it from its window");

            // the person closes the window: the panel goes back where it lay
            floating.Close();
            await Task.Delay(20);
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(PanelPlace.Left, model.Layout.PlaceOf(MovablePanel.ModePanel));
            Assert.AreSame(window, TopLevel.GetTopLevel(modePanel));
            Assert.IsTrue(modePanel.IsEffectivelyVisible);
            Assert.AreEqual(0, window.FloatingWindows.Count);

            // hidden while it floats, its window closes; shown, it floats again
            model.Layout.Move(MovablePanel.Inspector, PanelPlace.Float);
            Dispatcher.UIThread.RunJobs();
            model.Layout.Toggle(LayoutPanel.Inspector, false);
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(0, window.FloatingWindows.Count);
            model.Layout.Toggle(LayoutPanel.Inspector, true);
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(1, window.FloatingWindows.Count);
            model.Layout.Reset();
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(0, window.FloatingWindows.Count, "reset puts every panel back");
            window.Close();
        });

        [TestMethod]
        public Task ExpandOpensTreeItemsSoUiListsTheirChildren() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            model.SelectMode("units");
            Dispatcher.UIThread.RunJobs();
            int Items() => window.GetVisualDescendants().OfType<TreeViewItem>().Count(item => item.IsEffectivelyVisible);
            int before = Items();

            JsonObject expanded = (JsonObject)(await Send(automation, "expand", new JsonObject { ["label"] = "Armies and their unit groups", ["all"] = true }))["expanded"]!;

            Assert.IsTrue(expanded["changed"]!.GetValue<int>() > 0, expanded.ToJsonString());
            Assert.IsTrue(Items() > before, "the items in them show");
            await Send(automation, "expand", new JsonObject { ["label"] = "Armies and their unit groups", ["collapse"] = true });
            Assert.AreEqual(before, Items());
            window.Close();
        });
    }
}
