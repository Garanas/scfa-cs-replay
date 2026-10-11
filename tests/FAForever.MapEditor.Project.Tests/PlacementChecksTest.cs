using System.Diagnostics;
using FAForever.MapEditor.Project.Checks;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// The placement checks (user story U-12): structures that overlap, and structures on ground
    /// their blueprints rule out.
    /// </summary>
    [TestClass]
    public class PlacementChecksTest
    {
        private const string Army = "ARMY_1";

        private static MapDocument baseMap = null!;

        private static AssetCatalog catalog = null!;

        [ClassInitialize]
        public static void Initialize(TestContext _)
        {
            MapDocument imported = MapImporter.ImportFolder(ProjectFolders.HardFfa);
            Heightmap flat = new Heightmap { Scale = 1f / 128, Samples = TiledBuffer<ushort>.Create(imported.Width + 1, imported.Height + 1, (ushort)(20 * 128)) };
            baseMap = imported with { Terrain = imported.Terrain with { Heightmap = flat }, Water = imported.Water with { HasWater = false } };

            string folder = Path.Combine(Path.GetTempPath(), "faf-placement-" + Guid.NewGuid().ToString("N"));
            void Write(string id, string json)
            {
                string file = Path.Combine(folder, "units", id, id + "_unit.json");
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, json);
            }
            Write("uxb0101", """{ "id": "uxb0101", "name": "Land Factory", "faction": "Uef", "categories": ["UEF", "STRUCTURE", "LAND"], "footprint": { "sizeX": 5, "sizeZ": 5, "maxSlope": 0.25 }, "skirt": { "sizeX": 8, "sizeZ": 8, "offsetX": -1.5, "offsetZ": -1.5 }, "buildOnLayers": ["Land"] }""");
            Write("uxb0103", """{ "id": "uxb0103", "name": "Naval Factory", "faction": "Uef", "categories": ["UEF", "STRUCTURE", "NAVAL"], "footprint": { "sizeX": 5, "sizeZ": 5, "minWaterDepth": 1.5 }, "skirt": { "sizeX": 8, "sizeZ": 8, "offsetX": -1.5, "offsetZ": -1.5 }, "buildOnLayers": ["Water"] }""");
            Write("uxb5101", """{ "id": "uxb5101", "name": "Wall", "faction": "Uef", "categories": ["UEF", "STRUCTURE", "WALL"], "footprint": { "sizeX": 1, "sizeZ": 1 } }""");
            Write("uxl0001", """{ "id": "uxl0001", "name": "Tank", "faction": "Uef", "categories": ["UEF", "LAND", "MOBILE"], "footprint": { "sizeX": 1, "sizeZ": 1 } }""");
            Write("uxb9999", """{ "id": "uxb9999", "name": "Mystery", "faction": "Uef", "categories": ["UEF", "STRUCTURE"] }""");
            catalog = AssetCatalog.Load(folder);
        }

        private static Unit At(string blueprint, float x, float z, float heading = 0) =>
            new Unit { Name = "", Blueprint = blueprint, Position = new System.Numerics.Vector3(x, 20, z), Orientation = new System.Numerics.Vector3(0, heading, 0) };

        private static MapDocument With(MapDocument document, params Unit[] units) => document.AddUnits(Army, ["INITIAL"], units, out _);

        private static IReadOnlyList<Problem> Run(MapDocument document, string check) =>
            [.. new ProblemChecks().Run(new ProblemContext(document, catalog)).Where(problem => problem.Check == check)];

        [TestMethod]
        public void TheBlueprintsLimitsAreInTheCatalog()
        {
            PlacementLimits limits = catalog.Find("uxb0101")!.Limits!;

            Assert.AreEqual(new Skirt(8, 8, -1.5, -1.5), limits.Skirt);
            CollectionAssert.AreEqual(new[] { "Land" }, limits.Layers!.Value.ToArray());
            Assert.AreEqual(0.25, limits.MaxSlope);
            Assert.AreEqual(1.5, catalog.Find("uxb0103")!.Limits!.MinWaterDepth);
            Assert.IsNull(catalog.Find("uxb9999")!.Limits, "a blueprint that says nothing: nothing to check");
        }

        [TestMethod]
        public void TwoFactoriesWhoseSkirtsOverlapAreOneWarningNamingBoth()
        {
            MapDocument document = With(baseMap, At("uxb0101", 100.5f, 100.5f), At("uxb0101", 106.5f, 100.5f), At("uxb0101", 200.5f, 100.5f));

            Problem problem = Run(document, "units-overlap").Single();

            Assert.AreEqual(ProblemSeverity.Warning, problem.Severity);
            StringAssert.Contains(problem.Message, "Land Factory UNIT_");
            Assert.IsNotNull(problem.Thing);
            Assert.AreEqual(1, problem.Related!.Count);
            Assert.AreNotEqual(problem.Thing, problem.Related[0]);
            Assert.IsNotNull(problem.Place);
            Assert.IsTrue(problem.Place!.Value.X is > 100 and < 108, "near the shared ground");
        }

        [TestMethod]
        public void FactoriesFarEnoughApartAndWallsTouchingEdgeToEdgeAreFine()
        {
            // skirts 8 wide from -4 to +4: 8 apart touch
            MapDocument document = With(baseMap, At("uxb0101", 100.5f, 100.5f), At("uxb0101", 108.5f, 100.5f),
                At("uxb5101", 10.5f, 10.5f), At("uxb5101", 11.5f, 10.5f), At("uxb5101", 12.5f, 10.5f));

            Assert.AreEqual(0, Run(document, "units-overlap").Count);
            Assert.AreEqual(2, Run(With(document, At("uxb5101", 12f, 10.5f)), "units-overlap").Count, "a wall half into two others overlaps both");
        }

        [TestMethod]
        public void WrecksMobileUnitsAndUnknownBlueprintsAreLeftOut()
        {
            MapDocument document = baseMap.AddUnits(Army, ["INITIAL", UnitWrecks.GroupName], [At("uxb0101", 100.5f, 100.5f)], out _);
            document = With(document, At("uxb0101", 102.5f, 100.5f), At("uxl0001", 102.5f, 100.5f), At("uxl0001", 102.5f, 100.5f), At("uxb9999", 102.5f, 100.5f));

            Assert.AreEqual(0, Run(document, "units-overlap").Count);
            Assert.AreEqual(0, Run(document, "units-ground").Count);
        }

        [TestMethod]
        public void ANavalFactoryOnLandAndALandFactoryUnderWaterAreWarnings()
        {
            MapDocument dry = With(baseMap, At("uxb0103", 100.5f, 100.5f), At("uxb0101", 150.5f, 150.5f));
            Problem onLand = Run(dry, "units-ground").Single();
            StringAssert.Contains(onLand.Message, "Naval Factory");
            StringAssert.Contains(onLand.Message, "cannot be built on land");
            Assert.IsNotNull(onLand.Thing);

            // the water 30 above the ground of 20
            MapDocument wet = With(baseMap with { Water = baseMap.Water with { HasWater = true, Elevation = 50 } }, At("uxb0103", 100.5f, 100.5f), At("uxb0101", 150.5f, 150.5f));
            Problem underWater = Run(wet, "units-ground").Single();
            StringAssert.Contains(underWater.Message, "Land Factory");
            StringAssert.Contains(underWater.Message, "cannot be built under water");

            MapDocument shallow = With(baseMap with { Water = baseMap.Water with { HasWater = true, Elevation = 20.5f } }, At("uxb0103", 100.5f, 100.5f));
            StringAssert.Contains(Run(shallow, "units-ground").Single().Message, "the water is 0.5 deep, it needs 1.5");
        }

        [TestMethod]
        public void AFactoryOnACliffIsAWarningAndOneOnASlightSlopeIsNot()
        {
            Heightmap heights = baseMap.Terrain.Heightmap;
            TiledBuffer<ushort>.Builder builder = heights.Samples.ToBuilder();
            for (int z = 0; z <= heights.Height; z++)
            {
                for (int x = 100; x <= heights.Width; x++)
                {
                    builder[x, z] = (ushort)((20 + (x < 110 ? 0 : 10)) * 128 + (x - 100) * 4);
                }
            }
            MapDocument slope = baseMap with { Terrain = baseMap.Terrain with { Heightmap = heights with { Samples = builder.ToBuffer() } } };
            // a cliff of 10 between x 109 and 110; a gentle slope of 4/128 an ogrid elsewhere
            MapDocument document = With(slope, At("uxb0101", 109.5f, 100.5f), At("uxb0101", 160.5f, 100.5f));

            Problem problem = Run(document, "units-ground").Single();

            StringAssert.Contains(problem.Message, "the ground rises");
            Assert.IsTrue(problem.Place!.Value.X < 111);
        }

        [TestMethod]
        public void TwoThousandStructuresAreCheckedQuickly()
        {
            List<Unit> units = [];
            for (int i = 0; i < 2000; i++)
            {
                units.Add(At(i % 3 == 0 ? "uxb5101" : "uxb0101", 5.5f + i % 50 * 7, 5.5f + i / 50 * 7));
            }
            MapDocument document = With(baseMap, [.. units]);

            Stopwatch clock = Stopwatch.StartNew();
            IReadOnlyList<Problem> problems = new ProblemChecks().Run(new ProblemContext(document, catalog));
            clock.Stop();

            Assert.IsTrue(clock.ElapsedMilliseconds < 3000, $"took {clock.ElapsedMilliseconds} ms");
            Assert.IsTrue(problems.Count(problem => problem.Check == "units-overlap") > 0, "factories 7 apart overlap with their skirts of 8");
            Assert.IsTrue(problems.Count(problem => problem.Check == "units-overlap") <= PlacementChecks.MostProblems + 1, "capped, with one line saying how many more");
        }
    }
}
