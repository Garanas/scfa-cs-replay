using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Checks;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// Placement checks in the window (user story U-12): the place tool's preview turns red where
    /// a click would overlap a structure or break the ground rules, and the Problems tab lists the
    /// same, selecting both units.
    /// </summary>
    [TestClass]
    public class PlacementChecksTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private sealed class NoDialogs : IEditorDialogs
        {
            public Task<string?> PickFolderAsync(string title, string? startIn = null) => Task.FromResult<string?>(null);

            public Task<SaveChoice> AskToSaveChangesAsync(string mapName) => Task.FromResult(SaveChoice.Discard);
        }

        private sealed class Asking(bool answer) : IEditorDialogs
        {
            public int Asked { get; private set; }

            public Task<string?> PickFolderAsync(string title, string? startIn = null) => Task.FromResult<string?>(null);

            public Task<SaveChoice> AskToSaveChangesAsync(string mapName) => Task.FromResult(SaveChoice.Discard);

            public Task<bool> ConfirmAsync(string title, string message, string action)
            {
                Asked++;
                return Task.FromResult(answer);
            }
        }

        /// <summary>
        /// The test assets with a land factory (5 by 5, skirt 8 by 8, land only), a naval factory
        /// (water only, 1.5 deep at least) and a mobile unit.
        /// </summary>
        private static AssetCatalog Catalog()
        {
            string folder = TestAssets.Folder();
            void Write(string id, string json)
            {
                string file = Path.Combine(folder, "units", id, id + "_unit.json");
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, json);
            }
            Write("uxb0101", """{ "id": "uxb0101", "name": "Land Factory", "faction": "Uef", "categories": ["UEF", "STRUCTURE", "LAND"], "footprint": { "sizeX": 5, "sizeZ": 5 }, "skirt": { "sizeX": 8, "sizeZ": 8, "offsetX": -1.5, "offsetZ": -1.5 }, "buildOnLayers": ["Land"] }""");
            Write("uxb0103", """{ "id": "uxb0103", "name": "Naval Factory", "faction": "Uef", "categories": ["UEF", "STRUCTURE", "NAVAL"], "footprint": { "sizeX": 5, "sizeZ": 5, "minWaterDepth": 1.5 }, "skirt": { "sizeX": 8, "sizeZ": 8, "offsetX": -1.5, "offsetZ": -1.5 }, "buildOnLayers": ["Water"] }""");
            Write("uxl0001", """{ "id": "uxl0001", "name": "Tank", "faction": "Uef", "categories": ["UEF", "LAND", "MOBILE"], "footprint": { "sizeX": 1, "sizeZ": 1 } }""");
            return AssetCatalog.Load(folder);
        }

        private static async Task<MainViewModel> Open()
        {
            MainViewModel model = new MainViewModel(new NoDialogs());
            model.Assets.Use(Catalog());
            await model.OpenAsync(HardFfa);
            model.SelectTool("units.place");
            return model;
        }

        private static Task<CommandRun> Run(MainViewModel model, string id, JsonObject? arguments = null) =>
            model.Commands.RunAsync(id, arguments ?? [], interactive: false);

        private static (int Blocked, string? Why) Preview(MainViewModel model)
        {
            JsonObject place = model.Tool<UnitPlaceTool>().Describe();
            return (place["blockedCount"]!.GetValue<int>(), place["why"]?.GetValue<string>());
        }

        /// <summary>
        /// Level ground at 20, where a structure stands anywhere.
        /// </summary>
        private static void Flat(MainViewModel model) => model.Map!.Session.Edit("Flat", document =>
            document with { Terrain = document.Terrain with { Heightmap = document.Terrain.Heightmap with { Samples = TiledBuffer<ushort>.Create(document.Width + 1, document.Height + 1, (ushort)(20 * 128)) } } });

        private static void Water(MainViewModel model, float elevation) =>
            model.Map!.Session.Edit("Water", document => document with { Water = document.Water with { HasWater = true, Elevation = elevation } });

        [TestMethod]
        public async Task ThePreviewTurnsRedOverAStructureAndTheStatusSaysWhyButPlacingIsAllowed()
        {
            MainViewModel model = await Open();
            Water(model, -1000);
            await Run(model, "units.pick", new JsonObject { ["key"] = "uxb0101" });
            await Run(model, "units.add", new JsonObject { ["x"] = 100, ["z"] = 100 });
            string first = model.ShownDocument!.AllUnits().Last().Unit.Name;
            UnitPlaceTool tool = model.Tool<UnitPlaceTool>();

            model.Pointer = (140, 140);
            Assert.AreEqual((0, null), Preview(model), "far away");
            Assert.IsFalse(tool.Shapes(ToolView.Flat(0.5f)).OfType<ToolLine>().Any(line => line.Ink == ToolInk.Danger));

            model.Pointer = (104, 100);
            (int blocked, string? why) = Preview(model);
            Assert.AreEqual(1, blocked);
            Assert.AreEqual($"Overlaps the Land Factory {first}", why);
            Assert.IsTrue(tool.Shapes(ToolView.Flat(0.5f)).OfType<ToolLine>().Any(line => line.Ink == ToolInk.Danger), "the footprint is drawn in the danger colour");
            StringAssert.Contains(model.Status, "Overlaps the Land Factory");
            Assert.IsTrue(tool.Describe()["blocked"]!.GetValue<bool>());

            int units = model.ShownDocument!.AllUnits().Count();
            await Run(model, "units.add", new JsonObject { ["x"] = 104, ["z"] = 100 });
            Assert.AreEqual(units + 1, model.ShownDocument!.AllUnits().Count(), "placing is allowed");

            model.Problems.Run();
            Problem overlap = model.Problems.Items.Select(item => item.Problem).Single(problem => problem.Check == "units-overlap");
            Assert.AreEqual(ProblemSeverity.Warning, overlap.Severity);
            Assert.AreEqual(1, overlap.Related!.Count);
            int line = model.Problems.Items.ToList().FindIndex(item => item.Problem == overlap);
            Assert.IsTrue(model.ShowProblem(line));
            Assert.AreEqual(2, model.Selection.Count, "both units are selected");
        }

        [TestMethod]
        public async Task TheLayerRulesTurnThePreviewRedAndProblemsListTheStructures()
        {
            MainViewModel model = await Open();
            Water(model, -1000);
            await Run(model, "units.pick", new JsonObject { ["key"] = "uxb0103" });
            model.Pointer = (60, 60);
            (int blocked, string? why) = Preview(model);
            Assert.AreEqual(1, blocked, "a naval factory on land");
            StringAssert.Contains(why!, "cannot be built on land");

            Water(model, 1000);
            await Run(model, "units.pick", new JsonObject { ["key"] = "uxb0101" });
            Assert.AreEqual(1, Preview(model).Blocked, "a land factory under water");
            await Run(model, "units.add", new JsonObject { ["x"] = 60, ["z"] = 60 });
            model.Problems.Run();
            Problem ground = model.Problems.Items.Select(item => item.Problem).Single(problem => problem.Check == "units-ground");
            StringAssert.Contains(ground.Message, "cannot be built under water");
            Assert.IsNotNull(ground.Thing);

            await Run(model, "units.pick", new JsonObject { ["key"] = "uxl0001" });
            Assert.AreEqual(0, Preview(model).Blocked, "a mobile unit is never checked");
        }

        [TestMethod]
        public async Task ADragThatLeavesTheMapIsDrawnRedWhereItIsLeftOutAndTheStatusSaysHowMany()
        {
            MainViewModel model = await Open();
            Water(model, -1000);
            await Run(model, "units.pick", new JsonObject { ["key"] = "uxl0001" });
            await Run(model, "units.placeMode", new JsonObject { ["mode"] = "line", ["spacing"] = 3 });
            UnitPlaceTool tool = model.Tool<UnitPlaceTool>();

            tool.Press(-5, 20, ToolInput.Default);
            tool.Drag(10, 20, ToolInput.Default);

            JsonObject state = tool.Describe();
            Assert.AreEqual(6, state["planned"]!.GetValue<int>());
            Assert.AreEqual(2, state["outside"]!.GetValue<int>());
            StringAssert.Contains(model.Status, "2 beyond the map's edge are left out");
            Assert.IsTrue(tool.Shapes(ToolView.Flat(0.5f)).OfType<ToolLine>().Any(line => line.Ink == ToolInk.Danger), "the outside ones are drawn red");
            tool.Release();
            await Task.Delay(200);
            Assert.AreEqual(4, model.Selection.Count, "the ones on the map are placed");
        }

        [TestMethod]
        public async Task PlannedStructuresThatOverlapEachOtherAreRedToo()
        {
            MainViewModel model = await Open();
            Water(model, -1000);
            AssetEntry factory = model.Assets.Catalog!.Find("uxb0101")!;
            UnitPlaceTool tool = model.Tool<UnitPlaceTool>();
            System.Collections.Immutable.ImmutableArray<UnitPlace> places = [new UnitPlace(100.5f, 100.5f, 0), new UnitPlace(104.5f, 100.5f, 0), new UnitPlace(140.5f, 100.5f, 0)];

            UnitPlaceTool.PlaceVerdict verdict = tool.VerdictOf(model.ShownDocument!, factory, places);

            CollectionAssert.AreEqual(new[] { true, true, false }, verdict.Blocked);
            Assert.AreEqual("Overlaps another of the units to place", verdict.Why);
        }

        [TestMethod]
        public async Task ALineOfStructuresIsLaidOutSkirtToSkirtSoNoneIsRed()
        {
            MainViewModel model = await Open();
            Water(model, -1000);
            Flat(model);
            await Run(model, "units.pick", new JsonObject { ["key"] = "uxb0101" });
            await Run(model, "units.placeMode", new JsonObject { ["mode"] = "rectangle" });
            UnitPlaceTool tool = model.Tool<UnitPlaceTool>();

            tool.Press(150, 150, ToolInput.Default);
            tool.Drag(190, 175, ToolInput.Default);

            Assert.AreEqual(5 * 3, tool.PlannedCount, "factories of 8 apart");
            Assert.AreEqual(0, tool.Describe()["blockedCount"]!.GetValue<int>(), tool.Describe()["why"]?.ToString());
            tool.Release();
            await Task.Delay(200);
            model.Problems.Run();
            Assert.AreEqual(0, model.Problems.Items.Count(item => item.Problem.Check == "units-overlap"), string.Join("; ", model.Problems.Items.Select(item => item.Problem.Message)));
        }

        [TestMethod]
        public async Task MoreThanFiveHundredUnitsAreAskedAboutFirst()
        {
            Asking no = new Asking(false);
            MainViewModel model = new MainViewModel(no);
            model.Assets.Use(Catalog());
            await model.OpenAsync(HardFfa);
            model.SelectTool("units.place");
            JsonObject big = new JsonObject { ["mode"] = "rectangle", ["x0"] = 20, ["z0"] = 20, ["x1"] = 60, ["z1"] = 40, ["blueprint"] = "uxl0001", ["spacing"] = 1, ["army"] = "ARMY_1" };
            int units = model.ShownDocument!.AllUnits().Count();

            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Run(model, "units.addMany", (JsonObject)big.DeepClone()), "automation must say confirm");
            Assert.AreEqual(units, model.ShownDocument!.AllUnits().Count());

            try
            {
                await model.Commands.RunAsync("units.addMany", (JsonObject)big.DeepClone(), interactive: true);
            }
            catch (OperationCanceledException)
            {
            }
            Assert.AreEqual(1, no.Asked, "a person is asked");
            Assert.AreEqual(units, model.ShownDocument!.AllUnits().Count(), "and said no");

            big["confirm"] = true;
            CommandRun placed = await Run(model, "units.addMany", big);
            Assert.AreEqual(41 * 21, ((JsonObject)placed.Result!)["count"]!.GetValue<int>());
            Assert.AreEqual(1, no.Asked, "confirm skips the question");

            // 500 or fewer are placed without a question
            await Run(model, "units.addMany", new JsonObject { ["mode"] = "rectangle", ["x0"] = 100, ["z0"] = 100, ["x1"] = 110, ["z1"] = 120, ["blueprint"] = "uxl0001", ["spacing"] = 1, ["army"] = "ARMY_1" });
            Assert.AreEqual(1, no.Asked);
        }

        [TestMethod]
        public async Task AutomationStateAndCheckMapListTheNewChecks()
        {
            MainViewModel model = await Open();
            Water(model, -1000);
            await Run(model, "units.pick", new JsonObject { ["key"] = "uxb0101" });
            await Run(model, "units.add", new JsonObject { ["x"] = 100, ["z"] = 100 });
            await Run(model, "units.add", new JsonObject { ["x"] = 103, ["z"] = 100 });
            JsonObject problems = CheckMapHandler.Describe(model.Problems);
            JsonObject item = ((JsonArray)problems["items"]!).Select(node => (JsonObject)node!).Single(entry => entry["check"]!.GetValue<string>() == "units-overlap");
            Assert.AreEqual(1, ((JsonArray)item["related"]!).Count);
            Assert.IsTrue(item.ContainsKey("place"));
        }
    }
}
