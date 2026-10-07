using System.Numerics;
using FAForever.FileFormats.Map;

namespace FAForever.FileFormats.Map.Tests
{
    [TestClass]
    public class NavPathsTest
    {
        /// <summary>A map of 64 by 64 ogrids with a wall at column 32, open in rows 50 to 59 when <paramref name="gap"/>.</summary>
        private static NavGrid Walled(bool gap)
        {
            ushort[] samples = new ushort[65 * 65];
            for (int z = 0; z <= 64; z++)
            {
                for (int x = 0; x <= 64; x++)
                {
                    bool wall = x == 32 && !(gap && z is >= 50 and <= 60);
                    samples[z * 65 + x] = (ushort)(wall ? 128 * 20 : 128 * 10);
                }
            }
            byte[] types = new byte[64 * 64];
            Array.Fill(types, (byte)1);
            return NavGenerator.Generate(new ScmapHeightmap(64, 64, 1f / 128, samples), null, types, []).Land;
        }

        [TestMethod]
        public void MeasuresStraightAndDiagonalSteps()
        {
            NavGrid grid = Walled(gap: true);

            float[] distances = NavPaths.DistancesFrom(grid, new Vector3(5.5f, 0, 5.5f));

            Assert.AreEqual(0, distances[5 * 64 + 5]);
            Assert.AreEqual(10, distances[5 * 64 + 15], 1e-4);
            Assert.AreEqual(10 * MathF.Sqrt(2), distances[15 * 64 + 15], 1e-3);
            Assert.IsTrue(float.IsPositiveInfinity(distances[5 * 64 + 31]), "the ogrids beside the wall are steep");
        }

        [TestMethod]
        public void RoutesThroughTheGap()
        {
            NavGrid grid = Walled(gap: true);

            NavRoute? route = NavPaths.FindRoute(grid, new Vector3(10.5f, 0, 10.5f), new Vector3(54.5f, 0, 10.5f));

            Assert.IsNotNull(route);
            Assert.IsTrue(route.Length > 80, "the route goes around through the gap");
            Assert.AreEqual(new Vector2(10.5f, 10.5f), route.Points[0]);
            Assert.AreEqual(new Vector2(54.5f, 10.5f), route.Points[^1]);
            Assert.IsTrue(route.Points.Any(point => point.X is > 31 and < 34 && point.Y is >= 50 and <= 60));
        }

        [TestMethod]
        public void FindsTheTwoNearestOrigins()
        {
            NavGrid grid = Walled(gap: true);
            Vector3[] origins = [new Vector3(5.5f, 0, 5.5f), new Vector3(20.5f, 0, 5.5f), new Vector3(54.5f, 0, 5.5f)];

            NearestOrigins[] nearest = NavPaths.NearestTwo(grid, origins);

            // ogrid (10, 5): 5 from the first origin, 10 from the second
            Assert.AreEqual(new NearestOrigins(0, 5, 1, 10), nearest[5 * 64 + 10]);
            // each origin's own ogrid: itself, then the nearest other one
            Assert.AreEqual(0, nearest[5 * 64 + 5].First);
            Assert.AreEqual(1, nearest[5 * 64 + 5].Second);
            // across the wall the third origin is first; the others come round through the gap
            NearestOrigins far = nearest[5 * 64 + 50];
            Assert.AreEqual(2, far.First);
            Assert.AreEqual(1, far.Second);
            // the search of the two nearest rounds the diagonal to 1.414
            float full = NavPaths.DistancesFrom(grid, origins[1])[5 * 64 + 50];
            Assert.AreEqual(full, far.SecondDistance, full * 1e-4);
        }

        [TestMethod]
        public void FindsTheSameLengthAsAFullSearch()
        {
            NavGrid grid = Walled(gap: true);
            Vector3 from = new Vector3(10.5f, 0, 10.5f);
            Vector3 to = new Vector3(54.5f, 0, 30.5f);

            NavRoute? route = NavPaths.FindRoute(grid, from, to);

            Assert.IsNotNull(route);
            Assert.AreEqual(NavPaths.DistancesFrom(grid, from)[30 * 64 + 54], route.Length, 1e-3);
        }

        [TestMethod]
        public void CoarsensToBlocksOfOneRegion()
        {
            NavGrid grid = Walled(gap: true);

            NavGrid coarse = grid.Coarsen(4);

            Assert.AreEqual(16, coarse.Width);
            Assert.AreEqual(16, coarse.Height);
            Assert.AreEqual(grid.Cells[0], coarse.Cells[0], "a block of one region keeps it");
            Assert.AreEqual(-1, coarse.Cells[2 * 16 + 7], "the block of the wall mixes the region with steep ogrids");
            Assert.AreSame(grid, grid.Coarsen(1));
        }

        [TestMethod]
        public void FindsNoRouteAcrossAWall()
        {
            NavGrid grid = Walled(gap: false);

            Assert.IsNull(NavPaths.FindRoute(grid, new Vector3(10.5f, 0, 10.5f), new Vector3(54.5f, 0, 10.5f)));
        }

        [TestMethod]
        public void FindsTheConnectingLayer()
        {
            Scmap scmap = ScmapParser.Parse(File.ReadAllBytes(ScmapParserTest.HardFfa + ".scmap"));
            MapSave save = MapSaveParser.Parse(File.ReadAllText(ScmapParserTest.HardFfa + "_save.lua"));
            MapNavigation navigation = NavGenerator.Generate(scmap, save);
            List<Vector3> starts = Enumerable.Range(1, 8).Select(i => save.GetMarker($"ARMY_{i}")!.Position).ToList();

            // the start positions of HardFFA are under water
            Assert.AreEqual(NavLayer.Amphibious, navigation.FindConnectingLayer(starts));
        }
    }
}
