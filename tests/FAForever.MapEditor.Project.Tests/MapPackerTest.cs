using FAForever.FileFormats.Map;

namespace FAForever.MapEditor.Project.Tests
{
    [TestClass]
    public class MapPackerTest
    {
        [ClassCleanup]
        public static void Cleanup() => ProjectFolders.Clean();

        private static Scmap ParseScmap(string folder) =>
            ScmapParser.Parse(File.ReadAllBytes(Directory.GetFiles(folder, "*.scmap").Single()));

        private static string Unpacked(string map)
        {
            string project = ProjectFolders.New("project");
            MapUnpacker.Unpack(map, project);
            return project;
        }

        [TestMethod]
        [DataRow(ProjectFolders.ThetaPassage)]
        [DataRow(ProjectFolders.HardFfa)]
        [DataRow(ProjectFolders.CoopR01)]
        public void PacksAnUnpackedMapIntoTheSameMap(string map)
        {
            string project = Unpacked(map);
            string output = ProjectFolders.New("output");

            string packed = MapPacker.Pack(project, output);

            Assert.AreEqual(Path.GetFileName(map), Path.GetFileName(packed), "the folder gets its version back");
            Scmap original = ParseScmap(map);
            Scmap repacked = ParseScmap(packed);
            Assert.AreEqual(Directory.GetFiles(map, "*.scmap").Single().Replace(map, ""), Directory.GetFiles(packed, "*.scmap").Single().Replace(packed, ""));

            // everything but the textures is the same: unpacking both gives the same JSON
            string again = ProjectFolders.New("again");
            MapUnpacker.UnpackScmap(repacked, again);
            foreach (string json in new[] { MapProjectLayout.Manifest, MapProjectLayout.Props, MapProjectLayout.Decals })
            {
                Assert.AreEqual(File.ReadAllText(Path.Combine(project, json)), File.ReadAllText(Path.Combine(again, json)), json);
            }

            CollectionAssert.AreEqual(original.Heightmap.Samples, repacked.Heightmap.Samples);
            CollectionAssert.AreEqual(original.TerrainTypes, repacked.TerrainTypes);
            CollectionAssert.AreEqual(original.WaterFoamMask, repacked.WaterFoamMask);
            CollectionAssert.AreEqual(original.WaterFlatnessMask, repacked.WaterFlatnessMask);
            CollectionAssert.AreEqual(original.WaterDepthBiasMask, repacked.WaterDepthBiasMask);

            // the uncompressed textures keep every pixel, the compressed ones nearly every
            AssertSamePixels(original.Preview, repacked.Preview, 0);
            for (int i = 0; i < original.StratumMasks.Count; i++)
            {
                AssertSamePixels(original.StratumMasks[i], repacked.StratumMasks[i], 0);
            }
            AssertSamePixels(original.NormalMaps[0], repacked.NormalMaps[0], 0.1);
            AssertSamePixels(original.WaterMap, repacked.WaterMap, 0.1);

            // the other files are copied as they are
            foreach (string file in Directory.GetFiles(map, "*.lua"))
            {
                CollectionAssert.AreEqual(File.ReadAllBytes(file), File.ReadAllBytes(Path.Combine(packed, Path.GetFileName(file))), file);
            }
        }

        private static void AssertSamePixels(byte[] expected, byte[] actual, double meanError)
        {
            (int width, int height, ScmapTextureFormat format, byte[] expectedRgba) = DdsTexture.Read(expected, "expected");
            (int actualWidth, int actualHeight, ScmapTextureFormat actualFormat, byte[] actualRgba) = DdsTexture.Read(actual, "actual");
            Assert.AreEqual((width, height, format), (actualWidth, actualHeight, actualFormat));

            double error = expectedRgba.Zip(actualRgba, (a, b) => Math.Abs(a - b)).Average();
            Assert.IsTrue(error <= meanError, $"a mean error of {error} per channel");
        }

