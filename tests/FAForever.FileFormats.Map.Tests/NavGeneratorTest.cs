using System.Numerics;
using FAForever.FileFormats.Lua;
using FAForever.FileFormats.Map;

namespace FAForever.FileFormats.Map.Tests
{
    [TestClass]
    public class NavGeneratorTest
    {
        private const int Size = 64;

        /// <summary>
        /// A heightmap of 64 by 64 ogrids with the height of each corner; heights are stored in
        /// steps of 1/128, as in real maps.
        /// </summary>
        private static ScmapHeightmap Heightmap(Func<int, int, float> height)
        {
            ushort[] samples = new ushort[(Size + 1) * (Size + 1)];
            for (int z = 0; z <= Size; z++)
            {
                for (int x = 0; x <= Size; x++)
                {
                    samples[z * (Size + 1) + x] = (ushort)Math.Round(height(x, z) * 128);
                }
            }
            return new ScmapHeightmap(Size, Size, 1f / 128, samples);
        }

        private static byte[] TerrainTypes(Func<int, int, byte>? type = null)
        {
            byte[] types = new byte[Size * Size];
            for (int z = 0; z < Size; z++)
            {
                for (int x = 0; x < Size; x++)
                {
                    types[z * Size + x] = type?.Invoke(x, z) ?? 1;
                }
            }
            return types;
        }

        private static MapMarker Marker(string type, float x, float z) =>
            new MapMarker($"{type} {x} {z}", type, new Vector3(x, 0, z), Vector3.Zero, new LuaData.Table(new Dictionary<string, LuaData>()));

        private static int Label(NavGrid grid, int x, int z) => grid.Cells[z * grid.Width + x];

        [TestMethod]
        public void AFlatDryMapIsOneLandRegion()
        {
            MapNavigation navigation = NavGenerator.Generate(Heightmap((_, _) => 10), null, TerrainTypes(), []);

            Assert.IsFalse(navigation.HasWater);
            Assert.AreEqual(1, navigation.Land.Labels.Count);
            Assert.AreEqual(Size * Size, navigation.Land.Labels[0].Cells);
            Assert.AreEqual(Size * Size * 0.0004, navigation.Land.Labels[0].Area, 1e-9);
            Assert.AreEqual(0, navigation.Water.Labels.Count);
            Assert.AreSame(navigation.Land, navigation.Hover, "without water the hover layer is the land layer");
            Assert.AreSame(navigation.Land, navigation.Amphibious);
        }

        [TestMethod]
        [DataRow(0.75f, 2)]
        [DataRow(95f / 128, 1)]
        public void ACliffSplitsTheLand(float step, int labels)
        {
            // the corners from column 32 on are higher: the ogrids of column 31 have one steep side
            MapNavigation navigation = NavGenerator.Generate(Heightmap((x, _) => x >= 32 ? 10 + step : 10), null, TerrainTypes(), []);

            Assert.AreEqual(labels, navigation.Land.Labels.Count);
            if (labels == 2)
            {
                Assert.AreEqual(-1, Label(navigation.Land, 31, 10));
                Assert.AreEqual(31 * Size, navigation.Land.Labels[0].Cells);
                Assert.AreEqual(32 * Size, navigation.Land.Labels[1].Cells);
                Assert.IsFalse(navigation.Land.CanPathTo(new Vector3(10, 0, 10), new Vector3(50, 0, 10)));
            }
        }

        [TestMethod]
        public void WaterDepthDecidesTheLayers()
        {
            // water at 30: a lake 10 deep in the middle, 30 deep at the bottom, dry land above it
            MapNavigation navigation = NavGenerator.Generate(
                Heightmap((_, z) => z < 20 ? 40 : z < 40 ? 20 : 0),
                30,
                TerrainTypes(),
                []);

            Assert.IsTrue(navigation.HasWater);

            // dry land, 10 deep, 30 deep
            Assert.IsTrue(Label(navigation.Land, 5, 5) > 0);
            Assert.AreEqual(-1, Label(navigation.Land, 5, 30));
            Assert.AreEqual(-1, Label(navigation.Water, 5, 5));
            Assert.IsTrue(Label(navigation.Water, 5, 30) > 0);
            Assert.IsTrue(Label(navigation.Water, 5, 50) > 0);
            Assert.IsTrue(Label(navigation.Amphibious, 5, 30) > 0);
            Assert.AreEqual(-1, Label(navigation.Amphibious, 5, 50), "amphibious units go at most 25 deep");
            Assert.IsTrue(Label(navigation.Hover, 5, 50) > 0);

            // the shores are steep: hover units cross them where the water is deep enough, amphibious units not
            Assert.AreEqual(1, navigation.Hover.Labels.Count);
            Assert.AreEqual(2, navigation.Amphibious.Labels.Count);
        }

        [TestMethod]
        public void BlockingTerrainSplitsTheLand()
        {
            // Dirt09 (type code 9) blocks; the game samples the type at an ogrid's far corner
            Assert.IsTrue(FAForever.FileFormats.Map.TerrainTypes.Get(9).Blocking);

            MapNavigation navigation = NavGenerator.Generate(Heightmap((_, _) => 10), null, TerrainTypes((x, _) => (byte)(x == 32 ? 9 : 1)), []);

            Assert.AreEqual(-1, Label(navigation.Land, 31, 10));
            Assert.IsTrue(Label(navigation.Land, 32, 10) > 0);

            // the last row samples its type past the edge of the map, where it is Default: a gap
            Assert.IsTrue(Label(navigation.Land, 31, Size - 1) > 0);
            Assert.AreEqual(1, navigation.Land.Labels.Count);
        }

