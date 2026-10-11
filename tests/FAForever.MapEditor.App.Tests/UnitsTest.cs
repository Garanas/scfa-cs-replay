using System.Numerics;
using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project;
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

        /// <summary>
        /// The examples and a test's own enemy base (the examples are all the civilians'): a factory
        /// and two turrets in their role groups, saved as <c>Test enemy base</c>.
        /// </summary>
        private static PresetLibrary ExamplesAndAnEnemyBase()
        {
            string folder = Path.Combine(AppContext.BaseDirectory, "presets", Guid.NewGuid().ToString("N"));
            ExampleUnitPresets.Ensure(Path.Combine(folder, PresetKinds.Units));
            PresetLibrary library = new PresetLibrary(folder);
            UnitPresetGroup group = new UnitPresetGroup("Test enemy base", [],
            [
                new UnitPresetGroup("Factories", [new UnitPresetUnit("ueb0101", 0, 0, 0, 0)], []),
                new UnitPresetGroup("Defense", [new UnitPresetUnit("ueb2101", -6, 0, 0, 0), new UnitPresetUnit("ueb2101", 6, 0, 0, 0)], []),
            ]);
            library.Save(Preset.Of(UnitPresets.Kind, "Test enemy base", new UnitPresetSettings(group, 3, ArmyRoles.Enemy)));
            return library;
        }

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
            armies.NewGroupName = "Town";
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
            model.Selection.Set([ObjectRef.Unit("UNIT_1")]);
            tool.Press(100.4f, 120.2f);

            PlacedUnit[] placed = [.. model.ShownDocument!.AllUnits().Where(unit => unit.Army == "ARMY_3")];
            Assert.AreEqual(2, placed.Length, "for another army");
            PlacedUnit farm = placed.Single(unit => unit.Unit.Blueprint == "uec1201");
            CollectionAssert.AreEqual(new[] { "INITIAL", "Village" }, farm.Group.ToArray(), "in a group of the preset's name");
            Assert.AreEqual((94.5f, 120f), (farm.Unit.Position.X, farm.Unit.Position.Z), "its middle on a whole ogrid: the farm stays on the grid");
            CollectionAssert.AreEqual(new[] { "INITIAL", "Village", "Houses" }, placed.Single(unit => unit.Unit.Blueprint == "uec1101").Group.ToArray());
            Assert.AreEqual("Place a preset", model.Map!.Session.UndoDescription);
            CollectionAssert.AreEquivalent(placed.Select(unit => ObjectRef.Unit(unit.Unit.Name)).ToArray(), model.Selection.Items.ToArray(), "what was placed is selected, not the unit selected before");

            CommandRun again = await Run(model, "units.placePreset", new JsonObject { ["name"] = "Village", ["x"] = 30, ["z"] = 30, ["army"] = "ARMY_4", ["heading"] = 90 });
            Assert.AreEqual("INITIAL/Village", ((JsonObject)again.Result!)["group"]!.GetValue<string>());
            Unit turned = model.ShownDocument!.AllUnits().Single(unit => unit.Army == "ARMY_4" && unit.Unit.Blueprint == "uec1201").Unit;
            Assert.AreEqual(30, turned.Position.X, 1e-4, "turned a quarter about its middle");
            Assert.AreEqual(24.5, turned.Position.Z, 1e-4);

            // a unit chosen again leaves the preset
            await Run(model, "units.pick", new JsonObject { ["key"] = "uec1101" });
            Assert.IsNull(tool.Preset);
        }

        /// <summary>
        /// A group placed from a preset knows the preset (user story U-9): the tree's badge and the
        /// list's count, selecting the instance, the preset's file changing on disk, saving the
        /// instance back (asked first), detaching it, and showing the preset.
        /// </summary>
        [TestMethod]
        public async Task APlacedPresetIsAnInstanceThatIsSelectedSavedBackAndDetached()
        {
            MainViewModel model = await Open();
            PresetLibrary library = new PresetLibrary(Path.Combine(AppContext.BaseDirectory, "presets", Guid.NewGuid().ToString("N")));
            model.Presets.Use(library);
            await Run(model, "units.add", new JsonObject { ["blueprint"] = "uec1201", ["x"] = 50, ["z"] = 50, ["army"] = "ARMY_1", ["group"] = "INITIAL/Town" });
            await Run(model, "units.add", new JsonObject { ["blueprint"] = "uec1101", ["x"] = 60, ["z"] = 50, ["army"] = "ARMY_1", ["group"] = "INITIAL/Town" });
            await Run(model, "units.saveGroupAsPreset", new JsonObject { ["army"] = "ARMY_1", ["group"] = "INITIAL/Town", ["name"] = "Village" });
            Assert.IsNull(model.ShownDocument!.FindGroup("ARMY_1", ["INITIAL", "Town"])!.Instance, "a group saved as a preset is no instance of it");

            await Run(model, "units.placePreset", new JsonObject { ["name"] = "Village", ["x"] = 100, ["z"] = 100, ["army"] = "ARMY_2", ["heading"] = 90 });
            await Run(model, "units.placePreset", new JsonObject { ["name"] = "Village", ["x"] = 200, ["z"] = 100, ["army"] = "ARMY_3" });
            PresetInstance instance = model.ShownDocument!.FindGroup("ARMY_2", ["INITIAL", "Village"])!.Instance ?? throw new AssertFailedException("the group placed is an instance");
            Assert.AreEqual("Village", instance.Preset);
            Assert.AreEqual((100f, 100f), (instance.X, instance.Z));
            Assert.AreEqual(MathF.PI / 2, instance.Heading, 1e-5);
            Assert.IsTrue((DateTimeOffset.Now - instance.PlacedAt).Duration() < TimeSpan.FromMinutes(1), "placed now");
            Assert.AreEqual("Place a preset", model.Map!.Session.UndoDescription);

            // the tree's badge and the list's count
            GroupNode node = model.Armies.Tree.SelectMany(n => n.SelfAndBelow()).Single(n => n.Key == "ARMY_2:INITIAL/Village");
            Assert.IsTrue(node.HasInstance);
            Assert.AreEqual(("Village", PresetInstanceState.Current, ""), (node.InstanceName, node.InstanceState, node.InstanceStateText));
            StringAssert.Contains(node.Description, "placed from the preset Village");
            Assert.AreEqual(new PresetInstanceCount(2, 0), model.Presets.Instances["Village"]);
            Assert.AreEqual(0, model.Problems.Items.Count(item => item.Problem.Check == "preset-changed"));

            // select the instance: the group's units; a group not placed from a preset refuses
            CommandRun selected = await Run(model, "units.selectInstance", new JsonObject { ["army"] = "ARMY_2", ["group"] = "INITIAL/Village" });
            Assert.AreEqual(2, selected.Result);
            Assert.IsTrue(model.Selection.Items.All(thing => model.ShownDocument!.FindUnit(thing.Name!)!.Army == "ARMY_2"));
            StringAssert.Contains(model.Status, "of the instance INITIAL/Village of ARMY_2 (the preset Village)");
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "units.selectInstance", new JsonObject { ["army"] = "ARMY_1", ["group"] = "INITIAL/Town" }), "no instance");

            // the preset's file changes on disk: the instances are out of date, the list and the check say so
            library.Save(Preset.Of(UnitPresets.Kind, "Village", library.Find(UnitPresets.Kind, "Village")!.Read<UnitPresetSettings>() with { Source = "edited by hand" }));
            model.Presets.Refresh();
            Assert.AreEqual(PresetInstanceState.Changed, node.InstanceState, "the same node tells its new state");
            Assert.AreEqual("preset changed", node.InstanceStateText);
            Assert.AreEqual(new PresetInstanceCount(2, 2), model.Presets.Instances["Village"]);
            Assert.AreEqual(2, model.Problems.Items.Count(item => item.Problem.Check == "preset-changed"));
            model.Armies.Choose("ARMY_2", ["INITIAL", "Village"]);
            Assert.IsTrue(model.Armies.IsInstanceChosen);
            Assert.AreEqual("Village", model.Armies.InstancePreset);
            StringAssert.StartsWith(model.Armies.InstancePlacedText, "at 100, 100 · 90° · ");
            Assert.AreEqual("×2 placed", model.Armies.InstanceCountText);
            StringAssert.StartsWith(model.Armies.InstanceNote, "The preset changed after this was placed");

            // save back: asked first (without a person, confirm must be given); then the file holds the group's layout and the instance is current
            await Run(model, "units.add", new JsonObject { ["blueprint"] = "uec1101", ["x"] = 110, ["z"] = 110, ["army"] = "ARMY_2", ["group"] = "INITIAL/Village" });
            Assert.AreEqual(PresetInstanceState.Changed, model.Armies.ChosenGroup!.InstanceState, "a unit added to it keeps the link");
            InvalidOperationException asked = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Run(model, "units.saveInstance", new JsonObject { ["army"] = "ARMY_2", ["group"] = "INITIAL/Village" }));
            StringAssert.Contains(asked.Message, "confirm: true");
            Assert.AreEqual("edited by hand", library.Find(UnitPresets.Kind, "Village")!.Read<UnitPresetSettings>().Source, "nothing was written");
            CommandRun saved = await Run(model, "units.saveInstance", new JsonObject { ["army"] = "ARMY_2", ["group"] = "INITIAL/Village", ["confirm"] = true });
            Assert.AreEqual(3, ((JsonObject)saved.Result!)["units"]!.GetValue<int>());
            UnitPresetSettings settings = library.Find(UnitPresets.Kind, "Village")!.Read<UnitPresetSettings>();
            Assert.AreEqual(3, settings.Count);
            Assert.AreEqual("edited by hand", settings.Source, "the file keeps what is not the layout");
            Assert.AreEqual(PresetInstanceState.Current, model.Armies.ChosenGroup!.InstanceState);
            Assert.AreEqual(PresetInstanceState.Changed, model.Armies.Tree.SelectMany(n => n.SelfAndBelow()).Single(n => n.Key == "ARMY_3:INITIAL/Village").InstanceState, "the other instance is out of date now");
            Assert.AreEqual(new PresetInstanceCount(2, 1), model.Presets.Instances["Village"]);
            Assert.AreEqual("Save the instance back to its preset", model.Map.Session.UndoDescription, "one step");

            // undo of the save puts the preset file back as it was (the hand-edited one), redo writes the saved layout again
            byte[] savedFile = File.ReadAllBytes(library.FileOf(UnitPresets.Kind, "Village"));
            model.Undo();
            Assert.AreEqual(2, library.Find(UnitPresets.Kind, "Village")!.Read<UnitPresetSettings>().Count, "the file holds its units from before the save");
            Assert.AreEqual("edited by hand", library.Find(UnitPresets.Kind, "Village")!.Read<UnitPresetSettings>().Source);
            Assert.AreEqual(PresetInstanceState.Changed, model.Armies.Tree.SelectMany(n => n.SelfAndBelow()).Single(n => n.Key == "ARMY_2:INITIAL/Village").InstanceState, "the instance is out of date again");
            model.Redo();
            CollectionAssert.AreEqual(savedFile, File.ReadAllBytes(library.FileOf(UnitPresets.Kind, "Village")), "redo writes what the save wrote");
            Assert.AreEqual(PresetInstanceState.Current, model.Armies.Tree.SelectMany(n => n.SelfAndBelow()).Single(n => n.Key == "ARMY_2:INITIAL/Village").InstanceState);

            // show the preset: the list highlights it; detach: the link is gone, the units stay, undo brings it back
            CommandRun shown = await Run(model, "units.showPreset", new JsonObject { ["name"] = "Village" });
            Assert.AreEqual("Village", model.Presets.Highlighted);
            Assert.IsTrue(((JsonObject)shown.Result!)["found"]!.GetValue<bool>());
            Assert.AreEqual("units", model.SelectedMode.Definition.Id);
            await Run(model, "units.detachInstance", new JsonObject { ["army"] = "ARMY_3", ["group"] = "INITIAL/Village" });
            Assert.IsNull(model.ShownDocument!.FindGroup("ARMY_3", ["INITIAL", "Village"])!.Instance);
            Assert.AreEqual(2, model.ShownDocument!.UnitsIn("ARMY_3", ["INITIAL", "Village"]).Count());
            Assert.AreEqual(new PresetInstanceCount(1, 0), model.Presets.Instances["Village"]);
            model.Undo();
            Assert.IsNotNull(model.ShownDocument!.FindGroup("ARMY_3", ["INITIAL", "Village"])!.Instance);

            // a project keeps the link; the export leaves it out
            string project = Path.Combine(AppContext.BaseDirectory, "projects", Guid.NewGuid().ToString("N"));
            await model.SaveAsAsync(project);
            StringAssert.Contains(File.ReadAllText(Path.Combine(project, "armies.json")), "\"preset\": \"Village\"");
        }

        [TestMethod]
        public async Task AnExampleUnitsPresetGoesToTheArmyOfItsRoleAndAWreckFieldIntoTheWreckage()
        {
            MainViewModel model = await Open();
            model.Presets.Use(ExamplesAndAnEnemyBase());
            Assert.IsTrue(model.Presets.Shown.Any(preset => preset.Name == "Aeon civilian town"), "the Units mode's panel lists the examples");

            await Run(model, "presets.apply", new JsonObject { ["kind"] = UnitPreset.Kind, ["name"] = "Aeon civilian town" });
            UnitPlaceTool tool = model.Tool<UnitPlaceTool>();
            Assert.AreEqual(ArmyRoles.Civilian, tool.PresetRole);
            tool.PlaceInto("ARMY_3", "INITIAL");
            tool.Press(200, 200);
            Assert.IsTrue(model.Selection.Items.Count() > 10);
            Assert.IsTrue(model.Selection.Items.All(unit => model.ShownDocument!.FindUnit(unit.Name!)!.Army == "NEUTRAL_CIVILIAN"), "a civilian town goes to the civilians, whatever the tool's army");
            CollectionAssert.AreEqual(new[] { "INITIAL", "Aeon civilian town" }, model.ShownDocument!.FindUnit(model.Selection.Items[0].Name!)!.Group.ToArray(), "in a group of its name");

            await Run(model, "presets.apply", new JsonObject { ["kind"] = UnitPreset.Kind, ["name"] = "Test enemy base" });
            Assert.AreEqual(ArmyRoles.Enemy, tool.PresetRole);
            tool.Press(300, 300);
            Assert.AreEqual(3, model.Selection.Items.Count());
            Assert.IsTrue(model.Selection.Items.All(unit => model.ShownDocument!.FindUnit(unit.Name!)!.Army == "ARMY_9"), "an enemy's base goes to the army no player takes, whatever the tool's army");
            CollectionAssert.AreEqual(new[] { "INITIAL", "Test enemy base", "Factories" }, model.ShownDocument!.FindUnit(model.Selection.Items[0].Name!)!.Group.ToArray(), "with the base's role groups");

            CommandRun wrecks = await Run(model, "units.placePreset", new JsonObject { ["name"] = "Wreck field, mixed", ["x"] = 100, ["z"] = 300 });
            Assert.AreEqual("NEUTRAL_CIVILIAN", ((JsonObject)wrecks.Result!)["army"]!.GetValue<string>());
            StringAssert.StartsWith(((JsonObject)wrecks.Result!)["group"]!.GetValue<string>(), UnitWrecks.GroupName + "/");
            Assert.IsTrue(model.Selection.Items.All(unit => UnitWrecks.IsWreck(model.ShownDocument!.FindUnit(unit.Name!)!)));

            CommandRun given = await Run(model, "units.placePreset", new JsonObject { ["name"] = "UEF farm", ["x"] = 300, ["z"] = 100, ["army"] = "ARMY_4" });
            Assert.AreEqual("ARMY_4", ((JsonObject)given.Result!)["army"]!.GetValue<string>(), "an army given wins over the role");
        }

        [TestMethod]
        public async Task AnEnemyPresetOnANewMapAddsAnArmyOfItsOwn()
        {
            MainViewModel model = new MainViewModel(new NoDialogs());
            model.Assets.Use(Catalog());
            model.OpenNewMap(NewMap.Create(new NewMapSettings { Size = 256 }));
            model.Presets.Use(ExamplesAndAnEnemyBase());

            await Run(model, "presets.apply", new JsonObject { ["kind"] = UnitPreset.Kind, ["name"] = "UEF civilian town" });
            CommandRun town = await Run(model, "units.placePreset", new JsonObject { ["x"] = 60, ["z"] = 60 });
            string civilians = ((JsonObject)town.Result!)["army"]!.GetValue<string>();

            await Run(model, "presets.apply", new JsonObject { ["kind"] = UnitPreset.Kind, ["name"] = "Test enemy base" });
            StringAssert.Contains(model.Status, $"the enemy army ({UnitPresets.EnemyArmy}, a new extra army)", "the hint says the army placing it adds");
            CommandRun enemy = await Run(model, "units.placePreset", new JsonObject { ["x"] = 190, ["z"] = 190 });
            JsonObject result = (JsonObject)enemy.Result!;
            Assert.AreEqual(UnitPresets.EnemyArmy, result["army"]!.GetValue<string>());
            Assert.AreNotEqual(civilians, UnitPresets.EnemyArmy, "the enemy base is not in the town's army");
            Assert.IsTrue(result["addedArmy"]!.GetValue<bool>());
            StringAssert.Contains(model.Status, "added the extra army ARMY_9");
            Assert.AreEqual("Place a preset", model.Map!.Session.UndoDescription, "the army comes with the units, in one step");
            model.Undo();
            Assert.IsFalse(model.ShownDocument!.Armies.Any(army => army.Name == UnitPresets.EnemyArmy), "undone, the army goes with the base");
        }

        [TestMethod]
        public void ATreeRowRepeatsThePresetNameOnlyWhenTheGroupIsNamedOtherwise()
        {
            Assert.IsTrue(GroupNode.NamedAfter("Cybran farm", "Cybran farm"));
            Assert.IsTrue(GroupNode.NamedAfter("cybran farm 2", "Cybran farm"), "a second placement");
            Assert.IsFalse(GroupNode.NamedAfter("Cybran farm north", "Cybran farm"));
            Assert.IsFalse(GroupNode.NamedAfter("Base", "Cybran farm"));
            Assert.IsFalse(GroupNode.NamedAfter("Cybran farm 2b", "Cybran farm"));
        }

        [TestMethod]
        public async Task TheUnitPresetsListFiltersByStarCivilianWrecksAndMine()
        {
            MainViewModel model = new MainViewModel(new NoDialogs());
            model.Assets.Use(Catalog());
            model.OpenNewMap(NewMap.Create(new NewMapSettings { Size = 256 }));
            PresetLibrary library = ExamplesAndAnEnemyBase();
            model.Presets.Use(library);
            model.SelectMode("units");
            model.Assets.Favourites.Open(Path.Combine(library.Folder, "favourites.json"));
            Assert.IsTrue(model.Presets.HasFilters);
            int all = model.Presets.Shown.Count;
            Assert.AreEqual(5, model.Presets.FilterChips.Count);

            JsonObject mine = (JsonObject)(await Run(model, "presets.filter", new JsonObject { ["filter"] = "mine" })).Result!;
            CollectionAssert.AreEqual(new[] { "Test enemy base" }, mine["shown"]!.AsArray().Select(name => name!.GetValue<string>()).ToArray(), "the examples are not the user's");
            Assert.IsTrue(model.Presets.FilterChips.Single(chip => chip.IsOn).Filter == PresetFilter.Mine);

            await Run(model, "presets.filter", new JsonObject { ["filter"] = "Civilian" });
            Assert.IsTrue(model.Presets.Shown.Count > 0 && model.Presets.Shown.Count < all);
            Assert.IsTrue(model.Presets.Shown.All(preset => preset.Read<UnitPresetSettings>().Role == ArmyRoles.Civilian));
            await Run(model, "presets.filter", new JsonObject { ["filter"] = "wrecks" });
            Assert.IsTrue(model.Presets.Shown.Count > 0 && model.Presets.Shown.All(preset => preset.Read<UnitPresetSettings>().Wrecks));

            await Run(model, "presets.filter", new JsonObject { ["filter"] = "starred" });
            Assert.AreEqual(0, model.Presets.Shown.Count);
            Assert.IsTrue(model.Presets.NoneMatch);
            Assert.IsFalse(model.Presets.IsEmpty, "an empty filter is not an empty list");
            await Run(model, "presets.star", new JsonObject { ["kind"] = UnitPresets.Kind, ["name"] = "UEF civilian town" });
            CollectionAssert.AreEqual(new[] { "UEF civilian town" }, model.Presets.Shown.Select(preset => preset.Name).ToArray());
            CollectionAssert.Contains(model.Assets.Favourites.Starred.ToArray(), PresetsViewModel.StarKey(UnitPresets.Kind, "UEF civilian town"), "kept as the shelf keeps its stars");
            await Run(model, "presets.star", new JsonObject { ["kind"] = UnitPresets.Kind, ["name"] = "UEF civilian town", ["on"] = false });
            Assert.AreEqual(0, model.Presets.Shown.Count);

            await Run(model, "presets.filter", new JsonObject { ["filter"] = "all" });
            Assert.AreEqual(all, model.Presets.Shown.Count);
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "presets.filter", new JsonObject { ["filter"] = "tall" }));
        }

        [TestMethod]
        public async Task ALineOfStructuresIsPlacedAsOneStepWithItsImagesIntoTheGroup()
        {
            MainViewModel model = await Open();
            await Run(model, "select.symmetry", new JsonObject { ["kind"] = "rotational", ["count"] = 2 });
            int steps = model.History.Count;

            CommandRun line = await Run(model, "units.addMany", new JsonObject { ["mode"] = "line", ["x0"] = 40.2, ["z0"] = 60.4, ["x1"] = 52.3, ["z1"] = 60.4, ["blueprint"] = "uec1201", ["army"] = "ARMY_1", ["group"] = "INITIAL/Farms" });
            JsonObject result = (JsonObject)line.Result!;
            Assert.AreEqual(5, result["count"]!.GetValue<int>(), "3 wide along x: every 3 ogrids over 12");
            Assert.AreEqual(5, result["images"]!.GetValue<int>(), "an image each in the other part");
            Assert.AreEqual(steps + 1, model.History.Count, "one step");
            StringAssert.StartsWith(model.Map!.Session.UndoDescription, "Place 5 units (UEF Farm) in a line");
            Assert.AreEqual(10, model.Selection.Count, "what was placed is selected, with its images");

            string[] names = [.. ((JsonArray)result["units"]!).Select(name => name!.GetValue<string>())];
            PlacedUnit[] placed = [.. names.Select(name => model.ShownDocument!.FindUnit(name)!)];
            Assert.IsTrue(placed.All(unit => unit.Army == "ARMY_1" && UnitGroupEdits.PathText(unit.Group) == "INITIAL/Farms"), "into the group, made for them");
            Assert.AreEqual((40.5f, 60f), (placed[0].Unit.Position.X, placed[0].Unit.Position.Z), "on the build grid");
            Assert.AreEqual(3, placed[1].Unit.Position.X - placed[0].Unit.Position.X, 1e-4, "butting together at the footprint");
            Assert.IsTrue(placed.All(unit => unit.Unit.Orientation.Y == 0), "facing along x");
            Assert.IsTrue(model.Undo());
            Assert.IsTrue(names.All(name => model.ShownDocument!.FindUnit(name) is null), "undone together");

            CommandRun tanks = await Run(model, "units.addMany", new JsonObject { ["mode"] = "line", ["x0"] = 100, ["z0"] = 100, ["x1"] = 100, ["z1"] = 109, ["blueprint"] = "uel0101", ["spacing"] = 3, ["faceAlong"] = true });
            string first = ((JsonArray)((JsonObject)tanks.Result!)["units"]!)[0]!.GetValue<string>();
            Assert.AreEqual(4, ((JsonObject)tanks.Result!)["count"]!.GetValue<int>(), "mobile units at the spacing");
            Assert.AreEqual(90, SelectionInspector.Degrees(UnitNamed(model, first).Orientation.Y), 1e-3, "facing along the line, down +z");

            CommandRun edge = await Run(model, "units.addMany", new JsonObject { ["mode"] = "line", ["x0"] = -5, ["z0"] = 5, ["x1"] = 10, ["z1"] = 5, ["blueprint"] = "uel0101", ["spacing"] = 3 });
            Assert.AreEqual(2, ((JsonObject)edge.Result!)["outside"]!.GetValue<int>(), "the places at -5 and -2 are left out");
            Assert.AreEqual(4, ((JsonObject)edge.Result!)["count"]!.GetValue<int>(), "1, 4, 7 and 10");
            StringAssert.Contains(model.Status, "left out beyond the map's edge");
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "units.addMany", new JsonObject { ["mode"] = "line", ["x0"] = -5, ["z0"] = 5, ["x1"] = -1, ["z1"] = 5, ["blueprint"] = "uel0101" }), "nothing of it lies on the map");
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "units.addMany", new JsonObject { ["mode"] = "ring", ["x"] = -5, ["z"] = 5, ["blueprint"] = "uel0101" }), "the click of a ring must lie on the map");
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "units.addMany", new JsonObject { ["mode"] = "line", ["blueprint"] = "uel0101" }), "no points");
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "units.addMany", new JsonObject { ["mode"] = "spiral", ["x0"] = 5, ["z0"] = 5, ["blueprint"] = "uel0101" }));
        }

        [TestMethod]
        public async Task AHollowRectangleOfWallsAndARingAroundTheSelectionOrTheStructureUnderTheMouse()
        {
            MainViewModel model = await Open();
            CommandRun walls = await Run(model, "units.addMany", new JsonObject { ["mode"] = "rectangle", ["x0"] = 20.3, ["z0"] = 30.2, ["x1"] = 30.1, ["z1"] = 35.9, ["blueprint"] = "uec1101", ["hollow"] = true, ["army"] = "ARMY_2" });
            Assert.AreEqual(2 * 10 + 2 * 4, ((JsonObject)walls.Result!)["count"]!.GetValue<int>(), "the border of 10 by 6 walls of 1 by 1");
            StringAssert.Contains(model.Status, "in a hollow rectangle");
            string[] names = [.. ((JsonArray)((JsonObject)walls.Result!)["units"]!).Select(name => name!.GetValue<string>())];
            Assert.IsTrue(names.All(name => { Unit wall = UnitNamed(model, name); return wall.Position.X is 20.5f or 29.5f || wall.Position.Z is 30.5f or 35.5f; }), "only the border");

            CommandRun filled = await Run(model, "units.addMany", new JsonObject { ["mode"] = "rectangle", ["x0"] = 60, ["z0"] = 60, ["x1"] = 66, ["z1"] = 64, ["blueprint"] = "uel0101", ["spacing"] = 2, ["heading"] = 45 });
            Assert.AreEqual(4 * 3, ((JsonObject)filled.Result!)["count"]!.GetValue<int>(), "mobile units on a grid of the spacing");

            // a ring around a factory: the selection, else the structure under the click
            await Run(model, "units.add", new JsonObject { ["blueprint"] = "uec1201", ["x"] = 100.5, ["z"] = 100, ["army"] = "ARMY_1" });
            string factory = model.ShownDocument!.AllUnits().Single(unit => unit.Unit.Blueprint == "uec1201").Unit.Name;
            model.Selection.Set([ObjectRef.Unit(factory)]);
            CommandRun ring = await Run(model, "units.addMany", new JsonObject { ["mode"] = "ring", ["blueprint"] = "uec1101", ["distance"] = 1, ["army"] = "ARMY_1" });
            JsonObject around = (JsonObject)ring.Result!;
            Assert.AreEqual(2 * 7 + 2 * 4, around["count"]!.GetValue<int>(), "a 3 by 2 footprint, one ogrid of room, walls of 1 by 1: 7 by 6 around it");
            string[] ringed = [.. ((JsonArray)around["units"]!).Select(name => name!.GetValue<string>())];
            Assert.AreEqual(97.5f, ringed.Min(name => UnitNamed(model, name).Position.X), "the footprint spans 99 to 102; the walls' inner edge at 98");
            Assert.AreEqual(103.5f, ringed.Max(name => UnitNamed(model, name).Position.X));
            Assert.AreEqual(97.5f, ringed.Min(name => UnitNamed(model, name).Position.Z), "the footprint spans 99 to 101 along z");
            Assert.AreEqual(102.5f, ringed.Max(name => UnitNamed(model, name).Position.Z));
            CollectionAssert.AreEquivalent(ringed.Select(ObjectRef.Unit).ToArray(), model.Selection.Items.ToArray(), "the ring is selected");

            model.Selection.Clear();
            CommandRun under = await Run(model, "units.addMany", new JsonObject { ["mode"] = "ring", ["x"] = 101, ["z"] = 100.5, ["blueprint"] = "uec1101", ["distance"] = 0, ["army"] = "ARMY_1" });
            Assert.AreEqual(2 * 5 + 2 * 2, ((JsonObject)under.Result!)["count"]!.GetValue<int>(), "around the factory under the click, touching it");
            model.Selection.Clear();
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "units.addMany", new JsonObject { ["mode"] = "ring", ["x"] = 200, ["z"] = 200, ["blueprint"] = "uec1101" }), "nothing to ring there");
        }

        [TestMethod]
        public async Task AScatterPlacesWrecksApartAndTheSameSeedGivesTheSamePlaces()
        {
            MainViewModel model = await Open();
            JsonObject arguments = new JsonObject { ["mode"] = "scatter", ["x"] = 100, ["z"] = 100, ["radius"] = 10, ["count"] = 8, ["leastDistance"] = 3, ["seed"] = 5, ["blueprint"] = "uel0101", ["wreck"] = true, ["army"] = "ARMY_1" };
            CommandRun scatter = await Run(model, "units.addMany", arguments);
            JsonObject result = (JsonObject)scatter.Result!;
            Assert.AreEqual(8, result["count"]!.GetValue<int>());
            Assert.IsTrue(result["wreck"]!.GetValue<bool>());
            StringAssert.StartsWith(result["group"]!.GetValue<string>(), UnitWrecks.GroupName);
            Unit[] wrecks = [.. ((JsonArray)result["units"]!).Select(name => UnitNamed(model, name!.GetValue<string>()))];
            Assert.IsTrue(wrecks.All(wreck => UnitWrecks.IsWreck(model.ShownDocument!.FindUnit(wreck.Name)!)));
            for (int i = 0; i < wrecks.Length; i++)
            {
                Assert.IsTrue(Vector2.Distance(new Vector2(wrecks[i].Position.X, wrecks[i].Position.Z), new Vector2(100, 100)) <= 10, "within the circle");
                for (int j = i + 1; j < wrecks.Length; j++)
                {
                    Assert.IsTrue(Vector2.Distance(new Vector2(wrecks[i].Position.X, wrecks[i].Position.Z), new Vector2(wrecks[j].Position.X, wrecks[j].Position.Z)) >= 3, "the least distance");
                }
            }
            Assert.IsTrue(wrecks.Select(wreck => wreck.Orientation.Y).Distinct().Count() > 1, "random headings");
            Assert.AreEqual(8, model.Selection.Count);

            model.Undo();
            CommandRun again = await Run(model, "units.addMany", (JsonObject)arguments.DeepClone());
            Unit[] same = [.. ((JsonArray)((JsonObject)again.Result!)["units"]!).Select(name => UnitNamed(model, name!.GetValue<string>()))];
            CollectionAssert.AreEqual(wrecks.Select(wreck => wreck.Position).ToArray(), same.Select(wreck => wreck.Position).ToArray(), "the same seed gives the same places");
        }

        [TestMethod]
        public async Task ThePlaceToolsModeAndSettingsAreSetAndItsDragPlacesALineWhichEscapeCancels()
        {
            MainViewModel model = await Open("units.select");
            CommandRun set = await Run(model, "units.placeMode", new JsonObject { ["mode"] = "line", ["spacing"] = 3, ["faceAlong"] = false, ["heading"] = 90 });
            UnitPlaceTool tool = model.Tool<UnitPlaceTool>();
            Assert.AreEqual("units.place", model.ActiveTool!.Definition.Id, "choosing a way chooses the Place tool");
            Assert.AreEqual(PlaceMode.Line, tool.Mode);
            Assert.AreEqual((3.0, false, 90.0), (tool.Spacing, tool.FaceAlong, tool.Heading));
            Assert.AreEqual("Line", ((JsonObject)set.Result!)["mode"]!.GetValue<string>());
            Assert.IsTrue(tool.Modes.Single(choice => choice.Mode == PlaceMode.Line).IsOn, "the segmented control shows it");

            await Run(model, "units.pick", new JsonObject { ["key"] = "uel0101" });
            int steps = model.History.Count;
            tool.Press(10, 10, ToolInput.Default);
            tool.Drag(22, 10.2f, ToolInput.Default);
            Assert.IsTrue(tool.IsDragging);
            Assert.AreEqual(5, tool.PlannedCount, "every 3 ogrids over 12");
            StringAssert.Contains(model.Status, "5 × Snoop");
            Assert.AreEqual(2 * 5, tool.Shapes(ToolView.Flat(0.5f)).Count, "the footprints under the mouse, each with its shadow");
            Assert.IsTrue(tool.Key("Escape", ToolModifiers.None), "Escape cancels the drag");
            Assert.IsFalse(tool.IsDragging);
            tool.Release();
            Assert.AreEqual(steps, model.History.Count, "nothing placed");

            tool.Press(10, 10, ToolInput.Default);
            tool.Drag(22, 20, new ToolInput(ToolModifiers.Shift, 0.5f));
            tool.Release();
            await Task.Yield();
            Assert.AreEqual(steps + 1, model.History.Count, "the release places the line as one step");
            StringAssert.StartsWith(model.Map!.Session.UndoDescription, "Place 6 units (Snoop) in a line");
            Unit last = UnitNamed(model, model.Selection.Items[^1].Name!);
            Assert.AreEqual(last.Position.X - 10, last.Position.Z - 10, 0.01, "with shift the line keeps to 45 degrees");
            Assert.AreEqual(90, SelectionInspector.Degrees(last.Orientation.Y), 1e-3, "not facing along: the facing set");

            CommandRun next = await Run(model, "units.placeMode");
            Assert.AreEqual("Rectangle", ((JsonObject)next.Result!)["mode"]!.GetValue<string>(), "without a mode, the next way");
            await Run(model, "units.placeMode", new JsonObject { ["mode"] = "scatter", ["radius"] = 12, ["count"] = 4, ["leastDistance"] = 1 });
            Assert.AreEqual(12, tool.CursorRadius, "the scatter brush's circle");
            tool.Press(100, 100, ToolInput.Default);
            tool.Release();
            await Task.Yield();
            Assert.AreEqual(steps + 2, model.History.Count, "a click of the scatter brush is one step");
            Assert.AreEqual(4, model.Selection.Count);
            StringAssert.StartsWith(model.Map!.Session.UndoDescription, "Scatter Snoop");
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "units.placeMode", new JsonObject { ["mode"] = "spiral" }));
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
