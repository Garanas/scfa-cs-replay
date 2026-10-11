using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using FAForever.MapEditor.App.Automation;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The draft and final heightmaps in the window (user story T-9): the shortcut and the panel's
    /// buttons switch what both views show (the top view's picture and the height under the mouse
    /// in the top and 3D views), the panel's parts have labels.
    /// </summary>
    [TestClass]
    public sealed class DraftAndFinalWindowTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "draft-final-window");

        [ClassCleanup]
        public static void Cleanup()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private static async Task<JsonObject> Send(AutomationServer automation, string command, JsonObject? arguments = null)
        {
            Dispatcher.UIThread.RunJobs();
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            JsonObject answer = (JsonObject)await automation.HandleAsync(command, arguments ?? []);
            Dispatcher.UIThread.RunJobs();
            return answer;
        }

        /// <summary>
        /// The height the status bar reports under the mouse at a place of the map, in a view.
        /// </summary>
        private static async Task<float> HeightUnderMouse(MainWindow window, AutomationServer automation, string view, float x, float z)
        {
            JsonObject point = (JsonObject)(await Send(automation, "view-point", new JsonObject { ["x"] = x, ["z"] = z }))["point"]!;
            JsonObject pixel = (JsonObject)point[view == "3d" ? "3d" : "top"]!;
            await Send(automation, "mouse", new JsonObject { ["view"] = view, ["x"] = pixel["x"]!.GetValue<double>(), ["y"] = pixel["y"]!.GetValue<double>(), ["action"] = "move" });
            string text = window.FindControl<TextBlock>("CursorText")!.Text ?? "";
            int at = text.IndexOf("height ", StringComparison.Ordinal);
            Assert.IsTrue(at >= 0, $"the status bar says the height: '{text}'");
            return float.Parse(text[(at + 7)..], CultureInfo.InvariantCulture);
        }

        [TestMethod]
        public Task TheSwitchChangesBothViewsAndThePanelHasLabels() => HeadlessApp.Run(async () =>
        {
            MainWindow window = new MainWindow { Width = 1280, Height = 800 };
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers { SaveChoice = SaveChoice.Discard });
            await model.OpenAsync(HardFfa);
            window.DataContext = model;
            window.Show();
            Dispatcher.UIThread.RunJobs();
            AutomationServer automation = new AutomationServer(window, model, port: 0);
            string project = Path.Combine(Root, Guid.NewGuid().ToString("N"));
            await model.SaveAsAsync(project);
            string output = Path.Combine(project, MapProjectLayout.ProceduralTemp, "gaea.png");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            HeightmapFiles.Write(output, new Heightmap { Scale = 1f / 128, Samples = TiledBuffer<ushort>.Create(129, 129, (ushort)(20 * 128)) });
            await model.Commands.RunAsync("terrain.useAsFinalBase", new JsonObject { ["path"] = output }, interactive: false);
            model.SelectMode("terrain");
            model.View = MapViewTab.SideBySide;
            Dispatcher.UIThread.RunJobs();
            float draftHeight = model.Map!.Document.DraftHeightmap.HeightAt(100, 100);
            Assert.IsTrue(Math.Abs(draftHeight - 20) > 1, "the draft and the final differ there");

            // the final: 20 everywhere in both views
            Assert.AreEqual(20, await HeightUnderMouse(window, automation, "top", 100, 100), 0.05, "the top view");
            model.Camera.Look(x: 100, z: 100, distance: 200, yaw: 30, pitch: 60);
            Assert.AreEqual(20, await HeightUnderMouse(window, automation, "3d", 100, 100), 0.5, "the 3D view");
            byte[] final = HeadlessApp.Capture(window.MapView);

            // the shortcut shows the draft in both
            window.MapView.Focus();
            window.KeyPress(Key.D, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.D, "D");
            window.KeyRelease(Key.D, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.D, "D");
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(HeightStackKind.Draft, model.ShownHeights, "Ctrl+Shift+D switched");
            Assert.AreEqual(draftHeight, await HeightUnderMouse(window, automation, "top", 100, 100), 0.05, "the top view shows the draft");
            Assert.AreEqual(draftHeight, await HeightUnderMouse(window, automation, "3d", 100, 100), 0.5, "the 3D view shows the draft");
            byte[] draft = HeadlessApp.Capture(window.MapView);
            CollectionAssert.AreNotEqual(final, draft, "the top view's picture changed");
            HeadlessApp.SaveCapture(draft, "draft-final-top-draft");
            HeadlessApp.SaveCapture(final, "draft-final-top-final");

            // the panel's buttons, by their labels
            await Send(automation, "click", new JsonObject { ["label"] = "Show the final heightmap" });
            Assert.AreEqual(HeightStackKind.Final, model.ShownHeights);
            Assert.AreEqual(20, await HeightUnderMouse(window, automation, "top", 100, 100), 0.05);

            JsonArray ui = UiTree.Describe(window, hidden: false);
            string text = ui.ToJsonString();
            foreach (string label in new[] { "Show the draft heightmap", "Preview of the draft heightmap", "Preview of the final heightmap", "Show the height layers shared by the draft and the final",
                "Show the curves among the height layers", "Write the draft for procedural tools", "Use an output as the final's base", "Write the draft again whenever it changes" })
            {
                StringAssert.Contains(text, $"\"label\":\"{label.Replace("'", "\\" + "u0027", StringComparison.Ordinal)}\"", label);
            }
            JsonObject state = (JsonObject)(await Send(automation, "state", new JsonObject { ["path"] = "draftFinal.shown" }));
            StringAssert.Contains(state.ToJsonString(), "final");
            window.Close();
        });
    }
}
