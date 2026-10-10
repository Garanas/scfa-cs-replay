using System.Collections.Immutable;
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
        public async Task SavingNeedsASelection()
        {
            MainViewModel model = await Open();
            model.Selection.Set([ObjectRef.Area("AREA_1")]);
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Run(model, "edit.saveSelectionPreset"));
        }
    }
}
