using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Threading;
using FAForever.MapEditor.App.Automation;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// Automation's tools for checking a change: <c>set</c> a control by its label, <c>focus</c>
    /// on things, read a <c>grid</c>, <c>screenshot</c> one part.
    /// </summary>
    [TestClass]
    public sealed class AutomationToolingTest
    {
        private static async Task<(MainWindow Window, MainViewModel Model, AutomationServer Automation)> Open()
        {
            MainWindow window = new MainWindow { Width = 1280, Height = 800 };
            MainViewModel model = new MainViewModel(new WindowDialogs(window));
            await model.OpenAsync("assets/maps/HardFFA.v0001");
            model.Assets.Use(TestAssets.Catalog());
            window.DataContext = model;
            window.Show();
            Dispatcher.UIThread.RunJobs();
            return (window, model, new AutomationServer(window, model, port: 0));
        }

        private static async Task<JsonObject> Send(AutomationServer automation, string command, JsonObject? arguments = null)
        {
            Dispatcher.UIThread.RunJobs();
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            JsonObject answer = (JsonObject)await automation.HandleAsync(command, arguments ?? []);
            Dispatcher.UIThread.RunJobs();
            return answer;
        }

        [TestMethod]
        public Task SetChangesControlsByTheirLabelsAsAPersonWould() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            model.SelectTool("terrain.sculpt");
            SculptTool sculpt = model.Tool<SculptTool>();

            JsonObject strength = await Send(automation, "set", new JsonObject { ["label"] = "Height brush strength", ["value"] = 0.3 });
            Assert.AreEqual(0.3, sculpt.Strength, 1e-6, "a slider");
            Assert.IsTrue(strength["set"]!["revealed"]!.GetValue<bool>(), "at 1280 pixels the strength lies behind More, which set opens");

            await Send(automation, "set", new JsonObject { ["label"] = "Height brush mode", ["value"] = "flatten" });
            Assert.AreEqual(HeightBrushMode.Flatten, sculpt.Mode, "a combo box, by the item's text");

            await Send(automation, "set", new JsonObject { ["label"] = "Keep the heights within limits", ["value"] = true });
            Assert.IsTrue(sculpt.Limited, "a check box, clicked");
            JsonObject typed = await Send(automation, "set", new JsonObject { ["label"] = "Lowest height the brush makes", ["value"] = "12" });
            Assert.AreEqual(12, sculpt.MinHeight, 1e-6, $"a text field: typed, Enter and leaving it: {typed["set"]}");
            Assert.AreEqual("12", typed["set"]!["value"]!.GetValue<string>());

            await Send(automation, "set", new JsonObject { ["label"] = "Keep the heights within limits", ["value"] = false });
            Assert.IsFalse(sculpt.Limited);

            JsonObject wrong = (JsonObject)await automation.HandleAsync("set", new JsonObject { ["label"] = "Height brush mode", ["value"] = "dig" })
                .ContinueWith(task => task.IsFaulted ? new JsonObject { ["error"] = task.Exception!.GetBaseException().Message } : task.Result);
            StringAssert.Contains(wrong["error"]!.GetValue<string>(), "Raise", "a wrong item lists the items");
            window.Close();
        });

        [TestMethod]
        public Task GridReadsAnyGridOfTheMap() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            MapDocument document = model.ShownDocument!;

            JsonObject height = (JsonObject)(await Send(automation, "grid", new JsonObject { ["target"] = "heightmap", ["x"] = 100, ["z"] = 60 }))["grid"]!;
            Assert.AreEqual(document.Terrain.Heightmap.GetHeight(100, 60), height["value"]!.GetValue<double>(), 1e-3);

            JsonObject stratum = (JsonObject)(await Send(automation, "grid", new JsonObject { ["target"] = "stratum:1", ["x"] = 100, ["z"] = 60 }))["grid"]!;
            JsonObject mask = await Send(automation, "mask", new JsonObject { ["layer"] = 1, ["x"] = 100, ["z"] = 60 });
            Assert.AreEqual(double.Parse(mask["value"]!.ToJsonString()), stratum["value"]!.GetValue<double>(), "the same as mask");

            JsonObject region = (JsonObject)(await Send(automation, "grid", new JsonObject { ["target"] = "types", ["x0"] = 0, ["z0"] = 0, ["x1"] = 256, ["z1"] = 256, ["step"] = 32 }))["grid"]!;
            Assert.AreEqual(9, ((JsonArray)region["rows"]!).Count, "every 32 ogrids from 0 to 256");
            Assert.IsNotNull(region["mean"]);

            JsonObject water = (JsonObject)(await Send(automation, "grid", new JsonObject { ["target"] = "water:foam", ["x"] = 10, ["z"] = 10 }))["grid"]!;
            Assert.IsNotNull(water["value"]);
            window.Close();
        });

        [TestMethod]
        public Task FocusFramesTheSelectionInBothViews() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            model.View = MapViewTab.Top;
            Marker spawn = model.ShownDocument!.Markers.Spawns[0];
            model.Selection.Set([ObjectRef.Marker(spawn.Id)]);

            JsonObject answer = await Send(automation, "focus", new JsonObject { ["selection"] = true });

            Assert.IsTrue(answer["ran"]!.GetValue<bool>());
            Point centre = window.MapView.Centre;
            Assert.AreEqual(spawn.Position.X, centre.X, 0.5, "the top view centres on it");
            Assert.AreEqual(spawn.Position.Z, centre.Y, 0.5);
            Assert.IsTrue(window.MapView.Zoom > 5, "close enough to see it and its surroundings");
            model.Camera.Update(10);
            Assert.AreEqual(spawn.Position.X, model.Camera.Now.X, 0.5, "the 3D camera goes there");

            Marker other = model.ShownDocument.Markers.Spawns[1];
            await Send(automation, "focus", new JsonObject { ["refs"] = new JsonArray(ObjectRef.Marker(spawn.Id).ToString(), ObjectRef.Marker(other.Id).ToString()) });
            Assert.AreEqual((spawn.Position.X + other.Position.X) / 2, window.MapView.Centre.X, 0.5, "two things: between them");

            await Send(automation, "focus", new JsonObject { ["x"] = 50, ["z"] = 50 });
            Assert.AreEqual(50, model.Camera.Now.X, 0.5, "with a place, the camera's focus as before");
            window.Close();
        });

        [TestMethod]
        public Task AScreenshotOfOnePart() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            model.SelectTool("terrain.sculpt");
            string folder = Path.Combine(Path.GetTempPath(), "faf-map-editor-tests", Guid.NewGuid().ToString("N"));
            try
            {
                foreach (string part in new[] { "options", "inspector", "panel", "dock", "rail", "top" })
                {
                    string file = (await Send(automation, "screenshot", new JsonObject { ["path"] = Path.Combine(folder, $"{part}.png"), ["part"] = part }))["file"]!.GetValue<string>();
                    using Avalonia.Media.Imaging.Bitmap picture = new Avalonia.Media.Imaging.Bitmap(file);
                    Assert.IsTrue(picture.PixelSize.Width > 20 && picture.PixelSize.Width < 1280, $"{part}: a part, not the window ({picture.PixelSize})");
                }
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
            window.Close();
        });

        [TestMethod]
        public Task TheDriverPaintsAStratumOnlyOnTheSlopeOfTheSlopeMap() => HeadlessApp.Run(async () =>
        {
            MainWindow window = new MainWindow { Width = 1280, Height = 800 };
            MainViewModel model = new MainViewModel(new WindowDialogs(window));
            await model.OpenAsync(TestMaps.SlopeProject(degrees: 30));
            window.DataContext = model;
            window.Show();
            EditorDriver editor = EditorDriver.InProcess(new AutomationServer(window, model, port: 0));

            await editor.Send("brush", new JsonObject { ["layer"] = 1, ["radius"] = 128, ["strength"] = 1 });
            await editor.Set("Paint only within a range of slopes", true);
            await editor.Set("Gentlest slope to paint, in degrees", "20");
            await editor.Set("Steepest slope to paint, in degrees", "40");
            await editor.Send("stroke", new JsonObject { ["points"] = new JsonArray(new JsonArray(128f, 64f), new JsonArray(128f, 192f)) });

            Assert.IsTrue(await editor.Grid("stratum:1", 128, 128) > 0, "the ramp (30 degrees) is painted");
            Assert.AreEqual(0, await editor.Grid("stratum:1", 40, 128), "the flat ground is not");
            Assert.AreEqual(0, await editor.Grid("stratum:1", 220, 128));
            window.Close();
        });
    }
}
