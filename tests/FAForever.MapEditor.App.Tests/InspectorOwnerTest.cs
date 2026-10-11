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
    /// Who has the inspector: what was chosen last of a shelf tile, things on the map and a part of
    /// the mode (a mode, an entry of the panel's lists). Issue 1 of the wave 10 walkthrough: a tile
    /// chosen on the shelf kept the inspector in every mode.
    /// </summary>
    [TestClass]
    public sealed class InspectorOwnerTest
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

        private static async Task ChooseTile(MainViewModel model)
        {
            Assert.IsTrue((await model.Commands.RunAsync("assets.select", new JsonObject { ["key"] = "uec1101" }, interactive: false)).Ran);
            Assert.IsTrue(model.Assets.HasSelection);
            Assert.IsFalse(model.InspectsMode, "a tile chosen on the shelf has the inspector");
        }

        [TestMethod]
        public Task ChoosingAModeGivesTheInspectorBack() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, _) = await Open();
            await ChooseTile(model);

            Assert.IsTrue((await model.Commands.RunAsync("procedural.mode", null, interactive: false)).Ran);

            Assert.IsFalse(model.Assets.HasSelection, "the tile is unchosen");
            Assert.IsTrue(model.InspectsMode);
            window.Close();
        });

        [TestMethod]
        public Task ClickingAnEntryOfAPanelsListGivesTheInspectorBack() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            model.SelectMode("procedural");
            await ChooseTile(model);

            await Send(automation, "click", new JsonObject { ["label"] = "Navigation mesh" });

            Assert.AreEqual("procedural.navigationMesh", model.ProceduralList.Parts.Chosen.Id);
            Assert.IsFalse(model.Assets.HasSelection, "the entry has the inspector");
            Assert.IsTrue(model.InspectsMode);

            // the entry chosen already, clicked again after a tile was chosen, has it too
            await ChooseTile(model);
            await Send(automation, "click", new JsonObject { ["label"] = "Navigation mesh" });
            Assert.IsFalse(model.Assets.HasSelection);
            window.Close();
        });

        [TestMethod]
        public Task InspectingAPartGivesTheInspectorBack() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, _) = await Open();
            await ChooseTile(model);

            Assert.IsTrue((await model.Commands.RunAsync("view.inspectPart", new JsonObject { ["part"] = "water.foam" }, interactive: false)).Ran);

            Assert.AreEqual("water", model.SelectedMode?.Definition.Id);
            Assert.IsFalse(model.Assets.HasSelection);
            window.Close();
        });

        [TestMethod]
        public Task ClickingAStratumInThePaintPanelGivesTheInspectorBack() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            model.SelectMode("paint");
            Dispatcher.UIThread.RunJobs();
            await ChooseTile(model);
            ListBox strata = window.GetVisualDescendants().OfType<ModePanel>().Single().GetVisualDescendants().OfType<ListBox>()
                .First(list => Avalonia.Automation.AutomationProperties.GetName(list) == "Layer textures");
            string second = UiTree.DisplayText(strata.Items.Cast<object?>().ElementAt(1)) ?? "";

            await Send(automation, "set", new JsonObject { ["label"] = "Layer textures", ["value"] = 1 });

            Assert.AreEqual(1, strata.SelectedIndex, $"the stratum {second} is chosen");
            Assert.IsFalse(model.Assets.HasSelection, "a click on a list's item in the panel gives the inspector back");
            window.Close();
        });
    }
}
