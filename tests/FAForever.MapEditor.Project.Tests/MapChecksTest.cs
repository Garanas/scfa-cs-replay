using System.Collections.Immutable;
using System.Numerics;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Checks;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.Project.Tests
{
    [TestClass]
    public class MapChecksTest
    {
        private static MapDocument hardFfa = null!;

        [ClassInitialize]
        public static void Initialize(TestContext _) => hardFfa = MapImporter.ImportFolder(ProjectFolders.HardFfa);

        private static IReadOnlyList<Problem> Run(ProblemContext context, string check) =>
            [.. new ProblemChecks().Run(context).Where(problem => problem.Check == check)];

        [TestMethod]
        public void FindsNothingWrongWithAWellMadeMap()
        {
            IReadOnlyList<Problem> problems = new ProblemChecks().Run(new ProblemContext(hardFfa));

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.Select(problem => problem.Message)));
        }

        [TestMethod]
        public void FindsAPlayableArmyWithoutASpawn()
        {
            Spawn first = hardFfa.Markers.Spawns[0];
            // the spawn's marker alone: deleting it in the editor takes its army along
            MapDocument document = hardFfa with { Markers = hardFfa.Markers with { Spawns = hardFfa.Markers.Spawns.RemoveAt(0) } };

            Problem problem = Run(new ProblemContext(document), "army-without-spawn").Single();

            Assert.AreEqual(ProblemSeverity.Error, problem.Severity);
            StringAssert.Contains(problem.Message, first.Army);
            Assert.IsNull(problem.Thing);
        }

        [TestMethod]
        public void FindsADepositOnSteepGround()
        {
            ResourceMarker mass = hardFfa.Markers.Resources.First(resource => resource.Kind == ResourceKind.Mass);
            Heightmap heights = hardFfa.Terrain.Heightmap;
            int x = (int)mass.Position.X;
            int z = (int)mass.Position.Z;
            TiledBuffer<ushort>.Builder builder = heights.Samples.ToBuilder();
            builder[x + 1, z] = (ushort)(heights.Samples[x + 1, z] + 20 / heights.Scale);
            MapDocument document = hardFfa with { Terrain = hardFfa.Terrain with { Heightmap = heights with { Samples = builder.ToBuffer() } } };

            Problem problem = Run(new ProblemContext(document), "steep-deposit").Single();

            Assert.AreEqual(ProblemSeverity.Warning, problem.Severity);
            Assert.AreEqual(ObjectRef.Marker(mass.Id), problem.Thing, "double-clicking it selects the deposit");
            StringAssert.Contains(problem.Message, mass.Name);
        }

        [TestMethod]
        public void FindsASpawnWithoutMassNearby()
        {
            Spawn spawn = hardFfa.Markers.Spawns[0];
            Vector2 at = new Vector2(spawn.Position.X, spawn.Position.Z);
            MapDocument document = hardFfa with
            {
                Markers = hardFfa.Markers with
                {
                    Resources = [.. hardFfa.Markers.Resources.Where(resource => Vector2.Distance(new Vector2(resource.Position.X, resource.Position.Z), at) > MapChecks.MassNearSpawn)],
                },
            };

            Problem problem = Run(new ProblemContext(document), "spawn-without-mass").Single(problem => problem.Thing == ObjectRef.Marker(spawn.Id));

            Assert.AreEqual(ProblemSeverity.Warning, problem.Severity);
            Assert.AreEqual(ObjectRef.Marker(spawn.Id), problem.Thing);
            StringAssert.Contains(problem.Message, spawn.Army);
        }

        [TestMethod]
        public void FindsTexturesNeitherTheMapNorTheWorkspaceHas()
        {
            Decal template = new Decal { Id = 1, Textures = ["/env/evergreen/decals/e_mud001_albedo.dds"], Scale = new Vector3(10, 10, 10) };
            MapDocument document = hardFfa with
            {
                Decals =
                [
                    template,
                    template with { Id = 2, Textures = ["/env/nowhere/decals/gone_albedo.dds"] },
                    template with { Id = 3, Textures = ["/env/nowhere/decals/gone_albedo.dds", ""] },
                    template with { Id = 4, Textures = ["/maps/hardffa.v0001/own_albedo.dds"] },
                ],
                Files = hardFfa.Files.SetItem("own_albedo.dds", ImmutableArray.Create<byte>(1, 2, 3)),
            };
            ProblemContext context = new ProblemContext(document, HasAsset: path => !path.Contains("nowhere") && !path.Contains("missing"));

            Problem decal = Run(context, "missing-texture").Single();
            StringAssert.Contains(decal.Message, "2 decals");
            StringAssert.Contains(decal.Message, "gone_albedo");
            Assert.AreEqual(ObjectRef.Decal(2), decal.Thing);

            // a stratum's texture, and a map's own file it lacks, found without a workspace
            MapDocument layers = document with
            {
                Decals = [template with { Id = 5, Textures = ["/maps/hardffa.v0001/lost_albedo.dds"] }],
                Terrain = document.Terrain with { Layers = document.Terrain.Layers.SetItem(1, document.Terrain.Layers[1] with { AlbedoPath = "/env/missing/layers/x_albedo.dds" }) },
            };
            Assert.AreEqual(2, Run(context with { Document = layers }, "missing-texture").Count);
            Problem own = Run(new ProblemContext(layers), "missing-texture").Single();
            StringAssert.Contains(own.Message, "lost_albedo");
        }

        [TestMethod]
        public void FindsPropsAndUnitsWithoutABlueprintOrModel()
        {
            string assets = ProjectFolders.New("assets");
            void Write(string path, string text)
            {
                string file = Path.Combine(assets, path);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, text);
            }
            Write("env/evergreen/props/trees/pine06_prop.json", """{ "id": "x", "lods": [ { "mesh": "/env/evergreen/props/trees/pine06_lod0.glb" } ] }""");
            Write("env/evergreen/props/trees/oak01_prop.json", """{ "id": "x" }""");
            AssetCatalog catalog = AssetCatalog.Load(assets);
            MapDocument document = hardFfa with
            {
                Props =
                [
                    new Prop { Id = 1, Blueprint = "/env/evergreen/props/trees/pine06_prop.bp" },
                    new Prop { Id = 2, Blueprint = "/env/evergreen/props/trees/oak01_prop.bp" },
                    new Prop { Id = 3, Blueprint = "/env/Evergreen/Props/Trees/Oak01_prop.bp" },
                    new Prop { Id = 4, Blueprint = "/env/evergreen/props/trees/lost_prop.bp" },
                ],
            };

            IReadOnlyList<Problem> problems = Run(new ProblemContext(document, catalog), "missing-model");

            Problem lost = problems.Single(problem => problem.Severity == ProblemSeverity.Warning);
            Assert.AreEqual(ObjectRef.Prop(4), lost.Thing);
            Problem oak = problems.Single(problem => problem.Severity == ProblemSeverity.Info);
            Assert.AreEqual(ObjectRef.Prop(2), oak.Thing);
            StringAssert.Contains(oak.Message, "2 props");
            Assert.AreEqual(0, Run(new ProblemContext(document), "missing-model").Count, "nothing to check against without the workspace");
        }

        [TestMethod]
        public void FindsANewPassageAfterWalkabilityWasGeneratedAgain()
        {
            WalkabilityLink passage = new WalkabilityLink(new Vector2(32.5f, 42.5f), 2);
            WalkabilityChanges changes = new WalkabilityChanges(NavLayer.Land, 64, 64, new WalkabilityChange[64 * 64], 5, 0, [passage], []);

            Problem problem = Run(new ProblemContext(hardFfa, Walkability: changes), "walkability-changes").Single();

            Assert.AreEqual(ProblemSeverity.Warning, problem.Severity);
            Assert.AreEqual(passage.Place, problem.Place);
            StringAssert.Contains(problem.Message, "new passage");
        }

        [TestMethod]
        public void PutsTheWorstFirstAndKeepsGoingWhenACheckFails()
        {
            ProblemChecks checks = new ProblemChecks(withMapChecks: false);
            checks.Register(new ProblemCheck("info", "Info", _ => [new Problem("info", ProblemSeverity.Info, "a")]));
            checks.Register(new ProblemCheck("broken", "Broken", _ => throw new InvalidOperationException("broken")));
            checks.Register(new ProblemCheck("error", "Error", _ => [new Problem("error", ProblemSeverity.Error, "b")]));
            List<FailedCheck> failed = [];

            IReadOnlyList<Problem> problems = checks.Run(new ProblemContext(hardFfa), failed);

            CollectionAssert.AreEqual(new[] { "error", "info" }, problems.Select(problem => problem.Check).ToArray());
            Assert.AreEqual("broken", failed.Single().Check.Id);
            Assert.ThrowsException<ArgumentException>(() => checks.Register(new ProblemCheck("info", "Again", _ => [])));
        }
    }
}
