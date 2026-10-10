using System.Text.Json.Nodes;
using System.Windows.Input;
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
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;
using Vector3 = System.Numerics.Vector3;

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
        public Task TheLeftMouseSculptsInThe3DView() => Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            Show(window, MapViewTab.ThreeD);
            Settle(model);
            Point middle = Middle(window.View3D, window);
            SculptTool sculpt = model.Tool<SculptTool>();
            sculpt.Radius = 10;
            sculpt.Strength = 2;
            float before = model.ShownDocument!.Terrain.Heightmap.GetHeight(128, 128);

            window.MouseMove(middle);
            window.MouseDown(middle, MouseButton.Left);
            window.MouseMove(middle + new Vector(20, 0));
            window.MouseUp(middle + new Vector(20, 0), MouseButton.Left);

            Assert.AreEqual("Raise terrain", model.Map!.Session.UndoDescription, "one step, as in the top view");
            Assert.IsTrue(model.ShownDocument!.Terrain.Heightmap.GetHeight(128, 128) > before, "the camera looks straight down at the middle of the map");
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
        public Task AltAndAClickInTheTopViewSendTheCameraThere() => Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            Show(window, MapViewTab.Top);
            Point at = Middle(window.MapView, window) + new Vector(-120, 80);
            Point place = window.MapView.ControlToMap(window.TranslatePoint(at, window.MapView)!.Value);

            window.MouseMove(at, RawInputModifiers.Alt);
            window.MouseDown(at, MouseButton.Left, RawInputModifiers.Alt);
            window.MouseUp(at, MouseButton.Left, RawInputModifiers.Alt);

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
        public Task TheMenusAskAgainWhenAStrokeStartsAndNotWhileASliderMoves() => Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            Show(window, MapViewTab.Top);
            MenuItem edit = window.FindControl<Menu>("MainMenu")!.Items.OfType<MenuItem>().Single(item => (string?)item.Header == "_Edit");
            ICommand undo = edit.Items.OfType<MenuItem>().Single(item => (string?)item.Header == "Undo").Command!;
            int asked = 0;
            undo.CanExecuteChanged += (_, _) => asked++;
            model.Stroke([(50, 50)]);
            asked = 0;

            for (int radius = 10; radius <= 40; radius++)
            {
                model.Tool<SculptTool>().Radius = radius;
            }
            model.Status = "Something happened";
            Assert.AreEqual(0, asked, "the radius and the status line change nothing a command needs");

            Point middle = Middle(window.MapView, window);
            window.MouseMove(middle);
            window.MouseDown(middle, MouseButton.Left);
            Assert.AreEqual(1, asked, "a stroke started");
            Assert.IsFalse(undo.CanExecute(null), "the stroke must end first");
            window.MouseUp(middle, MouseButton.Left);
            Assert.IsTrue(undo.CanExecute(null));
            window.Close();
        });

        [TestMethod]
        public Task ControlKOpensThePaletteAndEnterRunsTheCommand() => Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();

            window.KeyPress(Key.K, RawInputModifiers.Control, PhysicalKey.K, "k");
            Dispatcher.UIThread.RunJobs();
            Assert.IsTrue(model.Palette.IsOpen);

            window.KeyTextInput("3d view");
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual("view.3d", model.Palette.Selection?.Id, "the search box has the keyboard");
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Dispatcher.UIThread.RunJobs();

            Assert.IsFalse(model.Palette.IsOpen);
            Assert.AreEqual(MapViewTab.ThreeD, model.View);
            window.Close();
        });

        /// <summary>
        /// The window with the test assets, answering its questions with its own dialogs: the
        /// picker shows over it.
        /// </summary>
        private static async Task<(MainWindow Window, MainViewModel Model)> OpenWithItsDialogs()
        {
            MainWindow window = new MainWindow { Width = 1280, Height = 800 };
            MainViewModel model = new MainViewModel(new WindowDialogs(window));
            await model.OpenAsync(HardFfa);
            model.Assets.Use(TestAssets.Catalog());
            window.DataContext = model;
            window.Show();
            Dispatcher.UIThread.RunJobs();
            return (window, model);
        }

        private static T Labelled<T>(MainWindow window, string label) where T : Control => window.GetVisualDescendants().OfType<T>()
            .Single(control => Avalonia.Automation.AutomationProperties.GetName(control) == label);

        [TestMethod]
        public Task ControlPOpensThePickerTypingFiltersAndEnterChooses() => Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await OpenWithItsDialogs();
            model.SelectMode("units");

            window.KeyPress(Key.P, RawInputModifiers.Control, PhysicalKey.P, "p");
            Dispatcher.UIThread.RunJobs();
            Assert.IsTrue(model.Picker.IsOpen);
            Assert.AreEqual("Choose a unit", model.Picker.Title, "the Units mode asks for a unit");
            TextBox search = Labelled<TextBox>(window, "Search the picker");
            Assert.IsTrue(search.IsEffectivelyVisible);
            Assert.IsTrue(search.IsFocused, "the search has the keyboard");

            window.KeyTextInput("snoop");
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual("Snoop", model.Picker.Tiles.Single().Title);
            Assert.IsTrue(window.GetVisualDescendants().OfType<Button>()
                .Any(button => Avalonia.Automation.AutomationProperties.GetName(button)?.StartsWith("Snoop,", StringComparison.Ordinal) == true && button.IsEffectivelyVisible),
                "its tile shows");
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Dispatcher.UIThread.RunJobs();

            Assert.IsFalse(model.Picker.IsOpen);
            Assert.AreEqual("Snoop", model.Assets.Shown?.Name);
            TextBlock name = Labelled<TextBlock>(window, "Asset name");
            Assert.IsTrue(name.IsEffectivelyVisible, "the inspector shows the chosen unit");
            Assert.AreEqual("Snoop", name.Text);
            window.Close();
        });

        [TestMethod]
        public Task ThePickersArrowsMoveAndEscapeCancels() => Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await OpenWithItsDialogs();
            model.SelectMode("props");
            window.KeyPress(Key.P, RawInputModifiers.Control, PhysicalKey.P, "p");
            Dispatcher.UIThread.RunJobs();
            AssetTile first = model.Picker.Selected!;

            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Dispatcher.UIThread.RunJobs();
            Assert.AreNotEqual(first, model.Picker.Selected, "down from the search moves through the tiles");
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Dispatcher.UIThread.RunJobs();

            Assert.IsFalse(model.Picker.IsOpen);
            Assert.IsFalse(model.Assets.HasSelection, "nothing chosen");
            StringAssert.Contains(model.Status, "Chose nothing");
            window.Close();
        });

        [TestMethod]
        public Task AnAssetDraggedOntoTheTopViewDropsWhereTheMouseIs() => Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            model.Assets.Use(TestAssets.Catalog());
            Show(window, MapViewTab.Top);
            List<AssetDropEventArgs> drops = [];
            model.AssetDropped += (_, drop) =>
            {
                drops.Add(drop);
                drop.Handled = true;
            };
            Project.Workspaces.AssetEntry rock = model.Assets.Catalog.Find("env/evergreen/props/rocks/rock01_prop.bp")!;
            Point at = Middle(window.MapView, window);
            Point place = window.MapView.ControlToMap(window.TranslatePoint(at, window.MapView)!.Value);

            window.DragDrop(at, Avalonia.Input.Raw.RawDragEventType.DragEnter, AssetDrag.Of(rock), DragDropEffects.Copy, RawInputModifiers.None);
            window.DragDrop(at, Avalonia.Input.Raw.RawDragEventType.DragOver, AssetDrag.Of(rock), DragDropEffects.Copy, RawInputModifiers.None);
            StringAssert.StartsWith(window.FindControl<TextBlock>("CursorText")!.Text, "Drop rock01 at", "the status bar shows where it lands");
            window.DragDrop(at, Avalonia.Input.Raw.RawDragEventType.Drop, AssetDrag.Of(rock), DragDropEffects.Copy, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            AssetDropEventArgs drop = drops.Single();
            Assert.AreEqual(rock, drop.Entry);
            Assert.AreEqual(place.X, drop.X, 0.01);
            Assert.AreEqual(place.Y, drop.Z, 0.01);
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
            Assert.IsTrue(Item(terrain, "Save the brush as a preset...").Classes.Contains("planned"), "a command not built yet shows as planned");
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
            Click(window, views.GetVisualDescendants().OfType<TabItem>().ElementAt((int)MapViewTab.ThreeD));
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
            model.SelectTool("terrain.walkability");
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
        public Task ABrushOfAPictureShowsItsTurnedSquare() => Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            model.Assets.Use(TestAssets.Catalog());
            Show(window, MapViewTab.Top);
            model.ShowCamera = false;
            model.Tool<SculptTool>().Radius = 40;
            window.MouseMove(Middle(window.MapView, window));
            byte[] round = Png(window.CaptureRenderedFrame());

            model.Tool<SculptTool>().Shape.Choose("ridge.png");
            model.Tool<SculptTool>().Shape.Rotation = 30;

            CollectionAssert.AreNotEqual(round, Png(window.CaptureRenderedFrame()), "the square of the picture in place of the circle");
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
        [TestMethod]
        public Task ATileOfAPropDrawsItsMesh() => Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            model.Assets.Use(TestAssets.Catalog());
            model.Assets.Kind = Project.Workspaces.AssetKind.Prop;
            model.Assets.Scope = AssetScope.Everything;
            model.Dock = DockTab.Assets;
            Dispatcher.UIThread.RunJobs();

            AssetThumbnail rock = window.GetVisualDescendants().OfType<AssetThumbnail>()
                .Single(thumbnail => thumbnail.Model?.Mesh.EndsWith("rock01_lod0.glb", StringComparison.Ordinal) == true);
            Assert.IsTrue(rock.IsEffectivelyVisible);
            Button tile = rock.FindAncestorOfType<Button>()!;
            Assert.AreEqual(PlacementMode.Top, ToolTip.GetPlacement(tile), "the larger picture shows above the tile, clear of the pointer");
            await AssetThumbnail.Cache.LoadAsync(rock.Model!, rock.DecodeWidth);

            Bitmap? picture = AssetThumbnail.Cache.Get(rock.Model!, rock.DecodeWidth);
            Assert.IsNotNull(picture, "the mesh is drawn");
            Assert.AreEqual(rock.DecodeWidth, picture.PixelSize.Width);
            window.Close();
        });

        [TestMethod]
        public Task ATileOfTheShelfShowsInTheInspector() => Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            model.Assets.Use(TestAssets.Catalog());
            model.Assets.Kind = Project.Workspaces.AssetKind.Prop;
            model.Assets.Scope = AssetScope.Everything;
            model.Dock = DockTab.Assets;
            Dispatcher.UIThread.RunJobs();

            Button pine = window.GetVisualDescendants().OfType<Button>()
                .Single(button => Avalonia.Automation.AutomationProperties.GetName(button)?.StartsWith("pine06,", StringComparison.Ordinal) == true);
            Click(window, pine);

            Assert.AreEqual("pine06", model.Assets.Selected?.Title);
            TextBlock name = window.GetVisualDescendants().OfType<TextBlock>()
                .Single(text => Avalonia.Automation.AutomationProperties.GetName(text) == "Asset name");
            Assert.IsTrue(name.IsEffectivelyVisible);
            Assert.AreEqual("pine06", name.Text);

            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            Dispatcher.UIThread.RunJobs();
            Assert.AreNotEqual("pine06", model.Assets.Selected?.Title, "the arrow key moves to the next tile");
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Dispatcher.UIThread.RunJobs();
            Assert.IsFalse(model.Assets.HasSelection);
            Assert.IsFalse(name.IsEffectivelyVisible, "the inspector shows the mode again");
            window.Close();
        });

        [TestMethod]
        public Task TheSymmetryChoiceDrawsItsGuidesInTheTopView() => Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            Show(window, MapViewTab.Top);
            model.ShowCamera = false;
            window.CaptureRenderedFrame();
            byte[] TopView()
            {
                using RenderTargetBitmap picture = new RenderTargetBitmap(new PixelSize((int)window.MapView.Bounds.Width, (int)window.MapView.Bounds.Height));
                picture.Render(window.MapView);
                return Png(picture);
            }
            byte[] off = TopView();
            ComboBox choice = window.GetVisualDescendants().OfType<ComboBox>()
                .Single(box => Avalonia.Automation.AutomationProperties.GetName(box) == "Symmetry");

            choice.SelectedItem = model.Symmetry.Choices.Single(item => item.Title == "Rotational, 4 players");
            Dispatcher.UIThread.RunJobs();

            Assert.AreEqual(4, model.Symmetry.Chosen.Count);
            CollectionAssert.AreNotEqual(off, TopView(), "the guides are drawn over the map");
            model.ShowSymmetry = false;
            CollectionAssert.AreEqual(off, TopView(), "the overlay hides them");
            await model.Commands.RunAsync("select.symmetry", [], interactive: false);
            Assert.AreEqual(SymmetryChoice.Off, choice.SelectedItem, "the choice follows the command");
            window.Close();
        });

        [TestMethod]
        public Task TheSelectToolSelectsMovesAndClearsInTheTopView() => Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            Show(window, MapViewTab.Top);
            model.SelectTool("markers.select");
            Dispatcher.UIThread.RunJobs();
            Spawn spawn = model.ShownDocument!.Markers.Spawns[0];
            ObjectRef thing = ObjectRef.Marker(spawn.Id);
            Point at = window.MapView.TranslatePoint(window.MapView.MapToControl(spawn.Position.X, spawn.Position.Z), window)!.Value + new Vector(3, 0);
            Point place = window.MapView.ControlToMap(window.TranslatePoint(at, window.MapView)!.Value);
            thing = model.PickAt((float)place.X, (float)place.Y, (float)(1 / window.MapView.Zoom))!.Value;
            Vector3 start = model.ShownDocument.PlaceOf(thing)!.Value;

            window.MouseMove(at);
            window.MouseDown(at, MouseButton.Left);
            for (int i = 1; i <= 5; i++)
            {
                window.MouseMove(at + new Vector(i * 10, 0));
            }
            window.MouseUp(at + new Vector(50, 0), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.IsTrue(model.Selection.Contains(thing), "a click selects what lies under it");
            Assert.AreEqual(start.X + 50 / window.MapView.Zoom, model.ShownDocument!.PlaceOf(thing)!.Value.X, 0.01, "the drag moved it with the mouse");
            Assert.AreEqual(2, model.History.Count, "opened, and the move");

            window.KeyPress(Key.E, RawInputModifiers.None, PhysicalKey.E, "e");
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual("Turn the selection", model.Map!.Session.UndoDescription, "the view has the keyboard after the click: E turns");

            window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
            Dispatcher.UIThread.RunJobs();
            Assert.IsFalse(model.ShownDocument!.Exists(thing), "Delete removes it");
            Assert.IsTrue(model.Selection.IsEmpty);

            model.Undo();
            model.Selection.Set([thing]);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Dispatcher.UIThread.RunJobs();
            Assert.IsTrue(model.Selection.IsEmpty, "Escape selects nothing");
            window.Close();
        });

        [TestMethod]
        public Task TheBrushPresetsOfThePanelSaveAndApply() => Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            model.Presets.Use(new Project.Workspaces.PresetLibrary(Path.Combine(AppContext.BaseDirectory, "presets", Guid.NewGuid().ToString("N"))));
            model.SelectTool("terrain.sculpt");
            SculptTool sculpt = model.Tool<SculptTool>();
            sculpt.Radius = 25;
            Dispatcher.UIThread.RunJobs();
            Button Named(string name) => window.GetVisualDescendants().OfType<Button>()
                .Single(button => Avalonia.Automation.AutomationProperties.GetName(button) == name);

            Click(window, Named("Save the settings as a preset"));
            Assert.AreEqual("Raise 25", model.Presets.Shown.Single().Name);
            sculpt.Radius = 3;
            Click(window, Named("Apply the preset Raise 25"));

            Assert.AreEqual(25, sculpt.Radius);
            Click(window, Named("Delete the preset Raise 25"));
            Assert.AreEqual(0, model.Presets.Shown.Count);
            window.Close();
        });

        [TestMethod]
        public Task ABoxDrawnOnEmptyGroundSelectsAndTheInspectorShowsIt() => Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            Show(window, MapViewTab.Top);
            model.SelectTool("markers.select");
            Dispatcher.UIThread.RunJobs();
            Point from = window.MapView.TranslatePoint(new Point(2, 2), window)!.Value;
            Point to = window.MapView.TranslatePoint(new Point(window.MapView.Bounds.Width - 2, window.MapView.Bounds.Height - 2), window)!.Value;

            window.MouseMove(from);
            window.MouseDown(from, MouseButton.Left);
            window.MouseMove(Middle(window.MapView, window));
            Assert.IsNotNull(model.ActiveTool!.Tool!.Box, "the box shows while it is drawn");
            window.MouseMove(to);
            window.MouseUp(to, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.IsTrue(model.Selection.Count > 1);
            Assert.IsFalse(model.CanUndo, "selecting changes nothing on the map");
            TextBox x = window.GetVisualDescendants().OfType<TextBox>().Single(box => Avalonia.Automation.AutomationProperties.GetName(box) == "X of the selection");
            Assert.IsTrue(x.IsEffectivelyVisible, "the inspector shows the selection");
            Assert.AreEqual(SelectionInspector.Mixed, x.Text);
            window.Close();
        });
    

        [TestMethod]
        public Task AClickWithThePlaceToolPlacesADecalAndTheDecalsModeIsLabelled() => Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            Show(window, MapViewTab.Top);
            model.Assets.Use(TestAssets.Catalog());
            model.SelectMode("decals");
            await model.Commands.RunAsync("decals.choose", new JsonObject { ["key"] = "env/evergreen/decals/e_mud001_albedo.dds" }, interactive: false);
            Dispatcher.UIThread.RunJobs();
            int before = model.ShownDocument!.Decals.Length;
            Point at = Middle(window.MapView, window);
            Point place = window.MapView.ControlToMap(window.TranslatePoint(at, window.MapView)!.Value);

            window.MouseMove(at);
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.AreEqual(before + 1, model.ShownDocument!.Decals.Length, "a click places the decal");
            ObjectRef placed = ObjectRef.Decal(model.ShownDocument.Decals[^1].Id);
            Vector3 centre = model.ShownDocument.PlaceOf(placed)!.Value;
            Assert.AreEqual(place.X, centre.X, 0.01, "centred on the mouse");
            Assert.AreEqual(place.Y, centre.Z, 0.01);
            Assert.IsTrue(model.Selection.Contains(placed), "and selected");
            TextBox width = window.GetVisualDescendants().OfType<TextBox>().Single(box => Avalonia.Automation.AutomationProperties.GetName(box) == "Width of the selected decals");
            Assert.IsTrue(width.IsEffectivelyVisible, "the inspector shows the decal's own fields");
            Assert.AreEqual("16", width.Text);

            // the panel lists a group made of it; every control of the mode has a label
            await model.Commands.RunAsync("decals.addGroup", new JsonObject { ["name"] = "Roads" }, interactive: false);
            Dispatcher.UIThread.RunJobs();
            CheckBox shown = window.GetVisualDescendants().OfType<CheckBox>().Single(box => Avalonia.Automation.AutomationProperties.GetName(box) == "Show the decal group Roads");
            Click(window, shown);
            Assert.AreEqual(1, model.Decals.HiddenDecals.Count, "the check box hides the group");
            List<string> unlabelled = [];
            void Check(JsonArray nodes)
            {
                foreach (JsonObject node in nodes.Cast<JsonObject>())
                {
                    string type = node["type"]!.GetValue<string>();
                    if (type is "Slider" or "ComboBox" or "TextBox" or "Button" or "CheckBox" && node["label"] is null && node["text"] is null)
                    {
                        unlabelled.Add($"{type} at {node["bounds"]!.ToJsonString()}");
                    }
                    if (node["children"] is JsonArray children)
                    {
                        Check(children);
                    }
                }
            }
            Check(UiTree.Describe(window, hidden: true));
            CollectionAssert.AreEqual(Array.Empty<string>(), unlabelled);
            model.SelectTool("decals.select");
            Dispatcher.UIThread.RunJobs();
            unlabelled.Clear();
            Check(UiTree.Describe(window, hidden: true));
            CollectionAssert.AreEqual(Array.Empty<string>(), unlabelled, "the select tool's options too");
            window.Close();
        });

        [TestMethod]
        public Task AClickInTheTopViewPlacesTheChosenUnitAndTheTreeCountsIt() => Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            model.Assets.Use(UnitsTest.Catalog());
            Show(window, MapViewTab.Top);
            model.SelectTool("units.place");
            await model.Commands.RunAsync("units.pick", new JsonObject { ["key"] = "uel0101" }, interactive: false);
            Dispatcher.UIThread.RunJobs();
            TextBlock title = window.GetVisualDescendants().OfType<TextBlock>().First(block => block.Text == "Snoop (uel0101)");
            Assert.IsTrue(title.IsEffectivelyVisible, "the options bar says what a click places");

            Point at = Middle(window.MapView, window);
            Point place = window.MapView.ControlToMap(window.TranslatePoint(at, window.MapView)!.Value);
            window.MouseMove(at);
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            PlacedUnit unit = model.ShownDocument!.FindUnit("UNIT_1")!;
            Assert.AreEqual(place.X, unit.Unit.Position.X, 0.01, "where the mouse is");
            Assert.AreEqual(place.Y, unit.Unit.Position.Z, 0.01);
            Assert.AreEqual(("NEUTRAL_CIVILIAN", 2), (unit.Army, model.History.Count), "for the civilians, one step");

            TreeView tree = window.GetVisualDescendants().OfType<TreeView>().Single(view => Avalonia.Automation.AutomationProperties.GetName(view) == "Armies and their unit groups");
            Assert.IsTrue(tree.IsEffectivelyVisible, "the Units mode's panel shows the armies");
            CollectionAssert.Contains(tree.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text).ToList(), "NEUTRAL_CIVILIAN (1)");
            window.Close();
        });

        [TestMethod]
        public Task AClickWithAMarkerToolPlacesAMarkerAndTheMarkersPanelAndInspectorAreLabelled() => Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            Show(window, MapViewTab.Top);
            model.SelectTool("markers.mass");
            Dispatcher.UIThread.RunJobs();
            int deposits = model.ShownDocument!.Markers.Resources.Length;
            Point at = window.MapView.TranslatePoint(window.MapView.MapToControl(40.2, 70.8), window)!.Value;
            Point place = window.MapView.ControlToMap(window.TranslatePoint(at, window.MapView)!.Value);

            window.MouseMove(at);
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.AreEqual(deposits + 1, model.ShownDocument!.Markers.Resources.Length, "a click places a deposit");
            ResourceMarker placed = model.ShownDocument.Markers.Resources[^1];
            Assert.AreEqual(MathF.Floor((float)place.X) + 0.5f, placed.Position.X, "on the centre of the ogrid under the mouse");
            Assert.AreEqual(MathF.Floor((float)place.Y) + 0.5f, placed.Position.Z);
            Assert.IsTrue(model.Selection.Contains(ObjectRef.Marker(placed.Id)), "and selects it");
            ComboBox resource = window.GetVisualDescendants().OfType<ComboBox>().Single(box => Avalonia.Automation.AutomationProperties.GetName(box) == "Resource of the deposits");
            Assert.IsTrue(resource.IsEffectivelyVisible, "the inspector shows the deposit's fields");
            Assert.AreEqual("Mass", resource.SelectedItem);

            // every kind of marker selected, and a chain chosen: all the panel's and inspector's controls show
            MarkerSet markers = model.ShownDocument.Markers;
            await model.Commands.RunAsync("markers.addChain", new JsonObject { ["markers"] = new JsonArray(markers.Spawns[0].Id, placed.Id) }, interactive: false);
            model.Selection.Set([ObjectRef.Marker(markers.Spawns[0].Id), ObjectRef.Marker(placed.Id), ObjectRef.Marker(markers.AiMarkers[0].Id)]);
            model.SelectTool("markers.path");
            Dispatcher.UIThread.RunJobs();
            List<string> unlabelled = [];
            void Check(JsonArray nodes)
            {
                foreach (JsonObject node in nodes.Cast<JsonObject>())
                {
                    string type = node["type"]!.GetValue<string>();
                    if (type is "ComboBox" or "TextBox" or "ListBox" or "Button" or "CheckBox" && node["label"] is null && node["text"] is null)
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
            CollectionAssert.AreEqual(Array.Empty<string>(), unlabelled);
            string json = ui.ToJsonString();
            foreach (string label in new[] { "Kinds of markers shown", "Chains", "Name of the chain", "Adaptive map", "Type of the AI markers", "No-rush offset across", "Layer of the path nodes to place" })
            {
                StringAssert.Contains(json, $"\"label\":\"{label}\"");
            }
            window.Close();
        });

        [TestMethod]
        public Task AClickWithTheCloudsToolPlacesAWeatherGeneratorAndThePathNetworksButtonGenerates() => Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            Show(window, MapViewTab.Top);
            model.SelectTool("markers.weatherGenerator");
            Dispatcher.UIThread.RunJobs();
            Point at = window.MapView.TranslatePoint(window.MapView.MapToControl(120, 60), window)!.Value;

            window.MouseMove(at);
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            WeatherGenerator placed = model.ShownDocument!.Markers.WeatherGenerators[^1];
            Assert.IsTrue(model.Selection.Contains(ObjectRef.Marker(placed.Id)), "a click places a weather generator and selects it");
            TextBox height = window.GetVisualDescendants().OfType<TextBox>().Single(box => Avalonia.Automation.AutomationProperties.GetName(box) == "Cloud height");
            Assert.IsTrue(height.IsEffectivelyVisible, "the inspector shows its fields");
            Assert.AreEqual("180", height.Text);

            Button generate = window.GetVisualDescendants().OfType<Button>().Single(button => Avalonia.Automation.AutomationProperties.GetName(button) == "Generate path networks");
            Assert.IsTrue(generate.IsEffectivelyVisible, "the Markers panel offers the path networks");
            int nodes = model.ShownDocument.Markers.PathNodes.Length;
            generate.Command!.Execute(generate.CommandParameter);
            for (int i = 0; i < 250 && model.ShownDocument!.Markers.PathNodes.Length == nodes; i++)
            {
                await Task.Delay(20);
                Dispatcher.UIThread.RunJobs();
            }
            Assert.IsTrue(model.ShownDocument!.Markers.PathNodes.Length > nodes, "the button generates them");
            StringAssert.Contains(json(window), "\"label\":\"How the path networks stand\"");
            window.Close();

            static string json(MainWindow window) => UiTree.Describe(window, hidden: true).ToJsonString();
        });

        [TestMethod]
        public Task ThePropsModePlacesWithAClickAndShowsThePropInTheInspector() => Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            model.Assets.Use(TestAssets.Catalog());
            Show(window, MapViewTab.Top);
            await model.Commands.RunAsync("props.choose", new JsonObject { ["key"] = "env/evergreen/props/rocks/rock01_prop.bp" }, interactive: false);
            Dispatcher.UIThread.RunJobs();
            T Named<T>(string name) where T : Control => window.GetVisualDescendants().OfType<T>()
                .Single(control => Avalonia.Automation.AutomationProperties.GetName(control) == name);
            Assert.AreEqual("rock01", Named<TextBlock>("Prop to place").Text, "the tool's options show the prop to place");

            int before = model.ShownDocument!.Props.Length;
            Click(window, window.MapView);
            Assert.AreEqual(before + 1, model.ShownDocument!.Props.Length, "a click places it");
            Assert.AreEqual("Place a prop", model.Map!.Session.UndoDescription);
            TextBox pitch = Named<TextBox>("Pitch of the selected props");
            Assert.IsTrue(pitch.IsEffectivelyVisible, "the inspector shows the new prop's tilt");

            Click(window, window.GetVisualDescendants().OfType<Button>().Single(button => button.Content as string == "Upright"));
            Assert.AreEqual("Stand the props upright", model.Map.Session.UndoDescription);
            Assert.AreEqual("0", pitch.Text);

            // the panel's palette: an item with its weight and its cross
            model.SelectTool("props.paint");
            await model.Commands.RunAsync("props.addToPalette", new JsonObject { ["key"] = "env/evergreen/props/trees/pine06_prop.bp" }, interactive: false);
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual("1", Named<TextBox>("Weight of pine06").Text);
            Assert.IsTrue(Named<ComboBox>("Paint only where this layer covers").IsEffectivelyVisible);
            Click(window, Named<Button>("Take pine06 out of the palette"));
            Assert.AreEqual(0, model.Props.Palette.Count);
            window.Close();
        });

        [TestMethod]
        public Task EveryControlOfThePropsModeHasALabelOrText() => Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            model.Assets.Use(TestAssets.Catalog());
            await model.Commands.RunAsync("props.addToPalette", new JsonObject { ["key"] = "env/evergreen/props/trees/pine06_prop.bp" }, interactive: false);
            await model.Commands.RunAsync("props.place", new JsonObject { ["key"] = "env/evergreen/props/trees/pine06_prop.bp", ["x"] = 40, ["z"] = 40 }, interactive: false);
            List<string> unlabelled = [];
            void Check(JsonArray nodes)
            {
                foreach (JsonObject node in nodes.Cast<JsonObject>())
                {
                    string type = node["type"]!.GetValue<string>();
                    if (type is "Slider" or "ComboBox" or "TextBox" or "ListBox" or "Button" or "CheckBox" && node["label"] is null && node["text"] is null)
                    {
                        unlabelled.Add($"{type} at {node["bounds"]!.ToJsonString()}");
                    }
                    if (node["children"] is JsonArray children)
                    {
                        Check(children);
                    }
                }
            }
            foreach (string tool in new[] { "props.select", "props.place", "props.paint", "props.erase" })
            {
                model.SelectTool(tool);
                Dispatcher.UIThread.RunJobs();
                Check(UiTree.Describe(window, hidden: true));
            }
            CollectionAssert.AreEqual(Array.Empty<string>(), unlabelled, "a screen reader and an agent need a name for each");
            window.Close();
        });

        [TestMethod]
        public Task TheHandlesTurnAndMoveAlongOneAxisInTheTopView() => Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            Show(window, MapViewTab.Top);
            model.SelectTool("markers.select");
            MarkerSet markers = model.ShownDocument!.Markers;
            ObjectRef a = ObjectRef.Marker(markers.Resources[0].Id);
            ObjectRef b = ObjectRef.Marker(markers.Resources[1].Id);
            model.Selection.Set([a, b]);
            Dispatcher.UIThread.RunJobs();
            SelectTool tool = (SelectTool)model.ActiveTool!.Tool!;
            SelectionFrame frame = tool.Frame!;
            HandleLayout layout = HandleLayout.Of(frame, (float)(1 / window.MapView.Zoom));
            Point Window(System.Numerics.Vector3 place) => window.MapView.TranslatePoint(window.MapView.MapToControl(place.X, place.Z), window)!.Value;
            Vector3 before = model.ShownDocument.PlaceOf(a)!.Value;

            // the ring, dragged a quarter round with shift: a quarter turn about the selection's centre
            Point ring = Window(layout.Centre + new System.Numerics.Vector3(layout.RingRadius, 0, 0));
            Point centre = Window(layout.Centre);
            window.MouseMove(ring);
            window.MouseDown(ring, MouseButton.Left, RawInputModifiers.Shift);
            Vector radius = ring - centre;
            for (int i = 1; i <= 6; i++)
            {
                double angle = i / 6.0 * 1.45;
                window.MouseMove(centre + new Vector(radius.X * Math.Cos(angle), radius.X * Math.Sin(angle)), RawInputModifiers.Shift);
            }
            window.MouseUp(centre + new Vector(radius.X * Math.Cos(1.45), radius.X * Math.Sin(1.45)), MouseButton.Left, RawInputModifiers.Shift);
            Dispatcher.UIThread.RunJobs();

            Assert.AreEqual("Turn the selection", model.Map!.Session.UndoDescription);
            Assert.AreEqual(90, SelectionInspector.Degrees(model.ShownDocument!.HeadingOf(a)!.Value), 0.01, "shift snaps to 15 degrees");
            Assert.AreEqual(frame.Middle.X - (before.Z - frame.Middle.Y), model.ShownDocument!.PlaceOf(a)!.Value.X, 0.05);
            Assert.AreEqual(2, model.History.Count, "one step");

            // X held while the middle square is dragged: along x only
            Vector3 turned = model.ShownDocument!.PlaceOf(a)!.Value;
            Point middle = Window(tool.Frame!.Centre);
            window.KeyPress(Key.X, RawInputModifiers.None, PhysicalKey.X, "x");
            window.MouseMove(middle);
            window.MouseDown(middle, MouseButton.Left);
            for (int i = 1; i <= 5; i++)
            {
                window.MouseMove(middle + new Vector(i * 8, i * 6));
            }
            window.MouseUp(middle + new Vector(40, 30), MouseButton.Left);
            window.KeyRelease(Key.X, RawInputModifiers.None, PhysicalKey.X, "x");
            Dispatcher.UIThread.RunJobs();

            Vector3 moved = model.ShownDocument!.PlaceOf(a)!.Value;
            Assert.AreEqual(turned.X + 40 / window.MapView.Zoom, moved.X, 0.501, "the mouse's way along x (a deposit to the nearest ogrid centre)");
            Assert.AreEqual(turned.Z, moved.Z, 1e-3, "x held: z stays");
            Assert.AreEqual(3, model.History.Count);
            window.Close();
        });
    }
}
