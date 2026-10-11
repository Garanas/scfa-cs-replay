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
        public Task ChoosingAToolSaysItInTheStatusLine() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            Assert.IsTrue((await model.Commands.RunAsync("units.mode", null, interactive: false)).Ran);

            await Send(automation, "click", new JsonObject { ["label"] = "Select", ["under"] = "Tools" });

            Assert.AreEqual("units.select", model.ActiveTool?.Definition.Id);
            Assert.AreEqual("Units mode: Select", model.Status, "not the tool chosen before");
            window.Close();
        });

        [TestMethod]
        public void ATilesNameBreaksBetweenWordsOnlyAndAWordIsCutInItsMiddle()
        {
            Controls.TileTitleWrapConverter converter = Controls.TileTitleWrapConverter.Instance;
            System.Globalization.CultureInfo culture = System.Globalization.CultureInfo.InvariantCulture;
            Assert.AreEqual(Avalonia.Media.TextWrapping.NoWrap, converter.Convert("des_boulder01", typeof(Avalonia.Media.TextWrapping), null, culture));
            Assert.AreEqual(Avalonia.Media.TextTrimming.PrefixCharacterEllipsis, converter.Convert("des_boulder01", typeof(Avalonia.Media.TextTrimming), null, culture),
                "des_bou…lder01: the end that tells the variants apart shows");
            Assert.AreEqual(Avalonia.Media.TextWrapping.Wrap, converter.Convert("Aeon Administrative Structure", typeof(Avalonia.Media.TextWrapping), null, culture));
            Assert.AreEqual(Avalonia.Media.TextTrimming.CharacterEllipsis, converter.Convert("Aeon Administrative Structure", typeof(Avalonia.Media.TextTrimming), null, culture));
        }

        [TestMethod]
        public void SelectedUnitsShowWhatTheyCost()
        {
            string folder = TestAssets.Folder();
            File.WriteAllText(Path.Combine(Directory.CreateDirectory(Path.Combine(folder, "units", "uab0301")).FullName, "uab0301_unit.json"),
                """{ "id": "UAB0301", "name": "Factory", "faction": "Aeon", "categories": ["AEON", "STRUCTURE"], "massCost": 5220, "energyCost": 47400 }""");
            Project.Workspaces.AssetCatalog catalog = Project.Workspaces.AssetCatalog.Load(folder);
            Project.Editing.PlacedUnit Placed(string blueprint) =>
                new Project.Editing.PlacedUnit("ARMY_1", [], new Project.Files.InMemory.Unit { Name = "UNIT_1", Blueprint = blueprint });

            UnitCostLine one = UnitCostLine.Of([Placed("uab0301")], catalog)!;
            Assert.AreEqual((5220.0, 47400.0, (string?)null), (one.Mass, one.Energy, one.Suffix));
            Assert.AreEqual("5,220 mass, 47,400 energy", one.Text);

            UnitCostLine two = UnitCostLine.Of([Placed("uab0301"), Placed("uab0301"), Placed("unknown")], catalog)!;
            Assert.AreEqual((10440.0, "for 2 units"), (two.Mass, two.Suffix), "the units whose cost the catalogue knows");
            Assert.IsNull(UnitCostLine.Of([Placed("unknown")], catalog));
        }

        [TestMethod]
        public void APathNodesPlaceIsSaidToATenth()
        {
            Assert.AreEqual("x 406.3, z 283.2", Commands.AddGeneratedPathNodeHandler.Place(406.3352, 283.2033));
        }
    }
}
