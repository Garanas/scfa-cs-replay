using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FAForever.MapEditor.App.Automation;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// Automation's <c>click</c> and <c>set</c> aim at the control: shown behind More or scrolled
    /// into view, clicked once the mouse reaches it there, refused with why otherwise (never a click
    /// that lands elsewhere, issue 5 of the wave 10 walkthrough).
    /// </summary>
    [TestClass]
    public sealed class AutomationAimingTest
    {
        private static async Task<(MainWindow Window, MainViewModel Model, AutomationServer Automation)> Open(string? folder = null)
        {
            MainWindow window = new MainWindow { Width = 1280, Height = 800 };
            MainViewModel model = new MainViewModel(new WindowDialogs(window));
            await model.OpenAsync(folder ?? "assets/maps/HardFFA.v0001");
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
        public Task AChipBehindTheViewsMoreIsClickedNotTheMapUnderIt() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            model.SelectMode("paint");
            Dispatcher.UIThread.RunJobs();
            bool grid = model.Overlays.Single(overlay => overlay.Id == "grid").IsOn;
            int undo = model.History.Count;

            JsonObject answer = await Send(automation, "click", new JsonObject { ["label"] = "Show Grid" });

            Assert.AreNotEqual(grid, model.Overlays.Single(overlay => overlay.Id == "grid").IsOn, $"the chip was clicked: {answer}");
            Assert.AreEqual(undo, model.History.Count, "no stroke was painted on the map under the chip");
            Assert.IsTrue(answer["clicked"]!["finished"]!.GetValue<bool>());
            window.Close();
        });

        [TestMethod]
        public Task ClickChoosesAmongControlsOfOneLabelByTypeAndIndexAndSaysTheOthers() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            model.SelectMode("procedural");
            Dispatcher.UIThread.RunJobs();

            // the Connected regions tool's tile and the list's entry share the label
            JsonObject first = (JsonObject)(await Send(automation, "click", new JsonObject { ["label"] = "Connected regions", ["type"] = "Button" }))["clicked"]!;
            Assert.IsTrue(first["matches"]!.GetValue<int>() >= 2, $"{first}");
            Assert.IsTrue(first["others"]!.AsArray().Count >= 1);
            await Send(automation, "click", new JsonObject { ["label"] = "Walkability", ["type"] = "Button" });
            Assert.AreEqual("procedural.walkability", model.ProceduralList.Parts.Chosen.Id);
            await Send(automation, "click", new JsonObject { ["label"] = "Connected regions", ["type"] = "Button", ["index"] = 1 });
            Assert.AreEqual("procedural.navigationMesh", model.ProceduralList.Parts.Chosen.Id);
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Send(automation, "click", new JsonObject { ["label"] = "Connected regions", ["index"] = 9 }));

            // a check box answers its state after the click
            JsonObject toggled = (JsonObject)(await Send(automation, "click", new JsonObject { ["label"] = "Regenerate results on their own" }))["clicked"]!;
            Assert.IsFalse(toggled["checked"]!.GetValue<bool>(), $"{toggled}");
            Assert.IsFalse(model.RegenerateOnTheirOwn);

            // an open list shows in the state, which screenshots do not
            ComboBox combo = window.GetVisualDescendants().OfType<ComboBox>().First(box => box.IsEffectivelyVisible);
            combo.IsDropDownOpen = true;
            Dispatcher.UIThread.RunJobs();
            JsonObject state = await Send(automation, "state");
            Assert.IsTrue(state["popups"]!.AsArray().Count >= 1, $"{state["popups"]}");
            combo.IsDropDownOpen = false;
            Dispatcher.UIThread.RunJobs();
            Assert.IsNull((await Send(automation, "state"))["popups"]);
            window.Close();
        });

        [TestMethod]
        public Task AControlBelowThePanelsFoldIsScrolledToAndReachedInOneCall() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open(TestAssets.SandboxProject());
            model.SelectMode("terrain");
            Dispatcher.UIThread.RunJobs();
            ScrollViewer panel = window.GetVisualDescendants().OfType<ModePanel>().Single().GetVisualDescendants().OfType<ScrollViewer>().First();

            JsonObject set = await Send(automation, "set", new JsonObject { ["label"] = "Write the draft again whenever it changes", ["value"] = true });
            Assert.IsTrue(model.ShownDocument!.Procedural.WriteDraftOnChange, $"one call: {set}");

            JsonObject clicked = await Send(automation, "click", new JsonObject { ["label"] = "Write the draft for procedural tools" });
            Assert.IsTrue(clicked["clicked"]!["finished"]!.GetValue<bool>(), "the click waits for the command it started");
            StringAssert.StartsWith(model.Status, "Wrote the draft for procedural tools", "the answer's status says what the click did");

            // the brush presets end the panel, below its fold
            model.Presets.Use(new Project.Workspaces.PresetLibrary(Path.Combine(AppContext.BaseDirectory, "presets", Guid.NewGuid().ToString("N"))));
            panel.Offset = default;
            Dispatcher.UIThread.RunJobs();
            await Send(automation, "click", new JsonObject { ["label"] = "Save the brush's settings as a preset" });
            Assert.AreEqual(1, model.Presets.Shown.Count, "the button below the fold was clicked");
            Assert.IsTrue(panel.Offset.Y > 0, "the panel was scrolled to reach the button");
            window.Close();
        });

        [TestMethod]
        public Task AControlTheMouseCannotReachIsRefusedWithWhy() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            model.SelectMode("terrain");
            Dispatcher.UIThread.RunJobs();
            // a question over the window covers the panel's buttons
            Task<int> asked = model.Question.AskAsync("A question", "Over everything", ["Yes", "No"], cancel: 1, focus: 1);
            Dispatcher.UIThread.RunJobs();

            InvalidOperationException refused = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => Send(automation, "click", new JsonObject { ["label"] = "Curve", ["under"] = "Tools" }));
            StringAssert.Contains(refused.Message, "the mouse would reach", "it says what lies over it");
            Assert.AreNotEqual("terrain.curve", model.ActiveTool?.Definition.Id, "nothing was clicked");

            model.Question.Answer("No");
            await asked;
            window.Close();
        });
    }
}