        [TestMethod]
        public void GivesTheMapANewVersion()
        {
            string project = Unpacked(ProjectFolders.CoopR01);

            string packed = MapPacker.Pack(project, ProjectFolders.New("output"), version: 21);

            Assert.AreEqual("SCCA_Coop_R01.v0021", Path.GetFileName(packed));
            MapScenario scenario = MapScenarioParser.Parse(File.ReadAllText(Path.Combine(packed, "SCCA_Coop_R01_scenario.lua")));
            Assert.AreEqual(21, scenario.MapVersion);
            Assert.AreEqual("/maps/SCCA_Coop_R01.v0021/SCCA_Coop_R01.scmap", scenario.Map);
            Assert.AreEqual("/maps/SCCA_Coop_R01.v0021/SCCA_Coop_R01_save.lua", scenario.Save);

            string save = File.ReadAllText(Path.Combine(packed, "SCCA_Coop_R01_save.lua"));
            StringAssert.Contains(save, "plans = '/maps/SCCA_Coop_R01.v0021/SCCA_Coop_R01_playerplanlist.lua'");
            Assert.IsFalse(save.Contains(".v0020"), "every path names the new version");
        }

        [TestMethod]
        public void PacksTheVersionOfTheScenario()
        {
            string project = Unpacked(ProjectFolders.HardFfa);
            string scenario = Path.Combine(project, MapProjectLayout.MapFiles, "HardFFA_scenario.lua");
            File.WriteAllText(scenario, File.ReadAllText(scenario).Replace("map_version=1", "map_version=7"));

            string packed = MapPacker.Pack(project, ProjectFolders.New("output"));

            Assert.AreEqual("HardFFA.v0007", Path.GetFileName(packed));
            StringAssert.Contains(File.ReadAllText(Path.Combine(packed, "HardFFA_scenario.lua")), "map=\"/maps/HardFFA.v0007/HardFFA.scmap\"");
        }

        [TestMethod]
        public void BakesAnEditedImage()
        {
            string project = Unpacked(ProjectFolders.HardFfa);
            Scmap original = MapPacker.BuildScmap(project);
            ushort[] samples = (ushort[])original.Heightmap.Samples.Clone();
            samples[0] = 4096;
            ProjectImages.WriteGray16(Path.Combine(project, MapProjectLayout.Heightmap), 257, 257, samples);
            File.Delete(Path.Combine(project, MapProjectLayout.StratumMask(8)));

            Scmap scmap = MapPacker.BuildScmap(project);

            Assert.AreEqual(32f, scmap.Heightmap.GetHeight(0, 0));
            (_, _, _, byte[] mask) = DdsTexture.Read(scmap.StratumMasks[1], "mask");
            Assert.IsTrue(Enumerable.Range(0, mask.Length / 4).All(i => mask[i * 4 + 3] == 0), "a missing mask leaves its layer unused");
        }

        [TestMethod]
        public void ReadsARawHeightmap()
        {
            string project = Unpacked(ProjectFolders.HardFfa);
            ushort[] samples = MapPacker.BuildScmap(project).Heightmap.Samples;
            byte[] raw = new byte[samples.Length * 2];
            Buffer.BlockCopy(samples, 0, raw, 0, raw.Length);
            File.Delete(Path.Combine(project, MapProjectLayout.Heightmap));
            File.WriteAllBytes(Path.Combine(project, MapProjectLayout.HeightmapRaw), raw);

            CollectionAssert.AreEqual(samples, MapPacker.BuildScmap(project).Heightmap.Samples);

            File.WriteAllBytes(Path.Combine(project, MapProjectLayout.HeightmapRaw), raw[..^2]);
            MapProjectException exception = Assert.ThrowsException<MapProjectException>(() => MapPacker.BuildScmap(project));
            StringAssert.Contains(exception.Message, "257 by 257");
        }

        [TestMethod]
        public void NamesAnImageOfTheWrongSize()
        {
            string project = Unpacked(ProjectFolders.HardFfa);
            ProjectImages.WriteGray8(Path.Combine(project, MapProjectLayout.TerrainTypes), 100, 100, new byte[100 * 100]);

            MapProjectException exception = Assert.ThrowsException<MapProjectException>(() => MapPacker.BuildScmap(project));

            StringAssert.Contains(exception.Message, MapProjectLayout.TerrainTypes);
            StringAssert.Contains(exception.Message, "100 by 100 pixels, expected 256 by 256");
        }

        [TestMethod]
        public void NamesAMissingFile()
        {
            string project = Unpacked(ProjectFolders.HardFfa);
            File.Delete(Path.Combine(project, MapProjectLayout.Props));

            MapProjectException exception = Assert.ThrowsException<MapProjectException>(() => MapPacker.Pack(project, ProjectFolders.New("output")));

            StringAssert.Contains(exception.Message, "props.json is missing");
        }
    }
}
