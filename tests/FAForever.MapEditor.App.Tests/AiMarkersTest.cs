using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The Markers mode's generated path networks (R-3), weather markers (R-1), markers presets
    /// (F-10) and the adaptive script's optional groups (R-5).
    /// </summary>
    [TestClass]
    public class AiMarkersTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private sealed class NoDialogs : IEditorDialogs
        {
            public Task<string?> PickFolderAsync(string title, string? startIn = null) => Task.FromResult<string?>(null);

            public Task<SaveChoice> AskToSaveChangesAsync(string mapName) => Task.FromResult(SaveChoice.Discard);
        }

        private static async Task<MainViewModel> Open(string tool = "markers.select")
        {
            MainViewModel model = new MainViewModel(new NoDialogs());
            await model.OpenAsync(HardFfa);
            model.SelectTool(tool);
            return model;
        }

        private static Task<CommandRun> Run(MainViewModel model, string id, JsonObject? arguments = null) =>
            model.Commands.RunAsync(id, arguments ?? [], interactive: false);

        private static PathNode[] Nodes(MainViewModel model, PathLayer layer) => [.. model.ShownDocument!.Markers.PathNodes.Where(node => node.Layer == layer)];

        [TestMethod]
        public async Task GeneratesPathNetworksAsOneStepAndAgainWhenWalkabilityChanged()
        {
            MainViewModel model = await Open();
            int before = model.ShownDocument!.Markers.PathNodes.Length;

            CommandRun run = await Run(model, "markers.connectPaths", new JsonObject { ["layers"] = new JsonArray("Land", "Air"), ["spacing"] = 32, ["replace"] = true });

            Assert.IsTrue(run.Ran);
            JsonArray layers = ((JsonObject)run.Result!)["layers"]!.AsArray();
            Assert.AreEqual(2, layers.Count);
            Assert.IsTrue(Nodes(model, PathLayer.Land).Length > 10);
            Assert.IsTrue(Nodes(model, PathLayer.Land).All(node => node.Name.StartsWith("LandPN", StringComparison.Ordinal)));
            Assert.AreEqual(16, Nodes(model, PathLayer.Air).Length, "4 by 4 air nodes, twice the spacing apart over 256 ogrids");
            Assert.AreEqual("Generate the path networks", model.Map!.Session.UndoDescription);
            Assert.IsTrue(model.ShowPathNodes, "the nodes show");
            StringAssert.StartsWith(model.Markers.Paths.Result, "Generated: land");
            Assert.AreEqual(GeneratedStatus.Current, model.Generation.Rows.Single(row => row.Id == PathNetworkGenerator.GeneratorId).State);

            // added to, the network keeps the nodes it has and adds none on top of them
            int land = Nodes(model, PathLayer.Land).Length;
            await Run(model, "markers.connectPaths", new JsonObject { ["layers"] = new JsonArray("Land"), ["spacing"] = 32 });
            Assert.AreEqual(land, Nodes(model, PathLayer.Land).Length, "every part of the ground has its node already");

            // out of date when walkability's inputs change; generate.stale makes it again with its settings
            model.Map.Session.Edit("Raise the water", document => document with { Water = document.Water with { Elevation = document.Water.Elevation + 3 } });
            model.Generation.Refresh(model.Map.Document);
            Assert.IsTrue(model.Markers.Paths.IsStale);
            StringAssert.Contains(model.Markers.Paths.Status, "water level");
            await Run(model, "generate.stale");
            Assert.IsFalse(model.Markers.Paths.IsStale);

            model.Undo();
            model.Undo();
            model.Undo();
            model.Undo();
            Assert.AreEqual(before, model.ShownDocument!.Markers.PathNodes.Length, "each a step of undo");
        }

        [TestMethod]
        public async Task RefusesNoLayerAndASpacingOutOfRange()
        {
            MainViewModel model = await Open();
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "markers.connectPaths", new JsonObject { ["layers"] = new JsonArray() }));
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "markers.connectPaths", new JsonObject { ["spacing"] = 2 }));
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "markers.connectPaths", new JsonObject { ["layers"] = new JsonArray("Hover") }));
        }

        [TestMethod]
        public async Task TheWeatherToolsPlaceAndTheInspectorChangesThem()
        {
            MainViewModel model = await Open("markers.weatherDefinition");
            model.ActiveTool!.Tool!.Press(100, 100);
            await Task.Yield();
            WeatherDefinition definition = (WeatherDefinition)model.ShownDocument!.Find(model.Selection.Items.Single())!;
            Assert.AreEqual("Tundra", definition.MapStyle);
            Assert.AreEqual("Weather Definition 01", definition.Name);

            MarkerSelection section = (MarkerSelection)model.Selection.Inspector.Sections.Single();
            Assert.IsTrue(section.Weather.HasDefinitions);
            Assert.IsFalse(section.Weather.HasGenerators);
            ChoiceSetting style = (ChoiceSetting)section.Weather.Definition.Settings[0];
            Assert.AreEqual("Tundra", style.Chosen);
            style.Chosen = "Evergreen";
            await Task.Yield();
            await Run(model, "markers.setWeather", new JsonObject { ["type"] = 2, ["cloudType"] = "RainClouds", ["chance"] = 0.5 });
            definition = (WeatherDefinition)model.ShownDocument!.Find(model.Selection.Items.Single())!;
            Assert.AreEqual("Evergreen", definition.MapStyle);
            Assert.AreEqual(new WeatherChance { Type = "RainClouds", Chance = 0.5 }, definition.Types[1]);
            Assert.AreEqual("RainClouds", ((ChoiceSetting)section.Weather.Definition.Settings[3]).Chosen);

            model.SelectTool("markers.weatherGenerator");
            model.ActiveTool!.Tool!.Press(50, 50);
            await Task.Yield();
            Assert.IsTrue(section.Weather.HasGenerators || ((MarkerSelection)model.Selection.Inspector.Sections.Single()).Weather.HasGenerators);
            await Run(model, "markers.setWeather", new JsonObject { ["cloudHeight"] = 220, ["forceType"] = "StormClouds" });
            WeatherGenerator generator = (WeatherGenerator)model.ShownDocument!.Find(model.Selection.Items.Single())!;
            Assert.AreEqual(220, generator.CloudHeight);
            Assert.AreEqual("StormClouds", generator.ForceType);
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "markers.setWeather", new JsonObject { ["mapStyle"] = "Martian" }));
        }

        [TestMethod]
        public async Task AMarkersPresetIsSavedFromTheSelectionAndPlacedWithTheTool()
        {
            MainViewModel model = await Open();
            model.Presets.Use(new PresetLibrary(Path.Combine(AppContext.BaseDirectory, "presets", Guid.NewGuid().ToString("N"))));
            Assert.AreEqual(MarkerPresets.Kind, model.Presets.Kind, "the Markers mode's panel lists markers presets");
            MapDocument document = model.ShownDocument!;
            Spawn spawn = document.Markers.Spawns[0];
            ResourceMarker[] near = [.. document.Markers.Resources.OrderBy(r => System.Numerics.Vector3.Distance(r.Position, spawn.Position)).Take(2)];
            model.Selection.Set([ObjectRef.Marker(spawn.Id), .. near.Select(r => ObjectRef.Marker(r.Id))]);

            CommandRun saved = await Run(model, "edit.saveAsPreset", new JsonObject { ["name"] = "Start" });
            Assert.IsTrue(saved.Ran);
            await Run(model, "presets.apply", new JsonObject { ["kind"] = MarkerPresets.Kind, ["name"] = "Start" });
            Assert.AreEqual("markers.preset", model.ActiveTool!.Definition.Id, "applying chooses the preset tool");
            Assert.AreEqual("Start", model.Tool<MarkerPresetTool>().Preset);

            string next = document.NextSpawnName();
            model.ActiveTool.Tool!.Press(128, 128);
            await Task.Yield();
            Assert.AreEqual(3, model.Selection.Count, "what it placed is selected");
            Assert.IsTrue(model.ShownDocument!.Markers.Spawns.Any(s => s.Name == next), "the spawn is the next army");
            Assert.AreEqual(document.Markers.Resources.Length + 2, model.ShownDocument.Markers.Resources.Length);
            Assert.AreEqual("Place a markers preset", model.Map!.Session.UndoDescription);
        }

        [TestMethod]
        public async Task AnOptionalGroupTakesTheSelectedUnitsAndIsListed()
        {
            MainViewModel model = await Open();
            await Run(model, "map.addArmy", new JsonObject { ["name"] = "ARMY_17", ["extra"] = true });
            await Run(model, "units.add", new JsonObject { ["blueprint"] = "uec1101", ["x"] = 50, ["z"] = 50, ["army"] = "ARMY_17" });
            string unit = model.ShownDocument!.AllUnits().Single(u => u.Army == "ARMY_17").Unit.Name;
            model.Selection.Set([ObjectRef.Unit(unit)]);

            model.Markers.OptionName = "Civilian_Defenses";
            model.Markers.OptionLevel = "3";
            await model.Markers.AddOptionalGroupCommand.ExecuteAsync(null);

            PlacedUnit placed = model.ShownDocument!.FindUnit(unit)!;
            CollectionAssert.AreEqual(new[] { "Optional_Civilian_Defenses_3" }, placed.Group.ToArray());
            Assert.AreEqual("Optional_Civilian_Defenses_3 (ARMY_17): 1 units, from value 3 of optional_civilian_defenses", model.Markers.OptionalGroups.Single().Line);
        }
    }
}
