using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FAForever.MapEditor.App.Automation;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using ImageMagick;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The projected images in the real window, headless (user story N-2): the top view's pixels, the
    /// mouse and the key through automation, the panel's list and the inspector's fields, every
    /// control labelled.
    /// </summary>
    [TestClass]
    public sealed class ProjectedImagesWindowTest
    {
        private static async Task<(MainWindow Window, MainViewModel Model, AutomationServer Automation)> Open()
        {
            MainWindow window = new MainWindow { Width = 1280, Height = 800 };
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            await model.OpenAsync("assets/maps/HardFFA.v0001");
            window.DataContext = model;
            window.Show();
            Dispatcher.UIThread.RunJobs();
            AutomationServer automation = new AutomationServer(window, model, port: 0);
            Assert.IsTrue((await model.Commands.RunAsync("sketch.mode", null, interactive: false)).Ran);
            Assert.IsTrue((await model.Commands.RunAsync("view.top", null, interactive: false)).Ran);
            Settle();
            return (window, model, automation);
        }

        private static void Settle()
        {
            Dispatcher.UIThread.RunJobs();
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }

        private static async Task<JsonObject> Send(AutomationServer automation, string command, JsonObject? arguments = null)
        {
            Settle();
            JsonObject answer = (JsonObject)await automation.HandleAsync(command, arguments ?? []);
            for (int i = 0; i < 5; i++)
            {
                await Task.Delay(10);
                Dispatcher.UIThread.RunJobs();
            }
            return answer;
        }

        /// <summary>
        /// Where the chosen image's handles are in the top view's pixels (<c>state.images.points</c>).
        /// </summary>
        private static async Task<JsonObject> Points(AutomationServer automation) =>
            (JsonObject)(await Send(automation, "state", new JsonObject { ["path"] = "images.points" }))["images.points"]!;

        private static Task Run(MainViewModel model, string id, JsonObject? arguments = null) => model.Commands.RunAsync(id, arguments ?? [], interactive: false);

        /// <summary>
        /// The top view's colour at a place of the map, as drawn now.
        /// </summary>
        private static IMagickColor<ushort> PixelAt(MainWindow window, double x, double z)
        {
            Settle();
            Point at = window.MapView.MapToControl(x, z);
            using MagickImage picture = new MagickImage(HeadlessApp.Capture(window.MapView));
            using IPixelCollection<ushort> pixels = picture.GetPixels();
            IPixel<ushort> pixel = pixels.GetPixel((int)Math.Round(at.X), (int)Math.Round(at.Y));
            return pixel.ToColor()!;
        }

        private static (int Red, int Green, int Blue) Rgb(IMagickColor<ushort> color) => (color.R >> 8, color.G >> 8, color.B >> 8);

        private static bool IsRed(IMagickColor<ushort> color) => Rgb(color) is (> 200, < 60, < 60);

        private static bool IsBlue(IMagickColor<ushort> color) => Rgb(color) is (< 60, < 60, > 200);

        private static Control? InInspector(MainWindow window, string label) => window.GetVisualDescendants().OfType<Inspector>().Single()
            .GetVisualDescendants().OfType<Control>().FirstOrDefault(control => Avalonia.Automation.AutomationProperties.GetName(control) == label && control.IsEffectivelyVisible);

        private static string[] Unlabelled(MainWindow window)
        {
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
            Check(UiTree.Describe(window, hidden: false));
            return [.. unlabelled];
        }

        [TestMethod]
        public Task TheTopViewDrawsTheImageAtItsPlaceTurnedAndAtItsOpacity() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            await Run(model, "images.project", new JsonObject { ["path"] = ProjectedImagesTest.Picture(200, 100) });
            Settle();

            // fitted to the 256 by 256 map: 256 by 128 in the middle, red on the left, blue on the right
            Assert.IsTrue(IsRed(PixelAt(window, 64, 128)), $"{Rgb(PixelAt(window, 64, 128))}");
            Assert.IsTrue(IsBlue(PixelAt(window, 192, 128)), $"{Rgb(PixelAt(window, 192, 128))}");
            Assert.AreEqual(1, window.MapView.ImagesDrawn);
            Assert.IsFalse(IsRed(PixelAt(window, 64, 20)) || IsBlue(PixelAt(window, 64, 20)), "above the picture is the terrain");

            // turned half a turn: the sides swap
            await Run(model, "images.set", new JsonObject { ["turn"] = 180 });
            Assert.IsTrue(IsBlue(PixelAt(window, 64, 128)));
            Assert.IsTrue(IsRed(PixelAt(window, 192, 128)));
            // a quarter turn: red above the middle, blue below
            await Run(model, "images.set", new JsonObject { ["turn"] = 90, ["width"] = 100 });
            Assert.IsTrue(IsRed(PixelAt(window, 120, 90)), $"the left half turned clockwise is the top half: {Rgb(PixelAt(window, 120, 90))} {model.ShownDocument!.Images[0]}");
            Assert.IsTrue(IsBlue(PixelAt(window, 120, 166)));
            await Run(model, "images.set", new JsonObject { ["turn"] = 0, ["width"] = 256 });

            // half transparent: the terrain shows through
            await Run(model, "images.set", new JsonObject { ["opacity"] = 50 });
            (int red, int green, int blue) = Rgb(PixelAt(window, 64, 128));
            Assert.IsTrue(red is > 100 and < 250 && (green > 20 || blue > 20), $"red mixed with the terrain: {red} {green} {blue}");

            // without colour
            await Run(model, "images.set", new JsonObject { ["opacity"] = 100, ["greyscale"] = true });
            (red, green, blue) = Rgb(PixelAt(window, 64, 128));
            Assert.IsTrue(Math.Abs(red - green) < 8 && Math.Abs(green - blue) < 8, $"grey: {red} {green} {blue}");
            Assert.IsFalse(IsRed(PixelAt(window, 64, 128)));
            await Run(model, "images.set", new JsonObject { ["greyscale"] = false });
            Assert.IsTrue(IsRed(PixelAt(window, 64, 128)));

            // hidden: the eye, then the overlay
            await Run(model, "images.set", new JsonObject { ["shown"] = false });
            Assert.IsFalse(IsRed(PixelAt(window, 64, 128)));
            Assert.AreEqual(0, window.MapView.ImagesDrawn);
            await Run(model, "images.set", new JsonObject { ["shown"] = true });
            Assert.IsTrue(IsRed(PixelAt(window, 64, 128)));
            await Run(model, "view.overlay.images", new JsonObject { ["on"] = false });
            Assert.IsFalse(IsRed(PixelAt(window, 64, 128)), "the overlay hides every image");
            JsonObject state = (JsonObject)(await Send(automation, "state", new JsonObject { ["path"] = "images" }))["images"]!;
            Assert.IsFalse(state["shown"]!.GetValue<bool>());
            Assert.AreEqual(0, state["drawnTop"]!.GetValue<int>());
            window.Close();
        });

        [TestMethod]
        public Task AnImageUnderTheSketchAndOneOverItAreBothDrawn() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer _) = await Open();
            string file = ProjectedImagesTest.Picture(200, 100);
            await Run(model, "images.project", new JsonObject { ["path"] = file });
            await Run(model, "images.project", new JsonObject { ["path"] = file });
            await Run(model, "images.set", new JsonObject { ["name"] = "Draft", ["aboveSketch"] = true, ["turn"] = 180 });
            Settle();

            Assert.AreEqual(2, window.MapView.ImagesDrawn);
            Assert.IsTrue(IsBlue(PixelAt(window, 64, 128)), "the turned one lies over the other, whatever the list's order");
            await Run(model, "images.set", new JsonObject { ["name"] = "Draft", ["aboveSketch"] = false });
            Assert.IsTrue(IsRed(PixelAt(window, 64, 128)), "both under the sketch, the later one over the earlier");
            window.Close();
        });

        [TestMethod]
        public Task TheMouseThroughTheWindowMovesScalesAndTurnsTheImage() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            await Run(model, "images.project", new JsonObject { ["path"] = ProjectedImagesTest.Picture(100, 100) });
            Settle();
            await Run(model, "images.set", new JsonObject { ["width"] = 100 });

            JsonArray Of(JsonObject points, string name) => points[name]!.AsArray();
            JsonObject points = await Points(automation);
            double x = Of(points, "centre")[0]!.GetValue<double>();
            double y = Of(points, "centre")[1]!.GetValue<double>();

            await Send(automation, "mouse", new JsonObject { ["view"] = "top", ["x"] = x, ["y"] = y, ["action"] = "drag", ["to"] = new JsonArray(x + 40, y + 20) });
            ProjectedImage moved = model.ShownDocument!.FindImage("Draft")!;
            double zoom = window.MapView.Zoom;
            Assert.AreEqual(128 + 40 / zoom, moved.CentreX, 0.1);
            Assert.AreEqual(128 + 20 / zoom, moved.CentreZ, 0.1);
            Assert.AreEqual("Move the image Draft", model.Map!.Session.History.Last().Description);

            points = await Points(automation);
            double cornerX = Of(points, "bottomRight")[0]!.GetValue<double>();
            double cornerY = Of(points, "bottomRight")[1]!.GetValue<double>();
            await Send(automation, "mouse", new JsonObject { ["view"] = "top", ["x"] = cornerX, ["y"] = cornerY, ["action"] = "drag", ["to"] = new JsonArray(cornerX + 30, cornerY + 30) });
            ProjectedImage scaled = model.ShownDocument!.FindImage("Draft")!;
            Assert.IsTrue(scaled.Width > moved.Width, "dragging a corner outwards scales it");
            Assert.AreEqual(scaled.Width, scaled.Height, 0.1, "and keeps the picture's proportions");

            points = await Points(automation);
            double knobX = Of(points, "knob")[0]!.GetValue<double>();
            double knobY = Of(points, "knob")[1]!.GetValue<double>();
            double centreX = Of(points, "centre")[0]!.GetValue<double>();
            double centreY = Of(points, "centre")[1]!.GetValue<double>();
            await Send(automation, "mouse", new JsonObject
            {
                ["view"] = "top", ["x"] = knobX, ["y"] = knobY, ["action"] = "drag", ["to"] = new JsonArray(centreX + (centreY - knobY), centreY), ["modifiers"] = "Shift",
            });
            Assert.AreEqual(90.0, model.ShownDocument!.FindImage("Draft")!.Turn, "a quarter turn clockwise, in steps of 15");

            // every drag one step: project, set the width, move, scale, turn
            Assert.AreEqual(5, model.Map.Session.History.Count);
            window.Close();
        });

        [TestMethod]
        public Task TheKeyShowsAndHidesTheImages() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            await Run(model, "images.project", new JsonObject { ["path"] = ProjectedImagesTest.Picture() });
            Assert.IsTrue(model.ShowImages);

            await Send(automation, "key", new JsonObject { ["key"] = "Ctrl+Shift+J" });
            Assert.IsFalse(model.ShowImages);
            await Send(automation, "key", new JsonObject { ["key"] = "Ctrl+Shift+J" });
            Assert.IsTrue(model.ShowImages);
            window.Close();
        });

        [TestMethod]
        public Task ThePanelListsTheImagesAndTheInspectorEditsTheChosenOne() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            ModePanel panel = window.GetVisualDescendants().OfType<ModePanel>().Single();
            Assert.IsNull(InInspector(window, "Chosen image"), "nothing is chosen: the inspector shows no image");
            Assert.IsTrue(panel.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text?.StartsWith("None yet") == true && text.IsEffectivelyVisible));

            string file = ProjectedImagesTest.Picture(200, 100);
            await Run(model, "images.project", new JsonObject { ["path"] = file });
            await Run(model, "images.project", new JsonObject { ["path"] = file });
            Settle();
            ListBox list = panel.GetVisualDescendants().OfType<ListBox>().Single(box => Avalonia.Automation.AutomationProperties.GetName(box) == "Projected images");
            Assert.AreEqual(2, list.ItemCount);
            Assert.AreEqual("Draft 2", ((ProjectedImageItem)list.SelectedItem!).Name);
            Assert.AreEqual("Image: Draft 2", ((TextBlock)InInspector(window, "Chosen image")!).Text);
            StringAssert.Contains(((TextBlock)InInspector(window, "The chosen image's picture")!).Text, "200 × 100 pixels");
            CollectionAssert.AreEqual(Array.Empty<string>(), Unlabelled(window));

            // the eye of an entry, by its label
            await Send(automation, "click", new JsonObject { ["label"] = "Show the image Draft" });
            Assert.IsFalse(model.ShownDocument!.FindImage("Draft")!.Shown);
            // the list chooses
            await Send(automation, "click", new JsonObject { ["label"] = "Projected images", ["type"] = "ListBox" });
            model.Images.ChosenItem = model.Images.Items[0];
            Settle();
            Assert.AreEqual("Image: Draft", ((TextBlock)InInspector(window, "Chosen image")!).Text);

            // a field of the inspector
            await Send(automation, "set", new JsonObject { ["label"] = "Opacity", ["value"] = "30" });
            Assert.AreEqual(0.3, model.ShownDocument.FindImage("Draft")!.Opacity, 1e-9);
            await Send(automation, "set", new JsonObject { ["label"] = "Greyscale", ["value"] = true });
            Assert.IsTrue(model.ShownDocument.FindImage("Draft")!.Greyscale);
            await Send(automation, "click", new JsonObject { ["label"] = "Fit the image to the map" });
            Assert.AreEqual(256.0, model.ShownDocument.FindImage("Draft")!.Width);
            await Send(automation, "click", new JsonObject { ["label"] = "Remove the chosen image" });
            Assert.IsNull(model.ShownDocument.FindImage("Draft"));
            Assert.IsNull(InInspector(window, "Chosen image"), "the removed image leaves nothing to edit");
            CollectionAssert.AreEqual(Array.Empty<string>(), Unlabelled(window));
            window.Close();
        });

        [TestMethod]
        public Task TheToolRowAndTheViewMenuOfferWhatTheImagesNeed() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            await Run(model, "images.project", new JsonObject { ["path"] = ProjectedImagesTest.Picture() });
            Settle();

            ToolOptions options = window.GetVisualDescendants().OfType<ToolOptions>().Single();
            Assert.AreEqual("Image", model.ActiveTool!.Definition.Title);
            Assert.IsTrue(options.GetVisualDescendants().OfType<Button>().Any(button => button.IsEffectivelyVisible && Avalonia.Automation.AutomationProperties.GetName(button) == "Project an image from the tool's row"));
            Assert.IsTrue(options.GetVisualDescendants().OfType<CheckBox>().Any(box => Avalonia.Automation.AutomationProperties.GetName(box) == "Keep the chosen image's proportions"));
            Assert.IsNotNull(model.Overlays.SingleOrDefault(overlay => overlay.Id == "images"));

            JsonObject state = (JsonObject)(await Send(automation, "state", new JsonObject { ["path"] = "images" }))["images"]!;
            Assert.AreEqual(1, state["count"]!.GetValue<int>());
            Assert.IsNotNull(state["points"]!["knob"]);
            Assert.AreEqual(1, state["drawnTop"]!.GetValue<int>());
            window.Close();
        });
    }
}
