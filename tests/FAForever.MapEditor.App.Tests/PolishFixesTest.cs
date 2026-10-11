using System.Text.Json.Nodes;
using Avalonia.Threading;
using FAForever.MapEditor.App.Automation;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// Small fixes of the wave 10 walkthrough's polish: status lines in words (20), the modes'
    /// keys in the Modes menu (21), the presets' name field (22).
    /// </summary>
    [TestClass]
    public sealed class PolishFixesTest
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
        public Task TheModesMenuShowsEachModesKey() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, _, AutomationServer automation) = await Open();

            JsonArray items = (JsonArray)(await Send(automation, "menu", new JsonObject { ["path"] = "Modes" }))["menu"]!;

            JsonNode terrain = items.Single(item => item!["header"]!.GetValue<string>() == "Terrain")!;
            Assert.AreEqual("F2", terrain["shortcut"]?.GetValue<string>(), "the submenu's header shows the mode's key");
            Assert.AreEqual("F11", items.Single(item => item!["header"]!.GetValue<string>() == "Procedural")!["shortcut"]?.GetValue<string>());
            window.Close();
        });

        [TestMethod]
        public Task EachKindOfPresetKeepsItsOwnNameAndEnterSaves() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            model.Presets.Use(new Project.Workspaces.PresetLibrary(Path.Combine(AppContext.BaseDirectory, "presets", Guid.NewGuid().ToString("N"))));
            model.SelectMode("water");
            model.Presets.NewName = "Calm sea";
            model.SelectMode("terrain");
            Assert.AreEqual("", model.Presets.NewName, "the brush presets do not show the name typed for water");
            model.SelectMode("water");
            Assert.AreEqual("Calm sea", model.Presets.NewName, "the water presets keep it");

            model.SelectTool("terrain.sculpt");
            Dispatcher.UIThread.RunJobs();
            await Send(automation, "set", new JsonObject { ["label"] = "Name of the new preset", ["value"] = "Ridge", ["commit"] = "enter" });

            Assert.AreEqual("Ridge", model.Presets.Shown.Single().Name, "Enter in the name saved the preset");
            Assert.AreEqual("", model.Presets.NewName, "the field is empty again");
            window.Close();
        });

        [TestMethod]
        public void TheHeightLayersFiltersAreSaidInWords()
        {
            Assert.AreEqual("the height layers shared by the draft and the final", HeightLayersViewModel.UseWords("shared"));
            Assert.AreEqual("every height layer", HeightLayersViewModel.FiltersWords("all", "all"));
            Assert.AreEqual("the curves among the height layers", HeightLayersViewModel.FiltersWords("all", "curve"));
            Assert.AreEqual("the height layers only in the draft, and of them the curves", HeightLayersViewModel.FiltersWords("draft", "curve"));
        }

        [TestMethod]
        public void APathNodesPlaceIsSaidToATenth()
        {
            Assert.AreEqual("x 406.3, z 283.2", Commands.AddGeneratedPathNodeHandler.Place(406.3352, 283.2033));
        }
    }
}
