using System.Runtime.InteropServices;
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
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The Sketch mode in the real window (headless, user story N-1): the mouse draws and erases in
    /// the top view, the keys pick colours and widths, the chip and Ctrl+Shift+K show or hide the
    /// sketch, the panel and the inspector have labelled controls that work, and the top view draws
    /// the strokes in their colour, dimmed outside the mode.
    /// </summary>
    [TestClass]
    public sealed class SketchWindowTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private static async Task<(MainWindow Window, MainViewModel Model, AutomationServer Automation)> Open()
        {
            MainWindow window = new MainWindow { Width = 1280, Height = 800 };
            MainViewModel model = new MainViewModel(new WindowDialogs(window));
            await model.OpenAsync(HardFfa);
            window.DataContext = model;
            window.Show();
            model.View = MapViewTab.Top;
            model.SelectMode("sketch");
            Dispatcher.UIThread.RunJobs();
            return (window, model, new AutomationServer(window, model, port: 0));
        }

        private static Task<JsonObject> Send(AutomationServer automation, string command, JsonObject arguments)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            return automation.HandleAsync(command, arguments).ContinueWith(task => (JsonObject)task.Result);
        }

        private static Control Find(MainWindow window, string label) =>
            FAForever.MapEditor.App.Automation.UiTree.Find(window, label) ?? throw new AssertFailedException($"No control is labelled '{label}'");

        private static Point At(MainWindow window, double x, double z) =>
            window.MapView.TranslatePoint(window.MapView.MapToControl(x, z), window)!.Value;

        private static void Click(MainWindow window, Control control)
        {
            Point at = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
            window.MouseMove(at);
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }

        private static void Press(MainWindow window, Key key, RawInputModifiers modifiers = RawInputModifiers.None, PhysicalKey physical = PhysicalKey.None, string? symbol = null)
        {
            window.KeyPress(key, modifiers, physical, symbol);
            window.KeyRelease(key, modifiers, physical, symbol);
            Dispatcher.UIThread.RunJobs();
        }

        private static void Drag(MainWindow window, RawInputModifiers modifiers, params (double X, double Z)[] places)
        {
            Point first = At(window, places[0].X, places[0].Z);
            window.MouseMove(first, modifiers);
            window.MouseDown(first, MouseButton.Left, modifiers);
            foreach ((double x, double z) in places.Skip(1))
            {
                window.MouseMove(At(window, x, z), modifiers);
            }
            Point last = At(window, places[^1].X, places[^1].Z);
            window.MouseUp(last, MouseButton.Left, modifiers);
            Dispatcher.UIThread.RunJobs();
        }

        /// <summary>
        /// The colour the top view shows at a place of the map.
        /// </summary>
        private static (int R, int G, int B) PixelAt(MainWindow window, double x, double z)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            HeightmapView view = window.MapView;
            Point at = view.MapToControl(x, z);
            using RenderTargetBitmap bitmap = new RenderTargetBitmap(new PixelSize((int)view.Bounds.Width, (int)view.Bounds.Height));
            bitmap.Render(view);
            byte[] pixel = new byte[4];
            GCHandle pinned = GCHandle.Alloc(pixel, GCHandleType.Pinned);
            try
            {
                bitmap.CopyPixels(new PixelRect((int)at.X, (int)at.Y, 1, 1), pinned.AddrOfPinnedObject(), 4, 4);
            }
            finally
            {
                pinned.Free();
            }
            // BGRA
            return (pixel[2], pixel[1], pixel[0]);
        }

        [TestMethod]
        public Task TheControlsOfTheModeAreLabelled() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, _) = await Open();

            Find(window, "Show Sketch");
            foreach (string label in new[] { "Pencil red", "Pencil yellow", "Pencil green", "Pencil blue", "Pencil white", "Pencil black", "Thin strokes", "Medium strokes", "Thick strokes",
                "Mirror strokes by the symmetry", "Smooth the line", "Add a sheet" })
            {
                Find(window, label);
            }
            Assert.IsTrue(window.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "No sheet yet: draw with the pencil, or add one in the panel."), "the inspector says so");
            window.Close();
        });

        [TestMethod]
        public Task TheMouseDrawsAndTheTopViewShowsTheStroke() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            await model.Commands.RunAsync("sketch.setPencil", new JsonObject { ["colour"] = "red", ["width"] = "thick", ["mirror"] = false }, interactive: false);
            Dispatcher.UIThread.RunJobs();

            Point first = At(window, 60, 100);
            window.MouseMove(first);
            window.MouseDown(first, MouseButton.Left);
            window.MouseMove(At(window, 90, 100));
            Assert.AreEqual(1, model.Sketch.Pending.Count, "the line is shown while the mouse is down");
            JsonObject during = await Send(automation, "state", new JsonObject { ["path"] = "sketch.drawn" });
            Assert.AreEqual(1, during["sketch.drawn"]!["pending"]!.GetValue<int>());
            window.MouseMove(At(window, 120, 100));
            window.MouseUp(At(window, 120, 100), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.AreEqual(1, model.ShownDocument!.Sketch.Sheets.Single().Strokes.Length);
            Assert.AreEqual(0, model.Sketch.Pending.Count);
            JsonObject after = await Send(automation, "state", new JsonObject { ["path"] = "sketch" });
            Assert.AreEqual(1, after["sketch"]!["drawn"]!["strokes"]!.GetValue<int>(), "the top view drew it");
            (int r, int g, int b) = PixelAt(window, 90, 100);
            Assert.IsTrue(r > 200 && g < 140 && b < 130, $"the stroke is red where the mouse went: {r}, {g}, {b}");

            // a pencil's width is pixels on screen: the same after zooming in
            window.MapView.Show(8, 90, 100);
            Dispatcher.UIThread.RunJobs();
            (r, g, b) = PixelAt(window, 90, 100);
            Assert.IsTrue(r > 200 && g < 140 && b < 130, $"still red at another zoom: {r}, {g}, {b}");
            (int r2, int g2, int b2) = PixelAt(window, 90, 104);
            Assert.IsFalse(r2 > 200 && g2 < 140 && b2 < 130, "four ogrids away it is beside the line: a line of a few pixels, not of ogrids");
            window.Close();
        });

        [TestMethod]
        public Task OutsideTheSketchModeASheetIsDimmedAndTheOverlayHidesIt() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            await model.Commands.RunAsync("sketch.draw", new JsonObject
            {
                ["points"] = new JsonArray(new JsonArray(60, 100), new JsonArray(120, 100)), ["colour"] = "red", ["width"] = "thick", ["mirror"] = false,
            }, interactive: false);
            Dispatcher.UIThread.RunJobs();
            (int r, int g, int b) = PixelAt(window, 90, 100);
            Assert.IsTrue(r > 200 && g < 140 && b < 130, "fully red in the Sketch mode");

            model.SelectMode("terrain");
            Dispatcher.UIThread.RunJobs();
            (int dr, int dg, int db) = PixelAt(window, 90, 100);
            Assert.IsTrue(dr > dg + 20, "still red-ish in the Terrain mode");
            Assert.IsTrue(dg > g || db > b, $"but dimmed at the sheet's opacity: {dr}, {dg}, {db} against {r}, {g}, {b}");

            Press(window, Key.K, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.K, "k");
            Assert.IsFalse(model.ShowSketch, "Ctrl+Shift+K hides the sketch in any mode");
            JsonObject state = await Send(automation, "state", new JsonObject { ["path"] = "sketch.drawn" });
            Assert.AreEqual(0, state["sketch.drawn"]!["sheets"]!.GetValue<int>());
            (int hr, int hg, int hb) = PixelAt(window, 90, 100);
            Assert.IsFalse(hr > dr + 20 && hg < dg, $"nothing red is drawn: {hr}, {hg}, {hb}");

            Press(window, Key.K, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.K, "k");
            Assert.IsTrue(model.ShowSketch);
            window.Close();
        });

        [TestMethod]
        public Task TheKeysPickTheColourWidthAndTheModeInTheWindow() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, _) = await Open();
            model.SelectMode("terrain");
            Dispatcher.UIThread.RunJobs();

            Press(window, Key.F10, RawInputModifiers.None, PhysicalKey.F10);
            Assert.AreEqual("sketch", model.SelectedMode!.Definition.Id, "F10 chooses the mode");
            Assert.AreEqual("sketch.pencil", model.ActiveTool!.Definition.Id);

            window.MapView.Focus();
            Dispatcher.UIThread.RunJobs();
            PencilTool pencil = model.Tool<PencilTool>();
            Press(window, Key.D3, RawInputModifiers.None, PhysicalKey.Digit3, "3");
            Assert.AreEqual(SketchColour.Green, pencil.Colour);
            Press(window, Key.OemCloseBrackets, RawInputModifiers.None, PhysicalKey.BracketRight, "]");
            Assert.AreEqual(SketchWidth.Thick, pencil.Width);
            window.Close();
        });

        [TestMethod]
        public Task AnAltDragWithTheEraserRemovesWholeStrokesAndDoesNotMoveTheCamera() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, _) = await Open();
            await model.Commands.RunAsync("sketch.draw", new JsonObject { ["points"] = new JsonArray(new JsonArray(40, 100), new JsonArray(160, 100)), ["mirror"] = false }, interactive: false);
            await model.Commands.RunAsync("sketch.draw", new JsonObject { ["points"] = new JsonArray(new JsonArray(40, 140), new JsonArray(160, 140)), ["mirror"] = false }, interactive: false);
            await model.Commands.RunAsync("sketch.setEraser", new JsonObject { ["radius"] = 4, ["mirror"] = false }, interactive: false);
            Dispatcher.UIThread.RunJobs();
            var camera = model.Camera.Now;

            Drag(window, RawInputModifiers.Alt, (100, 90), (100, 110));

            Assert.AreEqual(1, model.ShownDocument!.Sketch.Sheets.Single().Strokes.Length, "the stroke the eraser touched is gone, whole");
            Assert.AreEqual(camera, model.Camera.Now, "alt with the eraser is the eraser's, not a click for the 3D camera");

            Drag(window, RawInputModifiers.None, (100, 130), (100, 150));
            Assert.AreEqual(2, model.ShownDocument.Sketch.Sheets.Single().Strokes.Length, "without alt it cuts the stroke in two");
            window.Close();
        });

        [TestMethod]
        public Task ThePanelAndTheInspectorEditTheChosenSheet() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, _) = await Open();

            Click(window, Find(window, "Add a sheet"));
            Assert.AreEqual("Sheet 1", model.Sketch.Chosen);
            Click(window, Find(window, "Add a sheet"));
            Assert.AreEqual("Sheet 2", model.Sketch.Chosen);
            Assert.AreEqual(2, model.Sketch.Items.Count);
            Dispatcher.UIThread.RunJobs();

            // the eye: Shown, then Hidden
            Click(window, Find(window, "Hide the sheet Sheet 1"));
            Assert.IsFalse(model.ShownDocument!.Sketch.Find("Sheet 1")!.Shown);
            Click(window, Find(window, "Show the sheet Sheet 1"));
            Assert.IsTrue(model.ShownDocument.Sketch.Find("Sheet 1")!.Shown);

            // the name and a legend text in the inspector, given on Enter
            TextBox name = (TextBox)Find(window, "Name of the sheet");
            name.Focus();
            name.Text = "Layout A";
            Press(window, Key.Enter, RawInputModifiers.None, PhysicalKey.Enter);
            Assert.IsNotNull(model.ShownDocument.Sketch.Find("Layout A"), "renamed");
            Assert.AreEqual("Layout A", model.Sketch.Chosen);

            TextBox legend = (TextBox)Find(window, "Legend of red");
            legend.Focus();
            legend.Text = "spawns";
            Press(window, Key.Enter, RawInputModifiers.None, PhysicalKey.Enter);
            Assert.AreEqual("spawns", model.ShownDocument.Sketch.Find("Layout A")!.LegendOf(SketchColour.Red));

            // the pencil's swatches and widths in the options row
            Click(window, Find(window, "Pencil blue"));
            Assert.AreEqual(SketchColour.Blue, model.Tool<PencilTool>().Colour);
            Click(window, Find(window, "Thin strokes"));
            Assert.AreEqual(SketchWidth.Thin, model.Tool<PencilTool>().Width);

            await model.Commands.RunAsync("sketch.draw", new JsonObject { ["points"] = new JsonArray(new JsonArray(60, 100), new JsonArray(120, 100)), ["mirror"] = false }, interactive: false);
            Dispatcher.UIThread.RunJobs();
            Click(window, Find(window, "Clear the chosen sheet"));
            Assert.IsTrue(model.ShownDocument.Sketch.Find("Layout A")!.Strokes.IsEmpty);
            Click(window, Find(window, "Delete the chosen sheet"));
            Assert.IsNull(model.ShownDocument.Sketch.Find("Layout A"));
            window.Close();
        });
    }
}
