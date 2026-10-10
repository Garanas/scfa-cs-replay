using System.Collections.Immutable;
using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Generation;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// Content to use again (user stories F-10, T-2, M-2, F-9): selection presets, stamps, map
    /// templates, stars and recents.
    /// </summary>
    [TestClass]
    public class ContentTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private sealed class NoDialogs : IEditorDialogs
        {
            public Task<string?> PickFolderAsync(string title, string? startIn = null) => Task.FromResult<string?>(null);

            public Task<SaveChoice> AskToSaveChangesAsync(string mapName) => Task.FromResult(SaveChoice.Discard);
        }

        internal static async Task<MainViewModel> Open(string tool = "map.select")
        {
            MainViewModel model = new MainViewModel(new NoDialogs());
            model.Assets.Use(UnitsTest.Catalog());
            model.Presets.Use(new PresetLibrary(Path.Combine(AppContext.BaseDirectory, "presets", Guid.NewGuid().ToString("N"))));
            await model.OpenAsync(HardFfa);
            model.SelectTool(tool);
            return model;
        }

        /// <summary>
        /// HardFFA open in a workspace of its own whose assets are the test assets.
        /// </summary>
        internal static async Task<(MainViewModel Model, Workspace Workspace)> OpenInWorkspace(string tool = "map.select")
        {
            string root = Path.Combine(AppContext.BaseDirectory, "workspaces", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "game"));
            Directory.CreateDirectory(Path.Combine(root, "vault"));
            Workspace workspace = Workspace.Create(Path.Combine(root, "workspace"), Path.Combine(root, "game"), null, Path.Combine(root, "vault"));
            string assets = TestAssets.Folder(unitModel: true);
            foreach (string file in Directory.EnumerateFiles(assets, "*", SearchOption.AllDirectories))
            {
                string to = Path.Combine(workspace.AssetsFolder, Path.GetRelativePath(assets, file));
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Copy(file, to);
            }
            MainViewModel model = new MainViewModel(new NoDialogs());
            model.Setup.Use(workspace);
            await model.LoadAssetsAsync(workspace);
            await model.OpenAsync(HardFfa);
            model.SelectTool(tool);
            return (model, workspace);
        }

        internal static Task<CommandRun> Run(MainViewModel model, string id, JsonObject? arguments = null) =>
            model.Commands.RunAsync(id, arguments ?? [], interactive: false);

        [TestMethod]
        public async Task ASelectionOfSeveralKindsIsSavedAsOnePresetAndPlacedWithTheSymmetry()
        {
            MainViewModel model = await Open();
            Assert.AreEqual(SelectionPresets.Kind, model.Presets.Kind, "the Map mode's panel lists selection presets");
            await Run(model, "units.add", new JsonObject { ["blueprint"] = "uec1201", ["x"] = 60, ["z"] = 60, ["army"] = "NEUTRAL_CIVILIAN", ["group"] = "INITIAL/Village" });
            await Run(model, "props.place", new JsonObject { ["key"] = "/env/evergreen/props/rocks/rock01_prop.bp", ["x"] = 64, ["z"] = 58 });
            int rock = model.ShownDocument!.Props[^1].Id;
            await Run(model, "markers.place", new JsonObject { ["kind"] = "mass", ["x"] = 56, ["z"] = 62 });
            ObjectRef mass = model.Selection.Items.Single();
            model.Selection.Set([ObjectRef.Unit("UNIT_1"), ObjectRef.Prop(rock), mass]);

            CommandRun saved = await Run(model, "edit.saveSelectionPreset");
            JsonObject result = (JsonObject)saved.Result!;
            Assert.AreEqual("Village", result["name"]!.GetValue<string>(), "named after the units' group");
            Assert.AreEqual("1 unit, 1 prop, 1 marker", result["summary"]!.GetValue<string>());
            Assert.AreEqual("Village", model.Presets.Shown.Single().Name);

            await Run(model, "presets.apply", new JsonObject { ["kind"] = SelectionPresets.Kind, ["name"] = "Village" });
            SelectionPresetTool tool = model.Tool<SelectionPresetTool>();
            Assert.AreEqual("map.preset", model.ActiveTool!.Definition.Id, "applying chooses the Preset tool");
            Assert.AreEqual("Preset Village (1 unit, 1 prop, 1 marker)", tool.Title);
            Assert.AreEqual("NEUTRAL_CIVILIAN", tool.Army, "the civilians' units");

            int props = model.ShownDocument!.Props.Length;
            int steps = model.History.Count;
            tool.Press(150, 100);
            Assert.AreEqual(steps + 1, model.History.Count, "one step of undo");
            Assert.AreEqual("Place a selection preset", model.Map!.Session.UndoDescription);
            Assert.AreEqual(props + 1, model.ShownDocument!.Props.Length);
            Assert.AreEqual(3, model.Selection.Count, "what it placed is selected");
            PlacedUnit placed = model.ShownDocument.AllUnits().Single(unit => unit.Army == "NEUTRAL_CIVILIAN" && unit.Unit.Blueprint == "uec1201" && unit.Unit.Name != "UNIT_1");
            CollectionAssert.AreEqual(new[] { "INITIAL", "Village 1" }, placed.Group.ToArray(), "a new group named after the preset in INITIAL, beside the one it was saved from");

            // Q and E turn by quarter turns
            Assert.IsTrue(tool.Key("E", ToolModifiers.None));
            Assert.AreEqual(90, tool.Heading);

            // with the symmetry, its images in the other part
            await Run(model, "select.symmetry", new JsonObject { ["kind"] = "rotational", ["count"] = 2 });
            CommandRun mirrored = await Run(model, "edit.placePreset", new JsonObject { ["x"] = 100, ["z"] = 60 });
            Assert.AreEqual(6, ((JsonArray)mirrored.Result!).Count, "three things and their three images");
            Assert.AreEqual(6, model.Selection.Count);

            Assert.IsTrue(model.Undo());
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "edit.placePreset", new JsonObject { ["name"] = "Nothing" }));
        }

        [TestMethod]
        public async Task APresetWithASpawnMovesTheNearestSpawnAndItsUnitsAreThatArmys()
        {
            MainViewModel model = await Open();
            MapDocument document = model.ShownDocument!;
            int armies = document.Armies.Length;
            Spawn first = document.Markers.Spawns.Single(spawn => spawn.Name == "ARMY_1");
            (float sx, float sz) = (MathF.Floor(first.Position.X) + 0.5f, MathF.Floor(first.Position.Z) + 0.5f);
            await Run(model, "units.add", new JsonObject { ["blueprint"] = "uec1201", ["x"] = sx + 6, ["z"] = sz, ["army"] = "ARMY_1", ["group"] = "INITIAL/Base" });
            await Run(model, "markers.place", new JsonObject { ["kind"] = "mass", ["x"] = sx - 5, ["z"] = sz });
            ObjectRef mass = model.Selection.Items.Single();
            model.Selection.Set([ObjectRef.Marker(first.Id), mass, ObjectRef.Unit("UNIT_1")]);
            await Run(model, "edit.saveSelectionPreset", new JsonObject { ["name"] = "Start" });
            await Run(model, "presets.apply", new JsonObject { ["kind"] = SelectionPresets.Kind, ["name"] = "Start" });
            SelectionPresetTool tool = model.Tool<SelectionPresetTool>();
            Assert.IsTrue(tool.HoldsSpawn);
            Assert.AreEqual("NEUTRAL_CIVILIAN", tool.Army, "the tool's army stays as it was; the spawn's army takes the units");

            // the preview: an outline per thing under the mouse (and none without the mouse)
            model.Pointer = null;
            Assert.AreEqual(0, tool.Shapes(ToolView.Flat(0.5f)).Count);
            model.Pointer = (100, 100);
            Assert.AreEqual(3 * 2, tool.Shapes(ToolView.Flat(0.5f)).Count, "three outlines, each with its shadow");

            // placed far away (HardFFA's spawns lie together in its middle): the nearest spawn moves there, no army is added
            CommandRun placed = await Run(model, "edit.placePreset", new JsonObject { ["x"] = 40, ["z"] = 40 });
            MapDocument after = model.ShownDocument!;
            Spawn[] movedSpawns = [.. after.Markers.Spawns.Where(spawn => document.Markers.Spawns.Single(o => o.Id == spawn.Id).Position != spawn.Position)];
            Assert.AreEqual(1, movedSpawns.Length, "one spawn moved");
            Spawn near = document.Markers.Spawns.Single(spawn => spawn.Id == movedSpawns[0].Id);
            Assert.AreEqual(40, movedSpawns[0].Position.X, 2, "into the base");
            Assert.AreEqual(armies, after.Armies.Length, "the map keeps its armies");
            Assert.AreEqual(document.Markers.Spawns.Length, after.Markers.Spawns.Length);
            Spawn moved = after.Markers.Spawns.Single(spawn => spawn.Name == near.Name);
            Assert.AreNotEqual(near.Position, moved.Position, "the nearest spawn moved into the base");
            PlacedUnit unit = after.AllUnits().Single(u => u.Unit.Blueprint == "uec1201" && u.Unit.Name != "UNIT_1");
            Assert.AreEqual(near.Name, unit.Army, "the base's units are the moved spawn's army's");
            CollectionAssert.Contains(model.Selection.Items.ToArray(), ObjectRef.Marker(moved.Id), "the moved spawn is selected with what was placed");
            Assert.AreEqual(3, ((JsonArray)placed.Result!).Count, "the mass and the unit placed, and the spawn moved");
        }

        [TestMethod]
        public async Task AStampIsPressedAtAClickAsOneStepTurnedAndSized()
        {
            (MainViewModel model, Workspace workspace) = await OpenInWorkspace("terrain.stamp");
            StampTool tool = model.Tool<StampTool>();
            string picture = Path.Combine(workspace.BrushesFolder, "hills", "cone.png");
            Directory.CreateDirectory(Path.GetDirectoryName(picture)!);
            // a cone: white in the middle, black at the sides
            ushort[] cone = [.. Enumerable.Range(0, 32 * 32).Select(i => (ushort)(65535 * Math.Max(0, 1 - Math.Sqrt(Math.Pow(i % 32 - 15.5, 2) + Math.Pow(i / 32 - 15.5, 2)) / 16)))];
            new HeightStampCapture(32, 32, cone, 0, 1).Save(picture);
            await model.LoadAssetsAsync(workspace);

            float before = model.ShownDocument!.Terrain.Heightmap.HeightAt(100, 100);
            float far = model.ShownDocument.Terrain.Heightmap.HeightAt(130, 100);
            tool.Press(100, 100);
            Assert.IsFalse(model.CanUndo && model.Map!.Session.UndoDescription!.StartsWith("Stamp"), "no picture chosen: nothing pressed");
            StringAssert.Contains(model.Status, "Choose the stamp's picture");

            model.Assets.Select("brushes/hills/cone.png");
            Assert.AreEqual("brushes/hills/cone.png", tool.Shape.Chosen.Key, "a brush chosen on the shelf is the stamp's picture");
            tool.Radius = 16;
            tool.Height = 30;
            tool.Edge = 0;
            int steps = model.History.Count;
            model.Stroke([(100, 100), (110, 100), (120, 100)]);
            Assert.AreEqual(steps + 1, model.History.Count, "one step");
            Assert.AreEqual("Stamp: raise terrain", model.Map!.Session.UndoDescription);
            Heightmap heights = model.ShownDocument!.Terrain.Heightmap;
            Assert.AreEqual(before + 30, heights.HeightAt(100, 100), 1.5, "the whole height at once, at the click");
            Assert.AreEqual(far, heights.HeightAt(130, 100), 0.01, "dragging presses nothing more");

            // turned by Q and E, sized by [ and ]
            Assert.IsTrue(model.ToolKey("E", ToolModifiers.None));
            Assert.AreEqual(15, tool.Shape.Rotation);
            Assert.IsTrue(model.ToolKey("OemCloseBrackets", ToolModifiers.None));
            Assert.IsTrue(tool.Radius > 16);

            // the settings as a brush preset, the picture by its key
            CommandRun saved = await Run(model, "terrain.saveBrush", new JsonObject { ["name"] = "Cone" });
            tool.Height = 1;
            tool.Shape.Choose(null);
            await Run(model, "presets.apply", new JsonObject { ["kind"] = BrushPreset.Kind, ["name"] = "Cone" });
            Assert.AreEqual(("terrain.stamp", 30.0, "brushes/hills/cone.png"), (model.ActiveTool!.Definition.Id, tool.Height, tool.Shape.Chosen.Key));
        }

        [TestMethod]
        public async Task APartOfTheMapIsTakenAsAStampAndPutBackElsewhere()
        {
            (MainViewModel model, Workspace workspace) = await OpenInWorkspace("terrain.sculpt");
            await Run(model, "brush.set", new JsonObject { ["layer"] = 0, ["mode"] = "raise", ["strength"] = 20, ["radius"] = 8 });
            model.Stroke([(60, 60)]);
            Heightmap shaped = model.ShownDocument!.Terrain.Heightmap;

            model.SelectTool("terrain.stamp");
            StampTool tool = model.Tool<StampTool>();
            ToolInput shift = new ToolInput(ToolModifiers.Shift, 0.5f);
            ((IMapTool)tool).Press(50, 50, shift);
            tool.Drag(70, 70);
            Assert.AreEqual((50f, 50f, 70f, 70f), tool.Box, "shift and a drag draw the box to take");
            tool.Release();
            await Task.Delay(100);
            Assert.IsNull(tool.Box);
            string file = Path.Combine(workspace.BrushesFolder, "stamps", "HardFFA 50 50.png");
            Assert.IsTrue(File.Exists(file), "a 16-bit picture in the brushes folder's stamps");
            Assert.AreEqual(("brushes/stamps/hardffa 50 50.png", HeightStampMode.Set, false), (tool.Shape.Chosen.Key, tool.Mode, tool.BaseFromGround));
            Assert.AreEqual(10.5, tool.Radius, "a pixel an ogrid");
            await model.LoadAssetsAsync(workspace);
            Assert.AreEqual("brushes/stamps/hardffa 50 50.png", tool.Shape.Chosen.Key, "still chosen when the brushes are read again");
            tool.Shape.Chosen = null!;
            Assert.AreEqual("brushes/stamps/hardffa 50 50.png", tool.Shape.Chosen.Key, "a combo box letting go of its item leaves the shape");

            ((IMapTool)tool).Press(150, 150, ToolInput.Default);
            tool.Release();
            Heightmap after = model.ShownDocument!.Terrain.Heightmap;
            for (int d = -8; d <= 8; d += 4)
            {
                Assert.AreEqual(shaped.GetHeight(60 + d, 60), after.GetHeight(150 + d, 150), 0.05, $"put back as it was at {d}");
            }
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "terrain.captureStamp", new JsonObject { ["x0"] = 5, ["z0"] = 5, ["x1"] = 5, ["z1"] = 5 }));
        }

        [TestMethod]
        public async Task AMapIsSavedAsATemplateAndTheNewMapPageStartsFromIt()
        {
            (MainViewModel model, Workspace workspace) = await OpenInWorkspace();
            CommandRun saved = await Run(model, "file.saveAsTemplate", new JsonObject { ["name"] = "Eight" });
            Assert.AreEqual("Settings", ((JsonObject)saved.Result!)["kind"]!.GetValue<string>());
            Assert.IsTrue(File.Exists(Path.Combine(workspace.TemplatesFolder, "Eight.json")));
            await Run(model, "file.saveAsTemplate", new JsonObject { ["name"] = "Whole", ["whole"] = true });
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "file.saveAsTemplate", new JsonObject { ["name"] = "sandbox" }));

            NewMapPageViewModel page = model.NewMapPage;
            page.Open(workspace.TemplatesFolder);
            CollectionAssert.AreEqual(new[] { "None: from the fields below", "Eight", "Whole", "Sandbox" }, page.Templates.Select(t => t.Title).ToArray());
            page.Players = "3";
            page.Template = page.Templates.Single(t => t.Title == "Eight");
            Assert.AreEqual("8", page.Players, "the template's settings fill the fields");
            Assert.IsTrue(page.UsesFields && page.StrataFromTemplate);
            Assert.IsNull(page.Arguments()!["biome"], "the template's own strata, not a biome");
            page.Name = "From eight";
            await page.CreateCommand.ExecuteAsync(null);
            Assert.AreEqual("From eight", model.ShownDocument!.Scenario.Name);
            Assert.AreEqual(8, model.ShownDocument.Scenario.PlayableArmies.Length);

            page.Open(workspace.TemplatesFolder);
            Assert.AreEqual("Eight", page.Template.Title, "the template chosen last");
            page.Template = page.Templates.Single(t => t.Title == "Whole");
            Assert.IsFalse(page.UsesFields, "a whole map takes only the name");
            CommandRun copied = await Run(model, "file.new", new JsonObject { ["template"] = "Whole", ["name"] = "Copy of Hard" });
            Assert.IsTrue(copied.Ran);
            Assert.AreEqual(("Copy of Hard", 1), (model.ShownDocument!.Scenario.Name, model.ShownDocument.Version));
            Assert.IsTrue(model.Map!.ProjectFolder is null, "no project until it is saved");
            await Run(model, "file.new", new JsonObject { ["template"] = "sandbox", ["name"] = "Try" });
            Assert.AreEqual(256, model.ShownDocument!.Width);
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "file.new", new JsonObject { ["template"] = "Nothing" }));
        }

        [TestMethod]
        public async Task DroppingInTheUnitTreeMovesUnitsAndGroupsAsOneStep()
        {
            MainViewModel model = await Open("units.select");
            ArmiesViewModel armies = model.Armies;
            await Run(model, "units.add", new JsonObject { ["blueprint"] = "uec1101", ["x"] = 50, ["z"] = 50, ["army"] = "ARMY_1" });
            await Run(model, "units.add", new JsonObject { ["blueprint"] = "uec1101", ["x"] = 60, ["z"] = 50, ["army"] = "ARMY_1" });
            await Run(model, "units.addGroup", new JsonObject { ["army"] = "ARMY_1", ["parent"] = "INITIAL", ["name"] = "Town" });
            GroupNode initial = armies.Tree.Single(node => node.Army == "ARMY_1").Children.Single(node => node.PathText == "INITIAL");
            CollectionAssert.AreEqual(new[] { "UNIT_1", "UNIT_2" }, initial.Units.Select(unit => unit.Name).ToArray(), "the tree shows the units after the groups");
            Assert.IsInstanceOfType<GroupNode>(initial.Items[0]);

            // a selected unit takes the other selected units along
            model.Selection.Set([ObjectRef.Unit("UNIT_1"), ObjectRef.Unit("UNIT_2")]);
            GroupNode town = initial.Children.Single();
            int steps = model.History.Count;
            Assert.IsNull(armies.DropProblem(initial.Units[0], town));
            await armies.Drop(initial.Units[0], town);
            Assert.IsTrue(model.ShownDocument!.AllUnits().Where(unit => unit.Army == "ARMY_1").All(unit => unit.Group.SequenceEqual(["INITIAL", "Town"])));
            Assert.AreEqual(steps + 1, model.History.Count, "one step");

            // a group onto another army's group; never into itself
            town = armies.Tree.Single(node => node.Army == "ARMY_1").SelfAndBelow().Single(node => node.PathText == "INITIAL/Town");
            GroupNode army2 = armies.Tree.Single(node => node.Army == "ARMY_2");
            Assert.IsNotNull(armies.DropProblem(town, town));
            Assert.IsNotNull(armies.DropProblem(town, armies.Tree.Single(node => node.Army == "ARMY_1").Children.Single(node => node.PathText == "INITIAL")), "it is there already");
            Assert.IsNotNull(armies.DropProblem(armies.Tree[0], army2), "an army stays where it is");
            await armies.Drop(town, army2);
            Assert.AreEqual(("ARMY_2", "Town"), (model.ShownDocument!.FindUnit("UNIT_1")!.Army, UnitGroupEdits.PathText(model.ShownDocument.FindUnit("UNIT_1")!.Group)));
            Assert.IsTrue(model.Undo());
            Assert.AreEqual("ARMY_1", model.ShownDocument!.FindUnit("UNIT_1")!.Army);

            // choosing a unit in the tree selects it and chooses its group
            UnitNode unit = armies.Tree.Single(node => node.Army == "ARMY_1").SelfAndBelow().SelectMany(node => node.Units).First();
            armies.ChosenNode = unit;
            await Task.Yield();
            Assert.AreEqual(unit.GroupKey, armies.ChosenGroup!.Key);
            Assert.AreEqual($"unit:{unit.Name}", model.Selection.Items.Single().ToString());
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "units.drop", new JsonObject { ["army"] = "ARMY_1", ["group"] = "INITIAL" }));
        }

        [TestMethod]
        public async Task StarsAndRecentsShowOnTheShelfAndInThePickerAndStayWithTheWorkspace()
        {
            (MainViewModel model, Workspace workspace) = await OpenInWorkspace("props.place");
            AssetsViewModel shelf = model.Assets;
            const string rock = "env/evergreen/props/rocks/rock01_prop.bp";

            CommandRun starred = await Run(model, "assets.star", new JsonObject { ["key"] = rock });
            Assert.IsTrue(((JsonObject)starred.Result!)["starred"]!.GetValue<bool>());
            Assert.IsTrue(File.Exists(Path.Combine(workspace.Folder, WorkspaceLayout.Favourites)), "kept in the workspace");
            shelf.Kind = AssetKind.Prop;
            shelf.Scope = AssetScope.Starred;
            Assert.AreEqual(rock, shelf.Tiles.Single().Entry.Key, "the shelf's Starred shows it");
            Assert.IsTrue(shelf.Tiles.Single().IsStarred);
            Assert.AreEqual(1, shelf.StarredCount);

            // what is chosen is the newest of the recents
            await Run(model, "assets.pick", new JsonObject { ["key"] = "/env/evergreen/props/trees/oak01_prop.bp" });
            shelf.Scope = AssetScope.Recent;
            Assert.AreEqual("env/evergreen/props/trees/oak01_prop.bp", shelf.Tiles[0].Entry.Key);
            shelf.Scope = AssetScope.Everything;
            AssetTile pine = shelf.Tiles.First(tile => tile.Entry.Key.Contains("pine06"));
            shelf.ChooseTileCommand.Execute(pine);
            Assert.AreEqual(pine.Entry.Key, shelf.Favourites.Recent[0], "a tile clicked on the shelf too");
            Assert.AreEqual("Star", shelf.ShownStarText);
            await Run(model, "assets.star");
            Assert.AreEqual("Starred", shelf.ShownStarText, "the inspector's star is the shown asset's");

            // the picker: Starred and Recent, a star of its own, a choice noted
            AssetPickerViewModel picker = model.Picker;
            Task<string?> picking = picker.PickAsync(AssetKind.Prop);
            CollectionAssert.AreEqual(new[] { "Starred 2", "Recent 2" }, picker.Shows.Select(choice => choice.Text).ToArray());
            picker.ChooseShowCommand.Execute(picker.Shows[0]);
            Assert.AreEqual(2, picker.Tiles.Count, "the starred props");
            Assert.IsTrue(picker.Tiles.All(tile => tile.IsStarred));
            picker.Select(picker.Tiles.Single(tile => tile.Entry.Key == rock));
            picker.ToggleStar();
            Assert.AreEqual(1, picker.Tiles.Count, "its star taken away, it leaves Starred");
            picker.Choose(picker.Tiles[0]);
            Assert.AreEqual(await picking, shelf.Favourites.Recent[0], "the choice is the newest of the recents");

            // another editor in the same workspace sees them
            AssetsViewModel other = new AssetsViewModel(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
            other.OpenFavourites(workspace);
            CollectionAssert.AreEqual(shelf.Favourites.Starred.ToArray(), other.Favourites.Starred.ToArray());
            CollectionAssert.AreEqual(shelf.Favourites.Recent.ToArray(), other.Favourites.Recent.ToArray());
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "assets.star", new JsonObject { ["key"] = "nothing" }));
        }

        [TestMethod]
        public async Task WalkabilityIsMarkedByHandAndGeneratedWithTheMarks()
        {
            MainViewModel model = await Open("terrain.walkability");
            WalkabilityMarkTool tool = model.Tool<WalkabilityMarkTool>();
            Assert.AreEqual("walkability marks", model.ShownGrid?.Key, "the top view shows the marks while the tool is active");
            await Run(model, "generate.walkability");
            // a place units can reach, its neighbours too
            NavGrid grid = model.Walkability.Grid!;
            (int x, int z) = Enumerable.Range(60, 140).SelectMany(px => Enumerable.Range(60, 140).Select(pz => (px, pz)))
                .First(p => Enumerable.Range(-6, 13).All(d => Walkability.IsPassable(grid, p.px + d, p.pz) && Walkability.IsPassable(grid, p.px, p.pz + d)));

            tool.Radius = 3;
            int steps = model.History.Count;
            model.Stroke([(x + 0.5f, z + 0.5f), (x + 4.5f, z + 0.5f)]);
            Assert.AreEqual(steps + 1, model.History.Count, "one step");
            Assert.AreEqual("Mark walkability blocked", model.Map!.Session.UndoDescription);
            Assert.AreEqual(WalkabilityMarks.Blocked, model.ShownDocument!.WalkabilityMarks![x, z]);
            Assert.AreEqual(GeneratedStatus.Stale, model.Generation.States.Single(state => state.Generator.Id == "walkability").Status, "walkability is out of date");
            await Run(model, "generate.walkability");
            Assert.IsFalse(Walkability.IsPassable(model.Walkability.Grid!, x, z), "generated with the mark");
            Assert.IsTrue(model.Walkability.Changes!.Blocked > 0);

            // erased, the mark goes
            tool.Erase = true;
            model.Stroke([(x + 0.5f, z + 0.5f)]);
            Assert.AreEqual(WalkabilityMarks.None, model.ShownDocument!.WalkabilityMarks![x, z]);
            Assert.AreEqual("Erase walkability marks", model.Map!.Session.UndoDescription);
            tool.Erase = false;
            tool.Mark = WalkabilityMark.Open;
            model.Stroke([(x + 0.5f, z + 0.5f)]);
            Assert.AreEqual(WalkabilityMarks.Open, model.ShownDocument!.WalkabilityMarks![x, z]);
            Assert.AreEqual(1, tool.Describe()["open"]!.GetValue<int>() > 0 ? 1 : 0);

            // the brush as a preset
            await Run(model, "terrain.saveBrush", new JsonObject { ["name"] = "Mark" });
            model.SelectTool("terrain.sculpt");
            await Run(model, "presets.apply", new JsonObject { ["kind"] = BrushPreset.Kind, ["name"] = "Mark" });
            Assert.AreEqual("terrain.walkability", model.ActiveTool!.Definition.Id);
            Assert.IsTrue(model.Undo());
        }

        [TestMethod]
        public async Task AnAdaptiveMapIsExportedWithItsOptionsTablesAndScript()
        {
            MainViewModel model = await Open("markers.select");
            await Run(model, "file.new", new JsonObject { ["name"] = "Adapting", ["size"] = 256, ["players"] = 2 });
            Assert.IsFalse(model.Markers.WritesAdaptiveFiles);
            StringAssert.StartsWith(model.Markers.AdaptiveFilesText, "A map that adapts");

            await Run(model, "units.add", new JsonObject { ["blueprint"] = "uec1101", ["x"] = 100, ["z"] = 100, ["army"] = "ARMY_17" });
            model.Selection.Set([ObjectRef.Unit("UNIT_1")]);
            await Run(model, "markers.addOptionalGroup", new JsonObject { ["option"] = "Players", ["level"] = 2, ["fromSelection"] = true });
            Assert.IsTrue(model.Markers.WritesAdaptiveFiles, "a new map's script is the editor's to write");
            StringAssert.Contains(model.Markers.OptionalGroups.Single().Line, "when 2 or more players are in the game");

            string output = Path.Combine(AppContext.BaseDirectory, "exports", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(output);
            await Run(model, "file.export", new JsonObject { ["folder"] = output });
            string folder = Directory.GetDirectories(output).Single();
            Assert.IsTrue(File.Exists(Path.Combine(folder, "adapting_options.lua")));
            Assert.IsTrue(File.Exists(Path.Combine(folder, "adapting_tables.lua")));
            StringAssert.StartsWith(File.ReadAllText(Path.Combine(folder, "adapting_script.lua")), AdaptiveScript.Header);
        }

        [TestMethod]
        public async Task SavingNeedsASelection()
        {
            MainViewModel model = await Open();
            model.Selection.Set([ObjectRef.Area("AREA_1")]);
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Run(model, "edit.saveSelectionPreset"));
        }
    }
}
