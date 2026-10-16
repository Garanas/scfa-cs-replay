using System.Numerics;
using System.Numerics;
using System.Text.Json.Nodes;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The Place tool's options row (QA 4, 19): the Group combo and the group chosen in the tree are one
    /// choice, and the first army it places for.
    /// </summary>
    [TestClass]
    public class PlaceRowTest
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

        [TestMethod]
        public async Task TheTreesGroupIsTheCombosGroupAndTheComboChoosesWhereUnitsGo()
        {
            MainViewModel model = await Open();
            UnitPlaceTool tool = model.Tool<UnitPlaceTool>();
            await Run(model, "units.add", new JsonObject { ["blueprint"] = "uel0101", ["x"] = 40, ["z"] = 40, ["army"] = "ARMY_1", ["group"] = "WRECKAGE/Field" });
            await Run(model, "units.add", new JsonObject { ["blueprint"] = "uel0101", ["x"] = 50, ["z"] = 40, ["army"] = "ARMY_1", ["group"] = "INITIAL/Town" });
            await Run(model, "units.pick", new JsonObject { ["key"] = "uel0101" });

            Assert.IsTrue(model.Armies.Choose("ARMY_1", ["WRECKAGE", "Field"]));

            Assert.AreEqual("ARMY_1", tool.Army);
            Assert.AreEqual("WRECKAGE/Field", tool.Group);
            Assert.AreEqual("WRECKAGE/Field", tool.ChosenGroup!.Path, "the combo shows it");
            Assert.AreEqual("WRECKAGE/Field", ((JsonObject)tool.Describe())["group"]!.GetValue<string>());
            tool.Press(60, 60);
            StringAssert.Contains(model.Status, "ARMY_1's WRECKAGE/Field", "the status says where it went");
            StringAssert.Contains(model.Status, "a wreck");

            // the combo chooses where they go, and the tree follows
            tool.ChosenGroup = tool.Groups.Single(choice => choice.Path == "INITIAL/Town");
            Assert.AreEqual("INITIAL/Town", tool.Group);
            Assert.AreEqual("INITIAL/Town", model.Armies.ChosenGroup!.PathText, "the tree shows the combo's group");
            tool.Press(70, 60);
            Assert.AreEqual("INITIAL/Town", string.Join('/', model.ShownDocument!.FindUnit("UNIT_4")!.Group));
        }

        [TestMethod]
        public async Task ThePlaceToolDefaultsToTheArmyWhoseSpawnLiesNearestTheViewsMiddle()
        {
            MainViewModel model = await Open();
            MapDocument document = model.ShownDocument!;
            Spawn[] spawns = [.. document.Markers.Spawns.Where(spawn => document.Scenario.PlayableArmies.Contains(spawn.Army))];
            Assert.IsTrue(spawns.Length >= 2, "the test map has playable armies with spawns");

            foreach (Spawn spawn in spawns)
            {
                UnitPlaceTool tool = new UnitPlaceTool(model) { ViewMiddle = () => new Vector2(spawn.Position.X + 0.2f, spawn.Position.Z) };
                tool.Refresh(document);
                Assert.AreEqual(spawn.Army, tool.Army, "the spawn nearest the view's middle: " + string.Join(" ", spawns.Select(s => $"{s.Army}@{s.Position}")));
                Assert.IsFalse(tool.ArmyChosen);
            }

            UnitPlaceTool chosen = new UnitPlaceTool(model) { ViewMiddle = () => new Vector2(spawns[0].Position.X, spawns[0].Position.Z) };
            chosen.Refresh(document);
            chosen.Army = "NEUTRAL_CIVILIAN";
            chosen.Refresh(document);
            Assert.AreEqual("NEUTRAL_CIVILIAN", chosen.Army, "a chosen army stays");
            Assert.IsTrue(chosen.ArmyChosen);

            MapDocument noSpawns = document with { Markers = document.Markers with { Spawns = [] } };
            UnitPlaceTool civilians = new UnitPlaceTool(model);
            civilians.Refresh(noSpawns);
            Assert.AreEqual("NEUTRAL_CIVILIAN", civilians.Army, "without playable armies, the civilians");
        }

        [TestMethod]
        public async Task TheInspectorsXAndZShowTheMixedSignAgainAfterAnEditThatLeavesThemMixed()
        {
            MainViewModel model = await Open();
            // a unit and its partner across the map: both are selected, one at x and the other at its mirror
            model.Symmetry.Chosen = model.Symmetry.Choices.Single(choice => choice.Kind == MapSymmetryKind.MirroredLeftRight);
            await Run(model, "units.add", new JsonObject { ["blueprint"] = "uec1201", ["x"] = 40, ["z"] = 40 });
            Assert.AreEqual(2, model.Selection.Items.Length, "the unit and its partner");
            SelectionInspector inspector = model.Selection.Inspector;
            Assert.AreEqual(SelectionInspector.Mixed, inspector.X);
            List<string?> changed = [];
            inspector.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

            inspector.X = "51.2";
            await Task.Delay(50);

            Assert.AreNotEqual(model.ShownDocument!.FindUnit("UNIT_1")!.Unit.Position.X, model.ShownDocument!.FindUnit("UNIT_2")!.Unit.Position.X, "the edit left them apart");
            Assert.AreEqual(SelectionInspector.Mixed, inspector.X);
            CollectionAssert.Contains(changed, nameof(SelectionInspector.X), "the field is told to show it again: the text typed would stay");
        }

        [TestMethod]
        public async Task AnArmyChosenInTheRowSurvivesAPlacementAndTheTreeShowsIt()
        {
            MainViewModel model = await Open();
            UnitPlaceTool tool = model.Tool<UnitPlaceTool>();
            await Run(model, "units.pick", new JsonObject { ["key"] = "uel0101" });
            Assert.IsTrue(model.Armies.Choose("ARMY_1", []));
            Assert.AreEqual("ARMY_1", tool.Army);

            tool.Army = "ARMY_2";
            Assert.AreEqual("ARMY_2", model.Armies.ChosenGroup!.Army, "the tree follows the row");
            tool.Press(60, 60);
            tool.Press(70, 60);

            Assert.AreEqual("ARMY_2", tool.Army, "the tree made again after a placement is no new choice of its group");
            Assert.AreEqual("ARMY_2", model.ShownDocument!.FindUnit("UNIT_2")!.Army);
        }
    }
}
