using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// Results regenerating on their own (the owner's decisions Q4 and Q5): every generator may, only
    /// while the views show its result; a result that is part of the map goes into it without a step
    /// of undo; the editor's switch; the workspace's defaults for new and imported maps.
    /// </summary>
    [TestClass]
    public class AutoRegenerateTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private static async Task<MainViewModel> Open()
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            await model.OpenAsync(HardFfa);
            model.AutoGeneration.Delay = TimeSpan.FromMilliseconds(20);
            return model;
        }

        private static async Task<CommandRun> Run(MainViewModel model, string id, JsonObject? arguments = null)
        {
            CommandRun run = await model.Commands.TryRunAsync(id, arguments ?? [], interactive: false);
            Assert.IsTrue(run.Ran, $"{id}: {run.Why ?? model.Status}");
            return run;
        }

        private static GeneratedStatus StateOf(MainViewModel model, string id) =>
            model.Generation.Results.States(model.Map!.Document).Single(state => state.Generator.Id == id).Status;

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
        public async Task AResultRegeneratesOnlyWhileTheViewsShowIt()
        {
            MainViewModel model = await Open();
            await Run(model, "generate.auto", new JsonObject { ["id"] = "walkability", ["on"] = true });

            Assert.IsFalse(model.AutoGeneration.IsPending("walkability"), "its overlay is off");
            CollectionAssert.AreEqual(new[] { "walkability" }, model.AutoGeneration.Hidden.ToArray());
            StringAssert.Contains(model.AutoRegenerateTip, "waiting until the views show them: walkability");

            // shown: it regenerates
            await Run(model, "view.overlay.walkability");
            Assert.IsTrue(model.AutoGeneration.IsPending("walkability"));
            await model.AutoGeneration.WhenIdleAsync();
            Assert.AreEqual(GeneratedStatus.Current, StateOf(model, "walkability"));

            // hidden again: a change waits until it shows
            await Run(model, "view.overlay.walkability");
            Raise(model, 100, 102);
            Assert.IsFalse(model.AutoGeneration.IsPending("walkability"));
            Assert.AreEqual(GeneratedStatus.Stale, StateOf(model, "walkability"));
            model.ShowWalkability = true;
            Assert.IsTrue(model.AutoGeneration.IsPending("walkability"), "showing it starts it");
            await model.AutoGeneration.WhenIdleAsync();
            Assert.AreEqual(GeneratedStatus.Current, StateOf(model, "walkability"));
        }

        [TestMethod]
        public async Task ADocumentResultOnItsOwnGoesIntoTheMapWithoutAStepAndNeverStartsItselfAgain()
        {
            MainViewModel model = await Open();
            model.SelectMode(ProceduralMode.Id);
            await Run(model, "generate.auto", new JsonObject { ["id"] = NormalMapGenerator.GeneratorId, ["on"] = true });
            Raise(model, 100, 102);
            int steps = model.History.Count;
            string undo = model.Map!.Session.UndoDescription!;
            TiledBuffer<Color32> before = model.Map.Document.Terrain.NormalMap;
            Assert.IsTrue(model.AutoGeneration.IsPending(NormalMapGenerator.GeneratorId), "the Procedural mode shows its picture");

            await model.AutoGeneration.WhenIdleAsync();

            Assert.AreNotSame(before, model.Map.Document.Terrain.NormalMap, "into the map");
            Assert.AreEqual(steps, model.History.Count, "no step of undo");
            Assert.AreEqual(undo, model.Map.Session.UndoDescription);
            Assert.AreEqual(GeneratedStatus.Current, StateOf(model, NormalMapGenerator.GeneratorId));
            Assert.IsFalse(model.AutoGeneration.IsPending(NormalMapGenerator.GeneratorId), "its own change does not start it again");
            Assert.AreEqual("Regenerated the normal map on its own", model.Status);
            int kept = model.AutoGeneration.Kept;
            await model.AutoGeneration.WhenIdleAsync();
            Assert.AreEqual(kept, model.AutoGeneration.Kept);

            // undo takes the ridge back with the normal map made for the map before it
            model.Undo();
            Assert.AreSame(before, model.Map.Document.Terrain.NormalMap);
            Assert.IsFalse(model.AutoGeneration.IsPending(NormalMapGenerator.GeneratorId), "the version before holds its own result");
        }

        [TestMethod]
        public async Task UndoAndRedoShowTheResultsBesideTheMapMadeForThatVersionAtOnce()
        {
            MainViewModel model = await Open();
            await Run(model, "generate.auto", new JsonObject { ["id"] = NavigationMeshGenerator.GeneratorId, ["on"] = true });
            await Run(model, "view.overlay.navigation");
            await model.AutoGeneration.WhenIdleAsync();
            NavigationMesh first = model.Navigation.Mesh!;
            object walkabilityBefore = model.Generation.Results.ResultOf(WalkabilityGenerator.GeneratorId)?.Value!;

            Raise(model, 100, 102);
            await model.AutoGeneration.WhenIdleAsync();
            NavigationMesh second = model.Navigation.Mesh!;
            Assert.AreNotSame(first, second);
            int kept = model.AutoGeneration.Kept;

            // undo: the mesh made for the version before shows at once, and nothing regenerates
            model.Undo();
            Assert.AreSame(first, model.Navigation.Mesh);
            Assert.AreEqual(GeneratedStatus.Current, StateOf(model, NavigationMeshGenerator.GeneratorId));
            Assert.IsFalse(model.AutoGeneration.IsPending(NavigationMeshGenerator.GeneratorId));
            if (walkabilityBefore is not null)
            {
                Assert.AreSame(walkabilityBefore, model.Generation.Results.ResultOf(WalkabilityGenerator.GeneratorId)!.Value);
            }

            // redo: the one made after the ridge
            model.Redo();
            Assert.AreSame(second, model.Navigation.Mesh);
            Assert.AreEqual(GeneratedStatus.Current, StateOf(model, NavigationMeshGenerator.GeneratorId));
            await model.AutoGeneration.WhenIdleAsync();
            Assert.AreEqual(kept, model.AutoGeneration.Kept, "nothing regenerated");
            Assert.AreEqual(2, model.Generation.Results.RecentOf(NavigationMeshGenerator.GeneratorId).Count);
        }

        [TestMethod]
        public async Task MasksFromRulesRegenerateOnTheirOwnWhileTheMaskShows()
        {
            MainViewModel model = await Open();
            await Run(model, "paint.addMask", new JsonObject { ["name"] = "flat" });
            await Run(model, "paint.maskRules", new JsonObject { ["name"] = "flat" });
            await Run(model, "paint.addMaskRule", new JsonObject { ["kind"] = "distance to water", ["min"] = 2, ["max"] = 10000 });
            await Run(model, "generate.auto", new JsonObject { ["id"] = MaskRulesGenerator.GeneratorId, ["on"] = true });
            Assert.IsTrue(model.AutoGeneration.IsShown(MaskRulesGenerator.GeneratorId, model.Map!.Document), "the mask with rules shows");
            byte[] mask = model.Map.Document.Masks["flat"].ToArray();
            int steps = model.History.Count;

            model.Map.Session.Edit("Raise the water", document => document with { Water = document.Water with { Elevation = document.Water.Elevation + 6 } });
            await model.AutoGeneration.WhenIdleAsync();

            Assert.IsFalse(model.Map.Document.Masks["flat"].ToArray().SequenceEqual(mask), "the mask follows the water");
            Assert.AreEqual(steps + 1, model.History.Count, "the water is the step; the mask is derived");
            Assert.AreEqual(GeneratedStatus.Current, StateOf(model, MaskRulesGenerator.GeneratorId));

            // the mask hidden in another mode: no stratum has rules, so it waits
            model.ShowMask = false;
            model.SelectMode("terrain");
            Assert.IsFalse(model.AutoGeneration.IsShown(MaskRulesGenerator.GeneratorId, model.Map.Document));
        }

        [TestMethod]
        public async Task TheSwitchTurnsItOffForEveryMapAndOnAgain()
        {
            MainViewModel model = await Open();
            model.ShowWalkability = true;
            await Run(model, "generate.auto", new JsonObject { ["on"] = true });
            await model.AutoGeneration.WhenIdleAsync();
            Assert.AreEqual("Auto", model.AutoRegenerateText);

            CommandRun off = await Run(model, "generate.automatic");
            Assert.AreEqual(false, off.Result);
            Assert.IsFalse(model.RegenerateOnTheirOwn);
            Assert.AreEqual("Auto off", model.AutoRegenerateText);
            Raise(model, 100, 102);
            Assert.IsFalse(model.AutoGeneration.IsPending("walkability"), "off: only when asked");
            Assert.IsTrue(model.Map!.Document.Procedural.RegeneratesOnItsOwn("walkability"), "the map keeps its setting");

            await Run(model, "generate.automatic", new JsonObject { ["on"] = true });
            Assert.IsTrue(model.AutoGeneration.IsPending("walkability"), "on again: what is out of date and shown regenerates");
            StringAssert.StartsWith(model.AutoRegenerateText, "Auto:");
            await model.AutoGeneration.WhenIdleAsync();
            Assert.AreEqual(GeneratedStatus.Current, StateOf(model, "walkability"));
        }

        [TestMethod]
        public async Task NewAndImportedMapsStartWithTheWorkspacesDefaults()
        {
            string root = Path.Combine(AppContext.BaseDirectory, "workspaces", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "game"));
            Workspace workspace = Workspace.Create(Path.Combine(root, "workspace"), Path.Combine(root, "game"), null, Path.Combine(root, "vault"));
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            model.Setup.Use(workspace);

            // without a block: walkability, connected regions and masks from rules
            model.OpenNewMap(Project.NewMap.Create(new Project.NewMapSettings { Size = 256 }));
            CollectionAssert.AreEquivalent(new[] { "walkability", "navigationMesh", "masksFromRules" }, model.Map!.Document.Procedural.AutoGenerate.ToArray());
            CollectionAssert.AreEquivalent(new[] { "walkability", "navigationMesh" }, model.AutoGeneration.Hidden.ToArray(),
                "masks from rules on a map without rules wait for their own command, as Everything leaves them");

            // this map's settings become the workspace's
            await Run(model, "generate.auto", new JsonObject { ["id"] = "navigationMesh", ["on"] = false });
            await Run(model, "generate.auto", new JsonObject { ["id"] = "pathNetwork", ["on"] = true });
            CommandRun used = await Run(model, "generate.useAsWorkspaceDefault");
            CollectionAssert.AreEquivalent(new[] { "masksFromRules", "pathNetwork", "walkability" }, ((JsonObject)used.Result!)["autoGenerate"]!.AsArray().Select(id => (string)id!).ToArray());
            Assert.AreEqual(Workspace.Open(workspace.Folder).Procedural, model.Setup.Workspace!.Procedural, "written into workspace.json");

            // the Workspace settings page's check boxes
            await Run(model, "workspace.setProcedural", new JsonObject { ["id"] = "walkability", ["auto"] = false });
            await Run(model, "workspace.setProcedural", new JsonObject { ["id"] = "pathNetwork", ["onExport"] = true });
            Assert.IsFalse((await model.Commands.TryRunAsync("workspace.setProcedural", new JsonObject { ["id"] = "walkability", ["onExport"] = true }, interactive: false)).Ran,
                "the export has no choice about walkability");
            WorkspaceProceduralRow row = model.WorkspaceProceduralRows.Single(line => line.Id == "pathNetwork");
            Assert.IsTrue(row.Auto && row.HasExport && row.OnExport);
            Assert.IsFalse(model.WorkspaceProceduralRows.Single(line => line.Id == "walkability").Auto);

            // an imported map starts with them; an opened project keeps its own
            await model.OpenAsync(HardFfa);
            CollectionAssert.AreEquivalent(new[] { "masksFromRules", "pathNetwork" }, model.Map!.Document.Procedural.AutoGenerate.ToArray());
            Assert.IsTrue(model.Map.Document.Procedural.PathNodes.WritesOnExport);
            string project = Path.Combine(root, "project");
            Project.MapProjectStore.Save(Project.MapImporter.ImportFolder(HardFfa), project);
            await model.OpenAsync(project);
            Assert.IsTrue(model.Map!.Document.Procedural.AutoGenerate.IsEmpty, "a project keeps its own settings");
        }
    }
}
