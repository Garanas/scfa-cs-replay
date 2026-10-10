using System.Numerics;
using System.Text.Json.Nodes;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Rendering;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Checks;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// Walkability, the normal map and the preview generated when asked (user stories T-7, S-4,
    /// F-7), and the problems of the map with going to one (F-14).
    /// </summary>
    [TestClass]
    public class GenerationTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private static async Task<MainViewModel> Open()
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            await model.OpenAsync(HardFfa);
            return model;
        }

        /// <summary>
        /// A wall 40 game units high across the whole map at x = 128 (one column of the heightmap, so its
        /// top is no flat path), as one step of undo.
        /// </summary>
        private static void BuildWall(MainViewModel model) => model.Map!.Session.Edit("Build a wall", document =>
        {
            Heightmap heightmap = document.Terrain.Heightmap;
            TiledBuffer<ushort>.Builder samples = heightmap.Samples.ToBuilder();
            for (int z = 0; z <= heightmap.Height; z++)
            {
                for (int x = 128; x <= 128; x++)
                {
                    samples[x, z] = (ushort)Math.Min(ushort.MaxValue, samples[x, z] + 40 / heightmap.Scale);
                }
            }
            return document with { Terrain = document.Terrain with { Heightmap = heightmap with { Samples = samples.ToBuffer() } } };
        });

        private static Task<CommandRun> Run(MainViewModel model, string id, JsonObject? arguments = null) =>
            model.Commands.RunAsync(id, arguments ?? [], interactive: false);

        [TestMethod]
        public async Task GeneratesWalkabilityWhenAskedAndShowsWhatChanged()
        {
            MainViewModel model = await Open();
            Assert.IsFalse(model.ShowWalkability);
            Assert.IsNull(model.Walkability.Navigation, "nothing generates by itself");

            CommandRun first = await Run(model, "generate.walkability");

            Assert.IsTrue(first.Ran);
            Assert.IsTrue(model.ShowWalkability, "generating shows it");
            Assert.IsNotNull(model.Walkability.Grid);
            Assert.IsNull(model.Walkability.Changes, "nothing to compare with the first time");
            Assert.AreEqual(GeneratedStatus.Current, model.Generation.States.Single(state => state.Generator.Id == "walkability").Status);
            int passable = model.Walkability.Grid!.Cells.Count(cell => cell > 0);
            StringAssert.Contains(model.Walkability.ChangesText, $"{passable:N0} of");

            BuildWall(model);
            GeneratedRow row = model.Generation.Rows.Single(row => row.Id == "walkability");
            Assert.IsTrue(row.IsStale);
            Assert.AreEqual("Out of date: heightmap changed", row.Status);
            Assert.AreEqual("Generate again", row.ButtonText);

            JsonObject again = (JsonObject)(await Run(model, "generate.walkability")).Result!;

            WalkabilityChanges changes = model.Walkability.Changes!;
            Assert.IsTrue(changes.Blocked > 0);
            Assert.AreEqual(0, changes.Opened);
            Assert.AreEqual(changes.Blocked, again["changes"]!["blocked"]!.GetValue<int>());
            StringAssert.Contains(model.Status, "newly blocked");
            Assert.IsTrue(changes.Closures.Count > 0, "the wall splits the land");
            Assert.IsTrue(Enumerable.Range(0, changes.Height).Any(z => changes.Cells[z * changes.Width + 128] == WalkabilityChange.Blocked));

            // the wall goes again: a new passage, which the Problems tab lists with its place
            model.Undo();
            await Run(model, "generate.walkability");
            Assert.IsTrue(model.Walkability.Changes!.Passages.Count > 0);
            Problem passage = model.Problems.Items.Select(item => item.Problem).First(problem => problem.Check == "walkability-changes");
            Assert.AreEqual(ProblemSeverity.Warning, passage.Severity);
            Assert.IsTrue(Math.Abs(passage.Place!.Value.X - 128) < 3, $"at {passage.Place}");

            // another layer compares that layer; another map forgets both results
            await Run(model, "view.walkability", new JsonObject { ["layer"] = "hover", ["changes"] = false });
            Assert.AreEqual(NavLayer.Hover, model.Walkability.Layer);
            Assert.AreEqual(NavLayer.Hover, model.Walkability.Changes!.Layer);
            Assert.IsFalse(model.Walkability.ShowChanges);
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "view.walkability", new JsonObject { ["layer"] = "air" }));
            await model.OpenAsync(HardFfa);
            Assert.IsNull(model.Walkability.Navigation);
            Assert.IsNull(model.Walkability.Changes);
        }

        [TestMethod]
        public async Task GeneratesTheNormalMapAndPreviewIntoTheMapAsOneStepEach()
        {
            MainViewModel model = await Open();
            MapDocument before = model.Map!.Document;

            await Run(model, "generate.normalMap");
            await Run(model, "file.renderPreview");

            MapDocument after = model.Map.Document;
            Assert.AreNotSame(before.Terrain.NormalMap, after.Terrain.NormalMap);
            Assert.AreEqual(before.Terrain.NormalMap.Width, after.Terrain.NormalMap.Width);
            Assert.AreEqual(MapPreview.Size, after.Terrain.Preview.Width);
            Assert.AreEqual(MapPreview.Size, after.Terrain.Preview.Height);
            Assert.IsTrue(after.Terrain.Preview.ToArray().All(pixel => pixel.Alpha == 255));
            CollectionAssert.AreEqual(new[] { "Generate the normal map", "Generate the preview" }, model.History.Skip(1).Select(entry => entry.Description).ToArray());
            Assert.IsTrue(model.Generation.Rows.Where(row => row.Id is "normalMap" or "preview").All(row => row.State == GeneratedStatus.Current));

            // a change of the heightmap leaves both out of date, and generating what is out of date puts them in the map again
            BuildWall(model);
            CollectionAssert.AreEquivalent(new[] { "normalMap", "preview" }, model.Generation.Stale.Select(state => state.Generator.Id).ToArray());
            MapDocument walled = model.Map.Document;
            await Run(model, "generate.stale");
            Assert.AreNotSame(walled.Terrain.NormalMap, model.Map.Document.Terrain.NormalMap);
            Assert.AreNotSame(walled.Terrain.Preview, model.Map.Document.Terrain.Preview);
            Assert.IsFalse(model.Generation.HasStale);

            model.Undo();
            model.Undo();
            model.Undo();
            Assert.AreSame(after.Terrain.NormalMap, model.Map.Document.Terrain.NormalMap, "each is a step of undo");
        }

        [TestMethod]
        public void ReducesAPictureByAveraging()
        {
            uint[] pixels = [0xFF000000, 0xFFFFFFFF, 0xFF000000, 0xFFFFFFFF];

            TiledBuffer<Color32> reduced = MapPreview.Reduce(pixels, 2, 2, 1, 1);

            Assert.AreEqual(new Color32(255, 127, 127, 127), reduced[0, 0]);
            Assert.AreEqual(new Color32(255, 255, 255, 255), MapPreview.Reduce(pixels, 2, 2, 4, 4)[2, 1], "larger: the nearest");
        }

        /// <summary>
        /// HardFFA without the mass deposits near its first spawn.
        /// </summary>
        private static Spawn TakeMassFromFirstSpawn(MainViewModel model)
        {
            Spawn spawn = model.Map!.Document.Markers.Spawns[0];
            Vector2 at = new Vector2(spawn.Position.X, spawn.Position.Z);
            model.Map.Session.Edit("Take the mass away", document => document with
            {
                Markers = document.Markers with
                {
                    Resources = [.. document.Markers.Resources.Where(resource => Vector2.Distance(new Vector2(resource.Position.X, resource.Position.Z), at) > MapChecks.MassNearSpawn)],
                },
            });
            return spawn;
        }

        [TestMethod]
        public async Task ChecksTheMapAfterEachStepAndGoesToAProblem()
        {
            MainViewModel model = await Open();
            Assert.IsTrue(model.Problems.IsEmpty, string.Join("; ", model.Problems.Items.Select(item => item.Message)));
            Assert.AreEqual("Problems", model.Problems.Header);

            Spawn spawn = TakeMassFromFirstSpawn(model);

            ProblemItem item = model.Problems.Items.First(item => item.Problem.Thing == ObjectRef.Marker(spawn.Id));
            Assert.IsTrue(item.IsWarning);
            Assert.IsTrue(model.Problems.Warnings > 0);
            Assert.AreEqual($"Problems ({model.Problems.Warnings})", model.Problems.Header);
            (double? X, double? Z, double? Zoom)? shown = null;
            model.TopViewRequested += (x, z, zoom) => shown = (x, z, zoom);

            CommandRun run = await Run(model, "problems.show", new JsonObject { ["index"] = item.Index });

            Assert.IsTrue((bool)run.Result!);
            CollectionAssert.AreEqual(new[] { ObjectRef.Marker(spawn.Id) }, model.Selection.Items.ToArray());
            Assert.AreEqual(spawn.Position.X, shown!.Value.X!.Value, 1e-3);
            Assert.AreEqual(spawn.Position.Z, shown.Value.Z!.Value, 1e-3);
            Assert.AreEqual(MainViewModel.ProblemZoom, shown.Value.Zoom);
            Assert.AreEqual(item.Message, model.Status);
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "problems.show", new JsonObject { ["index"] = 99 }));

            model.Undo();
            Assert.IsTrue(model.Problems.IsEmpty, "an undo checks again");
        }

        [TestMethod]
        public async Task CheckingTheMapListsWhatItFound()
        {
            MainViewModel model = await Open();
            model.Map!.Session.Edit("Take a spawn away", document => document with { Markers = document.Markers with { Spawns = document.Markers.Spawns.RemoveAt(0) } });

            JsonObject found = (JsonObject)(await Run(model, "plugins.check")).Result!;

            Assert.AreEqual(DockTab.Problems, model.Dock);
            Assert.AreEqual(1, found["errors"]!.GetValue<int>());
            JsonNode first = found["items"]![0]!;
            Assert.AreEqual("Error", first["severity"]!.GetValue<string>());
            Assert.AreEqual("army-without-spawn", first["check"]!.GetValue<string>());
            Assert.AreEqual(model.Problems.Summary, model.Status);
        }

        [TestMethod]
        public async Task SavesAMapWithProblemsAndSaysSo()
        {
            MainViewModel model = await Open();
            model.Map!.Session.Edit("Take a spawn away", document => document with { Markers = document.Markers with { Spawns = document.Markers.Spawns.RemoveAt(0) } });
            string folder = Path.Combine(AppContext.BaseDirectory, "projects", Guid.NewGuid().ToString("N"));

            await model.SaveAsAsync(folder);

            Assert.IsTrue(File.Exists(Path.Combine(folder, "map.json")), "problems never stop a save");
            StringAssert.Contains(model.Status, "1 error");
            StringAssert.Contains(model.Status, "see Problems");
        }

        [TestMethod]
        public async Task ACheckOfAFeatureJoinsTheList()
        {
            MainViewModel model = await Open();
            model.Problems.Checks.Register(new ProblemCheck("everything", "Everything", context => [new Problem("everything", ProblemSeverity.Info, "A note", Place: new Vector2(10, 20))]));
            model.Problems.Checks.Register(new ProblemCheck("broken", "Broken", _ => throw new InvalidOperationException("broken")));

            model.Problems.Run();

            Assert.AreEqual("A note", model.Problems.Items.Single().Message);
            Assert.AreEqual("1 note", model.Problems.Summary);
            Assert.AreEqual("Problems", model.Problems.Header, "notes are not counted on the tab");
            Assert.IsTrue(model.ShowProblem(0), "a place without a thing");
            Assert.AreEqual(0, model.Selection.Count);
        }
    }
}
