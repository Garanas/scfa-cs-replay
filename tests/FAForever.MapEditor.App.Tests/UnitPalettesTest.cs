using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// Unit palettes (user story U-8): choosing a palette and a slot sets the place tool's unit or
    /// preset, the keys 1 to 0 and Tab, setting and clearing a slot, new, renamed and deleted
    /// palettes, and the units placed last.
    /// </summary>
    [TestClass]
    public class UnitPalettesTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        /// <summary>
        /// A library with the palette <c>Base</c> (slot 1 the farm <c>uec1201</c>, slot 2 the preset
        /// <c>Village</c>, slot 10 the Snoop <c>uel0101</c>), the palette <c>Other</c> (empty) and the
        /// units preset <c>Village</c>.
        /// </summary>
        internal static PresetLibrary Library()
        {
            PresetLibrary library = new PresetLibrary(Path.Combine(AppContext.BaseDirectory, "presets", Guid.NewGuid().ToString("N")));
            UnitPresetGroup village = new UnitPresetGroup("Village", [new UnitPresetUnit("uec1101", -4, 0, 0, 0), new UnitPresetUnit("uec1101", 4, 0, 0, 0)], []);
            library.Save(Preset.Of(UnitPresets.Kind, "Village", new UnitPresetSettings(village, 2)));
            UnitPaletteSettings palette = UnitPalettes.SetSlot(UnitPalettes.Empty, 0, UnitPaletteSlot.OfUnit("uec1201"));
            palette = UnitPalettes.SetSlot(palette, 1, UnitPaletteSlot.OfPreset("Village"));
            palette = UnitPalettes.SetSlot(palette, 9, UnitPaletteSlot.OfUnit("uel0101"));
            library.Save(UnitPalettes.ToPreset("Base", palette));
            library.Save(UnitPalettes.ToPreset("Other", UnitPalettes.Empty));
            return library;
        }

        internal static async Task<(MainViewModel Model, PresetLibrary Library)> Open(IEditorDialogs? dialogs = null, UserSettings? settings = null)
        {
            MainViewModel model = new MainViewModel(dialogs ?? new MainViewModelTest.Answers());
            model.Assets.Use(UnitsTest.Catalog());
            PresetLibrary library = Library();
            if (settings is not null)
            {
                model.UnitPalettes.Load(settings);
            }
            model.Presets.Use(library);
            await model.OpenAsync(HardFfa);
            model.SelectTool("units.place");
            return (model, library);
        }

        private static Task<CommandRun> Run(MainViewModel model, string id, JsonObject? arguments = null) =>
            model.Commands.RunAsync(id, arguments ?? [], interactive: false);

        /// <summary>
        /// Lets a key's command, started without waiting, run.
        /// </summary>
        private static async Task Settle(Func<bool> done)
        {
            for (int i = 0; i < 50 && !done(); i++)
            {
                await Task.Delay(10);
            }
        }

        [TestMethod]
        public async Task ChoosingAPaletteAndASlotSetsThePlaceToolsUnitOrPreset()
        {
            (MainViewModel model, _) = await Open();
            UnitPalettesViewModel palettes = model.UnitPalettes;
            UnitPlaceTool tool = model.Tool<UnitPlaceTool>();
            CollectionAssert.AreEqual(new[] { "Base", "Other" }, palettes.Names.ToArray());
            Assert.AreEqual("Base", palettes.Chosen, "the first by name");
            Assert.AreEqual(10, palettes.Slots.Count);
            Assert.AreEqual("UEF Farm", palettes.Slots[0].Title, "a unit's name from the catalogue");
            Assert.AreEqual("Village", palettes.Slots[1].Title);
            Assert.AreEqual("2 units", palettes.Slots[1].Detail, "what the preset places");
            Assert.IsTrue(palettes.Slots[2].IsEmpty);
            Assert.AreEqual("0", palettes.Slots[9].Key);

            CommandRun picked = await Run(model, "units.slot", new JsonObject { ["n"] = 1 });
            Assert.AreEqual("uec1201", tool.Unit?.Key, "the slot's unit is what the tool places");
            Assert.AreEqual("units.place", model.ActiveTool!.Definition.Id);
            Assert.AreEqual(0, palettes.ChosenSlot);
            Assert.IsTrue(palettes.Slots[0].IsChosen);
            Assert.IsTrue(((JsonObject)picked.Result!)["picked"]!.GetValue<bool>());

            await Run(model, "units.slot", new JsonObject { ["n"] = 2 });
            Assert.AreEqual("Village", tool.Preset, "the slot's preset is what the tool places");
            Assert.AreEqual(1, palettes.ChosenSlot);
            Assert.IsFalse(palettes.Slots[0].IsChosen);

            await Run(model, "units.slot", new JsonObject { ["n"] = 0 });
            Assert.AreEqual("uel0101", tool.Unit?.Key, "0 is the tenth slot");
            Assert.IsNull(tool.Preset);
            Assert.AreEqual(9, palettes.ChosenSlot);

            CommandRun empty = await Run(model, "units.slot", new JsonObject { ["n"] = 3 });
            Assert.IsFalse(((JsonObject)empty.Result!)["picked"]!.GetValue<bool>());
            StringAssert.Contains(model.Status, "empty");
            Assert.AreEqual("uel0101", tool.Unit?.Key, "an empty slot changes nothing");
            await Assert.ThrowsExceptionAsync<ArgumentOutOfRangeException>(() => Run(model, "units.slot", new JsonObject { ["n"] = 11 }));

            // a unit chosen on the shelf that a slot holds is that slot; one no slot holds is none
            model.Assets.Select("uec1201");
            Assert.AreEqual(0, palettes.ChosenSlot);
            model.Assets.Select("ual0001");
            Assert.AreEqual(-1, palettes.ChosenSlot);

            await Run(model, "units.palette", new JsonObject { ["name"] = "other" });
            Assert.AreEqual("Other", palettes.Chosen, "by name, ignoring case");
            Assert.IsTrue(palettes.Slots.All(slot => slot.IsEmpty));
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "units.palette", new JsonObject { ["name"] = "Nowhere" }));

            JsonObject state = palettes.Describe();
            Assert.AreEqual("Other", state["chosen"]!.GetValue<string>());
            Assert.AreEqual(10, ((JsonArray)state["slots"]!).Count);
        }

        [TestMethod]
        public async Task TheKeysPickSlotsAndTabTheNextPalette()
        {
            (MainViewModel model, _) = await Open();
            UnitPalettesViewModel palettes = model.UnitPalettes;
            UnitPlaceTool tool = model.Tool<UnitPlaceTool>();

            Assert.IsTrue(tool.Key("D1", ToolModifiers.None), "1 picks the first slot");
            await Settle(() => tool.Unit is not null);
            Assert.AreEqual("uec1201", tool.Unit?.Key);
            Assert.IsTrue(tool.Key("NumPad2", ToolModifiers.None), "the number pad too");
            await Settle(() => tool.Preset is not null);
            Assert.AreEqual("Village", tool.Preset);
            Assert.IsFalse(tool.Key("D1", ToolModifiers.Shift), "shift and a digit are left alone");
            Assert.IsFalse(tool.Key("D1", ToolModifiers.Control), "Ctrl+1 is a view's shortcut");
            Assert.IsTrue(tool.Key("Q", ToolModifiers.None), "Q and E still turn");

            Assert.IsTrue(tool.Key("Tab", ToolModifiers.None));
            Assert.AreEqual("Other", palettes.Chosen, "Tab: the next palette");
            StringAssert.Contains(model.Status, "Other");
            Assert.IsTrue(tool.Key("Tab", ToolModifiers.None));
            Assert.AreEqual("Base", palettes.Chosen, "round the list");
            Assert.IsTrue(tool.Key("Tab", ToolModifiers.Shift));
            Assert.AreEqual("Other", palettes.Chosen, "Shift+Tab: the one before");
            Assert.AreEqual(-1, palettes.ChosenSlot, "Other holds nothing the tool places");

            CommandRun next = await Run(model, "units.nextPalette", new JsonObject { ["back"] = true });
            Assert.AreEqual("Base", ((JsonObject)next.Result!)["chosen"]!.GetValue<string>());
            Assert.AreEqual(1, palettes.ChosenSlot, "the slot of Base that holds the preset placed next");
        }

        [TestMethod]
        public async Task ASlotIsSetAndClearedAndThePalettesFileFollows()
        {
            MainViewModelTest.Answers answers = new MainViewModelTest.Answers();
            (MainViewModel model, PresetLibrary library) = await Open(answers);
            UnitPalettesViewModel palettes = model.UnitPalettes;

            CommandRun set = await Run(model, "units.setSlot", new JsonObject { ["slot"] = 3, ["unit"] = "UEC1101" });
            Assert.AreEqual("uec1101", ((JsonObject)set.Result!)["unit"]!.GetValue<string>(), "the id as the catalogue has it");
            Assert.AreEqual("UEF Residential Building", palettes.Slots[2].Title);
            Assert.AreEqual("uec1101", UnitPalettes.Read(library.Find(UnitPalettes.Kind, "Base")!).Slots[2].Unit, "written at once");

            await Run(model, "units.setSlot", new JsonObject { ["slot"] = 3, ["preset"] = "village" });
            Assert.AreEqual("Village", palettes.Slots[2].Slot.Preset, "a preset by name, ignoring case");
            Assert.IsTrue(palettes.Slots[2].IsPreset);

            await Run(model, "units.setSlot", new JsonObject { ["slot"] = 3, ["clear"] = true });
            Assert.IsTrue(palettes.Slots[2].IsEmpty);
            Assert.IsTrue(UnitPalettes.Read(library.Find(UnitPalettes.Kind, "Base")!).Slots[2].IsEmpty);

            await Run(model, "units.setSlot", new JsonObject { ["slot"] = 10, ["palette"] = "Other", ["unit"] = "uel0101" });
            Assert.AreEqual("Base", palettes.Chosen, "another palette's slot leaves the chosen one");
            Assert.AreEqual("uel0101", UnitPalettes.Read(library.Find(UnitPalettes.Kind, "Other")!).Slots[9].Unit);

            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "units.setSlot", new JsonObject { ["slot"] = 1, ["unit"] = "nothing" }), "a unit the workspace lacks");
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "units.setSlot", new JsonObject { ["slot"] = 1, ["preset"] = "nothing" }), "a preset the workspace lacks");
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "units.setSlot", new JsonObject { ["slot"] = 1, ["unit"] = "uec1101", ["preset"] = "Village" }), "not both");
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "units.setSlot", new JsonObject { ["slot"] = 1, ["palette"] = "Nowhere", ["unit"] = "uec1101" }));
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Run(model, "units.setSlot", new JsonObject { ["slot"] = 1 }), "without a unit it would ask the picker, which needs a person");

            // with a person, the picker chooses the unit, starting on the slot's own
            answers.Assets.Enqueue("ual0001");
            await model.Commands.RunAsync("units.setSlot", new JsonObject { ["slot"] = 1 }, interactive: true);
            Assert.AreEqual((AssetKind.Unit, "uec1201"), (answers.AssetQuestions.Single().Kind, answers.AssetQuestions.Single().Current));
            Assert.AreEqual("ual0001", palettes.Slots[0].Slot.Unit);
            answers.Assets.Enqueue(null);
            CommandRun cancelled = await model.Commands.RunAsync("units.setSlot", new JsonObject { ["slot"] = 1 }, interactive: true);
            Assert.IsTrue(((JsonObject)cancelled.Result!)["cancelled"]!.GetValue<bool>());
            Assert.AreEqual("ual0001", palettes.Slots[0].Slot.Unit, "cancelled: the slot stays");
        }

        [TestMethod]
        public async Task PalettesAreMadeRenamedAndDeletedAsFiles()
        {
            (MainViewModel model, PresetLibrary library) = await Open();
            UnitPalettesViewModel palettes = model.UnitPalettes;

            CommandRun made = await Run(model, "units.newPalette", new JsonObject { ["name"] = "Mine" });
            Assert.AreEqual("Mine", ((JsonObject)made.Result!)["name"]!.GetValue<string>());
            Assert.AreEqual("Mine", palettes.Chosen, "the new palette is chosen");
            Assert.IsTrue(palettes.Slots.All(slot => slot.IsEmpty));
            Assert.IsTrue(File.Exists(Path.Combine(library.Folder, PresetKinds.Palettes, "Mine.json")));
            CommandRun again = await Run(model, "units.newPalette", new JsonObject { ["name"] = "Mine" });
            Assert.AreEqual("Mine 2", ((JsonObject)again.Result!)["name"]!.GetValue<string>(), "a name taken gets a number");
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Run(model, "units.newPalette"), "without a name it asks, which needs a person");

            await Run(model, "units.renamePalette", new JsonObject { ["to"] = "Ours" });
            Assert.AreEqual("Ours", palettes.Chosen, "the chosen one is renamed and stays chosen");
            Assert.IsFalse(File.Exists(Path.Combine(library.Folder, PresetKinds.Palettes, "Mine 2.json")));
            Assert.IsNotNull(library.Find(UnitPalettes.Kind, "Ours"));
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "units.renamePalette", new JsonObject { ["to"] = "Base" }), "a name taken");
            await Run(model, "units.renamePalette", new JsonObject { ["name"] = "Mine", ["to"] = "Theirs" });
            CollectionAssert.AreEqual(new[] { "Base", "Other", "Ours", "Theirs" }, palettes.Names.ToArray());

            CommandRun deleted = await Run(model, "units.deletePalette");
            Assert.AreEqual("Ours", ((JsonObject)deleted.Result!)["name"]!.GetValue<string>(), "the chosen one");
            Assert.IsNull(library.Find(UnitPalettes.Kind, "Ours"));
            Assert.AreEqual("Base", palettes.Chosen, "a T1 base else the first by name");
            await Run(model, "units.deletePalette", new JsonObject { ["name"] = "Theirs" });
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "units.deletePalette", new JsonObject { ["name"] = "Theirs" }));
            CollectionAssert.AreEqual(new[] { "Base", "Other" }, palettes.Names.ToArray());
        }

        [TestMethod]
        public async Task TheUnitsPlacedLastAreOfferedAgainAndSurviveARestart()
        {
            UserSettings settings = new UserSettings(Path.Combine(AppContext.BaseDirectory, "settings", $"{Guid.NewGuid():N}.json"));
            (MainViewModel model, PresetLibrary library) = await Open(settings: settings);
            UnitPalettesViewModel palettes = model.UnitPalettes;
            UnitPlaceTool tool = model.Tool<UnitPlaceTool>();
            Assert.AreEqual(0, palettes.LastPlaced.Count);

            await Run(model, "units.add", new JsonObject { ["blueprint"] = "uec1101", ["x"] = 50, ["z"] = 50, ["army"] = "ARMY_1" });
            Assert.AreEqual("unit:uec1101", palettes.LastPlaced.Single().Key);
            Assert.AreEqual("UEF Residential Building", palettes.LastPlaced[0].Title);
            await Run(model, "units.add", new JsonObject { ["blueprint"] = "uel0101", ["x"] = 60, ["z"] = 50, ["army"] = "ARMY_1" });
            await Run(model, "units.add", new JsonObject { ["blueprint"] = "uec1101", ["x"] = 70, ["z"] = 50, ["army"] = "ARMY_1" });
            CollectionAssert.AreEqual(new[] { "unit:uec1101", "unit:uel0101" }, palettes.LastPlaced.Select(item => item.Key).ToArray(), "the newest first, each once");
            await Run(model, "units.placePreset", new JsonObject { ["name"] = "Village", ["x"] = 100, ["z"] = 100 });
            Assert.AreEqual("preset:Village", palettes.LastPlaced[0].Key);
            Assert.IsTrue(palettes.LastPlaced[0].IsPreset);
            Assert.AreEqual("Base", palettes.Chosen);
            Assert.AreEqual("Base", settings.Units.Palette, "the settings keep the palette");
            CollectionAssert.AreEqual(new[] { "preset:Village", "unit:uec1101", "unit:uel0101" }, settings.Units.LastPlaced!.ToArray(), "and the last placed");

            library.Save(Preset.Of(UnitPresets.Kind, "Hamlet", new UnitPresetSettings(new UnitPresetGroup("Hamlet", [new UnitPresetUnit("uec1101", 0, 0, 0, 0)], []), 1)));
            await Run(model, "units.placePreset", new JsonObject { ["name"] = "Hamlet", ["x"] = 120, ["z"] = 120 });
            foreach (string id in new[] { "ual0001", "uec1201", "uel0101", "uec1101" })
            {
                await Run(model, "units.add", new JsonObject { ["blueprint"] = id, ["x"] = 20, ["z"] = 20, ["army"] = "ARMY_1" });
            }
            Assert.AreEqual(UnitPalettesViewModel.LastPlacedCount, palettes.LastPlaced.Count, "at most six");
            Assert.AreEqual("unit:uec1101", palettes.LastPlaced[0].Key);

            await palettes.PickLastPlacedAsync(palettes.LastPlaced[1]);
            Assert.AreEqual("uel0101", tool.Unit?.Key, "a chip chooses it for the tool");
            await palettes.PickLastPlacedAsync(palettes.LastPlaced.Single(item => item.Name == "Village"));
            Assert.AreEqual("Village", tool.Preset);

            // another editor with the same settings starts with them, and with the palette
            await Run(model, "units.palette", new JsonObject { ["name"] = "Other" });
            (MainViewModel restarted, _) = await Open(settings: settings);
            Assert.AreEqual("Other", restarted.UnitPalettes.Chosen);
            CollectionAssert.AreEqual(palettes.LastPlaced.Select(item => item.Key).ToArray(), restarted.UnitPalettes.LastPlaced.Select(item => item.Key).ToArray());
        }

        [TestMethod]
        public async Task AScatterStrokeIsPlacedLastAChipThatReadsAlikeSaysItsFactionAndAChipChoosesThePlaceTool()
        {
            (MainViewModel model, _) = await Open();
            UnitPlaceTool tool = model.Tool<UnitPlaceTool>();
            await Run(model, "units.pick", new JsonObject { ["key"] = "uec1101" });
            await Run(model, "units.placeMode", new JsonObject { ["mode"] = "scatter" });
            tool.Press(100, 100);
            tool.Release();
            Assert.AreEqual("unit:uec1101", model.UnitPalettes.LastPlaced.Single().Key, "the brush feeds the list");

            // the same name twice: the faction after it; the tool tip has the id
            AssetEntry aeon = model.Assets.Find("ual0001")!;
            AssetEntry uef = model.Assets.Find("uec1101")!;
            LastPlacedItem alike = new LastPlacedItem("unit:a", uef with { Name = "Power Generator", Group = "UEF" }, null, qualify: true);
            Assert.AreEqual("Power Generator (UEF)", alike.ChipText);
            StringAssert.Contains(alike.Help, "unit:a".Substring(5));
            Assert.AreEqual("Residential", new LastPlacedItem("unit:b", uef with { Name = "Residential" }, null).ChipText);
            _ = aeon;

            // a chip chooses the Place tool, in the Select tool too
            model.SelectTool("units.select");
            await model.UnitPalettes.PickLastPlacedAsync(model.UnitPalettes.LastPlaced.Single());
            Assert.AreEqual("units.place", model.ActiveTool!.Definition.Id);
        }

        [TestMethod]
        public async Task TheExamplePalettesListAndASlotOfAUnitTheWorkspaceLacksIsMarked()
        {
            (MainViewModel model, PresetLibrary library) = await Open();
            UnitPalettesViewModel palettes = model.UnitPalettes;
            ExampleUnitPalettes.Ensure(Path.Combine(library.Folder, PresetKinds.Palettes));
            palettes.Refresh();
            Assert.AreEqual(22, palettes.Names.Count, "the twenty examples and the test's two");
            Assert.AreEqual("Base", palettes.Chosen, "what was chosen stays chosen");

            await Run(model, "units.palette", new JsonObject { ["name"] = "UEF T1 base" });
            Assert.IsTrue(palettes.Slots[0].IsMissing, "the test assets have no ueb0101");
            Assert.AreEqual("ueb0101", palettes.Slots[0].Title, "its id stands for its name");
            StringAssert.Contains(palettes.Slots[0].Label, "not in the workspace");
            CommandRun picked = await Run(model, "units.slot", new JsonObject { ["n"] = 1 });
            Assert.IsFalse(((JsonObject)picked.Result!)["picked"]!.GetValue<bool>(), "picks nothing");
            StringAssert.Contains(model.Status, "lacks");

            await Run(model, "units.palette", new JsonObject { ["name"] = "UEF civilian" });
            Assert.IsTrue(palettes.Slots[0].IsMissing, "the library of the test has no example unit presets");
            Assert.IsTrue(palettes.Slots[0].IsPreset);
        }

        [TestMethod]
        public void TheUnitsMenuAndThePanelHoldThePaletteCommands()
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            foreach (string id in new[] { "units.palette", "units.nextPalette", "units.slot", "units.setSlot", "units.newPalette", "units.renamePalette", "units.deletePalette" })
            {
                EditorCommand command = model.Commands.Find(id)!;
                Assert.AreEqual("U-8", command.Story, id);
            }
            ModeDefinition units = EditorModes.All.Single(mode => mode.Id == "units");
            Assert.IsInstanceOfType<UnitPaletteSection>(units.Sections[0], "the palette before the tree");
            Assert.IsInstanceOfType<UnitGroupsSection>(units.Sections[1]);
            Assert.IsTrue(ShortcutsViewModel.ToolKeys.Any(key => key.Keys == "1 to 9, 0"), "the shortcuts page lists the keys");
            Assert.IsTrue(ShortcutsViewModel.ToolKeys.Any(key => key.Keys == "Tab and Shift+Tab"));
        }
    }
}
