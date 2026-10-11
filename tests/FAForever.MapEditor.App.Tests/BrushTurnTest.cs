using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// Turning a brush's picture with Q and E (shift: 90 degrees) and with alt and the wheel over a
    /// view (5 degrees a notch), for every tool with a shape; a round brush, the select tools and
    /// the stamp keep the keys as they were.
    /// </summary>
    [TestClass]
    public sealed class BrushTurnTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private static Task Run(Func<Task> test) => HeadlessApp.Run(test);

        private static async Task<(MainWindow Window, MainViewModel Model)> Open()
        {
            MainWindow window = new MainWindow { Width = 1280, Height = 800 };
            MainViewModel model = new MainViewModel(new WindowDialogs(window));
            model.Assets.Use(TestAssets.Catalog());
            await model.OpenAsync(HardFfa);
            window.DataContext = model;
            window.Show();
            Dispatcher.UIThread.RunJobs();
            return (window, model);
        }

        private static void Press(MainWindow window, Key key, RawInputModifiers modifiers = RawInputModifiers.None, PhysicalKey physical = PhysicalKey.None, string? symbol = null)
        {
            window.KeyPress(key, modifiers, physical, symbol);
            window.KeyRelease(key, modifiers, physical, symbol);
            Dispatcher.UIThread.RunJobs();
        }

        private static Point Middle(Control view, MainWindow window) =>
            view.TranslatePoint(new Point(view.Bounds.Width / 2, view.Bounds.Height / 2), window)!.Value;

        [TestMethod]
        public Task QAndEAndAltWithTheWheelTurnTheBrushesPicture() => Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            model.SelectTool("terrain.sculpt");
            SculptTool sculpt = model.Tool<SculptTool>();
            sculpt.Shape.Choose("ridge.png");
            window.MapView.Focus();
            Dispatcher.UIThread.RunJobs();

            Press(window, Key.E, physical: PhysicalKey.E, symbol: "e");
            Assert.AreEqual(15, sculpt.Shape.Rotation);
            Assert.AreEqual("Brush rotation: 15°", model.Status, "the status line says the angle");
            Press(window, Key.Q, physical: PhysicalKey.Q, symbol: "q");
            Press(window, Key.Q, physical: PhysicalKey.Q, symbol: "q");
            Assert.AreEqual(-15, sculpt.Shape.Rotation, "Q anticlockwise");
            Press(window, Key.E, RawInputModifiers.Shift, PhysicalKey.E, "E");
            Assert.AreEqual(75, sculpt.Shape.Rotation, "shift: 90 degrees");
            Press(window, Key.E, RawInputModifiers.Shift, PhysicalKey.E, "E");
            Press(window, Key.E, RawInputModifiers.Shift, PhysicalKey.E, "E");
            Assert.AreEqual(-105, sculpt.Shape.Rotation, "within -180 to 180");

            // the options row's Rotation follows (it is bound to the shape)
            Slider? rotation = window.GetVisualDescendants().OfType<Slider>().FirstOrDefault(slider => Avalonia.Automation.AutomationProperties.GetName(slider) == "Brush rotation in degrees");
            if (rotation is not null)
            {
                Assert.AreEqual(-105, rotation.Value, 1e-9);
            }

            double zoom = window.MapView.Zoom;
            Point middle = Middle(window.MapView, window);
            window.MouseMove(middle);
            window.MouseWheel(middle, new Vector(0, 2), RawInputModifiers.Alt);
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(-95, sculpt.Shape.Rotation, "alt and the wheel: 5 degrees a notch");
            Assert.AreEqual(zoom, window.MapView.Zoom, "and no zoom");
            Assert.AreEqual("Brush rotation: -95°", model.Status);

            // every tool with a shape: the strata brush too
            model.SelectTool("paint.strata");
            StrataTool strata = model.Tool<StrataTool>();
            strata.Shape.Choose("ridge.png");
            window.MapView.Focus();
            Press(window, Key.E, physical: PhysicalKey.E, symbol: "e");
            Assert.AreEqual(15, strata.Shape.Rotation);
            window.Close();
        });

        [TestMethod]
        public Task ARoundBrushTheSelectToolAndTheStampKeepTheirKeys() => Run(async () =>
        {
            (MainWindow window, MainViewModel model) = await Open();
            model.SelectTool("terrain.sculpt");
            SculptTool sculpt = model.Tool<SculptTool>();
            Assert.IsFalse(sculpt.Shape.IsShaped);
            window.MapView.Focus();
            Dispatcher.UIThread.RunJobs();
            string status = model.Status;

            Press(window, Key.E, physical: PhysicalKey.E, symbol: "e");
            Assert.AreEqual(0, sculpt.Shape.Rotation, "a round brush is not turned");
            Assert.AreEqual(status, model.Status);
            double zoom = window.MapView.Zoom;
            Point middle = Middle(window.MapView, window);
            window.MouseMove(middle);
            window.MouseWheel(middle, new Vector(0, 1), RawInputModifiers.Alt);
            Dispatcher.UIThread.RunJobs();
            Assert.AreNotEqual(zoom, window.MapView.Zoom, "without a picture, alt and the wheel zoom as the wheel does");

            // the stamp turns its picture with its own keys, and with alt and the wheel
            model.SelectTool("terrain.stamp");
            StampTool stamp = model.Tool<StampTool>();
            stamp.Shape.Choose("ridge.png");
            window.MapView.Focus();
            Press(window, Key.E, physical: PhysicalKey.E, symbol: "e");
            Assert.AreEqual(15, stamp.Shape.Rotation);
            Assert.AreEqual("The stamp turned 15°", model.Status, "as it did");
            window.MouseMove(middle);
            window.MouseWheel(middle, new Vector(0, -1), RawInputModifiers.Alt);
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(10, stamp.Shape.Rotation);

            // a select tool turns the selection with Q and E
            model.SelectTool("markers.select");
            var spawn = model.ShownDocument!.Markers.Spawns[0];
            model.Selection.Set([Project.Editing.ObjectRef.Marker(spawn.Id)]);
            Assert.IsFalse(BrushKeys.TurnKey(model.ActiveTool!.Tool, "E", ToolModifiers.None, out _), "no brush to turn");
            window.Close();
        });

        [TestMethod]
        public void TurningWrapsAndRandomRotationKeepsTheStartingAngle()
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            model.Assets.Use(TestAssets.Catalog());
            SculptTool sculpt = model.Tool<SculptTool>();
            sculpt.Shape.Choose("ridge.png");
            sculpt.Shape.Rotation = 175;
            Assert.IsTrue(BrushKeys.Turn(sculpt, 15, out string? status));
            Assert.AreEqual(-170, sculpt.Shape.Rotation);
            sculpt.Shape.RandomRotation = true;
            Assert.IsTrue(BrushKeys.TurnWheel(sculpt, -2, out status));
            Assert.AreEqual(180, sculpt.Shape.Rotation, "-180 is 180");
            StringAssert.Contains(status, "random");
            Assert.IsFalse(BrushKeys.TurnKey(sculpt, "E", ToolModifiers.Control, out _), "control with E is not a turn");
            Assert.AreEqual(1, BrushKeys.Notches(0.2));
            Assert.AreEqual(-3, BrushKeys.Notches(-3));
        }
    }
}
