using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FAForever.MapEditor.App.Automation;
using FAForever.MapEditor.App.Controls;
using FAForever.MapEditor.App.Rendering.Gpu;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;
using FAForever.MapEditor.App.Views.SketchPanels;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// How the sketch looks (user stories N-1, N-3, N-4), headless: black and white have a rim on the panel's swatches and a
    /// halo round their lines, the chips and the note say where the sketch is turned on and off, the legend's key follows the
    /// sheet's opacity and keeps to a corner, the Symbol, Label and Select tools take a place in the 3D view, and a label's text
    /// is made into a picture for the 3D view.
    /// </summary>
    [TestClass]
    public sealed class SketchLooksWindowTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private static async Task<(MainWindow Window, MainViewModel Model, AutomationServer Automation)> Open(string mode = "sketch")
        {
            MainWindow window = new MainWindow { Width = 1280, Height = 800 };
            MainViewModel model = new MainViewModel(new WindowDialogs(window));
            await model.OpenAsync(HardFfa);
            window.DataContext = model;
            window.Show();
            model.View = MapViewTab.Top;
            model.SelectMode(mode);
            Dispatcher.UIThread.RunJobs();
            return (window, model, new AutomationServer(window, model, port: 0));
        }

        private static Task Run(MainViewModel model, string id, JsonObject? arguments = null)
        {
            Task run = model.Commands.RunAsync(id, arguments ?? [], interactive: false);
            Dispatcher.UIThread.RunJobs();
            return run;
        }

        private static (int R, int G, int B) PixelAtPixel(MainWindow window, Point at)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            HeightmapView view = window.MapView;
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
            return (pixel[2], pixel[1], pixel[0]);
        }

        private static int Brightness((int R, int G, int B) colour) => colour.R + colour.G + colour.B;

        [TestMethod]
        public Task TheSwatchesOfBlackAndWhiteHaveARimOfTheOtherTone() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, _) = await Open();
            await Run(model, "sketch.draw", new JsonObject { ["points"] = new JsonArray(new JsonArray(60, 100), new JsonArray(120, 100)) });
            await Run(model, "sketch.setLegend", new JsonObject { ["colour"] = "black", ["text"] = "Impassable" });
            Dispatcher.UIThread.RunJobs();

            List<Border> swatches = [.. window.GetVisualDescendants().OfType<Border>().Where(border => border.Classes.Contains("swatch") && border.IsEffectivelyVisible
                && border.Background is ISolidColorBrush)];
            Border[] black = [.. swatches.Where(border => ((ISolidColorBrush)border.Background!).Color == Color.FromUInt32(SketchLook.Argb(SketchColour.Black)))];
            Border[] white = [.. swatches.Where(border => ((ISolidColorBrush)border.Background!).Color == Color.FromUInt32(SketchLook.Argb(SketchColour.White)))];
            Assert.IsTrue(black.Length >= 2, "the pencil's swatch and the legend's dot of black");
            Assert.IsTrue(white.Length >= 2, "the pencil's swatch and the legend's dot of white");
            foreach (Border swatch in black)
            {
                Assert.AreEqual(Color.FromUInt32(SketchLook.Argb(SketchColour.White)), ((ISolidColorBrush)swatch.BorderBrush!).Color, "black has a light rim, which shows on a dark panel");
            }
            foreach (Border swatch in white)
            {
                Assert.AreEqual(Color.FromUInt32(SketchLook.Argb(SketchColour.Black)), ((ISolidColorBrush)swatch.BorderBrush!).Color, "white has a dark rim, which shows on a light panel");
            }
            window.Close();
        });

        [TestMethod]
        public Task ABlackLineHasALightHaloAndAWhiteLineADarkOneInTheTopView() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, _) = await Open();
            HeightmapView view = window.MapView;
            Point beside(double x, double z, double pixels) => new Point(view.MapToControl(x, z).X, view.MapToControl(x, z).Y + pixels);

            // a thick line is 7 pixels wide: its half is 3.5, the halo reaches 1.5 pixels beyond that
            (int, int, int) bareBlack = PixelAtPixel(window, beside(90, 100, 4.5));
            (int, int, int) bareWhite = PixelAtPixel(window, beside(90, 140, 4.5));
            await Run(model, "sketch.draw", new JsonObject { ["points"] = new JsonArray(new JsonArray(60, 100), new JsonArray(120, 100)), ["colour"] = "black", ["width"] = "thick", ["mirror"] = false });
            await Run(model, "sketch.draw", new JsonObject { ["points"] = new JsonArray(new JsonArray(60, 140), new JsonArray(120, 140)), ["colour"] = "white", ["width"] = "thick", ["mirror"] = false });

            (int, int, int) lineBlack = PixelAtPixel(window, beside(90, 100, 0));
            Assert.IsTrue(Brightness(lineBlack) < 250, $"the line itself is black: {lineBlack}");
            (int, int, int) haloBlack = PixelAtPixel(window, beside(90, 100, 4.5));
            Assert.IsTrue(Brightness(haloBlack) > Brightness(bareBlack) + 60 || Brightness(haloBlack) > 600, $"a light halo beside a black line: {haloBlack} (bare terrain {bareBlack})");
            (int, int, int) haloWhite = PixelAtPixel(window, beside(90, 140, 4.5));
            Assert.IsTrue(Brightness(haloWhite) < Brightness(bareWhite) - 60 || Brightness(haloWhite) < 150, $"a dark halo beside a white line: {haloWhite} (bare terrain {bareWhite})");
            window.Close();
        });

        [TestMethod]
        public Task TheSketchChipShowsBesideTheCameraChipAtTheSmallestWindowAndTheNoteSaysWhere() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, _) = await Open("terrain");
            await Run(model, "sketch.draw", new JsonObject { ["points"] = new JsonArray(new JsonArray(60, 100), new JsonArray(120, 100)) });
            Dispatcher.UIThread.RunJobs();

            Control chip = UiTree.Find(window, "Show Sketch") ?? throw new AssertFailedException("no Sketch chip");
            Assert.IsFalse(ToolRow.IsInOverflow(chip), "the Sketch chip shows in the views' bar at 1280 pixels");
            Control camera = UiTree.Find(window, "Show 3D camera")!;
            Assert.IsFalse(ToolRow.IsInOverflow(camera), "so does the 3D camera's");
            StringAssert.Contains(ToolTip.GetTip(chip) as string, "Ctrl+Shift+K", "the chip's tip names the key");

            StringAssert.Contains(model.SketchNote, "Sketch chip in the views' bar");
            StringAssert.Contains(model.SketchNote, "behind More when the window is narrow");
            StringAssert.Contains(model.SketchNote, "View menu");
            StringAssert.Contains(model.SketchNote, "Ctrl+Shift+K");
            model.ShowSketch = false;
            StringAssert.Contains(model.SketchNote, "View menu");

            // the View menu shows both keys beside its entries
            MenuItem view = window.GetVisualDescendants().OfType<Menu>().First().Items.OfType<MenuItem>().First(item => item.Header is string header && header.Contains("View"));
            MenuItem[] entries = [.. view.Items.OfType<MenuItem>()];
            Assert.AreEqual("Ctrl+Shift+K", entries.Single(item => item.Header as string == "Show the sketch").InputGesture?.ToString());
            Assert.AreEqual("Ctrl+Shift+J", entries.Single(item => item.Header as string == "Show the projected images in the top view").InputGesture?.ToString());
            window.Close();
        });

        [TestMethod]
        public Task TheKeyFollowsTheSheetsOpacityOutsideTheSketchModeAndShrinksWithASmallMap() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, _) = await Open();
            await Run(model, "sketch.draw", new JsonObject { ["points"] = new JsonArray(new JsonArray(60, 100), new JsonArray(120, 100)) });
            foreach (string colour in new[] { "red", "yellow", "green", "blue", "white", "black" })
            {
                await Run(model, "sketch.setLegend", new JsonObject { ["colour"] = colour, ["text"] = $"What {colour} means here" });
            }
            window.MapView.Fit();
            (Rect Box, double Opacity) Key()
            {
                // a frame may come one tick late: draw until two frames agree
                (Rect Box, double Opacity) drawn = (default, -1);
                for (int frame = 0; frame < 5; frame++)
                {
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    if (window.MapView.SketchKeyDrawn == drawn)
                    {
                        break;
                    }
                    drawn = window.MapView.SketchKeyDrawn;
                }
                return drawn;
            }

            (Rect box, double opacity) = Key();
            Assert.AreEqual(1.0, opacity, "full in the Sketch mode");
            double full = box.Width;
            Assert.IsTrue(full > 100, "the key is as wide as its texts need when the map fills the view");
            Assert.IsTrue(box.X < 40 && box.Bottom > window.MapView.Bounds.Height - 40, "in the lower left corner");

            model.SelectMode("terrain");
            Assert.AreEqual(0.45, Key().Opacity, 0.001, "at the sheet's opacity in the other modes");
            await Run(model, "sketch.setOpacity", new JsonObject { ["opacity"] = 0 });
            Assert.AreEqual(0, Key().Box.Width, "a sheet that does not show has no key");
            await Run(model, "sketch.setOpacity", new JsonObject { ["opacity"] = 0.45 });

            // zoomed out the key shrinks with the map, to half at the least, and takes no more than a third of the view
            window.MapView.Show(0.1, 512, 512);
            double small = Key().Box.Width;
            Assert.IsTrue(small < full, $"smaller zoomed out: {small} against {full}");
            Assert.IsTrue(small >= full * 0.5 - 1, $"never below half: {small}");
            window.Close();
        });

        private static ToolView ThreeD(float x, float z) =>
            // a view that is not straight down: a place shows twice as far from the origin, as with the 3D view's projection
            new ToolView(false, new Vector2(x, z) * 2, place => new Vector2(place.X, place.Z) * 2 + new Vector2(0, -place.Y), _ => 0.5f,
                (new Vector3(0, 100, 0), Vector3.Normalize(new Vector3(0, -1, 0.1f))), new Vector3(0, 100, 0));

        [TestMethod]
        public Task TheSymbolLabelAndSelectToolsTakeAPlaceInTheThreeDView() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, _) = await Open();
            await Run(model, "sketch.draw", new JsonObject { ["points"] = new JsonArray(new JsonArray(60, 100), new JsonArray(120, 100)), ["mirror"] = false });
            ToolInput input(float x, float z) => new ToolInput(ToolModifiers.None, 0.5f, Picks: true, View: ThreeD(x, z));

            SymbolTool symbol = model.Tool<SymbolTool>();
            model.SelectTool("sketch.symbol");
            symbol.Mirror = false;
            symbol.Press(150, 150, input(150, 150));
            symbol.Release();
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(1, model.ShownDocument!.Sketch.Sheets[0].Symbols.Length, "a symbol is stamped where the 3D view's mouse points");
            StringAssert.Contains(model.Status, "Stamped");

            LabelTool label = model.Tool<LabelTool>();
            model.SelectTool("sketch.label");
            label.Press(160, 170, input(160, 170));
            Dispatcher.UIThread.RunJobs();
            Assert.IsTrue(model.Sketch.IsWriting, "a label is begun");
            Assert.IsTrue(model.Sketch.Draft!.ThreeD, "over the 3D view");
            LabelEditor[] editors = [.. window.GetVisualDescendants().OfType<LabelEditor>()];
            Assert.AreEqual(2, editors.Length, "one box over each view");
            Assert.IsFalse(editors.Single(editor => !editor.ThreeD).IsVisible, "the top view's box stays away");
            Assert.IsTrue(editors.Single(editor => editor.ThreeD).IsVisible, "the 3D view's box shows");
            model.Sketch.Drop();
            Dispatcher.UIThread.RunJobs();
            Assert.IsFalse(editors.Single(editor => editor.ThreeD).IsVisible);

            SketchSelectTool select = model.Tool<SketchSelectTool>();
            model.SelectTool("sketch.select");
            select.Press(90, 100, input(90, 100));
            select.Release();
            Assert.IsTrue(select.HasSelection, "a click selects the stroke under the 3D view's mouse");
            Assert.IsTrue(select.Shapes(ThreeD(90, 100)).Count > 0, "and outlines it in the 3D view");
            window.Close();
        });

        [TestMethod]
        public Task ALabelsTextIsMadeIntoAPictureForTheThreeDViewOnTheInterfacesThread() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, _, _) = await Open();
            int drawn = 0;
            LabelBitmaps bitmaps = new LabelBitmaps(new SketchLabelPainter(() => new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold)), () => drawn++);
            SketchLabel label = new SketchLabel("Mid mass", new Vector2(50, 50), SketchColour.Yellow, 16);

            Assert.IsNull(bitmaps.Get(label, 24), "not made yet: it is asked for");
            Assert.AreEqual(1, bitmaps.Pending);
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(1, drawn, "the view is asked to draw again");
            LabelBitmap? bitmap = bitmaps.Get(label, 24);
            Assert.IsNotNull(bitmap);
            Assert.AreEqual(0, bitmaps.Pending);
            Assert.AreEqual(bitmap.Width * bitmap.Height * 4, bitmap.Rgba.Length);
            // yellow ink (red and green high, blue low, premultiplied) and a black halo round it
            bool ink = false, halo = false;
            for (int i = 0; i < bitmap.Rgba.Length; i += 4)
            {
                ink |= bitmap.Rgba[i + 3] > 200 && bitmap.Rgba[i] > 200 && bitmap.Rgba[i + 1] > 150 && bitmap.Rgba[i + 2] < 120;
                halo |= bitmap.Rgba[i + 3] > 200 && bitmap.Rgba[i] < 40 && bitmap.Rgba[i + 1] < 40 && bitmap.Rgba[i + 2] < 40;
            }
            Assert.IsTrue(ink, "the text is yellow, red first in memory");
            Assert.IsTrue(halo, "and has a black halo");
            Assert.AreSame(bitmap, bitmaps.Get(label, 24), "made once");
            window.Close();
        });
    }
}
