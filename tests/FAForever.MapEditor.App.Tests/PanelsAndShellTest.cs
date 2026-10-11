using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The panel says what there is and the inspector edits what you chose (Water, Lighting), the
    /// rail's Masks, Procedural and Plugins, the keyboard shortcuts and folders of Help, the workspace
    /// settings, the presets in one folder, clearing the palette and the assets' mass and energy.
    /// </summary>
    [TestClass]
    public class PanelsAndShellTest
    {
        private sealed class NoDialogs : IEditorDialogs
        {
            public Task<string?> PickFolderAsync(string title, string? startIn = null) => Task.FromResult<string?>(null);

            public Task<SaveChoice> AskToSaveChangesAsync(string mapName) => Task.FromResult(SaveChoice.Discard);
        }

        private static Task<CommandRun> Run(MainViewModel model, string id, JsonObject? arguments = null) => ContentTest.Run(model, id, arguments);

        [TestMethod]
        public async Task TheWaterPanelListsWhatThereIsAndTheInspectorEditsTheChosenOne()
        {
            MainViewModel model = await ContentTest.Open();
            model.SelectMode("water");
            PanelParts parts = model.WaterSettings.Parts;

            CollectionAssert.AreEqual(
                new[] { "water.elevations", "water.surface", "water.textures", "water.foam", "water.flatness", "water.depthBias", "water.waves" },
                parts.Items.Select(part => part.Id).ToArray());
            CollectionAssert.AreEqual(new[] { "Settings", "Maps", "Wave generators" }, parts.Groups.Select(group => group.Title).ToArray());
            Assert.AreEqual("water.foam", parts.Chosen.Id, "the mode's first tool paints water: its map shows");
            StringAssert.StartsWith(parts.Find("water.elevations")!.Detail, "Surface ");
            Assert.AreEqual("0 generators", parts.Find("water.waves")!.Detail);

            parts.Choose("water.surface");
            Assert.IsInstanceOfType<SettingsPart>(parts.Chosen.Content);
            Assert.AreEqual("Surface and reflection", ((SettingsPart)parts.Chosen.Content).Groups.Single().Title);
            Assert.AreEqual(5, ((SettingsPart)parts.Find("water.textures")!.Content).Groups.Count, "the textures with the four wave textures");

            // a map chosen makes the water brush paint it; the waves choose their tool
            parts.Choose("water.flatness");
            Assert.AreEqual("water.paint", model.ActiveTool!.Definition.Id);
            Assert.AreEqual(WaterMask.Flatness, model.Tool<WaterPaintTool>().Painted.Mask);
            Assert.AreSame(model.Tool<WaterPaintTool>(), ((WaterMapPart)parts.Chosen.Content).Brush, "the inspector edits the brush that paints it");
            parts.Choose("water.waves");
            Assert.AreEqual("water.waves", model.ActiveTool!.Definition.Id);
            Assert.IsInstanceOfType<WaveGeneratorsPart>(parts.Chosen.Content);

            // and back: the brush told to paint another map shows that one
            await Run(model, "brush.set", new JsonObject { ["waterMask"] = "depthBias" });
            Assert.AreEqual("water.depthBias", parts.Chosen.Id);
            await model.WaterSettings.AddGeneratorCommand.ExecuteAsync(null);
            Assert.AreEqual("1 generator", parts.Find("water.waves")!.Detail);
        }

        [TestMethod]
        public async Task TheLightingPanelListsItsPartsAndTheSkyHasPresetsOfItsOwn()
        {
            MainViewModel model = await ContentTest.Open();

            string answer = (string)(await Run(model, "view.inspectPart", new JsonObject { ["part"] = "lighting.sky" })).Result!;
            Assert.AreEqual("lighting", model.SelectedMode!.Definition.Id, "the part's mode is chosen first");
            Assert.AreEqual("lighting.sky", model.LightingSettings.Parts.Chosen.Id);
            Assert.AreEqual("lighting.sky", answer);
            Assert.AreEqual(PresetKinds.Sky, model.Presets.Kind, "the panel lists the sky's presets while the sky is chosen");
            Assert.IsTrue(model.PanelSections.OfType<PresetSection>().Single().Kind == PresetKinds.Sky);

            Preset saved = model.Presets.Save(null, "Black sky");
            Assert.AreEqual(PresetKinds.Sky, saved.Kind);
            string cubemap = model.ShownDocument!.Sky.Cubemap;
            await Run(model, "lighting.setSky", new JsonObject { ["cubemap"] = "/textures/environment/skycube_other.dds" });
            await Run(model, "presets.apply", new JsonObject { ["kind"] = "sky", ["name"] = "Black sky" });
            Assert.AreEqual(cubemap, model.ShownDocument!.Sky.Cubemap, "the sky's preset brings its cube map back");

            model.LightingSettings.Parts.Choose("lighting.fog");
            Assert.AreEqual(AtmospherePresets.LightingKind, model.Presets.Kind);
            Assert.AreSame(model.WideDecals.Shadow, model.LightingSettings.Parts.Find("lighting.shadowDecal")!.Content, "the map-wide shadow decal's slot");
            StringAssert.Contains(model.LightingSettings.Parts.Find("lighting.sun")!.Detail, "° up");
            CollectionAssert.AreEqual(new[] { "Settings", "Sky", "Map-wide decals" }, model.LightingSettings.Parts.Groups.Select(group => group.Title).ToArray());
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "view.inspectPart", new JsonObject { ["part"] = "nothing" }));
        }

        [TestMethod]
        public async Task TheTerrainPanelListsStampPresetsWhileTheStampToolIsActive()
        {
            MainViewModel model = await ContentTest.Open("terrain.sculpt");
            Assert.AreEqual(PresetKinds.Brushes, model.Presets.Kind);

            model.SelectTool("terrain.stamp");
            Assert.AreEqual(PresetKinds.Stamps, model.Presets.Kind);
            JsonObject saved = (JsonObject)(await Run(model, "terrain.saveBrush", new JsonObject { ["name"] = "Crater" })).Result!;
            Assert.AreEqual("stamps", saved["kind"]!.GetValue<string>());
            Assert.IsTrue(File.Exists(Path.Combine(model.Presets.Library!.Folder, "stamps", "Crater.json")));
            Assert.AreEqual("Crater", model.Presets.Shown.Single().Name);
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Run(model, "edit.saveAsPreset", new JsonObject { ["kind"] = "brushes" }),
                "the stamp's settings are no brush preset");

            model.SelectTool("terrain.sculpt");
            Assert.AreEqual(PresetKinds.Brushes, model.Presets.Kind);
            await Run(model, "presets.apply", new JsonObject { ["kind"] = "stamps", ["name"] = "Crater" });
            Assert.AreEqual("terrain.stamp", model.ActiveTool!.Definition.Id, "a stamp preset chooses the stamp tool");
        }

        [TestMethod]
        public async Task ThePresetSectionShowsItsFolderAndOldKindNamesStillWork()
        {
            MainViewModel model = await ContentTest.Open("terrain.sculpt");
            string folder = (string)(await Run(model, "presets.showFolder")).Result!;
            Assert.AreEqual(Path.Combine(model.Presets.Library!.Folder, "brushes"), folder);
            Assert.IsTrue(Directory.Exists(folder), "made when missing");
            Assert.AreEqual(Path.Combine(model.Presets.Library.Folder, "maps"), (string)(await Run(model, "presets.showFolder", new JsonObject { ["kind"] = "templates" })).Result!);

            model.Tool<SculptTool>().Radius = 21;
            await Run(model, "edit.saveAsPreset", new JsonObject { ["kind"] = "brush", ["name"] = "Old name" });
            Assert.IsTrue(File.Exists(Path.Combine(folder, "Old name.json")), "the old kind name saves into the new folder");
            await Run(model, "presets.apply", new JsonObject { ["kind"] = "brush", ["name"] = "Old name" });
        }

        [TestMethod]
        public async Task AWorkspaceMovesItsOldPresetFoldersWhenItOpens()
        {
            (MainViewModel model, Workspace workspace) = await ContentTest.OpenInWorkspace();
            string old = Path.Combine(workspace.PresetsFolder, "props-palette");
            Directory.CreateDirectory(old);
            File.WriteAllText(Path.Combine(old, "Forest.json"), """{ "formatVersion": 1, "kind": "props-palette", "name": "Forest", "settings": { "props": [] } }""");
            Directory.CreateDirectory(Path.Combine(workspace.Folder, "templates"));
            File.WriteAllText(Path.Combine(workspace.Folder, "templates", "Eight.json"), """{ "size": 512 }""");

            model.Setup.Use(workspace);

            Assert.AreEqual(2, model.Presets.Moved.Count);
            Assert.IsTrue(File.Exists(Path.Combine(workspace.PresetsFolder, "props", "Forest.json")));
            Assert.IsTrue(File.Exists(Path.Combine(workspace.TemplatesFolder, "Eight.json")), "the templates are the presets' maps");
            model.SelectMode("props");
            Assert.AreEqual("Forest", model.Presets.Shown.Single().Name);
        }

        [TestMethod]
        public void TheRailHasTheMasksAndProceduralModesAndThePluginsBelow()
        {
            MainViewModel model = new MainViewModel(new NoDialogs());
            ModeDefinition masks = EditorModes.All.Single(mode => mode.Id == "masks");
            ModeDefinition procedural = EditorModes.All.Single(mode => mode.Id == "procedural");

            Assert.AreEqual("Shift+F3", masks.Gesture, "beside the Paint mode (F3) the masks came from");
            Assert.IsInstanceOfType<MasksSection>(masks.Sections[0], "the masks, filled in by wave 9B");
            Assert.AreEqual(EditorModes.BottomGroup, procedural.Group);
            CollectionAssert.AreEqual(new[] { "procedural" }, model.BottomRailModes.Select(mode => mode.Definition.Id).ToArray());
            Assert.AreEqual("paint", model.RailModes[model.RailModes.ToList().FindIndex(mode => mode.Definition.Id == "masks") - 1].Definition.Id);
            Assert.IsFalse(model.RailModes.Any(mode => mode.Definition.Id == "procedural"));
            StringAssert.Contains(model.Modes.Single(mode => mode.Definition.Id == "masks").Tip, "Shift+F3");

            model.SelectMode("masks");
            Assert.AreEqual("masks.paint", model.ActiveTool!.Definition.Id, "the mask brush moved here from the Paint mode");
            model.SelectMode("procedural");
            Assert.AreEqual("procedural.regions", model.ActiveTool!.Definition.Id);
        }

        [TestMethod]
        public async Task TheShortcutsPageListsEveryGestureTheToolKeysAndTheMouseWithASearch()
        {
            MainViewModel model = new MainViewModel(new NoDialogs());
            int count = (int)(await Run(model, "help.shortcuts")).Result!;

            Assert.IsTrue(model.Shortcuts.IsOpen);
            Assert.AreEqual("shortcuts", model.OpenPageName);
            Assert.AreEqual(count, model.Shortcuts.Count);
            CollectionAssert.AreEqual(new[] { "Commands", "Keys of the tools", "The mouse in the views" }, model.Shortcuts.Sections.Select(section => section.Title).ToArray());
            ShortcutItem[] commands = [.. model.Shortcuts.Sections[0].Items];
            Assert.AreEqual(model.Commands.All.Count(command => command.Gesture is not null), commands.Length, "every command with a shortcut");
            Assert.IsTrue(commands.Any(item => item.Keys == "Ctrl+Z" && item.Where == "Edit"));
            Assert.IsTrue(commands.Any(item => item.Keys == "Shift+F3" && item.What == "Masks mode"));
            Assert.IsTrue(commands.Any(item => item.Keys == "Ctrl+]"), "a bracket shows as itself");

            model.Shortcuts.Query = "3d wheel";
            Assert.AreEqual("The mouse in the views", model.Shortcuts.Sections.Single().Title);
            Assert.AreEqual(1, model.Shortcuts.Count);
            model.Shortcuts.Query = "nothing like this";
            Assert.IsTrue(model.Shortcuts.IsEmpty);
            await Run(model, "help.shortcuts", new JsonObject { ["query"] = "undo" });
            Assert.IsTrue(model.Shortcuts.Sections[0].Items.All(item => item.What.Contains("undo", StringComparison.OrdinalIgnoreCase) || item.Keys.Contains("undo", StringComparison.OrdinalIgnoreCase) || item.Where.Contains("undo", StringComparison.OrdinalIgnoreCase)));

            Assert.AreEqual("Ctrl+1", ShortcutsViewModel.KeysOf("Ctrl+D1"));
            Assert.AreEqual("Ctrl+Shift+[", ShortcutsViewModel.KeysOf("Ctrl+Shift+OemOpenBrackets"));
            Assert.IsTrue(model.ClosePage());
            Assert.IsFalse(model.Shortcuts.IsOpen);
        }

        [TestMethod]
        public async Task HelpsFoldersShowTheWorkspacesFoldersAndAnswerTheirPaths()
        {
            (MainViewModel model, Workspace workspace) = await ContentTest.OpenInWorkspace();
            List<string> shown = [];
            model.ShowFolderRequested += shown.Add;

            Assert.AreEqual(workspace.PresetsFolder, (string)(await Run(model, "help.presetsFolder")).Result!);
            Assert.AreEqual(workspace.ProjectsFolder, (string)(await Run(model, "help.projectsFolder")).Result!);
            Assert.AreEqual(workspace.AssetsFolder, (string)(await Run(model, "help.assetsFolder")).Result!);
            Assert.AreEqual(workspace.GameFolder, (string)(await Run(model, "help.gameFolder")).Result!);
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "help.fafDataFolder"), "a workspace without FAF names none");
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "help.exportFolder"), "the vault's maps are not there yet");
            Directory.CreateDirectory(workspace.MapsFolder);
            Assert.AreEqual(workspace.MapsFolder, (string)(await Run(model, "help.exportFolder")).Result!);
            Assert.IsFalse((await Run(model, "help.projectFolder")).Ran, "an imported map is no project yet");
            CollectionAssert.Contains(shown, workspace.PresetsFolder);

            MenuLayout help = Menus.Bar.Single(menu => menu.Title == "Help");
            MenuLayout folders = help.Items.OfType<MenuLayout>().Single(menu => menu.Title == "Folders");
            CollectionAssert.IsSubsetOf(new[] { "help.workspaceFolder", "help.projectsFolder", "help.projectFolder", "help.presetsFolder", "assets.brushesFolder",
                "plugins.showFolder", "help.assetsFolder", "help.gameFolder", "help.fafDataFolder", "help.exportFolder", "help.diagnosticsFolder" }, folders.Items.ToArray());
        }

        [TestMethod]
        public async Task TheWorkspaceSettingsChangeWorkspaceJsonAndShowTheAssets()
        {
            (MainViewModel model, Workspace workspace) = await ContentTest.OpenInWorkspace();
            JsonObject page = (JsonObject)(await Run(model, "file.workspaceSettings")).Result!;
            Assert.AreEqual("workspaceSettings", model.OpenPageName);
            Assert.AreEqual(workspace.GameFolder, model.WorkspaceSettings.GameFolder);
            Assert.AreEqual(workspace.MapsFolder, page["exportFolder"]!.GetValue<string>());
            Assert.IsFalse(model.WorkspaceSettings.AssetsCurrent, "the test assets have no manifest");
            Assert.AreEqual("Not converted yet.", model.WorkspaceSettings.AssetsText);
            model.ClosePage();

            string vault = Path.Combine(Path.GetDirectoryName(workspace.Folder)!, "other vault");
            Directory.CreateDirectory(vault);
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "file.workspaceSettings", new JsonObject { ["vaultFolder"] = vault }),
                "the test's game folder holds no archives");
            string game = Path.Combine(Path.GetDirectoryName(workspace.Folder)!, "real game");
            foreach (string name in GameArchives.Names)
            {
                Directory.CreateDirectory(Path.Combine(game, "gamedata"));
                File.WriteAllText(Path.Combine(game, "gamedata", name + ".scd"), "");
            }
            await Run(model, "file.workspaceSettings", new JsonObject { ["gameFolder"] = game, ["vaultFolder"] = vault });

            Workspace saved = Workspace.Open(workspace.Folder);
            Assert.AreEqual(Path.GetFullPath(vault), saved.VaultFolder, "workspace.json has it");
            Assert.AreEqual(Path.GetFullPath(game), saved.GameFolder);
            Assert.AreEqual(Path.Combine(Path.GetFullPath(vault), "maps"), model.Setup.Workspace!.MapsFolder, "the export goes there now");
        }

        [TestMethod]
        public async Task ThePaletteIsClearedAtOnce()
        {
            MainViewModel model = await ContentTest.Open("props.paint");
            AssetEntry prop = model.Assets.Catalog.Entries.First(entry => entry.Kind == AssetKind.Prop);
            model.Props.AddToPalette(prop.Key);

            Assert.AreEqual(1, (int)(await Run(model, "props.clearPalette")).Result!);
            Assert.IsTrue(model.Props.IsPaletteEmpty);
            Assert.AreEqual("Cleared the palette of 1 prop; Restore or undo brings them back", model.Status);
            Assert.AreEqual(0, (int)(await Run(model, "props.clearPalette")).Result!);
            Assert.IsTrue(model.Props.CanRestorePalette, "a second Clear of nothing keeps what the first took away");

            // Restore brings it back with its weight
            Assert.AreEqual(1, (int)(await Run(model, "props.restorePalette")).Result!);
            Assert.AreEqual(prop.Key, model.Props.Palette.Single().Key);
            Assert.IsFalse(model.Props.CanRestorePalette);
        }

        [TestMethod]
        public async Task UndoRightAfterClearingThePaletteBringsItBack()
        {
            MainViewModel model = await ContentTest.Open("props.paint");
            AssetEntry prop = model.Assets.Catalog.Entries.First(entry => entry.Kind == AssetKind.Prop);
            await Run(model, "props.place", new JsonObject { ["x"] = 50, ["z"] = 50, ["key"] = prop.Key });
            int props = model.ShownDocument!.Props.Length;
            model.Props.AddToPalette(prop.Key, 3);

            await Run(model, "props.clearPalette");
            Assert.IsTrue(model.CanUndo);
            await Run(model, "edit.undo");
            Assert.AreEqual(3, model.Props.Palette.Single().Weight, "the palette is back, with its weight");
            Assert.AreEqual(props, model.ShownDocument!.Props.Length, "the step before (the prop placed) is not undone");
            Assert.AreEqual("Undone: Clear the palette (1 prop back)", model.Status);

            // once the map changed after Clear, undo is the map's again and Restore stays offered
            await Run(model, "props.clearPalette");
            await Run(model, "props.place", new JsonObject { ["x"] = 80, ["z"] = 80, ["key"] = prop.Key });
            await Run(model, "edit.undo");
            Assert.AreEqual(props, model.ShownDocument!.Props.Length, "the prop placed after Clear is undone");
            Assert.IsTrue(model.Props.IsPaletteEmpty);
            Assert.IsTrue(model.Props.CanRestorePalette);
        }

        [TestMethod]
        public void TilesShowMassAndEnergyAndTheShelfSortsByThem()
        {
            string folder = TestAssets.Folder();
            void Unit(string id, int mass, int energy) => File.WriteAllText(Path.Combine(Directory.CreateDirectory(Path.Combine(folder, "units", id)).FullName, id + "_unit.json"),
                $$"""{ "id": "{{id.ToUpperInvariant()}}", "name": "{{id}}", "faction": "Aeon", "categories": ["AEON", "STRUCTURE"], "massCost": {{mass}}, "energyCost": {{energy}} }""");
            Unit("uab0301", 5220, 47400);
            Unit("uab1101", 75, 750);
            Unit("uab2101", 200, 2000);
            AssetsViewModel assets = new AssetsViewModel(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
            assets.Use(AssetCatalog.Load(folder));
            assets.Kind = AssetKind.Unit;
            assets.Scope = AssetScope.Everything;
            assets.Group = "Aeon";

            AssetTile factory = assets.Tiles.Single(tile => tile.Entry.Key == "uab0301");
            Assert.AreEqual("5.2k · 47.4k", factory.Economy);
            Assert.AreEqual(("0.27", "850", "7.5M"), (AssetSearch.Short(0.27), AssetSearch.Short(850), AssetSearch.Short(7_506_000)));
            Assert.AreEqual("Costs 5,220 mass and 47,400 energy", factory.EconomyHelp);
            StringAssert.Contains(factory.Label, "Costs 5,220 mass");
            Assert.IsTrue(assets.CanSort);

            assets.Sort = AssetSort.Mass;
            CollectionAssert.AreEqual(new[] { "uab0301", "uab2101", "uab1101" }, assets.Tiles.Where(tile => tile.Entry.Key.StartsWith("uab", StringComparison.Ordinal)).Select(tile => tile.Entry.Key).ToArray());
            assets.SortIndex = 2;
            Assert.AreEqual(AssetSort.Energy, assets.Sort);

            assets.Kind = AssetKind.Prop;
            AssetTile tree = assets.Tiles.First();
            Assert.IsTrue(tree.ShowsEconomy);
            StringAssert.Contains(tree.EconomyHelp, "Reclaim");
            assets.Kind = AssetKind.Decal;
            Assert.IsFalse(assets.CanSort);
            Assert.IsFalse(assets.Tiles.Any(tile => tile.ShowsEconomy));
        }
    }
}
