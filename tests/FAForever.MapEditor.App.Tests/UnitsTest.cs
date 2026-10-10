using System.Numerics;
using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The Units mode and the armies (user stories U-2, U-3, U-4, M-4): placing units, their
    /// groups, units presets, and the Map mode's armies and teams.
    /// </summary>
    [TestClass]
    public class UnitsTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private sealed class NoDialogs : IEditorDialogs
        {
            public Task<string?> PickFolderAsync(string title, string? startIn = null) => Task.FromResult<string?>(null);

            public Task<SaveChoice> AskToSaveChangesAsync(string mapName) => Task.FromResult(SaveChoice.Discard);
        }

        /// <summary>
        /// The test assets with a structure of 3 by 2 ogrids, <c>uec1201</c>.
        /// </summary>
        internal static AssetCatalog Catalog()
        {
            string folder = TestAssets.Folder();
            string file = Path.Combine(folder, "units", "uec1201", "uec1201_unit.json");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, """{ "id": "UEC1201", "name": "UEF Farm", "faction": "Uef", "categories": ["UEF", "STRUCTURE", "CIVILIAN"], "footprint": { "sizeX": 3, "sizeZ": 2 } }""");
            return AssetCatalog.Load(folder);
        }

        private static async Task<MainViewModel> Open(string tool = "units.place")
        {
            MainViewModel model = new MainViewModel(new NoDialogs());
            model.Assets.Use(Catalog());
            await model.OpenAsync(HardFfa);
            model.SelectTool(tool);
            return model;
        }

        private static Task<CommandRun> Run(MainViewModel model, string id, JsonObject? arguments = null) =>
            model.Commands.RunAsync(id, arguments ?? [], interactive: false);

        private static Unit UnitNamed(MainViewModel model, string name) => model.ShownDocument!.FindUnit(name)!.Unit;

        [TestMethod]
        public async Task AClickPlacesTheChosenUnitAsOneStepAndAStructureSnapsToTheBuildGrid()
        {
            MainViewModel model = await Open();
            UnitPlaceTool tool = model.Tool<UnitPlaceTool>();
            Assert.AreEqual("NEUTRAL_CIVILIAN", tool.Army, "civilians, the units maps place most");
            Assert.AreEqual(UnitGroupEdits.InitialGroup, tool.Group);

            tool.Press(40, 60);
            Assert.IsFalse(model.CanUndo, "no unit chosen: nothing placed");
            StringAssert.Contains(model.Status, "Choose a unit");

            await Run(model, "units.pick", new JsonObject { ["key"] = "uec1201" });
            Assert.AreEqual("uec1201", tool.Unit?.Key, "the picker chooses what the tool places");
            tool.Press(40.3f, 60.7f);
            tool.Release();
            PlacedUnit placed = model.ShownDocument!.FindUnit("UNIT_1")!;
            Assert.AreEqual(("NEUTRAL_CIVILIAN", "INITIAL"), (placed.Army, UnitGroupEdits.PathText(placed.Group)));
            Assert.AreEqual((40.5f, 61f), (placed.Unit.Position.X, placed.Unit.Position.Z), "3 wide on a half ogrid, 2 deep on a whole one");
            Assert.AreEqual(model.ShownDocument.Terrain.Heightmap.HeightAt(40.5f, 61), placed.Unit.Position.Y, 1e-4, "on the terrain");
            Assert.AreEqual("Place a unit", model.Map!.Session.UndoDescription);
            Assert.AreEqual(2, model.History.Count, "opened, and the unit");

            // a structure turns a quarter turn, which swaps its sides on the grid
            Assert.IsTrue(tool.Key("E", ToolModifiers.None));
            Assert.AreEqual(90, tool.Heading);
            tool.Press(40.3f, 60.7f);
            Unit turned = UnitNamed(model, "UNIT_2");
            Assert.AreEqual((40f, 60.5f), (turned.Position.X, turned.Position.Z));
            Assert.AreEqual(MathF.PI / 2, turned.Orientation.Y, 1e-4);

            // a mobile unit turns by 15 degrees and stands where it is put
            await Run(model, "units.pick", new JsonObject { ["key"] = "uel0101" });
            tool.Heading = 0;
            tool.Key("Q", ToolModifiers.None);
            Assert.AreEqual(345, tool.Heading);
            tool.Press(10.3f, 20.7f);
            Unit mobile = UnitNamed(model, "UNIT_3");
            Assert.AreEqual((10.3f, 20.7f), (mobile.Position.X, mobile.Position.Z));
            Assert.AreEqual(-15, SelectionInspector.Degrees(mobile.Orientation.Y) - 360, 1e-3);

            Assert.IsTrue(model.Undo());
            Assert.IsNull(model.ShownDocument!.FindUnit("UNIT_3"));
        }

        [TestMethod]
        public async Task AUnitChosenOnTheShelfOrDroppedOnTheMapIsPlaced()
        {
            MainViewModel model = await Open("terrain.sculpt");
            UnitPlaceTool tool = model.Tool<UnitPlaceTool>();
            model.Assets.Select("uec1101");
            Assert.AreEqual("uec1101", tool.Unit?.Key, "choosing a unit tile chooses it for the tool");

            Assert.IsTrue(model.DropAsset("uel0101", 20, 30), "the place tool takes a unit dropped in any mode");
            Assert.AreEqual("units.place", model.ActiveTool!.Definition.Id);
            Unit dropped = UnitNamed(model, "UNIT_1");
            Assert.AreEqual(("uel0101", 20f, 30f), (dropped.Blueprint, dropped.Position.X, dropped.Position.Z));
            Assert.AreEqual("uel0101", tool.Unit?.Key);

            CommandRun added = await Run(model, "units.add", new JsonObject { ["blueprint"] = "uec1101", ["x"] = 50, ["z"] = 50, ["army"] = "ARMY_2", ["group"] = "INITIAL/Town" });
            Assert.AreEqual("UNIT_2", ((JsonObject)added.Result!)["name"]!.GetValue<string>());
            CollectionAssert.AreEqual(new[] { "INITIAL", "Town" }, model.ShownDocument!.FindUnit("UNIT_2")!.Group.ToArray(), "the group is made");
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "units.add", new JsonObject { ["x"] = 5, ["z"] = 5, ["army"] = "ARMY_99" }));
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "units.add", new JsonObject { ["x"] = -5, ["z"] = 5 }));
        }

        [TestMethod]
        public async Task TheTreeShowsTheGroupsAndItsButtonsChangeThem()
        {
            MainViewModel model = await Open();
            ArmiesViewModel armies = model.Armies;
            GroupNode civilians = armies.Tree.Single(node => node.Army == "NEUTRAL_CIVILIAN");
            Assert.AreEqual("INITIAL", civilians.Children.Single().PathText);

            await Run(model, "units.add", new JsonObject { ["blueprint"] = "uec1101", ["x"] = 50, ["z"] = 50, ["army"] = "ARMY_1" });
            await Run(model, "units.add", new JsonObject { ["blueprint"] = "uec1101", ["x"] = 60, ["z"] = 50, ["army"] = "ARMY_1" });
            Assert.AreEqual("ARMY_1 (2)", armies.Tree.Single(node => node.Army == "ARMY_1").Label, "with the units in its groups");

            Assert.IsTrue(armies.Choose("ARMY_1", ["INITIAL"]));
            Assert.AreEqual(("ARMY_1", "INITIAL"), (model.Tool<UnitPlaceTool>().Army, model.Tool<UnitPlaceTool>().Group), "the place tool places into the chosen group");
            armies.GroupName = "Town";
            await armies.AddGroupCommand.ExecuteAsync(null);
            Assert.AreEqual("INITIAL/Town", armies.ChosenGroup?.PathText, "the new group is chosen");
            Assert.AreEqual("INITIAL/Town", model.Tool<UnitPlaceTool>().Group, "and the place tool places into it");
            Assert.IsTrue(armies.Tree.Single(node => node.Army == "ARMY_1").Children[0].IsExpanded, "and shown");

            model.Selection.Set([ObjectRef.Unit("UNIT_1")]);
            await armies.MoveSelectionHereCommand.ExecuteAsync(null);
            CollectionAssert.AreEqual(new[] { "INITIAL", "Town" }, model.ShownDocument!.FindUnit("UNIT_1")!.Group.ToArray());
            Assert.AreEqual("Town (1)", armies.ChosenGroup!.Label);

            armies.GroupName = "Village";
            await armies.RenameGroupCommand.ExecuteAsync(null);
            Assert.AreEqual("INITIAL/Village", armies.ChosenGroup?.PathText);

            model.Selection.Clear();
            Assert.IsTrue(armies.Choose("ARMY_1", []));
            await armies.SelectGroupUnitsCommand.ExecuteAsync(null);
            Assert.AreEqual(2, model.Selection.Count, "the army's units, in all its groups");

            armies.Choose("ARMY_1", ["INITIAL", "Village"]);
            await armies.RemoveGroupCommand.ExecuteAsync(null);
            Assert.IsNull(model.ShownDocument!.FindGroup("ARMY_1", ["INITIAL", "Village"]));
            CollectionAssert.AreEqual(new[] { "INITIAL" }, model.ShownDocument.FindUnit("UNIT_1")!.Group.ToArray(), "its unit is in the group above");

            CommandRun moved = await Run(model, "units.moveGroup", new JsonObject { ["army"] = "ARMY_1", ["group"] = "INITIAL", ["toArmy"] = "ARMY_2", ["toParent"] = "" });
            Assert.AreEqual("INITIAL 1", ((JsonObject)moved.Result!)["group"]!.GetValue<string>(), "ARMY_2 has an INITIAL already");
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "units.renameGroup", new JsonObject { ["army"] = "ARMY_1", ["group"] = "NOWHERE", ["name"] = "x" }));
        }

        [TestMethod]
        public async Task TheSelectedUnitsGoToAnotherArmyOrGroup()
        {
            MainViewModel model = await Open();
            await Run(model, "units.add", new JsonObject { ["blueprint"] = "uec1101", ["x"] = 50, ["z"] = 50, ["army"] = "ARMY_1" });
            await Run(model, "units.add", new JsonObject { ["blueprint"] = "uec1101", ["x"] = 60, ["z"] = 50, ["army"] = "ARMY_2" });
            model.Selection.Set([ObjectRef.Unit("UNIT_1"), ObjectRef.Unit("UNIT_2")]);
            UnitSelection section = model.Selection.Inspector.Sections.OfType<UnitSelection>().Single();
            Assert.IsNull(section.Army, "they belong to different armies");

            CommandRun grouped = await Run(model, "units.moveToGroup", new JsonObject { ["group"] = "INITIAL/Base" });
            Assert.AreEqual(2, grouped.Result);
            Assert.AreEqual("ARMY_2", model.ShownDocument!.FindUnit("UNIT_2")!.Army, "each stays in its army");
            Assert.AreEqual("INITIAL/Base", section.Group);

            section.Army = "NEUTRAL_CIVILIAN";
            Assert.IsTrue(model.ShownDocument!.AllUnits().All(unit => unit.Army == "NEUTRAL_CIVILIAN"), "the inspector's army gives them to it");
            Assert.AreEqual("NEUTRAL_CIVILIAN", section.Army);
            Assert.AreEqual("Give the selected units to an army", model.Map!.Session.UndoDescription);
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "units.moveToArmy", new JsonObject { ["army"] = "ARMY_99" }));
        }

        [TestMethod]
        public async Task StructuresTurnInQuarterTurnsWithTheSelectTool()
        {
            MainViewModel model = await Open("units.select");
            await Run(model, "units.add", new JsonObject { ["blueprint"] = "uec1201", ["x"] = 50, ["z"] = 50 });
            model.Selection.Set([ObjectRef.Unit("UNIT_1")]);
            Assert.IsTrue(model.ActiveTool!.Tool!.Key("E", ToolModifiers.None));
            await Task.Yield();
            Assert.AreEqual(MathF.PI / 2, UnitNamed(model, "UNIT_1").Orientation.Y, 1e-4);
        }

        [TestMethod]
        public async Task AUnitsPresetIsSavedFromTheSelectionAndPlacedForAnyArmy()
        {
            MainViewModel model = await Open();
            model.Presets.Use(new PresetLibrary(Path.Combine(AppContext.BaseDirectory, "presets", Guid.NewGuid().ToString("N"))));
            await Run(model, "units.add", new JsonObject { ["blueprint"] = "uec1201", ["x"] = 50, ["z"] = 50, ["army"] = "ARMY_1", ["group"] = "INITIAL/Town" });
            await Run(model, "units.add", new JsonObject { ["blueprint"] = "uec1101", ["x"] = 60, ["z"] = 50, ["army"] = "ARMY_1", ["group"] = "INITIAL/Town/Houses" });
            Assert.AreEqual(UnitPreset.Kind, model.Presets.Kind, "the Units mode's panel lists units presets");

            model.Selection.Set([ObjectRef.Unit("UNIT_1"), ObjectRef.Unit("UNIT_2")]);
            CommandRun saved = await Run(model, "edit.saveAsPreset", new JsonObject { ["name"] = "Village" });
            Assert.AreEqual("Village", ((JsonObject)saved.Result!)["name"]!.GetValue<string>());
            Assert.AreEqual("Village", model.Presets.Shown.Single().Name);

            model.Selection.Clear();
            model.SelectTool("units.select");
            await Run(model, "presets.apply", new JsonObject { ["kind"] = UnitPreset.Kind, ["name"] = "Village" });
            UnitPlaceTool tool = model.Tool<UnitPlaceTool>();
            Assert.AreEqual("units.place", model.ActiveTool!.Definition.Id, "applying chooses the place tool");
            Assert.AreEqual("Village", tool.Preset);
            tool.PlaceInto("ARMY_3", "INITIAL");
            tool.Press(100.4f, 120.2f);

            PlacedUnit[] placed = [.. model.ShownDocument!.AllUnits().Where(unit => unit.Army == "ARMY_3")];
            Assert.AreEqual(2, placed.Length, "for another army");
            PlacedUnit farm = placed.Single(unit => unit.Unit.Blueprint == "uec1201");
            CollectionAssert.AreEqual(new[] { "INITIAL", "Village" }, farm.Group.ToArray(), "in a group of the preset's name");
            Assert.AreEqual((94.5f, 120f), (farm.Unit.Position.X, farm.Unit.Position.Z), "its middle on a whole ogrid: the farm stays on the grid");
            CollectionAssert.AreEqual(new[] { "INITIAL", "Village", "Houses" }, placed.Single(unit => unit.Unit.Blueprint == "uec1101").Group.ToArray());
            Assert.AreEqual("Place a preset", model.Map!.Session.UndoDescription);

            CommandRun again = await Run(model, "units.placePreset", new JsonObject { ["name"] = "Village", ["x"] = 30, ["z"] = 30, ["army"] = "ARMY_4", ["heading"] = 90 });
            Assert.AreEqual("INITIAL/Village", ((JsonObject)again.Result!)["group"]!.GetValue<string>());
            Unit turned = model.ShownDocument!.AllUnits().Single(unit => unit.Army == "ARMY_4" && unit.Unit.Blueprint == "uec1201").Unit;
            Assert.AreEqual(30, turned.Position.X, 1e-4, "turned a quarter about its middle");
            Assert.AreEqual(24.5, turned.Position.Z, 1e-4);

            // a unit chosen again leaves the preset
            await Run(model, "units.pick", new JsonObject { ["key"] = "uec1101" });
            Assert.IsNull(tool.Preset);
        }

        [TestMethod]
        public async Task TheArmiesAreAddedRenamedChangedAndRemoved()
        {
            MainViewModel model = await Open();
            CommandRun shown = await Run(model, "map.armies");
            Assert.AreEqual("map", model.SelectedMode!.Definition.Id);
            Assert.AreEqual(10, ((JsonArray)shown.Result!).Count);
            ArmiesViewModel armies = model.Armies;

            await armies.AddArmyCommand.ExecuteAsync(null);
            Assert.AreEqual("ARMY_10", armies.ChosenArmy?.Name, "the new army is chosen");
            Assert.IsTrue(armies.ChosenArmy!.Playable && armies.ChosenArmy.HasSpawn);

            armies.ArmyName = "NORTH";
            await armies.RenameArmyCommand.ExecuteAsync(null);
            Assert.AreEqual("NORTH", armies.ChosenArmy?.Name);
            Assert.IsTrue(model.ShownDocument!.Markers.Spawns.Any(spawn => spawn.Name == "NORTH"), "its spawn too");

            armies.Team = "Top";
            await armies.SetTeamCommand.ExecuteAsync(null);
            Assert.AreEqual("Top", model.ShownDocument!.TeamOf("NORTH"));
            armies.Aeon = true;
            await Task.Yield();
            CollectionAssert.AreEqual(new[] { "aeon" }, model.ShownDocument!.Scenario.ArmyFactions["NORTH"].ToArray());
            Assert.IsTrue(armies.Aeon);

            armies.Alliances.Single(row => row.Other == "ARMY_1").Stance = "Ally";
            await Task.Yield();
            Assert.AreEqual("Ally", model.ShownDocument!.FindArmy("ARMY_1")!.Alliances["NORTH"]);

            armies.Playable = false;
            await Task.Yield();
            Assert.IsFalse(model.ShownDocument!.IsPlayable("NORTH"));
            Assert.IsNull(model.ShownDocument.TeamOf("NORTH"), "an extra army is in no team");

            await armies.RemoveArmyCommand.ExecuteAsync(null);
            Assert.IsNull(model.ShownDocument!.FindArmy("NORTH"));
            Assert.IsNull(armies.ChosenArmy);
            Assert.AreEqual(10, armies.Rows.Count);

            await armies.AddExtraArmyCommand.ExecuteAsync(null);
            Assert.IsFalse(armies.ChosenArmy!.Playable);
            Assert.IsFalse(armies.ChosenArmy.HasSpawn, "an extra army has no spawn");
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "map.renameArmy", new JsonObject { ["army"] = "ARMY_1", ["name"] = "ARMY_2" }));
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "map.setAlliance", new JsonObject { ["army"] = "ARMY_1", ["other"] = "ARMY_2", ["stance"] = "Friends" }));
        }
    }
}
