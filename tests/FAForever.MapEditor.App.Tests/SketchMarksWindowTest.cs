using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using FAForever.MapEditor.App.Automation;
using FAForever.MapEditor.App.Controls;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The sketch's symbols, labels and Select tool in the real window (headless, user story N-4): the
    /// rows' controls are labelled, the mouse stamps, writes (the text box over the view), selects and moves,
    /// the top view draws symbols and labels, and the handles of the Select and Image tools have a colour no
    /// pencil has.
    /// </summary>
    [TestClass]
    public sealed class SketchMarksWindowTest
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

        private static Task Run(MainViewModel model, string id, JsonObject? arguments = null)
        {
            Task run = model.Commands.RunAsync(id, arguments ?? [], interactive: false);
            Dispatcher.UIThread.RunJobs();
            return run;
        }

        private static Control Find(MainWindow window, string label) =>
            UiTree.Find(window, label) ?? throw new AssertFailedException($"No control is labelled '{label}'");

        private static Point At(MainWindow window, double x, double z) =>
            window.MapView.TranslatePoint(window.MapView.MapToControl(x, z), window)!.Value;

        private static void Click(MainWindow window, double x, double z, RawInputModifiers modifiers = RawInputModifiers.None)
        {
            Point at = At(window, x, z);
            window.MouseMove(at, modifiers);
            window.MouseDown(at, MouseButton.Left, modifiers);
            window.MouseUp(at, MouseButton.Left, modifiers);
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
            window.MouseUp(At(window, places[^1].X, places[^1].Z), MouseButton.Left, modifiers);
            Dispatcher.UIThread.RunJobs();
        }

        private static void Press(MainWindow window, Key key, PhysicalKey physical = PhysicalKey.None, string? symbol = null, RawInputModifiers modifiers = RawInputModifiers.None)
        {
            window.KeyPress(key, modifiers, physical, symbol);
            window.KeyRelease(key, modifiers, physical, symbol);
            Dispatcher.UIThread.RunJobs();
        }

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
            return (pixel[2], pixel[1], pixel[0]);
        }

        [TestMethod]
        public Task TheRowsOfTheThreeToolsAreLabelled() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, _) = await Open();

            model.SelectTool("sketch.symbol");
            Dispatcher.UIThread.RunJobs();
            foreach (string label in new[] { "Symbol spawn", "Symbol mass", "Symbol hydrocarbon", "Symbol arrow", "Symbol cross", "Symbol question",
                "Symbol red", "Symbol blue", "Symbol black", "Small symbols", "Medium symbols", "Large symbols", "Mirror symbols by the symmetry", "Sheet stamped into" })
            {
                Find(window, label);
            }
            Assert.IsNull(UiTree.Find(window, "Turn of the arrow in degrees"), "only an arrow has a direction");
            model.Tool<SymbolTool>().Kind = SketchSymbolKind.Arrow;
            Dispatcher.UIThread.RunJobs();
            Assert.IsTrue(Find(window, "Turn of the arrow in degrees").IsVisible);

            model.SelectTool("sketch.label");
            Dispatcher.UIThread.RunJobs();
            foreach (string label in new[] { "Label red", "Label white", "Small labels", "Large labels", "Sheet written in", "Label keys" })
            {
                Find(window, label);
            }

            model.SelectTool("sketch.select");
            Dispatcher.UIThread.RunJobs();
            foreach (string label in new[] { "Selection", "Recolour the selection red", "Recolour the selection black", "Small selected symbols and labels", "Delete the selected items of the sketch", "Select keys" })
            {
                Find(window, label);
            }
            Assert.IsFalse(Find(window, "Delete the selected items of the sketch").IsEnabled, "nothing to delete yet");
            window.Close();
        });

        [TestMethod]
        public Task TheMouseStampsASymbolAndTheTopViewDrawsItInItsColour() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            await Run(model, "sketch.setSymbol", new JsonObject { ["kind"] = "cross", ["colour"] = "white", ["size"] = 24, ["mirror"] = false });

            Click(window, 100, 100);

            SketchSymbol cross = model.ShownDocument!.Sketch.Sheets.Single().Symbols.Single();
            Assert.AreEqual(SketchSymbolKind.Cross, cross.Kind);
            Assert.IsTrue(Vector2Close(cross.Place, 100, 100), "stamped under the mouse");
            JsonObject state = (JsonObject)(await automation.HandleAsync("state", new JsonObject { ["path"] = "sketch" }))["sketch"]!;
            Assert.AreEqual(1, state["drawn"]!["symbols"]!.GetValue<int>());
            (int r, int g, int b) = PixelAt(window, 100, 100);
            Assert.IsTrue(r > 220 && g > 215 && b > 200, $"the cross is the sketch's white at its middle: {r}, {g}, {b}");

            // an arrow follows the drag
            await Run(model, "sketch.setSymbol", new JsonObject { ["kind"] = "arrow", ["colour"] = "blue", ["size"] = 24 });
            Drag(window, RawInputModifiers.None, (150, 150), (170, 150), (190, 150));
            SketchSymbol arrow = model.ShownDocument!.Sketch.Sheets.Single().Symbols[1];
            Assert.AreEqual(0f, arrow.Turn, 0.5f, "pointing towards +x");
            window.Close();
        });

        private static bool Vector2Close(System.Numerics.Vector2 place, double x, double z) => Math.Abs(place.X - x) < 1 && Math.Abs(place.Y - z) < 1;

        [TestMethod]
        public Task TheLabelToolOpensATextBoxOverTheViewEnterKeepsAndEscapeDrops() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            await Run(model, "sketch.setLabel", new JsonObject { ["colour"] = "green", ["size"] = "large" });

            Click(window, 120, 90);
            Dispatcher.UIThread.RunJobs();
            Control box = Find(window, "Label text");
            Assert.IsTrue(box.IsVisible, "the text box opens where the label goes");
            Point middle = box.TranslatePoint(new Point(box.Bounds.Width / 2, box.Bounds.Height / 2), window)!.Value;
            Point place = At(window, 120, 90);
            Assert.IsTrue(Math.Abs(middle.X - place.X) < 4 && Math.Abs(middle.Y - place.Y) < 4, $"centred on the click: {middle} against {place}");
            Assert.AreEqual(220, box.Bounds.Width, 1, "the width of its token");

            window.KeyTextInput("ramp");
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual("ramp", model.Sketch.LabelText);
            Press(window, Key.Enter, PhysicalKey.Enter);
            Assert.IsFalse(model.Sketch.IsWriting);
            SketchLabel label = model.ShownDocument!.Sketch.Sheets.Single().Labels.Single();
            Assert.AreEqual("ramp", label.Text);
            Assert.AreEqual(SketchColour.Green, label.Colour);
            JsonObject state = (JsonObject)(await automation.HandleAsync("state", new JsonObject { ["path"] = "sketch" }))["sketch"]!;
            Assert.AreEqual(1, state["drawn"]!["labels"]!.GetValue<int>(), "the top view drew the label");

            Click(window, 30, 30);
            window.KeyTextInput("never");
            Press(window, Key.Escape, PhysicalKey.Escape);
            Assert.IsFalse(model.Sketch.IsWriting);
            Assert.IsNull(UiTree.Find(window, "Label text"), "the box is gone");
            Assert.AreEqual(1, model.ShownDocument.Sketch.Sheets.Single().Labels.Length);
            window.Close();
        });

        [TestMethod]
        public Task TheSelectToolBoxesMovesAndDeletesWithTheMouseAndKeys() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, _) = await Open();
            await Run(model, "sketch.draw", new JsonObject { ["points"] = new JsonArray(new JsonArray(20, 20), new JsonArray(60, 20)), ["mirror"] = false });
            await Run(model, "sketch.symbol", new JsonObject { ["kind"] = "mass", ["x"] = 100, ["z"] = 100, ["mirror"] = false });
            model.SelectTool("sketch.select");
            window.MapView.Focus();
            Dispatcher.UIThread.RunJobs();
            SketchSelectTool tool = model.Tool<SketchSelectTool>();

            Drag(window, RawInputModifiers.None, (10, 10), (50, 40), (80, 60));
            Assert.AreEqual(1, tool.Selected.Length, "the box takes the stroke it crosses");
            Assert.AreEqual("1 stroke selected", tool.Summary);
            Assert.IsTrue(Find(window, "Delete the selected items of the sketch").IsEnabled);

            Drag(window, RawInputModifiers.None, (40, 20), (60, 40));
            Assert.AreEqual(new System.Numerics.Vector2(40, 40), model.ShownDocument!.Sketch.Sheets.Single().Strokes[0].Points[0], "the drag moved it by the mouse's offset");
            Assert.AreEqual(1, model.History.Count(step => step.Description == "Move in the sketch"), "one step for the whole drag");

            Click(window, 100, 100, RawInputModifiers.Shift);
            Assert.AreEqual(2, tool.Selected.Length, "Shift adds the symbol");

            Press(window, Key.Delete, PhysicalKey.Delete);
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
            Assert.IsTrue(model.ShownDocument.Sketch.Sheets.Single().IsBlank, "Delete removes what is selected");
            Assert.AreEqual(0, tool.Selected.Length);
            Assert.IsTrue(model.Undo());
            Assert.AreEqual(2, model.ShownDocument!.Sketch.Sheets.Single().ItemCount);
            window.Close();
        });

        [TestMethod]
        public Task AnAltDragWithTheEraserRemovesSymbolsAndLabelsToo() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, _) = await Open();
            await Run(model, "sketch.symbol", new JsonObject { ["kind"] = "mass", ["x"] = 100, ["z"] = 100, ["mirror"] = false });
            await Run(model, "sketch.label", new JsonObject { ["text"] = "ramp", ["x"] = 150, ["z"] = 150 });
            model.SelectTool("sketch.eraser");
            await Run(model, "sketch.setEraser", new JsonObject { ["radius"] = 6, ["mirror"] = false });

            Drag(window, RawInputModifiers.None, (80, 100), (120, 100));
            Assert.AreEqual(1, model.ShownDocument!.Sketch.Sheets.Single().Symbols.Length, "rubbing leaves the symbol");
            Drag(window, RawInputModifiers.Alt, (80, 100), (120, 100));
            Assert.AreEqual(0, model.ShownDocument.Sketch.Sheets.Single().Symbols.Length, "Alt takes it");
            Drag(window, RawInputModifiers.Alt, (140, 150), (160, 150));
            Assert.AreEqual(0, model.ShownDocument.Sketch.Sheets.Single().Labels.Length);
            window.Close();
        });


        [TestMethod]
        public Task MouseDragWithAPathGoesThroughEveryPlaceInOneCall() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            await Run(model, "sketch.setPencil", new JsonObject { ["mirror"] = false, ["colour"] = "blue" });
            Point a = window.MapView.MapToControl(40, 40);
            Point b = window.MapView.MapToControl(100, 40);
            Point c = window.MapView.MapToControl(100, 100);

            JsonNode answer = await automation.HandleAsync("mouse", new JsonObject
            {
                ["view"] = "top",
                ["action"] = "drag",
                ["path"] = new JsonArray(new JsonArray(a.X, a.Y), new JsonArray(b.X, b.Y), new JsonArray(c.X, c.Y)),
            });

            Assert.IsNotNull(answer["mouse"]);
            SketchStroke stroke = model.ShownDocument!.Sketch.Sheets.Single().Strokes.Single();
            Assert.IsTrue(stroke.Points.Length >= 3, "the corner is a place of the line");
            Assert.IsTrue(stroke.Points.Any(place => Math.Abs(place.X - 100) < 1.5 && Math.Abs(place.Y - 40) < 1.5), "it went through the corner");
            Assert.IsTrue(Vector2Close(stroke.Points[^1], 100, 100), "and ended at the last place");

            await Assert.ThrowsExceptionAsync<ArgumentException>(() => automation.HandleAsync("mouse", new JsonObject
            {
                ["view"] = "top",
                ["action"] = "drag",
                ["path"] = new JsonArray(new JsonArray(a.X, a.Y)),
            }));
            window.Close();
        });
        [TestMethod]
        public Task TheHandlesOfTheSelectAndImageToolsHaveAColourNoPencilHas() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, _) = await Open();
            await Run(model, "sketch.symbol", new JsonObject { ["kind"] = "mass", ["x"] = 100, ["z"] = 100, ["mirror"] = false });
            await Run(model, "sketch.select", new JsonObject { ["all"] = true });
            ToolView view = window.MapView.ToolViewOf(null);

            IReadOnlyList<ToolShape> outline = model.Tool<SketchSelectTool>().Shapes(view);
            Assert.IsTrue(outline.OfType<ToolLine>().Any(line => line.Ink == ToolInk.Handle), "the outline is in the handle colour");
            Assert.IsTrue(outline.OfType<ToolLine>().Any(line => line.Ink == ToolInk.Shadow), "over a dark rim");
            Assert.IsFalse(outline.OfType<ToolLine>().Any(line => line.Ink == ToolInk.Selection), "never the selection's yellow, which is the yellow pencil's");

            // the Image tool: a picture projected, its handles
            string picture = Path.Combine(AppContext.BaseDirectory, "marks-picture.png");
            using (WriteableBitmap bitmap = new WriteableBitmap(new PixelSize(8, 8), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul))
            {
                bitmap.Save(picture);
            }
            await Run(model, "images.project", new JsonObject { ["path"] = picture });
            model.SelectTool("sketch.image");
            Dispatcher.UIThread.RunJobs();
            IReadOnlyList<ToolShape> handles = model.Tool<ImageTool>().Shapes(view);
            Assert.IsTrue(handles.OfType<ToolSquare>().Any(square => square.Fill == ToolInk.Handle && square.Stroke == ToolInk.Shadow), "the corner squares");
            Assert.IsTrue(handles.OfType<ToolDisc>().Any(disc => disc.Fill == ToolInk.Handle), "the turning knob");
            Assert.IsFalse(handles.OfType<ToolLine>().Any(line => line.Ink == ToolInk.Selection) || handles.OfType<ToolSquare>().Any(square => square.Stroke == ToolInk.Selection));

            // the colour is a token of both themes, and none of the sketch's six
            foreach (Avalonia.Styling.ThemeVariant variant in new[] { Avalonia.Styling.ThemeVariant.Dark, Avalonia.Styling.ThemeVariant.Light })
            {
                Assert.IsTrue(Application.Current!.TryGetResource("MapSketchHandleBrush", variant, out object? brush) && brush is Avalonia.Media.ISolidColorBrush, variant.ToString());
                uint argb = ((Avalonia.Media.ISolidColorBrush)brush!).Color.ToUInt32();
                foreach (SketchColour colour in Enum.GetValues<SketchColour>())
                {
                    Assert.AreNotEqual(SketchLook.Argb(colour), argb, $"{variant}: not the sketch's {colour}");
                }
            }
            window.Close();
        });
    }
}
