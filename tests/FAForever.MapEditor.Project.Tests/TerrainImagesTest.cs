using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;

namespace FAForever.MapEditor.Project.Tests
{
    [TestClass]
    public class TerrainImagesTest
    {
        private static Heightmap Heights(int size, Func<int, int, float> height, float scale = 1f / 128) => new Heightmap
        {
            Scale = scale,
            Samples = TiledBuffer<ushort>.FromArray(size + 1, size + 1,
                [.. Enumerable.Range(0, (size + 1) * (size + 1)).Select(i => (ushort)MathF.Round(height(i % (size + 1), i / (size + 1)) / scale))]),
        };

        [TestMethod]
        public void MakesTheNormalMapOfFlatTerrainPointUp()
        {
            TiledBuffer<Color32> normals = TerrainImages.NormalMap(Heights(64, (_, _) => 20), 64, 64);

            Assert.IsTrue(normals.ToArray().All(pixel => pixel == new Color32(128, 0, 128, 0)), "x and z are 0: 128 in alpha and green");
        }

        [TestMethod]
        public void WritesTheSlopeEastInAlphaAndSouthInGreen()
        {
            // rising by one game unit an ogrid to the east: the normal leans west, (-1, 1, 0) / √2
            TiledBuffer<Color32> east = TerrainImages.NormalMap(Heights(64, (x, _) => x), 64, 64);
            Assert.AreEqual(new Color32((byte)MathF.Round((0.5f - 0.5f / MathF.Sqrt(2)) * 255), 0, 128, 0), east[32, 32]);

            // rising to the south: the normal leans north, z negative
            TiledBuffer<Color32> south = TerrainImages.NormalMap(Heights(64, (_, z) => z), 64, 64);
            Assert.AreEqual(new Color32(128, 0, (byte)MathF.Round((0.5f - 0.5f / MathF.Sqrt(2)) * 255), 0), south[32, 32]);
        }

        [TestMethod]
        public void MakesAnImageOfAnotherSize()
        {
            TiledBuffer<Color32> normals = TerrainImages.NormalMap(Heights(64, (x, _) => x), 32, 16);

            Assert.AreEqual(32, normals.Width);
            Assert.AreEqual(16, normals.Height);
            Assert.IsTrue(normals.ToArray().All(pixel => pixel.Alpha < 128 && pixel.Green == 128));
        }

        /// <summary>
        /// Theta Passage's normal map was made from its heightmap by the FAF map editor: the generated
        /// one differs by less than a step of a channel on average (DXT5 itself loses about one).
        /// </summary>
        [TestMethod]
        public void MakesTheNormalMapAMapMakerMade()
        {
            MapDocument document = MapImporter.ImportFolder(ProjectFolders.ThetaPassage);
            TiledBuffer<Color32> own = document.Terrain.NormalMap;

            TiledBuffer<Color32> made = TerrainImages.NormalMap(document.Terrain.Heightmap, own.Width, own.Height);

            Color32[] a = own.ToArray();
            Color32[] b = made.ToArray();
            double alpha = a.Zip(b).Average(pair => Math.Abs(pair.First.Alpha - pair.Second.Alpha));
            double green = a.Zip(b).Average(pair => Math.Abs(pair.First.Green - pair.Second.Green));
            Assert.IsTrue(alpha < 1, $"alpha differs by {alpha:0.00} on average");
            Assert.IsTrue(green < 1.2, $"green differs by {green:0.00} on average");
            Assert.IsTrue(b.All(pixel => pixel.Red == 0 && pixel.Blue == 0));
        }
    }
}
