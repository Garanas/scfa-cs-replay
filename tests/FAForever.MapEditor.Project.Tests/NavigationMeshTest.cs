using System.Diagnostics;
using System.Numerics;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;

namespace FAForever.MapEditor.Project.Tests
{
    [TestClass]
    public class NavigationMeshTest
    {
        [TestMethod]
        public void ListsTheRegionsWithTheirStartsAndDeposits()
        {
            MapDocument document = MapImporter.ImportFolder(ProjectFolders.ThetaPassage);

            NavigationMesh mesh = (NavigationMesh)new NavigationMeshGenerator().Generate(document);

            IReadOnlyList<NavRegion> land = mesh.RegionsOf(NavLayer.Land);
            Assert.IsTrue(land.Count > 0);
            CollectionAssert.AreEqual(land.OrderByDescending(region => region.Cells).ToList(), land.ToList(), "the largest first");
            // every start lies in a region of the land, and the deposits are counted as the game assigns them
            CollectionAssert.AreEquivalent(document.Markers.Spawns.Select(spawn => spawn.Army).ToList(), land.SelectMany(region => region.Starts).ToList());
            Assert.AreEqual(mesh.Navigation.Land.Labels.Sum(label => label.Extractors.Count), land.Sum(region => region.Mass));
            Spawn first = document.Markers.Spawns[0];
            Assert.AreEqual(land.Single(region => region.Starts.Contains(first.Army)), mesh.RegionAt(NavLayer.Land, first.Position.X, first.Position.Z));
        }

        [TestMethod]
        public void SaysWhichStartsLieOnGroundUnitsCannotLeave()
        {
            // HardFFA's eight starts crowd the middle of the map, on ground too steep for land units
            MapDocument document = MapImporter.ImportFolder(ProjectFolders.HardFfa);

            NavigationMesh mesh = (NavigationMesh)new NavigationMeshGenerator().Generate(document);

            Assert.AreEqual(8, mesh.StrandedOf(NavLayer.Land, document.Markers.Spawns).Count);
        }

        [TestMethod]
        public void UsesWalkabilitysCurrentMeshAndReadsTheSpawns()
        {
            MapDocument document = MapImporter.ImportFolder(ProjectFolders.HardFfa);
            GeneratedResults results = new GeneratedResults();
            results.Register(new WalkabilityGenerator());
            results.Register(new NavigationMeshGenerator());
            results.Generate(WalkabilityGenerator.GeneratorId, document);

            GeneratedResult result = results.Generate(NavigationMeshGenerator.GeneratorId, document);
            MapDocument moved = document with { Markers = document.Markers with { Spawns = document.Markers.Spawns.SetItem(0, document.Markers.Spawns[0] with { Position = new Vector3(10, 0, 10) }) } };

            Assert.AreSame(results.ResultOf(WalkabilityGenerator.GeneratorId)!.Value, ((NavigationMesh)result.Value).Navigation, "walkability's current mesh is used");
            CollectionAssert.AreEqual(new[] { "spawns" }, result.ChangedInputs(moved).ToArray());
            Assert.AreEqual(GeneratedStatus.Current, results.States(document with { Markers = document.Markers with { PathNodes = [] } })[1].Status, "path nodes do not matter");
        }

        [TestMethod]
        public void ACancelledRunStops()
        {
            MapDocument document = MapImporter.ImportFolder(ProjectFolders.HardFfa);
            using CancellationTokenSource cancel = new CancellationTokenSource();
            cancel.Cancel();

            Assert.ThrowsException<OperationCanceledException>(() => ((IGenerator)new WalkabilityGenerator()).Generate(document, cancel.Token));
            Assert.ThrowsException<OperationCanceledException>(() => ((IGenerator)new NavigationMeshGenerator()).Generate(document, cancel.Token));
            Assert.ThrowsException<OperationCanceledException>(() => ((IGenerator)new NormalMapGenerator()).Generate(document, cancel.Token), "a generator that cannot stop midway does not start");
        }

        /// <summary>
        /// On a map of 4096 by 4096 ogrids (81 km) the connected regions take well under a second
        /// once walkability is there.
        /// </summary>
        [TestMethod]
        public void IsQuickEnoughOnTheLargestMaps()
        {
            MapDocument document = Large(MapImporter.ImportFolder(ProjectFolders.HardFfa), 4096);
            Stopwatch watch = Stopwatch.StartNew();
            MapNavigation navigation = Walkability.Generate(document);
            TimeSpan walkability = watch.Elapsed;

            NavigationMesh mesh = NavigationMesh.Of(document, navigation);
            TimeSpan regions = watch.Elapsed - walkability;

            Console.WriteLine($"4096 map: walkability {walkability.TotalMilliseconds:0} ms, connected regions {regions.TotalMilliseconds:0} ms");
            Assert.AreEqual(4, mesh.RegionsOf(NavLayer.Land).SelectMany(region => region.Starts).Count(), "the gentle hills hold every start");
            Assert.IsTrue(regions < TimeSpan.FromSeconds(5), $"{regions.TotalSeconds:0.0} s");
        }

        /// <summary>
        /// A map of <paramref name="size"/> ogrids with gentle hills and four starts near the corners.
        /// </summary>
        internal static MapDocument Large(MapDocument template, int size)
        {
            int side = size + 1;
            ushort[] samples = new ushort[side * side];
            Parallel.For(0, side, z =>
            {
                for (int x = 0; x < side; x++)
                {
                    samples[z * side + x] = (ushort)(2000 + 600 * MathF.Sin(x / 97f) * MathF.Cos(z / 131f));
                }
            });
            Spawn Start(int army, float x, float z) => new Spawn { Id = army, Name = $"ARMY_{army}", Position = new Vector3(x, 0, z) };
            return template with
            {
                Terrain = template.Terrain with
                {
                    Heightmap = new Heightmap { Scale = 1f / 128, Samples = TiledBuffer<ushort>.FromArray(side, side, samples) },
                    Types = TiledBuffer<byte>.Create(size, size, 1),
                },
                Water = template.Water with { HasWater = false },
                Markers = new MarkerSet { Spawns = [Start(1, 300, 300), Start(2, 700, 400), Start(3, size - 300, size - 300), Start(4, size - 800, size - 350)] },
            };
        }
    }
}
