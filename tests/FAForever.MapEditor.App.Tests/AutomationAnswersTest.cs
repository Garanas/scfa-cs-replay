using System.Text.Json.Nodes;
using Avalonia.Threading;
using FAForever.MapEditor.App.Automation;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// What automation answers: a short state by default, the whole or parts of it when asked,
    /// <c>ui</c> filtered, <c>perf</c> saved and compared, a refusing command as <c>ran: false</c>,
    /// a question a command asks, <c>focus</c> at an angle, a picture of the question.
    /// </summary>
    [TestClass]
    public sealed class AutomationAnswersTest
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
        public Task ACommandAnswersAShortStateUnlessAskedForMoreOrLess() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();

            JsonObject tool = await Send(automation, "tool", new JsonObject { ["id"] = "terrain.sculpt" });
            Assert.AreEqual("terrain.sculpt", tool["tool"]!.GetValue<string>());
            Assert.AreEqual(0, tool["selection"]!["count"]!.GetValue<int>());
            Assert.IsTrue(tool.ContainsKey("dialog") && tool.ContainsKey("undo") && tool.ContainsKey("status"));
            Assert.IsFalse(tool.ContainsKey("gpu") || tool.ContainsKey("views") || tool.ContainsKey("problems"), "the short state leaves the rest out");

            JsonObject full = await Send(automation, "tool", new JsonObject { ["id"] = "terrain.sculpt", ["full"] = true });
            Assert.IsTrue(full.ContainsKey("views") && full.ContainsKey("gpu"), "full: the whole state");

            JsonObject quiet = await Send(automation, "tool", new JsonObject { ["id"] = "terrain.sculpt", ["quiet"] = true });
            Assert.IsFalse(quiet.ContainsKey("title"), "quiet: no state");
            Assert.IsTrue(quiet["ran"]!.GetValue<bool>() && quiet["ok"]!.GetValue<bool>(), "but its own answer");

            JsonObject state = await Send(automation, "state");
            Assert.IsTrue(state.ContainsKey("views") && state.ContainsKey("problems"), "state is the whole state");

            model.Selection.Set([ObjectRef.Marker(model.ShownDocument!.Markers.Spawns[0].Id)]);
            JsonObject one = await Send(automation, "state", new JsonObject { ["path"] = "selection.count" });
            Assert.AreEqual(1, one["selection.count"]!.GetValue<int>());
            Assert.IsFalse(one.ContainsKey("title"), "only the value asked for");

            JsonObject some = await Send(automation, "tab", new JsonObject { ["name"] = "top", ["paths"] = new JsonArray("tab", "views.top.visible", "no.such.thing") });
            Assert.AreEqual("top", some["tab"]!.GetValue<string>());
            Assert.IsTrue(some["views.top.visible"]!.GetValue<bool>());
            Assert.IsTrue(some.ContainsKey("no.such.thing") && some["no.such.thing"] is null, "a path that leads nowhere is null");
            window.Close();
        });

        [TestMethod]
        public void APathFindsKeysThatHoldDots()
        {
            JsonObject snapshot = new JsonObject { ["spans"] = new JsonObject { ["3d.frame"] = new JsonObject { ["meanMs"] = 4.5 }, ["3d"] = new JsonObject { ["x"] = 1 } } };
            Assert.AreEqual(4.5, AutomationServer.At(snapshot, "spans.3d.frame.meanMs")!.GetValue<double>(), "the longest key first");
            Assert.AreEqual(1, AutomationServer.At(snapshot, "spans.3d.x")!.GetValue<int>());
            Assert.IsNull(AutomationServer.At(snapshot, "spans.3d.frame.p95Ms"));
            Assert.AreEqual(2, AutomationServer.At(new JsonObject { ["list"] = new JsonArray(1, 2) }, "list.1")!.GetValue<int>());
        }

        [TestMethod]
        public Task UiFindsControlsByTypeAndText() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            model.SelectTool("terrain.sculpt");

            JsonArray buttons = (JsonArray)(await Send(automation, "ui", new JsonObject { ["type"] = "Button" }))["ui"]!;
            Assert.IsTrue(buttons.Count > 5);
            Assert.IsTrue(buttons.All(node => node!["type"]!.GetValue<string>() == "Button" && node["children"] is null), "a flat list of buttons");

            JsonArray radius = (JsonArray)(await Send(automation, "ui", new JsonObject { ["text"] = "brush radius" }))["ui"]!;
            Assert.IsTrue(radius.Any(node => node!["label"]?.GetValue<string>() == "Brush radius in ogrids"), radius.ToJsonString());

            JsonArray sliders = (JsonArray)(await Send(automation, "ui", new JsonObject { ["type"] = "slider", ["text"] = "radius" }))["ui"]!;
            Assert.IsTrue(sliders.Count >= 1 && sliders.All(node => node!["type"]!.GetValue<string>() == "Slider"), "both, ignoring case");
            window.Close();
        });

        [TestMethod]
        public void PerfComparesTwoSnapshotsTheSpanThatGrewMostFirst()
        {
            static JsonObject Span(double mean, double total, long count) => new JsonObject { ["meanMs"] = mean, ["totalMs"] = total, ["count"] = count, ["p95Ms"] = mean * 2 };
            JsonObject before = new JsonObject { ["spans"] = new JsonObject { ["3d.frame"] = Span(4, 400, 100), ["top.draw"] = Span(2, 20, 10), ["gone"] = Span(1, 1, 1) } };
            JsonObject after = new JsonObject { ["spans"] = new JsonObject { ["3d.frame"] = Span(9, 900, 100), ["top.draw"] = Span(1.5, 15, 10), ["new"] = Span(3, 3, 1) } };

            JsonArray compared = AutomationServer.ComparePerformance(before, after);

            CollectionAssert.AreEqual(new[] { "3d.frame", "new", "top.draw", "gone" }, compared.Select(span => span!["span"]!.GetValue<string>()).ToArray());
            Assert.AreEqual(5, compared[0]!["meanChangeMs"]!.GetValue<double>());
            Assert.IsNull(compared[1]!["before"], "a span that is new has no before");
            Assert.AreEqual(-1, compared[3]!["meanChangeMs"]!.GetValue<double>());
        }

        [TestMethod]
        public Task PerfSavesItsSpansAndComparesWithThem() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            string file = Path.Combine(Path.GetTempPath(), "faf-map-editor-tests", Guid.NewGuid().ToString("N"), "before.json");
            try
            {
                using (FAForever.MapEditor.App.Diagnostics.Performance.Measure("test.span"))
                {
                }
                JsonObject saved = await Send(automation, "perf", new JsonObject { ["save"] = file });
                Assert.AreEqual(Path.GetFullPath(file), saved["saved"]!.GetValue<string>());
                Assert.IsTrue(File.Exists(file));

                JsonObject only = await Send(automation, "perf", new JsonObject { ["spans"] = "test." });
                Assert.IsTrue(((JsonObject)only["spans"]!).All(span => span.Key.Contains("test.")), "spans: only those whose name holds it");
                JsonObject mean = await Send(automation, "perf", new JsonObject { ["path"] = "spans.test.span.count" });
                Assert.IsTrue(mean["spans.test.span.count"]!.GetValue<long>() >= 1, "a path through a name with dots");

                JsonObject compared = await Send(automation, "perf", new JsonObject { ["before"] = file });
                Assert.AreEqual("now", compared["after"]!.GetValue<string>());
                Assert.IsTrue(((JsonArray)compared["compare"]!).Any(span => span!["span"]!.GetValue<string>() == "test.span"));
            }
            finally
            {
                Directory.Delete(Path.GetDirectoryName(file)!, recursive: true);
            }
            window.Close();
        });

        [TestMethod]
        public Task ACommandThatRefusesAnswersRanFalseWithWhy() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();

            JsonObject refused = await Send(automation, "run", new JsonObject { ["id"] = "decals.forward" });
            Assert.IsFalse(refused["ran"]!.GetValue<bool>(), "nothing selected: the handler refuses");
            Assert.IsFalse(string.IsNullOrEmpty(refused["why"]?.GetValue<string>()));
            Assert.IsTrue(refused["ok"]!.GetValue<bool>(), "an answer, not an error");

            JsonObject wrong = await Send(automation, "map.information", new JsonObject { ["type"] = "moon" });
            Assert.IsFalse(wrong["ran"]!.GetValue<bool>(), "a value the command does not take");
            StringAssert.Contains(wrong["why"]!.GetValue<string>(), "moon");

            await Assert.ThrowsExceptionAsync<ArgumentException>(() => automation.HandleAsync("run", new JsonObject { ["id"] = "no.such.command" }), "a command that does not exist stays an error");
            EditorDriver driver = EditorDriver.InProcess(automation);
            InvalidOperationException thrown = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => driver.Run("decals.forward"));
            StringAssert.Contains(thrown.Message, "did not run", "the driver throws with why");
            window.Close();
        });

        [TestMethod]
        public Task RunAnswersTheQuestionACommandAsksAndAPictureShowsIt() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            model.EditMap("A change", document => document with { Scenario = document.Scenario with { Description = "changed" } });

            JsonObject asking = await Send(automation, "run", new JsonObject { ["id"] = "file.open", ["interactive"] = true });

            Assert.IsFalse(asking["finished"]!.GetValue<bool>(), "it waits for the answer, so run answers at once");
            Assert.AreEqual("Unsaved changes", asking["asked"]!["title"]!.GetValue<string>());
            Assert.AreEqual("Unsaved changes", asking["dialog"]!["title"]!.GetValue<string>(), "the short state has the question too");

            string file = Path.Combine(Path.GetTempPath(), "faf-map-editor-tests", Guid.NewGuid().ToString("N"), "dialog.png");
            try
            {
                string shot = (await Send(automation, "screenshot", new JsonObject { ["path"] = file, ["part"] = "dialog" }))["file"]!.GetValue<string>();
                using Avalonia.Media.Imaging.Bitmap picture = new Avalonia.Media.Imaging.Bitmap(shot);
                Assert.IsTrue(picture.PixelSize.Width > 100 && picture.PixelSize.Width < 1000, $"the question's card, not the window ({picture.PixelSize})");
            }
            finally
            {
                Directory.Delete(Path.GetDirectoryName(file)!, recursive: true);
            }

            JsonObject answered = await Send(automation, "dialog", new JsonObject { ["button"] = "Cancel" });
            Assert.IsNull(answered["dialog"]);
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => automation.HandleAsync("screenshot", new JsonObject { ["path"] = file, ["part"] = "dialog" }), "no question, no picture");
            window.Close();
        });

        [TestMethod]
        public Task FocusShowsThingsAtAnAngleAtOnce() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            Marker spawn = model.ShownDocument!.Markers.Spawns[0];

            await Send(automation, "focus", new JsonObject { ["ref"] = ObjectRef.Marker(spawn.Id).ToString() });

            Assert.AreEqual(50, model.Camera.Now.Pitch, 0.01, "oblique, not straight down");
            Assert.AreEqual(spawn.Position.X, model.Camera.Now.X, 0.5, "there at once, without gliding");
            Assert.IsTrue(model.Camera.Now.Distance > MainViewModel.FrameMinimumRadius * 2, $"far enough to see around it ({model.Camera.Now.Distance})");

            await Send(automation, "focus", new JsonObject { ["ref"] = ObjectRef.Marker(spawn.Id).ToString(), ["pitch"] = 90 });
            Assert.AreEqual(90, model.Camera.Now.Pitch, 0.01, "pitch: 90 looks straight down");
            window.Close();
        });
    }
}
