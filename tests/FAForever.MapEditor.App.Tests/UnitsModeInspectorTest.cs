using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FAForever.MapEditor.App.Automation;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;
using FAForever.MapEditor.App.Views.MapPanels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The Units mode as "pick on the left, edit in the inspector" (user stories U-3, U-4, M-4): the
    /// tree of armies and groups stays the panel's list, and the chosen army or group shows its
    /// fields and buttons in the inspector; an army the same fields as the Map mode's.
    /// </summary>
    [TestClass]
    public sealed class UnitsModeInspectorTest
    {
        private static async Task<(MainWindow Window, MainViewModel Model, AutomationServer Automation)> Open()
        {
            MainWindow window = new MainWindow { Width = 1280, Height = 800 };
            MainViewModel model = new MainViewModel(new WindowDialogs(window));
            await model.OpenAsync("assets/maps/HardFFA.v0001");
            model.Assets.Use(TestAssets.Catalog());
            model.Presets.Use(new PresetLibrary(Path.Combine(AppContext.BaseDirectory, "presets", Guid.NewGuid().ToString("N"))));
            window.DataContext = model;
            window.Show();
            Dispatcher.UIThread.RunJobs();
            AutomationServer automation = new AutomationServer(window, model, port: 0);
            Assert.IsTrue((await model.Commands.RunAsync("units.mode", null, interactive: false)).Ran);
            foreach ((int x, string army) in new[] { (60, "ARMY_1"), (80, "ARMY_1"), (100, "ARMY_2") })
            {
                Assert.IsTrue((await model.Commands.RunAsync("units.add", new JsonObject { ["blueprint"] = "uec1101", ["x"] = x, ["z"] = 60, ["army"] = army }, interactive: false)).Ran);
            }
            await Send(automation, "expand", new JsonObject { ["label"] = "Armies and their unit groups", ["all"] = true });
            return (window, model, automation);
        }

        private static async Task<JsonObject> Send(AutomationServer automation, string command, JsonObject? arguments = null)
        {
            Dispatcher.UIThread.RunJobs();
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            JsonObject answer = (JsonObject)await automation.HandleAsync(command, arguments ?? []);
            Dispatcher.UIThread.RunJobs();
            return answer;
        }

        private static Control? InInspector(MainWindow window, string label) => window.GetVisualDescendants().OfType<Inspector>().Single()
            .GetVisualDescendants().OfType<Control>().FirstOrDefault(control => Avalonia.Automation.AutomationProperties.GetName(control) == label && control.IsEffectivelyVisible);

        [TestMethod]
        public Task AGroupChosenInTheTreeShowsItsFieldsAndButtonsInTheInspector() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            ModePanel panel = window.GetVisualDescendants().OfType<ModePanel>().Single();
            Assert.IsFalse(panel.GetVisualDescendants().OfType<Button>().Any(button => button.IsEffectivelyVisible && Avalonia.Automation.AutomationProperties.GetName(button) == "Rename the chosen group"),
                "the panel holds the tree and the presets, no buttons for the chosen group");

            await Send(automation, "click", new JsonObject { ["label"] = "Group INITIAL of ARMY_1, 2 units" });

            Assert.AreEqual("INITIAL", ((TextBlock)InInspector(window, "Chosen army or group")!).Text);
            Assert.AreEqual("ARMY_1", ((TextBlock)InInspector(window, "Army of the chosen group")!).Text);
            Assert.AreEqual("INITIAL", ((TextBlock)InInspector(window, "Path of the chosen group")!).Text);
            Assert.AreEqual("2 units", ((TextBlock)InInspector(window, "Units of the chosen army or group")!).Text);
            Assert.IsNotNull(InInspector(window, "Rename the chosen group"));
            Assert.IsNotNull(InInspector(window, "Remove the chosen group"));
            Assert.IsNull(InInspector(window, "Name of the chosen army"), "a group shows no army's fields");

            // a group added from the inspector, named there, is chosen, and renamed there
            await Send(automation, "set", new JsonObject { ["label"] = "Name of the new group", ["value"] = "Town" });
            await Send(automation, "click", new JsonObject { ["label"] = "Add a group to the chosen army or group" });
            Assert.AreEqual("INITIAL/Town", model.Armies.ChosenGroup?.PathText);
            Assert.AreEqual("", model.Armies.NewGroupName, "the field is empty again");
            await Send(automation, "set", new JsonObject { ["label"] = "Name of the chosen group", ["value"] = "Village" });
            await Send(automation, "click", new JsonObject { ["label"] = "Rename the chosen group" });
            Assert.IsNotNull(model.ShownDocument!.FindGroup("ARMY_1", ["INITIAL", "Village"]));
            Assert.AreEqual("Village", ((TextBlock)InInspector(window, "Chosen army or group")!).Text);

            // Select its units: the selection then has the inspector
            await Send(automation, "click", new JsonObject { ["label"] = "Group INITIAL of ARMY_1, 2 units" });
            await Send(automation, "click", new JsonObject { ["label"] = "Select the units of the chosen army or group" });
            Assert.AreEqual(2, model.Selection.Count);
            Assert.AreEqual(InspectorOwner.Selection, model.InspectorOwner);
            window.Close();
        });

        [TestMethod]
        public Task AnArmyChosenInTheTreeShowsTheMapModesArmyFields() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();

            await Send(automation, "click", new JsonObject { ["label"] = "Army ARMY_2, 1 unit" });

            Assert.AreEqual("ARMY_2", ((TextBlock)InInspector(window, "Chosen army or group")!).Text);
            Assert.IsNull(InInspector(window, "Path of the chosen group"), "an army has no path");
            Assert.IsNull(InInspector(window, "Rename the chosen group"), "nor the group's Rename and Remove");
            Assert.AreEqual("ARMY_2", model.Armies.ChosenArmy?.Name, "the army chosen in the tree is the Map mode's chosen army");
            TextBox name = (TextBox)InInspector(window, "Name of the chosen army")!;
            Assert.AreEqual("ARMY_2", name.Text);
            Assert.IsNotNull(InInspector(window, "Players take the chosen army"));
            Assert.AreEqual(1, window.GetVisualDescendants().OfType<Inspector>().Single().GetVisualDescendants().OfType<ArmyPanel>().Count(view => view.IsEffectivelyVisible),
                "the Map mode's army view");

            await Send(automation, "set", new JsonObject { ["label"] = "Team of the chosen army", ["value"] = "2" });
            await Send(automation, "click", new JsonObject { ["label"] = "Put the chosen army in the team" });
            Assert.AreEqual("2", model.ShownDocument!.TeamOf("ARMY_2"), "the fields edit the army");

            // the Map mode's list shows the same army with the same view
            Assert.IsTrue((await model.Commands.RunAsync("map.mode", null, interactive: false)).Ran);
            Dispatcher.UIThread.RunJobs();
            ArmyPanel map = window.GetVisualDescendants().OfType<ModePanel>().Single().GetVisualDescendants().OfType<ArmyPanel>().Single();
            Assert.IsTrue(map.IsEffectivelyVisible);
            Assert.AreEqual("ARMY_2", map.GetVisualDescendants().OfType<TextBox>().First(box => Avalonia.Automation.AutomationProperties.GetName(box) == "Name of the chosen army").Text);
            window.Close();
        });

        [TestMethod]
        public Task SaveAsAPresetSavesTheGroupsUnitsWhateverIsSelected() => HeadlessApp.Run(async () =>
        {
            (MainWindow window, MainViewModel model, AutomationServer automation) = await Open();
            model.Selection.Set([ObjectRef.Unit("UNIT_3")]);
            await Send(automation, "click", new JsonObject { ["label"] = "Group INITIAL of ARMY_1, 2 units" });
            Assert.AreEqual(1, model.Selection.Count, "the selection stays");

            await Send(automation, "click", new JsonObject { ["label"] = "Save the units of the chosen army or group as a preset" });

            Preset group = model.Presets.Library!.Find(UnitPreset.Kind, "INITIAL (2)") ?? throw new AssertFailedException("the group's preset is saved");
            Assert.AreEqual(2, group.Read<UnitPresetSettings>().Count, "its two units, not the one selected");
            StringAssert.StartsWith(model.Status, "Saved the preset INITIAL (2)");

            // the panel's Save keeps saving the selection
            CommandRun selected = await model.Commands.RunAsync("edit.saveAsPreset", new JsonObject { ["name"] = "Selected" }, interactive: false);
            Assert.AreEqual(1, model.Presets.Library.Find(UnitPreset.Kind, "Selected")!.Read<UnitPresetSettings>().Count);

            // by command: an army's units, and a group without units refused
            CommandRun army = await model.Commands.RunAsync("units.saveGroupAsPreset", new JsonObject { ["army"] = "ARMY_1", ["group"] = "", ["name"] = "Whole army" }, interactive: false);
            Assert.AreEqual(2, ((JsonObject)army.Result!)["units"]!.GetValue<int>());
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => model.Commands.RunAsync("units.saveGroupAsPreset", new JsonObject { ["army"] = "NEUTRAL_CIVILIAN", ["group"] = "INITIAL" }, interactive: false));
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => model.Commands.RunAsync("units.saveGroupAsPreset", new JsonObject { ["army"] = "ARMY_1", ["group"] = "Nowhere" }, interactive: false));
            window.Close();
        });
    }
}
