using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using FAForever.MapEditor.App.Automation;
using FAForever.MapEditor.App.Controls;
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

        private static void Show(MainWindow window, string view)
        {
            window.ShownView = view;
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

        /// <remarks>
        /// This and the other tests of the 3D view's input also check that the mouse reaches it at
        /// all: its picture comes from the graphics card, so it claims its rectangle itself
        /// (<c>ICustomHitTest</c>); without that, they fail.
        /// </remarks>
        [TestMethod]
        public Task TheWheelZoomsTheCameraTowardsTheMouse() => Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            Show(window, "3d");
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
            Show(window, "3d");
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
            Show(window, "3d");
            Point middle = Middle(window.View3D, window);

            window.MouseMove(middle);
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            window.MouseMove(middle + new Vector(40, -200));
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            window.MouseMove(middle + new Vector(400, -200));
            Settle(model);

            Assert.AreEqual(-10, model.Camera.Now.Yaw, 0.01, "a quarter degree a pixel, clockwise to the right");
            Assert.AreEqual(40, model.Camera.Now.Pitch, 0.01, "up tilts towards the horizon");
            window.Close();
        });

        [TestMethod]
        public Task HomeShowsTheWholeMapAgain() => Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            Show(window, "3d");
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
            Show(window, "top");
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
            Show(window, "top");
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
        public Task SwitchingViewsKeepsBothInTheWindow() => Run(async () =>
        {
            (MainWindow window, _) = await Open();

            Show(window, "3d");
            Show(window, "top");

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
            Show(window, "top");
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