        [TestMethod]
        public void OnlyThePlayableAreaIsPathable()
        {
            MapNavigation navigation = NavGenerator.Generate(Heightmap((_, _) => 10), null, TerrainTypes(), [], new MapArea("AREA_1", 0, 0, 32, 64));

            Assert.AreEqual(32 * Size, navigation.Land.Labels.Single().Cells);
            Assert.AreEqual(-1, Label(navigation.Land, 32, 10));
        }

        [TestMethod]
        public void NavalLayersCompressTwiceAsCoarse()
        {
            // water at 10 over the corners up to column 32: the ogrids of column 32 are half deep,
            // and the aligned 2 by 2 blocks of columns 32 and 33 are mixed
            MapNavigation navigation = NavGenerator.Generate(Heightmap((x, _) => x <= 32 ? 0 : 20), 10, TerrainTypes(), []);

            Assert.AreEqual(32 * Size, navigation.Water.Labels.Single().Cells);
            Assert.AreEqual(-1, Label(navigation.Water, 32, 10));
        }

        [TestMethod]
        public void SmallRegionsWithoutResourcesAreCulled()
        {
            // a plateau of 10 by 10 ogrids, surrounded by cliffs
            static float Plateau(int x, int z) => x is >= 10 and <= 20 && z is >= 10 and <= 20 ? 20 : 10;

            MapNavigation without = NavGenerator.Generate(Heightmap(Plateau), null, TerrainTypes(), []);
            MapNavigation with = NavGenerator.Generate(Heightmap(Plateau), null, TerrainTypes(), [Marker("Mass", 15.5f, 15.5f)]);

            Assert.AreEqual(1, without.Land.Labels.Count);
            Assert.AreEqual(-1, Label(without.Land, 15, 15));

            Assert.AreEqual(2, with.Land.Labels.Count);
            NavLabel plateau = with.Land.GetLabel(new Vector3(15.5f, 20, 15.5f))!;
            Assert.AreEqual(100, plateau.Cells);
            Assert.AreEqual(1, plateau.Extractors.Count);
        }

        [TestMethod]
        public void AssignsResourceMarkersToTheirLabels()
        {
            MapNavigation navigation = NavGenerator.Generate(
                Heightmap((x, _) => x >= 32 ? 20 : 10),
                null,
                TerrainTypes(),
                [Marker("Mass", 5.5f, 5.5f), Marker("Mass", 6.5f, 5.5f), Marker("Hydrocarbon", 50, 50), Marker("Land Path Node", 50, 50)]);

            NavLabel left = navigation.Land.GetLabel(new Vector3(5.5f, 10, 5.5f))!;
            NavLabel right = navigation.Land.GetLabel(new Vector3(50, 20, 50))!;
            Assert.AreEqual(2, left.Extractors.Count);
            Assert.AreEqual(0, left.Hydrocarbons.Count);
            Assert.AreEqual(0, right.Extractors.Count);
            Assert.AreEqual(1, right.Hydrocarbons.Count);
        }

        [TestMethod]
        [DataRow(256, 1)]
        [DataRow(1024, 2)]
        [DataRow(2048, 4)]
        [DataRow(4096, 4)]
        public void UsesTheCompressionThresholdOfTheGame(int mapSize, int threshold)
        {
            Assert.AreEqual(threshold, NavGenerator.GetCompressionThreshold(mapSize));
        }

        [TestMethod]
        public void ConnectsTheSpawnsOfThetaPassage()
        {
            (Scmap scmap, MapSave save, MapScenario scenario) = Load(ScmapParserTest.ThetaPassage);

            MapArea? area = NavGenerator.FindPlayableArea(scenario, save);
            MapNavigation navigation = NavGenerator.Generate(scmap, save, area);

            Assert.AreEqual(new MapArea("AREA_1", 0, 0, 256, 256), area);
            Assert.IsFalse(navigation.HasWater);
            NavLabel? spawn = navigation.Land.GetLabel(save.GetMarker("ARMY_1")!.Position);
            Assert.IsNotNull(spawn);
            Assert.AreSame(spawn, navigation.Land.GetLabel(save.GetMarker("ARMY_2")!.Position));
            Assert.AreEqual(24, spawn.Extractors.Count, "every mass spot is reachable over land");
            Assert.AreEqual(2, spawn.Hydrocarbons.Count);
        }

        [TestMethod]
        public void StartsTheArmiesOfHardFfaUnderWater()
        {
            // the spawns of HardFFA are under the water: only amphibious units can leave them
            (Scmap scmap, MapSave save, MapScenario scenario) = Load(ScmapParserTest.HardFfa);

            MapNavigation navigation = NavGenerator.Generate(scmap, save, NavGenerator.FindPlayableArea(scenario, save));

            Assert.IsTrue(navigation.HasWater);
            foreach (string army in scenario.PlayerArmies)
            {
                Vector3 position = save.GetMarker(army)!.Position;
                Assert.IsNull(navigation.Land.GetLabel(position), army);
                Assert.IsNotNull(navigation.Amphibious.GetLabel(position), army);
            }
            Assert.AreEqual(1, navigation.Amphibious.Labels.Count);
        }

        [TestMethod]
        public void UsesTheWholeMapOutsideSkirmish()
        {
            (_, MapSave save, MapScenario scenario) = Load(ScmapParserTest.CoopR01);

            Assert.IsTrue(save.Areas.Count > 0);
            Assert.IsNull(NavGenerator.FindPlayableArea(scenario, save));
        }

        private static (Scmap, MapSave, MapScenario) Load(string map) => (
            ScmapParser.Parse(File.ReadAllBytes(map + ".scmap")),
            MapSaveParser.Parse(File.ReadAllText(map + "_save.lua")),
            MapScenarioParser.Parse(File.ReadAllText(map + "_scenario.lua")));
    }
}
