using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using FAForever.MapEditor.App.Automation;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// Automation's reach into the menus and the window, its keys by name, the part under a
    /// heading, and what its answers say about the keyboard.
    /// </summary>
    [TestClass]
    public sealed class AutomationShellTest
    {
        private static async Task<(MainWindow Window, MainViewModel Model, AutomationServer Automation)> Open()
        {
            MainWindow window = new MainWindow { Width = 1280, Height = 800 };
            MainViewModel model = new MainViewModel(new WindowDialogs(window));
            await model.OpenAsync("assets/maps/HardFFA.v0001");
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
        [DataRow("Ctrl+2", Key.D2, RawInputModifiers.Control)]
        [DataRow("2", Key.D2, RawInputModifiers.None)]
        [DataRow("Ctrl+Shift+OemOpenBrackets", Key.OemOpenBrackets, RawInputModifiers.Control | RawInputModifiers.Shift)]
        [DataRow("]", Key.OemCloseBrackets, RawInputModifiers.None)]
        public void DigitsAndBracketsAreTheirKeys(string text, Key key, RawInputModifiers modifiers) =>
            Assert.AreEqual((key, modifiers), WindowInput.ParseKey(text));

        [TestMethod]
        [DataRow("Ctrl+Banana")]
        [DataRow("Hyper+K")]
        [DataRow("42")]
        [DataRow("")]
        public void AnUnknownKeyIsRefused(string text) =>
            Assert.ThrowsException<ArgumentException>(() => WindowInput.ParseKey(text));

        [TestMethod]
        public Task TheMenusAreReadAndClickedByTheirPath() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            JsonArray bar = (JsonArray)(await Send(automation, "menu"))["menu"]!;
            CollectionAssert.Contains(bar.Select(item => item!["header"]!.GetValue<string>()).ToArray(), "File");
            JsonArray file = (JsonArray)(await Send(automation, "menu", new JsonObject { ["path"] = "File" }))["menu"]!;
            JsonObject template = (JsonObject)file.First(item => item!["header"]?.GetValue<string>() == "Save as a template")!;
            Assert.IsNotNull(template["enabled"]);

            await Send(automation, "menu", new JsonObject { ["path"] = "Select/Select all" });
            Assert.IsTrue(model.Selection.Count > 0, "the item ran its command");
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => automation.HandleAsync("menu", new JsonObject { ["path"] = "File/Nothing" }));
            window.Close();
        });

        [TestMethod]
        public Task TheWindowIsSizedAndClosedAsItsCloseButtonDoes() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            JsonObject sized = (JsonObject)(await Send(automation, "window", new JsonObject { ["width"] = 1600, ["height"] = 900 }))["window"]!;
            Assert.AreEqual(1600, sized["width"]!.GetValue<double>());
            Assert.AreEqual(900, sized["height"]!.GetValue<double>());

            model.EditMap("A change", document => document with { Scenario = document.Scenario with { Description = "changed" } });
            await Send(automation, "window", new JsonObject { ["close"] = true });
            JsonObject state = await Send(automation, "state");
            Assert.AreEqual("Unsaved changes", state["dialog"]!["title"]!.GetValue<string>(), "the close question");
            await Send(automation, "dialog", new JsonObject { ["button"] = "Cancel" });
            Assert.IsTrue(window.IsVisible, "cancelled: the window stays");
            window.Close();
        });

        [TestMethod]
        public Task UnderTakesASectionsHeadingAndKeyAnswersNameTheFocusedField() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            model.SelectMode("terrain");
            JsonArray part = (JsonArray)(await Send(automation, "ui", new JsonObject { ["under"] = "Generated from the layers", ["type"] = "Button" }))["ui"]!;
            Assert.IsTrue(part.Count >= 2, "the buttons of the part the heading heads");

            model.NewMapPage.Open();
            Dispatcher.UIThread.RunJobs();
            await Send(automation, "click", new JsonObject { ["label"] = "Name of the new map" });
            JsonObject key = await Send(automation, "key", new JsonObject { ["key"] = "End" });
            Assert.AreEqual("TextBox Name of the new map \"New map\"", key["focused"]!.GetValue<string>());
            window.Close();
        });

        [TestMethod]
        public Task AFailedCallLeavesTheConsolesCountAlone() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            int problems = model.Diagnostics.ConsoleProblems.Count;
            JsonObject refused = await Send(automation, "run", new JsonObject { ["id"] = "file.saveAsTemplate" });
            Assert.IsFalse(refused["ran"]!.GetValue<bool>());
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(problems, model.Diagnostics.ConsoleProblems.Count, "the caller has the answer; the Console counts the editor's own problems");
            window.Close();
        });
    }
}
