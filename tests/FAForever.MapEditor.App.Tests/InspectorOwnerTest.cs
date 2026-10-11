using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FAForever.MapEditor.App.Automation;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;
using FAForever.MapEditor.Project.Editing;

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

        /// <summary>
        /// HardFFA with two units of ARMY_1 selected in the Units mode, the selection owning the inspector.
        /// </summary>
        private static async Task<(MainWindow Window, MainViewModel Model, AutomationServer Automation)> OpenWithSelectedUnits()
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            model.View = MapViewTab.Top;
            Assert.IsTrue((await model.Commands.RunAsync("units.mode", null, interactive: false)).Ran);
            model.SelectTool("units.select");
            foreach (int x in new[] { 60, 80 })
            {
                Assert.IsTrue((await model.Commands.RunAsync("units.add", new JsonObject { ["blueprint"] = "uec1101", ["x"] = x, ["z"] = 60, ["army"] = "ARMY_1" }, interactive: false)).Ran);
            }
            await Send(automation, "run", new JsonObject { ["id"] = "select.set", ["refs"] = new JsonArray("unit:UNIT_1", "unit:UNIT_2") });
            await Send(automation, "expand", new JsonObject { ["label"] = "Armies and their unit groups", ["all"] = true });
            Assert.AreEqual(InspectorOwner.Selection, model.InspectorOwner, "a selection changed by a command has the inspector");
            Assert.IsTrue(model.InspectsSelection);
            return (window, model, automation);
        }

        private static Control Labelled(MainWindow window, string label) => window.GetVisualDescendants().OfType<Control>()
            .First(control => Avalonia.Automation.AutomationProperties.GetName(control) == label);

        private static async Task<string> Owner(AutomationServer automation) =>
            ((JsonObject)(await Send(automation, "state"))["inspector"]!)["owner"]!.GetValue<string>();

        [TestMethod]
        public Task ChoosingAGroupInTheTreeKeepsTheSelectionAndAClickOnTheMapTakesTheInspectorBack() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await OpenWithSelectedUnits();

            await Send(automation, "click", new JsonObject { ["label"] = "Group INITIAL of NEUTRAL_CIVILIAN, 0 units" });

            Assert.AreEqual(InspectorOwner.Part, model.InspectorOwner, "the group has the inspector");
            Assert.AreEqual(2, model.Selection.Count, "the selection stays");
            Assert.IsFalse(model.InspectsSelection);
            Assert.IsTrue(model.InspectsMode);
            Assert.AreEqual("part", await Owner(automation));
            Assert.IsTrue(Labelled(window, "Chosen army or group").IsEffectivelyVisible, "the inspector shows the group");
            Assert.AreEqual("INITIAL", ((TextBlock)Labelled(window, "Chosen army or group")).Text);

            // the buttons that read the selection still work on it
            await Send(automation, "click", new JsonObject { ["label"] = "Move the selected units into the chosen army or group" });
            Assert.AreEqual("NEUTRAL_CIVILIAN", model.ShownDocument!.FindUnit("UNIT_1")!.Army, "Move the selection here moved the selected units");
            Assert.AreEqual(InspectorOwner.Part, model.InspectorOwner, "and the group keeps the inspector");

            // a click on the map gives the inspector to the selection, also a click on a thing selected already (which keeps the selection to drag it)
            JsonObject top = (JsonObject)((JsonObject)(await Send(automation, "view-point", new JsonObject { ["ref"] = "unit:UNIT_1" }))["point"]!)["top"]!;
            await Send(automation, "mouse", new JsonObject { ["view"] = "top", ["x"] = top["x"]!.DeepClone(), ["y"] = top["y"]!.DeepClone() });
            CollectionAssert.AreEqual(new[] { ObjectRef.Unit("UNIT_1"), ObjectRef.Unit("UNIT_2") }, model.Selection.Items.ToArray());
            Assert.AreEqual(InspectorOwner.Selection, model.InspectorOwner);
            Assert.IsTrue(model.InspectsSelection);
            Assert.AreEqual("selection", await Owner(automation));
            window.Close();
        });

        [TestMethod]
        public Task ChoosingTheModeKeepsTheSelectionAndDeleteStillActsOnIt() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await OpenWithSelectedUnits();

            Assert.IsTrue((await model.Commands.RunAsync("units.mode", null, interactive: false)).Ran);
            Assert.AreEqual(InspectorOwner.Part, model.InspectorOwner, "the mode chosen again has the inspector");
            Assert.AreEqual(2, model.Selection.Count);

            Assert.IsTrue((await model.Commands.RunAsync("edit.delete", null, interactive: false)).Ran);
            Assert.IsNull(model.ShownDocument!.FindUnit("UNIT_1"), "Delete removed the selected units");
            Assert.IsNull(model.ShownDocument.FindUnit("UNIT_2"));
            Assert.AreEqual(InspectorOwner.Part, model.InspectorOwner);
            window.Close();
        });

        [TestMethod]
        public Task ASelectionThatOnlyLosesWhatAnUndoTookAwayLeavesTheInspectorWithThePart() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await OpenWithSelectedUnits();
            Assert.IsTrue((await model.Commands.RunAsync("units.add", new JsonObject { ["blueprint"] = "uec1101", ["x"] = 100, ["z"] = 60, ["army"] = "ARMY_1" }, interactive: false)).Ran);
            await Send(automation, "run", new JsonObject { ["id"] = "select.set", ["refs"] = new JsonArray("unit:UNIT_1", "unit:UNIT_3") });
            await Send(automation, "click", new JsonObject { ["label"] = "Group INITIAL of ARMY_1, 3 units" });
            Assert.AreEqual(InspectorOwner.Part, model.InspectorOwner);

            model.Selection.Prune(model.ShownDocument!.RemoveUnit("UNIT_3"));

            CollectionAssert.AreEqual(new[] { ObjectRef.Unit("UNIT_1") }, model.Selection.Items.ToArray(), "what the map lost drops out");
            Assert.AreEqual(InspectorOwner.Part, model.InspectorOwner, "and the part keeps the inspector");
            window.Close();
        });

        [TestMethod]
        public Task AUnitChosenInTheTreeIsSelectedAndTheSelectionHasTheInspector() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await OpenWithSelectedUnits();
            await Send(automation, "click", new JsonObject { ["label"] = "Group INITIAL of ARMY_1, 2 units" });
            Assert.AreEqual(InspectorOwner.Part, model.InspectorOwner);

            await Send(automation, "click", new JsonObject { ["label"] = "Unit UNIT_2, uec1101, in INITIAL of ARMY_1" });

            CollectionAssert.AreEqual(new[] { ObjectRef.Unit("UNIT_2") }, model.Selection.Items.ToArray(), "the unit is selected");
            Assert.AreEqual(InspectorOwner.Selection, model.InspectorOwner);
            Assert.IsTrue(model.InspectsSelection);

            // the group again, then the same unit, which is selected already: the selection has the inspector again
            await Send(automation, "click", new JsonObject { ["label"] = "Group INITIAL of ARMY_1, 2 units" });
            Assert.AreEqual(InspectorOwner.Part, model.InspectorOwner);
            await Send(automation, "click", new JsonObject { ["label"] = "Unit UNIT_2, uec1101, in INITIAL of ARMY_1" });
            Assert.AreEqual(InspectorOwner.Selection, model.InspectorOwner);
            window.Close();
        });

        [TestMethod]
        public Task ATileTakesTheInspectorAndGivesItBackToThePart() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            await ChooseTile(model);
            Assert.AreEqual(InspectorOwner.Tile, model.InspectorOwner);
            Assert.AreEqual("tile", await Owner(automation));

            model.Assets.Select((AssetTile?)null);
            Assert.AreEqual(InspectorOwner.Part, model.InspectorOwner, "nothing selected: the part has it again");
            window.Close();
        });

        [TestMethod]
        public Task TheSelectionCaptionShowsOnlyInAModeWithoutPartsOfItsOwn() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, _) = await Open();
            TextBlock Caption() => window.GetVisualDescendants().OfType<Inspector>().Single().GetVisualDescendants().OfType<TextBlock>()
                .Single(block => block.Text?.StartsWith("Nothing is selected", StringComparison.Ordinal) == true);

            model.SelectMode("units");
            Dispatcher.UIThread.RunJobs();
            Assert.IsFalse(model.HasNoInspectorParts);
            Assert.IsFalse(Caption().IsEffectivelyVisible, "the Units mode's inspector shows the chosen army or group");

            // the Markers mode lists its kinds, layers and chains (wave 14B): the chosen entry is its part
            model.SelectMode("markers");
            Dispatcher.UIThread.RunJobs();
            Assert.IsFalse(model.HasNoInspectorParts);
            Assert.IsFalse(Caption().IsEffectivelyVisible, "the Markers mode's inspector shows the chosen kind, layer or chain");

            // a selection takes the inspector, and choosing the mode's part gives it back while the selection stays
            model.Selection.Set([ObjectRef.Marker(model.ShownDocument!.Markers.Spawns[0].Id)]);
            Assert.IsTrue(model.InspectsSelection);
            model.InspectTheMode();
            Assert.IsFalse(model.InspectsSelection);
            Assert.IsTrue(model.Selection.HasAny);
            window.Close();
        });

        [TestMethod]
        public void ForPartsFiltersByTheChosenEntryAndForToolsByTheTool()
        {
            PlannedSection always = new PlannedSection("Always", "F-1", "always");
            PlannedSection sky = new PlannedSection("Sky", "L-1", "sky") { ForParts = ["lighting.sky"] };
            PlannedSection sculpt = new PlannedSection("Sculpt", "T-1", "sculpt") { ForTools = ["terrain.sculpt"] };

            Assert.IsTrue(MainViewModel.Shows(always, null, null));
            Assert.IsTrue(MainViewModel.Shows(sky, "lighting.sun", "lighting.sky"));
            Assert.IsFalse(MainViewModel.Shows(sky, "lighting.sun", "lighting.fog"));
            Assert.IsFalse(MainViewModel.Shows(sky, null, null), "a mode without a list never shows it");
            Assert.IsTrue(MainViewModel.Shows(sculpt, "terrain.sculpt", null));
            Assert.IsFalse(MainViewModel.Shows(sculpt, "terrain.curve", null));
        }
    }
}
