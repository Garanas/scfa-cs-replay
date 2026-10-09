using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FAForever.MapEditor.App.Automation;
using FAForever.MapEditor.App.Controls;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The editor's real window without a screen (Avalonia.Headless): the mouse and keyboard as a
    /// person uses them, and what the window then shows. The 3D view needs a graphics card, which
    /// these tests do not have: they check how it handles input, not what it draws.
    /// </summary>
    [TestClass]
    public sealed class MainWindowTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private sealed class NoDialogs : IEditorDialogs
        {
            public Task<string?> PickFolderAsync(string title, string? startIn = null) => Task.FromResult<string?>(null);

            public Task<SaveChoice> AskToSaveChangesAsync(string mapName) => Task.FromResult(SaveChoice.Discard);
        }

        private static Task Run(Func<Task> test) => HeadlessApp.Run(test);

        private static async Task<(MainWindow Window, MainViewModel Model)> Open()
        {
            MainViewModel model = new MainViewModel(new NoDialogs());
            await model.OpenAsync(HardFfa);
            MainWindow window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            return (window, model);
        }

        private static void Show(MainWindow window, MapViewTab view)
        {
            ((MainViewModel)window.DataContext!).View = view;
            Dispatcher.UIThread.RunJobs();
        }

        private static TabControl Tabs(MainWindow window, string name) => window.GetVisualDescendants().OfType<TabControl>()
            .Single(tabs => Avalonia.Automation.AutomationProperties.GetName(tabs) == name);

        private static void Click(MainWindow window, Control control)
        {
            Point at = Middle(control, window);
            window.MouseMove(at);
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }

        private static Point Middle(Control control, MainWindow window) =>
            control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

        /// <summary>
        /// The 3D view moves its camera only while it draws, which needs a graphics card: let it arrive.
        /// </summary>
        private static void Settle(MainViewModel model)
        {
            for (int i = 0; i < 100 && model.Camera.Update(0.1f); i++)
            {
            }
        }

        [TestMethod]
        public Task TheViewsStayInPlaceWhateverTheTool() => Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            double Top()
            {
                Dispatcher.UIThread.RunJobs();
                return window.MapView.TranslatePoint(new Point(0, 0), window)!.Value.Y;
            }
            double sculpting = Top();

            model.Tool<SculptTool>().Mode = Project.Editing.HeightBrushMode.Flatten;
            double flattening = Top();
            model.SelectTool("paint.strata");
            double painting = Top();
            model.SelectTool("terrain.curve");
            double notBuilt = Top();

            Assert.AreEqual(sculpting, flattening, "the height to flatten to adds no row");
            Assert.AreEqual(sculpting, painting);
            Assert.AreEqual(sculpting, notBuilt, "what a tool not built yet will do adds no row");
        });

        /// <remarks>
        /// This and the other tests of the 3D view's input also check that the mouse reaches it at
        /// all: its picture comes from the graphics card, so it claims its rectangle itself
        /// (<c>ICustomHitTest</c>); without that, they fail.
        /// </remarks>
        [TestMethod]
        public Task TheWheelZoomsTheCameraTowardsTheMouse() => Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            Show(window, MapViewTab.ThreeD);
            Point middle = Middle(window.View3D, window);
            float before = model.Camera.Now.Distance;

            window.MouseMove(middle + new Vector(150, 0));
            window.MouseWheel(middle + new Vector(150, 0), new Vector(0, 1));
            Settle(model);

            Assert.IsTrue(model.Camera.Now.Distance < before * 0.85f, $"from {before} to {model.Camera.Now.Distance}");
            Assert.IsTrue(model.Camera.Now.X > 128, "towards the mouse, right of the middle");
            window.Close();
        });

        [TestMethod]
        public Task TheMiddleButtonDragsTheMap() => Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            Show(window, MapViewTab.ThreeD);
            Point middle = Middle(window.View3D, window);

            window.MouseMove(middle);
            window.MouseDown(middle, MouseButton.Middle);
            window.MouseMove(middle + new Vector(100, 0));
            window.MouseUp(middle + new Vector(100, 0), MouseButton.Middle);
            Settle(model);

            Assert.IsTrue(model.Camera.Now.X < 128, "the map follows the mouse to the right: the camera looks further west");
            window.Close();
        });

        [TestMethod]
        public Task SpaceAndTheMouseTurnTheCamera() => Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            Show(window, MapViewTab.ThreeD);
            Point middle = Middle(window.View3D, window);

            window.MouseMove(middle);
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            window.MouseMove(middle + new Vector(40, -60));
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            window.MouseMove(middle + new Vector(100, -60));
            Settle(model);

            Assert.AreEqual(-10, model.Camera.Now.Yaw, 0.01, "a quarter degree a pixel, clockwise to the right");
            Assert.AreEqual(75, model.Camera.Now.Pitch, 0.01, "up tilts towards the horizon");
            window.Close();
        });

        [TestMethod]
        public Task HomeShowsTheWholeMapAgain() => Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            Show(window, MapViewTab.ThreeD);
            Point middle = Middle(window.View3D, window);
            model.Camera.Look(x: 10, z: 10, distance: 20, pitch: 30);

            window.MouseMove(middle);
            window.KeyPress(Key.Home, RawInputModifiers.None, PhysicalKey.Home, null);

            Assert.AreEqual((128f, 128f, 90f), (model.Camera.Now.X, model.Camera.Now.Z, model.Camera.Now.Pitch));
            window.Close();
        });

        [TestMethod]
        public Task ControlAndAClickInTheTopViewSendTheCameraThere() => Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            Show(window, MapViewTab.Top);
            Point at = Middle(window.MapView, window) + new Vector(-120, 80);
            Point place = window.MapView.ControlToMap(window.TranslatePoint(at, window.MapView)!.Value);

            window.MouseMove(at, RawInputModifiers.Control);
            window.MouseDown(at, MouseButton.Left, RawInputModifiers.Control);
            window.MouseUp(at, MouseButton.Left, RawInputModifiers.Control);

            Assert.AreEqual(place.X, model.Camera.Now.X, 0.01);
            Assert.AreEqual(place.Y, model.Camera.Now.Z, 0.01);
            Assert.IsFalse(model.CanUndo, "it does not paint");
            window.Close();
        });

        [TestMethod]
        public Task PaintingInTheTopViewIsOneStepOfUndo() => Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            Show(window, MapViewTab.Top);
            Point middle = Middle(window.MapView, window);

            window.MouseMove(middle);
            window.MouseDown(middle, MouseButton.Left);
            for (int i = 1; i <= 10; i++)
            {
                window.MouseMove(middle + new Vector(i * 10, 0));
            }
            window.MouseUp(middle + new Vector(100, 0), MouseButton.Left);

            Assert.IsTrue(model.CanUndo);
            Assert.AreEqual(2, model.History.Count, "opened, and the stroke");
            window.Close();
        });

        [TestMethod]
        public Task ControlKOpensThePaletteAndEnterRunsTheCommand() => Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();

            window.KeyPress(Key.K, RawInputModifiers.Control, PhysicalKey.K, "k");
            Dispatcher.UIThread.RunJobs();
            Assert.IsTrue(model.IsPaletteOpen);

            window.KeyTextInput("3d view");
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual("view.3d", model.PaletteSelection?.Id, "the search box has the keyboard");
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Dispatcher.UIThread.RunJobs();

            Assert.IsFalse(model.IsPaletteOpen);
            Assert.AreEqual(MapViewTab.ThreeD, model.View);
            window.Close();
        });

        [TestMethod]
        public Task TheMenusAndShortcutsComeFromTheLayout() => Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            Menu menu = window.FindControl<Menu>("MainMenu")!;
            MenuItem[] bar = [.. menu.Items.OfType<MenuItem>()];
            MenuItem Item(MenuItem parent, string header) => parent.Items.OfType<MenuItem>().Single(item => (string?)item.Header == header);

            CollectionAssert.AreEqual(new[] { "_File", "_Edit", "_Select", "_View", "_Map", "_Modes", "_Generate", "_Plugins", "_Help" },
                bar.Select(item => (string?)item.Header).ToArray());
            MenuItem terrain = Item(bar[5], "Terrain");
            Assert.IsTrue(Item(terrain, "Add a height layer...").Classes.Contains("planned"), "a command not built yet shows as planned");
            Assert.IsNotNull(Item(terrain, "Curve"), "the tools of the mode follow");
            Assert.AreEqual(KeyGesture.Parse("F2"), Item(terrain, "Terrain mode").InputGesture);
            Assert.IsInstanceOfType<Separator>(bar[0].Items[3], "the separators of the layout");

            window.KeyPress(Key.F6, RawInputModifiers.None, PhysicalKey.F6, null);
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual("markers", model.SelectedMode!.Definition.Id, "a mode's shortcut runs its command");
            window.Close();
        });

        [TestMethod]
        public Task TheTabsAreInTheOrderOfTheirEnums() => Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            TabControl dock = Tabs(window, "Dock");
            foreach (DockTab tab in Enum.GetValues<DockTab>())
            {
                model.Dock = tab;
                Dispatcher.UIThread.RunJobs();
                StringAssert.StartsWith(((TabItem)dock.SelectedItem!).Header?.ToString(), tab.ToString());
            }

            Click(window, dock.GetVisualDescendants().OfType<TabItem>().First());
            Assert.AreEqual(DockTab.Problems, model.Dock, "a click on a tab chooses it in the view model");

            TabControl views = Tabs(window, "View");
            Click(window, views.GetVisualDescendants().OfType<TabItem>().Last());
            Assert.AreEqual(MapViewTab.ThreeD, model.View);
            Assert.IsTrue(window.View3D.IsVisible);
            Assert.IsFalse(window.MapView.IsVisible);
            window.Close();
        });

        [TestMethod]
        public Task TheMarkersPanelRunsTheOverlaysCommands() => Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            List<string> ran = [];
            model.Commands.Ran += command => ran.Add(command.Id);
            model.SelectMode("markers");
            Dispatcher.UIThread.RunJobs();
            CheckBox Box(string title) => window.GetVisualDescendants().OfType<CheckBox>().Single(box => box.Content as string == title);
            Assert.IsTrue(Box("Markers").IsChecked);
            Assert.IsFalse(Box("Path nodes").IsChecked);

            Click(window, Box("Path nodes"));
            Click(window, Box("Markers"));

            CollectionAssert.AreEqual(new[] { "view.overlay.pathNodes", "view.overlay.markers" }, ran);
            Assert.IsTrue(model.ShowPathNodes);
            Assert.IsFalse(model.ShowMarkers);
            Assert.IsFalse(Box("Markers").IsChecked, "the box shows the overlay");

            model.ShowMarkers = true;
            Assert.IsTrue(Box("Markers").IsChecked, "and follows the chip and the menu");
            window.Close();
        });

        [TestMethod]
        public Task TheRailChoosesTheMode() => Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            Button markers = window.GetVisualDescendants().OfType<Button>()
                .Single(button => Avalonia.Automation.AutomationProperties.GetName(button) == "Markers" && button.Classes.Contains("rail"));
            Point at = Middle(markers, window);

            window.MouseMove(at);
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);

            Assert.AreEqual("markers", model.SelectedMode!.Definition.Id);
            Assert.AreEqual("markers.select", model.ActiveTool!.Definition.Id);
            window.Close();
        });

        [TestMethod]
        public Task AToolNotBuiltYetDoesNotPaint() => Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            Show(window, MapViewTab.Top);
            model.SelectTool("terrain.curve");
            Point middle = Middle(window.MapView, window);

            window.MouseMove(middle);
            window.MouseDown(middle, MouseButton.Left);
            window.MouseMove(middle + new Vector(60, 0));
            window.MouseUp(middle + new Vector(60, 0), MouseButton.Left);

            Assert.IsFalse(model.CanUndo);
            window.Close();
        });

        [TestMethod]
        public Task SwitchingViewsKeepsBothInTheWindow() => Run(async () =>
        {
            (MainWindow window, _) = await Open();

            Show(window, MapViewTab.ThreeD);
            Show(window, MapViewTab.Top);

            // in the tree, so the 3D view keeps its context and textures on the graphics card
            Assert.AreSame(window, TopLevel.GetTopLevel(window.View3D));
            Assert.IsFalse(window.View3D.IsVisible);
            Assert.IsTrue(window.MapView.IsVisible);
            window.Close();
        });

        [TestMethod]
        public Task TheTopViewShowsWhatTheCameraSees() => Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            Show(window, MapViewTab.Top);
            model.ShowCamera = false;
            byte[] without = Png(window.CaptureRenderedFrame());
            model.ShowCamera = true;
            byte[] with = Png(window.CaptureRenderedFrame());

            CollectionAssert.AreNotEqual(without, with, "the camera's footprint is drawn over the map");
            window.Close();
        });

        [TestMethod]
        public Task EveryControlOneCanUseHasALabelOrText() => Run(async () =>
        {
            (MainWindow window, _) = await Open();
            List<string> unlabelled = [];
            void Check(JsonArray nodes)
            {
                foreach (JsonObject node in nodes.Cast<JsonObject>())
                {
                    string type = node["type"]!.GetValue<string>();
                    if (type is "Slider" or "ComboBox" or "TextBox" or "ListBox" or "ProgressBar" or "Button" or "CheckBox"
                        && node["label"] is null && node["text"] is null)
                    {
                        unlabelled.Add($"{type} at {node["bounds"]!.ToJsonString()}");
                    }
                    if (node["children"] is JsonArray children)
                    {
                        Check(children);
                    }
                }
            }

            JsonArray ui = UiTree.Describe(window, hidden: true);
            Check(ui);

            CollectionAssert.AreEqual(Array.Empty<string>(), unlabelled, "a screen reader and an agent need a name for each");
            string json = ui.ToJsonString();
            StringAssert.Contains(json, "\"label\":\"3D view\"");
            StringAssert.Contains(json, "\"label\":\"Brush radius in ogrids\"");
            window.Close();
        });

        /// <summary>
        /// The frame as PNG bytes, to compare two.
        /// </summary>
        private static byte[] Png(Bitmap? frame)
        {
            Assert.IsNotNull(frame, "the window drew a frame");
            using MemoryStream stream = new MemoryStream();
            frame.Save(stream, new PngBitmapEncoderOptions());
            return stream.ToArray();
        }
    }
}
