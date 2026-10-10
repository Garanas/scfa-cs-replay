using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Tests
{
    [TestClass]
    public class HeightmapFilesTest
    {
        [ClassCleanup]
        public static void Cleanup() => ProjectFolders.Clean();

        [TestMethod]
        public void ResamplesBetweenTheNearestSamples()
        {
            ushort[] small = [0, 100, 200, 300];

            ushort[] large = HeightmapFiles.Resample(small, 2, 2, 3, 3);

            CollectionAssert.AreEqual(new ushort[] { 0, 50, 100, 100, 150, 200, 200, 250, 300 }, large, "the corners stay, the middle lies between");
            CollectionAssert.AreEqual(small, HeightmapFiles.Resample(large, 3, 3, 2, 2));
        }

        [TestMethod]
        [DataRow("heights.png")]
        [DataRow("heights.r16")]
        public void WritesAndReadsTheMapsHeightmap(string name)
        {
            MapDocument document = MapImporter.ImportFolder(ProjectFolders.HardFfa);
            string file = Path.Combine(ProjectFolders.New("heightmap"), name);

            HeightmapFiles.Write(file, document.Terrain.Heightmap);
            (int width, int height, ushort[] samples) = HeightmapFiles.Read(file);

            Assert.AreEqual(257, width);
            Assert.AreEqual(257, height);
            CollectionAssert.AreEqual(document.Terrain.Heightmap.Samples.ToArray(), samples);
        }

        [TestMethod]
        public void ImportsAHeightmapOfAnotherSize()
        {
            MapDocument document = MapImporter.ImportFolder(ProjectFolders.HardFfa);
            string file = Path.Combine(ProjectFolders.New("heightmap"), "world-machine.r16");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            // 129 by 129 samples rising from left to right
            byte[] bytes = new byte[129 * 129 * 2];
            for (int z = 0; z < 129; z++)
            {
                for (int x = 0; x < 129; x++)
                {
                    ushort value = (ushort)(x * 100);
                    bytes[(z * 129 + x) * 2] = (byte)value;
                    bytes[(z * 129 + x) * 2 + 1] = (byte)(value >> 8);
                }
            }
            File.WriteAllBytes(file, bytes);

            MapDocument imported = HeightmapFiles.Import(document, file, out (int Width, int Height) original);

            Assert.AreEqual((129, 129), original);
            Assert.AreEqual(257, imported.Terrain.Heightmap.Samples.Width);
            Assert.AreEqual(0, imported.Terrain.Heightmap.Samples[0, 10]);
            Assert.AreEqual(12800, imported.Terrain.Heightmap.Samples[256, 10]);
            Assert.AreEqual(6400, imported.Terrain.Heightmap.Samples[128, 200]);
            Assert.AreSame(document.Water, imported.Water, "only the heights change");
        }

        [TestMethod]
        public void RefusesARawFileThatIsNoSquare()
        {
            string file = Path.Combine(ProjectFolders.New("heightmap"), "odd.r16");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllBytes(file, new byte[6]);
            Assert.ThrowsException<MapProjectException>(() => HeightmapFiles.Read(file));
            Assert.ThrowsException<MapProjectException>(() => HeightmapFiles.Read(Path.Combine(Path.GetDirectoryName(file)!, "missing.png")));
        }
    }
}
