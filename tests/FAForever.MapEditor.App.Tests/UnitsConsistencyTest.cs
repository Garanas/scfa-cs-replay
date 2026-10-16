using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The Units mode shows what the model holds: another map leaves nothing of the one before in
    /// the place tool or the tree, a deleted preset is no longer what a click places, the palette
    /// chosen is the one the settings keep, a resize keeps the stored selections.
    /// </summary>
    [TestClass]
    public class UnitsConsistencyTest
    {
        private static Task<CommandRun> Run(MainViewModel model, string id, JsonObject? arguments = null) =>
            model.Commands.RunAsync(id, arguments ?? [], interactive: false);

        [TestMethod]
        public async Task AnotherMapLeavesNoArmyGroupOrExpandedNodeOfTheOneBefore()
        {
            (MainViewModel model, _) = await UnitPalettesTest.Open();
            UnitPlaceTool tool = model.Tool<UnitPlaceTool>();
            await Run(model, "units.addGroup", new JsonObject { ["army"] = "ARMY_2", ["name"] = "Town" });
            Assert.AreEqual("ARMY_2", tool.Army);
            Assert.AreEqual("Town", tool.Group);
            Assert.AreEqual("ARMY_2:Town", model.Armies.ChosenGroup?.Key);
            Assert.IsTrue(tool.ArmyChosen);

            model.OpenNewMap(Project.NewMap.Create(new Project.NewMapSettings { Size = 256 }));

            Assert.IsNull(model.Armies.ChosenGroup, "nothing is chosen in the tree of the other map");
            Assert.IsNull(model.Armies.ChosenArmy);
            Assert.IsFalse(tool.ArmyChosen, "the army is the default of the map shown, not a choice");
            Assert.AreEqual(tool.DefaultArmy(model.ShownDocument!), tool.Army);
            Assert.AreEqual("INITIAL", tool.Group);
            Assert.IsTrue(model.Armies.Tree.SelectMany(node => node.SelfAndBelow()).All(node => !node.IsExpanded), "no node stays open: " + string.Join(", ", model.Armies.Tree.SelectMany(node => node.SelfAndBelow()).Where(node => node.IsExpanded).Select(node => node.Key)));
        }

        [TestMethod]
        public async Task AResizeKeepsTheArmyAndGroupChosen()
        {
            (MainViewModel model, _) = await UnitPalettesTest.Open();
            UnitPlaceTool tool = model.Tool<UnitPlaceTool>();
            await Run(model, "units.addGroup", new JsonObject { ["army"] = "ARMY_2", ["name"] = "Town" });
            await Run(model, "map.resize", new JsonObject { ["width"] = 512, ["height"] = 512 });
            Assert.AreEqual("ARMY_2", tool.Army);
            Assert.AreEqual("Town", tool.Group);
        }

        [TestMethod]
        public async Task APresetDeletedIsNoLongerWhatTheToolPlaces()
        {
            (MainViewModel model, PresetLibrary library) = await UnitPalettesTest.Open();
            UnitPlaceTool tool = model.Tool<UnitPlaceTool>();
            await Run(model, "presets.apply", new JsonObject { ["kind"] = "units", ["name"] = "Village" });
            Assert.AreEqual("Village", tool.Preset);
            await Run(model, "presets.delete", new JsonObject { ["kind"] = "units", ["name"] = "Village" });
            Assert.IsNull(tool.Preset, "the tool places a unit again");
            Assert.IsNull(model.UnitPalettes.Slots.FirstOrDefault(slot => slot.IsChosen), "and no slot is lit for it");
            Assert.AreEqual("Choose a unit", tool.UnitButtonText);
        }

        [TestMethod]
        public async Task ANewRenamedOrDeletedPaletteIsTheOneTheSettingsKeep()
        {
            UserSettings settings = new UserSettings(Path.Combine(AppContext.BaseDirectory, "settings", $"{Guid.NewGuid():N}.json"));
            (MainViewModel model, _) = await UnitPalettesTest.Open(settings: settings);
            await Run(model, "units.newPalette", new JsonObject { ["name"] = "Ours" });
            Assert.AreEqual("Ours", model.UnitPalettes.Chosen);
            Assert.AreEqual("Ours", settings.Units.Palette);
            await Run(model, "units.renamePalette", new JsonObject { ["to"] = "Theirs" });
            Assert.AreEqual("Theirs", settings.Units.Palette);
            await Run(model, "units.deletePalette");
            Assert.AreEqual(model.UnitPalettes.Chosen, settings.Units.Palette);
        }

        [TestMethod]
        public async Task AResizeKeepsTheStoredSelectionsOfAMapThatIsNoProject()
        {
            (MainViewModel model, _) = await UnitPalettesTest.Open();
            model.OpenNewMap(Project.NewMap.Create(new Project.NewMapSettings { Size = 256 }));
            await Run(model, "units.add", new JsonObject { ["blueprint"] = "uec1101", ["x"] = 50, ["z"] = 50, ["army"] = "ARMY_1" });
            await Run(model, "select.all");
            await Run(model, "select.store", new JsonObject { ["slot"] = 3 });
            Assert.IsTrue(model.StoredSelections.HasChips);
            await Run(model, "map.resize", new JsonObject { ["width"] = 512, ["height"] = 512 });
            Assert.IsTrue(model.StoredSelections.HasChips, "the digit still holds the units");
        }
    }
}
