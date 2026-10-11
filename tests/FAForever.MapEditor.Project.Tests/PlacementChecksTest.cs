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
            Write("uxb0101", """{ "id": "uxb0101", "name": "Land Factory", "faction": "Uef", "categories": ["UEF", "STRUCTURE", "LAND"], "footprint": { "sizeX": 5, "sizeZ": 5, "maxSlope": 0.25 }, "skirt": { "sizeX": 8, "sizeZ": 8, "offsetX": -1.5, "offsetZ": -1.5 }, "buildOnLayers": ["Land"], "maxGroundVariation": 1.1, "flattenSkirt": true }""");
            Write("uxb0103", """{ "id": "uxb0103", "name": "Naval Factory", "faction": "Uef", "categories": ["UEF", "STRUCTURE", "NAVAL"], "footprint": { "sizeX": 5, "sizeZ": 5, "minWaterDepth": 1.5 }, "skirt": { "sizeX": 8, "sizeZ": 8, "offsetX": -1.5, "offsetZ": -1.5 }, "buildOnLayers": ["Water"] }""");
            Write("uxb5101", """{ "id": "uxb5101", "name": "Wall", "faction": "Uef", "categories": ["UEF", "STRUCTURE", "WALL"], "footprint": { "sizeX": 1, "sizeZ": 1 } }""");
            Write("uxl0001", """{ "id": "uxl0001", "name": "Tank", "faction": "Uef", "categories": ["UEF", "LAND", "MOBILE"], "footprint": { "sizeX": 1, "sizeZ": 1 } }""");
            // the game's: a storage has a footprint of 1, an extractor and a power generator none (1 by 1), both a skirt of 2 about the centre
            Write("uxb1106", """{ "id": "uxb1106", "name": "Mass Storage", "faction": "Uef", "categories": ["UEF", "STRUCTURE", "LAND"], "footprint": { "sizeX": 1, "sizeZ": 1 }, "skirt": { "sizeX": 2, "sizeZ": 2, "offsetX": -0.5, "offsetZ": -0.5 }, "buildOnLayers": ["Land", "Seabed"] }""");
            Write("uxb1103", """{ "id": "uxb1103", "name": "Mass Extractor", "faction": "Uef", "categories": ["UEF", "STRUCTURE", "LAND"], "skirt": { "sizeX": 2, "sizeZ": 2, "offsetX": -0.5, "offsetZ": -0.5 }, "buildOnLayers": ["Land", "Seabed"] }""");
            Write("uxb1101", """{ "id": "uxb1101", "name": "Power Generator", "faction": "Uef", "categories": ["UEF", "STRUCTURE", "LAND"], "skirt": { "sizeX": 2, "sizeZ": 2, "offsetX": -0.5, "offsetZ": -0.5 }, "buildOnLayers": ["Land"] }""");
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
            Assert.AreEqual(1.1, limits.MaxGroundVariation);
            Assert.IsTrue(limits.FlattenSkirt);
            Assert.AreEqual(1.0, catalog.Find("uxb1106")!.Limits!.MaxGroundVariation, "the engine's default when the blueprint gives none");
            Assert.IsFalse(catalog.Find("uxb1106")!.Limits!.FlattenSkirt);
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
        public void WrecksAndMobileUnitsAreLeftOut()
        {
            MapDocument document = baseMap.AddUnits(Army, ["INITIAL", UnitWrecks.GroupName], [At("uxb0101", 100.5f, 100.5f)], out _);
            document = With(document, At("uxb0101", 108.5f, 100.5f), At("uxl0001", 108.5f, 100.5f), At("uxl0001", 108.5f, 100.5f));

            Assert.AreEqual(0, Run(document, "units-overlap").Count);
            Assert.AreEqual(0, Run(document, "units-ground").Count);
        }

        [TestMethod]
        public void AStructureWhoseBlueprintSaysNothingHasTheEnginesDefaultFootprint()
        {
            // 1 by 1, so the next ogrid is free and the same one is not
            Assert.AreEqual(0, Run(With(baseMap, At("uxb9999", 102.5f, 100.5f), At("uxb9999", 103.5f, 100.5f)), "units-overlap").Count);
            Assert.AreEqual(1, Run(With(baseMap, At("uxb9999", 102.5f, 100.5f), At("uxb9999", 102.5f, 100.5f)), "units-overlap").Count);
        }

        [TestMethod]
        public void AMassStorageBesideAnExtractorAndAFactoryBesideAGeneratorTouchButDoNotOverlap()
        {
            // the game's pairs: the skirt of a unit without a footprint lies at the corner of the 1 by 1 footprint plus the offset
            MapDocument beside = With(baseMap, At("uxb1106", 238.5f, 283.5f), At("uxb1103", 240.5f, 283.5f));
            Assert.AreEqual(0, Run(beside, "units-overlap").Count);
            Assert.AreEqual(0, Run(With(baseMap, At("uxb1106", 240.5f, 283.5f), At("uxb1103", 238.5f, 283.5f)), "units-overlap").Count, "the same pair the other way round");
            Assert.AreEqual(1, Run(With(baseMap, At("uxb1106", 238.5f, 283.5f), At("uxb1103", 239.5f, 283.5f)), "units-overlap").Count);

            // a factory's skirt runs from 96.5 to 104.5, a generator's from 104.5 to 106.5 and from 94.5 to 96.5
            MapDocument factory = With(baseMap, At("uxb0101", 100.5f, 100.5f), At("uxb1101", 105.5f, 100.5f), At("uxb1101", 95.5f, 100.5f), At("uxb1101", 100.5f, 105.5f), At("uxb1101", 100.5f, 95.5f));
            Assert.AreEqual(0, Run(factory, "units-overlap").Count);
            Assert.AreEqual(1, Run(With(baseMap, At("uxb0101", 100.5f, 100.5f), At("uxb1101", 104.5f, 100.5f)), "units-overlap").Count);
        }

        [TestMethod]
        public void TheSkirtIsAtLeastTheFootprintAndItsOffsetNeverPositive()
        {
            AssetEntry factory = catalog.Find("uxb0101")!;

            PlacementRect rect = PlacementChecks.SkirtOf(factory, 100.5f, 100.5f, 0);

            Assert.AreEqual(new PlacementRect(96.5f, 96.5f, 104.5f, 104.5f), rect);
            PlacementRect extractor = PlacementChecks.SkirtOf(catalog.Find("uxb1103")!, 240.5f, 283.5f, 0);
            Assert.AreEqual(new PlacementRect(239.5f, 282.5f, 241.5f, 284.5f), extractor, "from the corner of a footprint of 1 by 1 at 240 and 283, less half an ogrid");
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
        public void AFactoryOnACliffIsAWarningAndOneOnLevelGroundIsNot()
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
            // a cliff of 10 between x 109 and 110; level ground west of it
            MapDocument document = With(slope, At("uxb0101", 109.5f, 100.5f), At("uxb0101", 60.5f, 100.5f));

            Problem problem = Run(document, "units-ground").Single();

            StringAssert.Contains(problem.Message, "the ground around it");
            StringAssert.Contains(problem.Message, "it allows 1.1");
            Assert.IsTrue(problem.Place!.Value.X < 111);
        }

        [TestMethod]
        public void AFactoryOnAHillIsTooSteepAndTheEngineAllowsALittleOnAStructureWithoutFlatten()
        {
            // the hill of the QA: 42.9, 38.5 and 29.6 at x of 60, 64 and 68, level along z
            Heightmap heights = baseMap.Terrain.Heightmap;
            TiledBuffer<ushort>.Builder builder = heights.Samples.ToBuilder();
            for (int z = 0; z <= heights.Height; z++)
            {
                for (int x = 0; x <= heights.Width; x++)
                {
                    float height = x <= 60 ? 42.9f : x <= 64 ? 42.9f - (x - 60) * 1.1f : 38.5f - (x - 64) * 2.225f;
                    builder[x, z] = (ushort)MathF.Round(height * 128);
                }
            }
            MapDocument hill = baseMap with { Terrain = baseMap.Terrain with { Heightmap = heights with { Samples = builder.ToBuffer() } } };

            Problem factory = Run(With(hill, At("uxb0101", 64.5f, 100.5f)), "units-ground").Single();
            StringAssert.Contains(factory.Message, "Land Factory");

            // a generator (no flatten, 2 by 2 skirt) on a slope of 0.6 an ogrid: 1.2 under its skirt of 3 samples, more than 1
            Heightmap ramp = baseMap.Terrain.Heightmap;
            TiledBuffer<ushort>.Builder ramped = ramp.Samples.ToBuilder();
            for (int z = 0; z <= ramp.Height; z++)
            {
                for (int x = 0; x <= ramp.Width; x++)
                {
                    ramped[x, z] = (ushort)MathF.Round((20 + x * 0.6f) * 128);
                }
            }
            MapDocument sloped = baseMap with { Terrain = baseMap.Terrain with { Heightmap = ramp with { Samples = ramped.ToBuffer() } } };
            StringAssert.Contains(Run(With(sloped, At("uxb1101", 100.5f, 100.5f)), "units-ground").Single().Message, "the ground under it rises and falls 1.2, it allows 1");
            Assert.AreEqual(0, Run(With(baseMap, At("uxb1101", 100.5f, 100.5f)), "units-ground").Count, "flat ground");
        }

        [TestMethod]
        public void TheGroundOfAStructureOnTheMapsEdgeIsReadWithinTheMap()
        {
            Assert.AreEqual(0, Run(With(baseMap, At("uxb0101", 4.5f, 4.5f), At("uxb1101", 0.5f, baseMap.Height - 0.5f)), "units-ground").Count);
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
