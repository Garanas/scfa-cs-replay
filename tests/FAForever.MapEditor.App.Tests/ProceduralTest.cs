using System.Text.Json.Nodes;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Rendering;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;
using Microsoft.Extensions.Logging.Abstractions;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The Procedural mode: the generated results gathered with their previews and settings,
    /// walkability regenerating on its own, the navigation mesh (T-8) and the export as steps (M-9).
    /// </summary>
    [TestClass]
    public class ProceduralTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "procedural");

        private static async Task<MainViewModel> Open()
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            await model.OpenAsync(HardFfa);
            model.AutoGeneration.Delay = TimeSpan.FromMilliseconds(20);
            return model;
        }

        private static Task<CommandRun> Run(MainViewModel model, string id, JsonObject? arguments = null) =>
            model.Commands.TryRunAsync(id, arguments ?? [], interactive: false);

        private static void Raise(MainViewModel model, int x0, int x1) => model.Map!.Session.Edit("Raise a ridge", document =>
        {
            Heightmap heightmap = document.Terrain.Heightmap;
            TiledBuffer<ushort>.Builder samples = heightmap.Samples.ToBuilder();
            for (int z = 0; z <= heightmap.Height; z++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    samples[x, z] = (ushort)Math.Min(ushort.MaxValue, samples[x, z] + 30 / heightmap.Scale);
                }
            }
            return document with { Terrain = document.Terrain with { Heightmap = heightmap with { Samples = samples.ToBuffer() } } };
        });

        [TestMethod]
        public void TheModeGathersWhatIsGenerated()
        {
            ModeDefinition procedural = EditorModes.All.Single(mode => mode.Id == "procedural");

            Assert.AreSame(EditorModes.All[^1], procedural, "at the bottom of the rail");
            CollectionAssert.AreEqual(new[] { "Generated results" }, procedural.Sections.Select(section => section.Title).ToArray());
            Assert.IsFalse(EditorModes.All.Single(mode => mode.Id == "terrain").Sections.Any(section => section.Title == "Generated from the layers"), "moved out of the Terrain panel");
            Assert.IsFalse(EditorModes.All.Single(mode => mode.Id == "markers").Sections.Any(section => section.Title == "Path networks"), "moved out of the Markers panel");
        }

        [TestMethod]
        public async Task EachRowHasItsPreviewItsOwnRegeneratingAndWhatTheExportDoes()
        {
            MainViewModel model = await Open();

            ProceduralRow walkability = model.Procedural.Rows.Single(row => row.Id == "walkability");
            ProceduralRow normalMap = model.Procedural.Rows.Single(row => row.Id == "normalMap");
            ProceduralRow preview = model.Procedural.Rows.Single(row => row.Id == "preview");
            ProceduralRow navigation = model.Procedural.Rows.Single(row => row.Id == "navigationMesh");
            Assert.AreEqual("generate.walkability", walkability.Row.CommandId, "the commands keep their ids");
            Assert.AreEqual("markers.connectPaths", model.Procedural.Rows.Single(row => row.Id == "pathNetwork").Row.CommandId);
            Assert.AreEqual("walkability", walkability.Overlay);
            Assert.AreEqual("navigation", navigation.Overlay);
            Assert.IsTrue(walkability.CanAuto);
            Assert.IsFalse(normalMap.CanAuto);
            Assert.AreEqual(ExportUse.WhenOutOfDate, normalMap.Export);
            Assert.IsTrue(normalMap.OnExport);
            Assert.AreEqual(ExportUse.Always, preview.Export);
            Assert.AreEqual(ExportUse.None, walkability.Export);

            await model.RunAsync(walkability.OverlayCommand!);
            Assert.IsTrue(model.Procedural.Rows.Single(row => row.Id == "walkability").PreviewOn, "the preview runs the overlay's command");

            Assert.IsTrue((await Run(model, "generate.onExport", new JsonObject { ["id"] = "normalMap", ["on"] = false })).Ran);
            Assert.IsFalse(model.Procedural.Rows.Single(row => row.Id == "normalMap").OnExport);
            Assert.AreEqual("Keep the normal map on export", model.Map!.Session.UndoDescription);
            Assert.IsFalse((await Run(model, "generate.onExport", new JsonObject { ["id"] = "preview" })).Ran, "the preview is always rendered");
            Assert.IsFalse((await Run(model, "generate.auto", new JsonObject { ["id"] = "normalMap" })).Ran, "the normal map has not opted in");
        }

        [TestMethod]
        public async Task EverythingMakesWhatIsMissingAndSaysWhyItLeavesTheRest()
        {
            MainViewModel model = await Open();
            Raise(model, 100, 102);
            CommandRun listed = await Run(model, "generate.all", new JsonObject { ["list"] = true });
            JsonObject list = (JsonObject)listed.Result!;
            string[] toMake = [.. list["made"]!.AsArray().Select(item => (string)item!["id"]!)];
            CollectionAssert.IsSubsetOf(new[] { "walkability", "navigationMesh" }, toMake, "never made: made");
            Assert.IsTrue(model.Generation.Results.InOrder().Select(generator => generator.Id).Where(toMake.Contains).SequenceEqual(toMake), "in the order one feeds the next");
            Assert.IsFalse(model.Generation.Results.States(model.Map!.Document).All(state => state.Status != GeneratedStatus.Missing), "listing makes nothing");

            CommandRun run = await Run(model, "generate.all");
            JsonObject result = (JsonObject)run.Result!;
            CollectionAssert.AreEqual(toMake, result["made"]!.AsArray().Select(item => (string)item!["id"]!).ToArray());
            Dictionary<string, string> left = result["left"]!.AsArray().ToDictionary(item => (string)item!["id"]!, item => (string)item!["why"]!);
            StringAssert.Contains(left["skyCube"], "only when asked");
            Assert.AreEqual("the map has no rules layer", left[MaskRulesGenerator.GeneratorId]);
            Assert.AreEqual("the map has no prop rules", left[PropRulesGenerator.GeneratorId]);
            StringAssert.Contains(model.Status, "left out: ");
            StringAssert.Contains(model.Status, "(never made)");

            IReadOnlyList<GeneratedState> states = model.Generation.Results.States(model.Map!.Document);
            Assert.IsFalse(states.Any(state => state.Status == GeneratedStatus.Stale), "nothing out of date");
            CollectionAssert.AreEquivalent(left.Keys.ToArray(), states.Where(state => state.Status == GeneratedStatus.Missing && model.Generation.CommandOf(state.Generator.Id) is not null)
                .Select(state => state.Generator.Id).ToArray(), "missing are only those left out, with why");
        }

        [TestMethod]
        public async Task WalkabilityRegeneratesASecondAfterAChange()
        {
            MainViewModel model = await Open();
            Assert.IsTrue((await Run(model, "generate.auto", new JsonObject { ["on"] = true })).Ran);
            Assert.IsTrue(model.Map!.Document.Procedural.RegeneratesOnItsOwn("walkability"));
            Assert.AreEqual("Regenerate the walkability on its own", model.Map.Session.UndoDescription);

            // turned on, a map without walkability gets it; a result kept outside the map is no step of undo
            int steps = model.History.Count;
            await model.AutoGeneration.WhenIdleAsync();
            Assert.AreEqual(GeneratedStatus.Current, model.Generation.Results.States(model.Map.Document)[0].Status);
            Assert.AreEqual(steps, model.History.Count, "the first run on its own adds no step");
            Assert.AreEqual("Regenerate the walkability on its own", model.Map.Session.UndoDescription, "the last step is still turning it on");
            int kept = model.AutoGeneration.Kept;

            // a change to its inputs: it is out of date, then current again on its own
            Raise(model, 100, 102);
            Assert.IsTrue(model.AutoGeneration.IsPending("walkability"), "the timer runs");
            await model.AutoGeneration.WhenIdleAsync();
            Assert.AreEqual(kept + 1, model.AutoGeneration.Kept);
            Assert.AreEqual(steps + 1, model.History.Count, "only the change is a step");
            Assert.AreEqual(GeneratedStatus.Current, model.Generation.Results.States(model.Map.Document).Single(state => state.Generator.Id == "walkability").Status);
            StringAssert.StartsWith(model.Status, "Regenerated the walkability on its own");

            // a change to something else leaves it alone
            model.Map.Session.Edit("Recolour the water", document => document with { Water = document.Water with { SurfaceColor = new System.Numerics.Vector3(1, 0, 0) } });
            Assert.IsFalse(model.AutoGeneration.IsPending("walkability"));

            // turned off, a change waits for Generate
            Assert.IsTrue((await Run(model, "generate.auto", new JsonObject { ["on"] = false })).Ran);
            Raise(model, 140, 142);
            Assert.IsFalse(model.AutoGeneration.IsPending("walkability"));
            Assert.AreEqual(GeneratedStatus.Stale, model.Generation.Results.States(model.Map.Document)[0].Status);
        }

        /// <summary>
        /// A generator that waits until it is let go, and notes whether a run was stopped.
        /// </summary>
        private sealed class SlowGenerator : IGenerator
        {
            public SemaphoreSlim Started { get; } = new SemaphoreSlim(0);

            public int Runs;

            public int Stopped;

            public string Id => "slow";

            public string Title => "Slow";

            public string Story => "T-7";

            public IReadOnlyList<GeneratedInput> Inputs { get; } = [GeneratedInput.Heightmap];

            public object Generate(MapDocument document) => Generate(document, CancellationToken.None);

            public object Generate(MapDocument document, CancellationToken cancel)
            {
                Interlocked.Increment(ref Runs);
                Started.Release();
                try
                {
                    Task.Delay(TimeSpan.FromSeconds(5), cancel).Wait(cancel);
                }
                catch (OperationCanceledException)
                {
                    Interlocked.Increment(ref Stopped);
                    throw;
                }
                return document.Terrain.Heightmap;
            }
        }

        [TestMethod]
        public async Task ANewerChangeStopsARunAndStartsAgain()
        {
            MapDocument document = Project.MapImporter.ImportFolder(HardFfa) with
            {
                Procedural = ProceduralSettings.Default.WithAutoGenerate("slow", true),
            };
            GenerationViewModel generation = new GenerationViewModel();
            SlowGenerator slow = new SlowGenerator();
            generation.Register(slow, "generate.slow");
            MapDocument current = document;
            AutoGeneration auto = new AutoGeneration(generation, () => current, null, NullLogger.Instance) { Delay = TimeSpan.FromMilliseconds(10) };
            auto.Allow("slow");

            auto.Follow();
            Assert.IsTrue(await slow.Started.WaitAsync(TimeSpan.FromSeconds(5)), "it runs a moment after");
            Assert.IsTrue(generation.IsRunning("slow"));

            current = document with { Terrain = document.Terrain with { Heightmap = document.Terrain.Heightmap with { Scale = document.Terrain.Heightmap.Scale * 2 } } };
            auto.Follow();

            Assert.AreEqual(1, auto.Cancelled, "the newer change stopped the run");
            Assert.IsTrue(await slow.Started.WaitAsync(TimeSpan.FromSeconds(5)), "and started again");
            Assert.AreEqual(2, slow.Runs);
            auto.StopAll();
            await auto.WhenIdleAsync();
            Assert.IsFalse(generation.IsRunning("slow"));
            Assert.IsNull(generation.Results.ResultOf("slow"), "a stopped run keeps nothing");
        }

        [TestMethod]
        public async Task TheNavigationMeshShowsRegionsStartsAndRoutes()
        {
            MainViewModel model = await Open();

            CommandRun run = await Run(model, "generate.navigation");

            Assert.IsTrue(run.Ran, run.Why);
            Assert.IsTrue(model.ShowNavigation, "the views colour the regions");
            Assert.IsNotNull(model.Navigation.Mesh);
            Assert.IsTrue(model.Navigation.Regions.Count > 0);
            StringAssert.Contains(model.Navigation.Summary, "Starts in no region", "HardFFA's starts crowd ground land units cannot leave");
            Assert.AreEqual("Current", model.Procedural.Rows.Single(row => row.Id == "navigationMesh").Row.State.ToString());

            // one tint at a time
            model.ShowWalkability = true;
            Assert.IsFalse(model.ShowNavigation);
            model.ShowNavigation = true;
            Assert.IsFalse(model.ShowWalkability);

            // the region under a click, and the list's choice
            NavRegion largest = model.Navigation.Regions[0].Region;
            Assert.IsTrue((await Run(model, "generate.setNavigation", new JsonObject { ["region"] = largest.Id })).Ran);
            Assert.AreEqual(largest.Id, model.Navigation.ChosenRegion);
            Assert.IsTrue(model.Navigation.Regions[0].IsChosen);
            model.SelectTool("procedural.regions");
            model.Stroke([(5, 5)]);
            Assert.AreEqual(model.Navigation.Mesh!.RegionAt(NavLayer.Land, 5, 5)?.Id, model.Navigation.ChosenRegion);

            // the layer is the view's; the routes the map's, which makes the mesh out of date
            Assert.IsTrue((await Run(model, "generate.setNavigation", new JsonObject { ["layer"] = "water", ["routes"] = "all" })).Ran);
            Assert.AreEqual(NavLayer.Water, model.Navigation.Layer);
            Assert.AreEqual(RoutePairs.All, model.Map!.Document.Procedural.Navigation.Routes);
            Assert.AreEqual(RoutePairs.All, model.NavigationRoutes);
            CollectionAssert.Contains(model.Generation.Results.States(model.Map.Document).Single(state => state.Generator.Id == "navigationMesh").ChangedInputs.ToArray(), "navigation settings");
            Assert.IsFalse((await Run(model, "generate.setNavigation", new JsonObject { ["layer"] = "air" })).Ran, "air units go anywhere: no layer");
        }

        [TestMethod]
        public void RegionPixelsColourEachRegionInTurn()
        {
            NavLabel Label(int id) => new NavLabel(id, NavLayer.Land, 1, [], []);
            NavGrid grid = new NavGrid(NavLayer.Land, 3, 1, [1, -1, 2], [Label(1), Label(2)]);
            System.Numerics.Vector4[] palette = [new(1, 0, 0, 1), new(0, 1, 0, 1)];

            uint[] pixels = RegionPixels.Of(grid, palette, new(0, 0, 0, 0.5f), 2, new(0, 0, 1, 1));

            Assert.AreEqual(0xFFFF0000u, pixels[0]);
            Assert.AreEqual(0x7F000000u, pixels[1], "where units cannot go: darkened");
            Assert.AreEqual(0xFF0000FFu, pixels[2], "the chosen region");
        }

        [TestMethod]
        public async Task TheExportRunsItsStepsAndAlwaysRendersThePreview()
        {
            MainViewModel model = await Open();
            List<string> progress = [];
            model.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.Status) && model.Status.StartsWith("Exporting, step", StringComparison.Ordinal))
                {
                    progress.Add(model.Status);
                }
            };

            int steps = model.History.Count;
            MapDocument before = model.Map!.Document;
            string folder = await model.ExportAsync(Path.Combine(Root, Guid.NewGuid().ToString("N")));

            Assert.AreEqual(steps, model.History.Count, "the export works on a copy: no step of undo");
            Assert.AreSame(before, model.Map.Document, "the project is as it was");
            CollectionAssert.AreEqual(new[] { "normalMap", "preview", "wideDecals", "pathNodes", "walkabilityMarks", "ddsTextures", "write" }, model.Export.Lines.Select(line => line.Id).ToArray());
            CollectionAssert.AreEqual(new[] { ExportStepStatus.Skipped, ExportStepStatus.Done, ExportStepStatus.Skipped, ExportStepStatus.Skipped, ExportStepStatus.Skipped, ExportStepStatus.Skipped, ExportStepStatus.Done }, model.Export.Lines.Select(line => line.Status).ToArray());
            Assert.AreEqual("Exporting, step 2 of 7: Render the preview", progress[0]);
            StringAssert.StartsWith(File.ReadAllText(Path.Combine(folder, "HardFFA_scenario.lua")), "-- Made with the FAF Map Editor ");

            // a second export renders it again
            await model.ExportAsync(Path.Combine(Root, Guid.NewGuid().ToString("N")));
            Assert.AreEqual(ExportStepStatus.Done, model.Export.Lines[1].Status);
            Assert.AreEqual(steps, model.History.Count);

            // another map forgets the last export
            await model.OpenAsync(HardFfa);
            Assert.IsTrue(model.Export.Lines.All(line => line.Status == ExportStepStatus.Waiting));
            Assert.IsNull(model.Export.LastFolder);
        }

        [TestMethod]
        public async Task AStepChangesOnlyWhatIsExported()
        {
            MainViewModel model = await Open();
            model.Map!.Session.Edit("Mark", document => document with
            {
                WalkabilityMarks = TiledBuffer<byte>.Create(document.Width, document.Height, WalkabilityMarks.None).ToBuilder().Also(marks => marks[10, 10] = WalkabilityMarks.Blocked).ToBuffer(),
            });
            // what wave 9 adds the same way: path nodes written as markers, the project left alone
            model.Export.Register(new ExportStep("test.marker", "Add a marker", ExportStep.PathNodesOrder)
            {
                Run = run =>
                {
                    run.Document = run.Document with { Markers = run.Document.Markers with { AiMarkers = [.. run.Document.Markers.AiMarkers, new AiMarker { Id = 999_999, Name = "Exported only", Type = "Rally Point" }] } };
                    run.Note = "one marker";
                    return Task.CompletedTask;
                },
            });
            MapDocument before = model.Map.Document;

            string folder = await model.ExportAsync(Path.Combine(Root, Guid.NewGuid().ToString("N")));

            CollectionAssert.AreEqual(new[] { "normalMap", "preview", "wideDecals", "pathNodes", "test.marker", "walkabilityMarks", "ddsTextures", "write" }, model.Export.Lines.Select(line => line.Id).ToArray());
            Assert.AreEqual("Done: one marker", model.Export.Lines.Single(line => line.Id == "test.marker").StatusText);
            Assert.AreEqual("Done: 1 ogrid blocked, 0 ogrids open", model.Export.Lines.Single(line => line.Id == "walkabilityMarks").StatusText);
            StringAssert.Contains(File.ReadAllText(Path.Combine(folder, "HardFFA_save.lua")), "Exported only");
            Assert.IsFalse(model.Map.Document.Markers.AiMarkers.Any(marker => marker.Name == "Exported only"), "the map keeps what the map maker made");
            Assert.AreSame(before.WalkabilityMarks, model.Map.Document.WalkabilityMarks);
            Project.Files.InMemory.MapDocument imported = Project.MapImporter.ImportFolder(folder);
            Assert.AreEqual(WalkabilityMarks.BlockingType, imported.Terrain.Types[11, 11], "the mark is a blocking type in the files, at the ogrid's far corner");
        }

        [TestMethod]
        public async Task AFailedStepStopsTheExportAndSaysWhy()
        {
            MainViewModel model = await Open();
            model.Export.Register(new ExportStep("test.fail", "Fail", ExportStep.DecalsOrder) { Run = _ => throw new InvalidOperationException("no decal texture") });

            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => model.ExportAsync(Path.Combine(Root, Guid.NewGuid().ToString("N"))));

            ExportStepState failed = model.Export.Lines.Single(line => line.Id == "test.fail");
            Assert.AreEqual("Failed: no decal texture", failed.StatusText);
            Assert.AreEqual(ExportStepStatus.Waiting, model.Export.Lines[^1].Status, "nothing was written");
            Assert.IsFalse(model.Export.IsRunning);
        }
    }

    internal static class BuilderExtensions
    {
        public static T Also<T>(this T value, Action<T> action)
        {
            action(value);
            return value;
        }
    }
}
