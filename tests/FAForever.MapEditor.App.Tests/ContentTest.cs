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
        public async Task SavingNeedsASelection()
        {
            MainViewModel model = await Open();
            model.Selection.Set([ObjectRef.Area("AREA_1")]);
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Run(model, "edit.saveSelectionPreset"));
        }
    }
}
