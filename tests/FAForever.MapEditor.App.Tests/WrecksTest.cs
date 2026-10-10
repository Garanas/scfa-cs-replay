using System.Text.Json.Nodes;
using Avalonia.Threading;
using FAForever.MapEditor.App.Automation;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;
using FAForever.MapEditor.Project.Editing;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// Units placed as wrecks (user story U-5): the place tool's As wrecks, <c>units.asWrecks</c> on
    /// the selection and the inspector's check box, all through the army's <c>WRECKAGE</c> group.
    /// </summary>
    [TestClass]
    public class WrecksTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private sealed class NoDialogs : IEditorDialogs
        {
            public Task<string?> PickFolderAsync(string title, string? startIn = null) => Task.FromResult<string?>(null);

            public Task<SaveChoice> AskToSaveChangesAsync(string mapName) => Task.FromResult(SaveChoice.Discard);
        }

        private static async Task<MainViewModel> Open()
        {
            MainViewModel model = new MainViewModel(new NoDialogs());
            model.Assets.Use(UnitsTest.Catalog());
            await model.OpenAsync(HardFfa);
            model.SelectTool("units.place");
            return model;
        }

        private static Task<CommandRun> Run(MainViewModel model, string id, JsonObject? arguments = null) =>
            model.Commands.RunAsync(id, arguments ?? [], interactive: false);

        private static PlacedUnit Placed(MainViewModel model, string name) => model.ShownDocument!.FindUnit(name)!;

        [TestMethod]
        public async Task ThePlaceToolPlacesWrecksIntoTheArmysWreckageGroup()
        {
            MainViewModel model = await Open();
            UnitPlaceTool tool = model.Tool<UnitPlaceTool>();
            await Run(model, "units.pick", new JsonObject { ["key"] = "uel0101" });
            tool.AsWrecks = true;

            tool.Press(40, 60);

            PlacedUnit wreck = Placed(model, "UNIT_1");
            Assert.AreEqual(("NEUTRAL_CIVILIAN", "WRECKAGE"), (wreck.Army, UnitGroupEdits.PathText(wreck.Group)));
            Assert.IsTrue(UnitWrecks.IsWreck(wreck));
            StringAssert.EndsWith(model.Status, "as a wreck");
            Assert.IsTrue(tool.Describe()["asWrecks"]!.GetValue<bool>());

            CommandRun unit = await Run(model, "units.add", new JsonObject { ["x"] = 50, ["z"] = 50, ["wreck"] = false });
            Assert.IsFalse(((JsonObject)unit.Result!)["wreck"]!.GetValue<bool>(), "the argument wins over the tool");
            Assert.AreEqual("INITIAL", UnitGroupEdits.PathText(Placed(model, "UNIT_2").Group));
        }

        [TestMethod]
        public async Task TheSelectionBecomesWrecksAndUnitsAgainAsOneStepEach()
        {
            MainViewModel model = await Open();
            await Run(model, "units.add", new JsonObject { ["blueprint"] = "uel0101", ["x"] = 40, ["z"] = 40 });
            await Run(model, "units.add", new JsonObject { ["blueprint"] = "uel0101", ["x"] = 60, ["z"] = 40 });
            model.Selection.Set([ObjectRef.Unit("UNIT_1"), ObjectRef.Unit("UNIT_2")]);
            UnitSelection section = model.Selection.Inspector.Sections.OfType<UnitSelection>().Single();
            Assert.AreEqual(false, section.Wrecks);
            int steps = model.History.Count;

            CommandRun run = await Run(model, "units.asWrecks");

            Assert.AreEqual(2, run.Result);
            Assert.AreEqual(steps + 1, model.History.Count);
            CollectionAssert.AreEquivalent(new[] { "UNIT_1", "UNIT_2" }, model.ShownDocument!.Wrecks().ToArray());
            Assert.AreEqual(true, section.Wrecks, "the inspector shows it");

            model.Selection.Set([ObjectRef.Unit("UNIT_1")]);
            section.Wrecks = false;
            await WaitFor(() => model.ShownDocument!.Wrecks().Count == 1);
            Assert.AreEqual("INITIAL", UnitGroupEdits.PathText(Placed(model, "UNIT_1").Group), "the check box made it a unit again");

            model.Selection.Set([ObjectRef.Unit("UNIT_1"), ObjectRef.Unit("UNIT_2")]);
            Assert.IsNull(section.Wrecks, "some are");

            model.Undo();
            Assert.AreEqual(2, model.ShownDocument!.Wrecks().Count);
        }

        [TestMethod]
        public async Task WrecksOfAPlayableArmyAreNamedInTheStatusAndProblems()
        {
            MainViewModel model = await Open();
            await Run(model, "units.add", new JsonObject { ["blueprint"] = "uel0101", ["x"] = 40, ["z"] = 40, ["army"] = "ARMY_1" });
            model.Selection.Set([ObjectRef.Unit("UNIT_1")]);

            await Run(model, "units.asWrecks");

            StringAssert.Contains(model.Status, "ARMY_1 is playable");
            Assert.IsTrue(model.Problems.Run().Any(problem => problem.Check == "wrecks-in-playable-army"));
        }

        [TestMethod]
        public Task ThePersonChecksAsWrecksAndClicksTheMap() => HeadlessApp.Run(async () =>
        {
            MainWindow window = new MainWindow { Width = 1280, Height = 800 };
            MainViewModel model = new MainViewModel(new WindowDialogs(window));
            model.Assets.Use(UnitsTest.Catalog());
            await model.OpenAsync(HardFfa);
            window.DataContext = model;
            window.Show();
            Dispatcher.UIThread.RunJobs();
            EditorDriver editor = EditorDriver.InProcess(new AutomationServer(window, model, port: 0));
            await editor.Tool("units.place");
            await editor.Run("units.pick", new JsonObject { ["key"] = "uel0101" });

            await editor.Set("Place units as wrecks", true);
            Assert.IsTrue(model.Tool<UnitPlaceTool>().AsWrecks, "the check box");
            JsonObject point = (JsonObject)(await editor.Send("view-point", new JsonObject { ["x"] = 128, ["z"] = 128 }))["point"]!["top"]!;
            await editor.Click("top", point["x"]!.GetValue<double>(), point["y"]!.GetValue<double>());
            Dispatcher.UIThread.RunJobs();

            JsonObject got = await editor.Send("get", new JsonObject { ["ref"] = "unit:UNIT_1" });
            Assert.IsTrue(got["object"]!["wreck"]!.GetValue<bool>(), got.ToJsonString());
            HeadlessApp.SaveCapture(HeadlessApp.Capture(window), "wreck-top-view");
            window.Close();
        });

        private static async Task WaitFor(Func<bool> condition)
        {
            for (int i = 0; i < 100 && !condition(); i++)
            {
                await Task.Delay(10);
            }
            Assert.IsTrue(condition());
        }
    }
}
