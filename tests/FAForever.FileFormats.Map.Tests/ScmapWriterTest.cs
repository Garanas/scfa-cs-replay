using FAForever.FileFormats.Map;

namespace FAForever.FileFormats.Map.Tests
{
    [TestClass]
    public class ScmapWriterTest
    {
        [TestMethod]
        [DataRow(ScmapParserTest.ThetaPassage)]
        [DataRow(ScmapParserTest.HardFfa)]
        [DataRow(ScmapParserTest.CoopR01)]
        public void WritesAParsedMapBackToTheSameBytes(string map)
        {
            byte[] original = File.ReadAllBytes(map + ".scmap");

            byte[] written = ScmapWriter.Write(ScmapParser.Parse(original));

            Assert.AreEqual(original.Length, written.Length);
            int firstDifference = original.AsSpan().CommonPrefixLength(written);
            Assert.AreEqual(original.Length, firstDifference, $"the bytes differ from offset {firstDifference}");
        }

        [TestMethod]
        public void WritesAChangedMapThatParsesAgain()
        {
            Scmap scmap = ScmapParser.Parse(File.ReadAllBytes(ScmapParserTest.HardFfa + ".scmap"));
            ushort[] samples = (ushort[])scmap.Heightmap.Samples.Clone();
            samples[0] = 4096;
            Scmap changed = scmap with
            {
                Heightmap = scmap.Heightmap with { Samples = samples },
                Props = [.. scmap.Props.Skip(1)],
                Water = scmap.Water with { Elevation = 12.5f },
            };

            Scmap reparsed = ScmapParser.Parse(ScmapWriter.Write(changed));

            Assert.AreEqual(32f, reparsed.Heightmap.GetHeight(0, 0));
            Assert.AreEqual(scmap.Props.Count - 1, reparsed.Props.Count);
            Assert.AreEqual(scmap.Props[1], reparsed.Props[0]);
            Assert.AreEqual(12.5f, reparsed.Water.Elevation);
        }

        [TestMethod]
        public void WritesToAStream()
        {
            byte[] original = File.ReadAllBytes(ScmapParserTest.ThetaPassage + ".scmap");
            using MemoryStream stream = new MemoryStream();

            ScmapWriter.Write(ScmapParser.Parse(original), stream);

            CollectionAssert.AreEqual(original, stream.ToArray());
        }

        [TestMethod]
        public void RejectsAMapThatDoesNotFitItsVersion()
        {
            Scmap scmap = ScmapParser.Parse(File.ReadAllBytes(ScmapParserTest.ThetaPassage + ".scmap"));

            ArgumentException noSkybox = Assert.ThrowsException<ArgumentException>(() => ScmapWriter.Write(scmap with { Skybox = null }));
            StringAssert.Contains(noSkybox.Message, "skybox");

            ArgumentException noMinimap = Assert.ThrowsException<ArgumentException>(() => ScmapWriter.Write(scmap with { Version = 56, Minimap = null }));
            StringAssert.Contains(noMinimap.Message, "minimap");

            ArgumentException unknownVersion = Assert.ThrowsException<ArgumentException>(() => ScmapWriter.Write(scmap with { Version = 57 }));
            StringAssert.Contains(unknownVersion.Message, "57");
        }

        [TestMethod]
        public void RejectsArraysThatDoNotFollowTheSize()
        {
            Scmap scmap = ScmapParser.Parse(File.ReadAllBytes(ScmapParserTest.HardFfa + ".scmap"));

            ArgumentException heightmap = Assert.ThrowsException<ArgumentException>(() =>
                ScmapWriter.Write(scmap with { Heightmap = scmap.Heightmap with { Samples = new ushort[10] } }));
            StringAssert.Contains(heightmap.Message, "heightmap");

            ArgumentException terrainTypes = Assert.ThrowsException<ArgumentException>(() =>
                ScmapWriter.Write(scmap with { TerrainTypes = new byte[10] }));
            StringAssert.Contains(terrainTypes.Message, "terrain types");
        }

        [TestMethod]
        public void RejectsAStringWithAZeroByte()
        {
            Scmap scmap = ScmapParser.Parse(File.ReadAllBytes(ScmapParserTest.HardFfa + ".scmap"));

            Assert.ThrowsException<ArgumentException>(() => ScmapWriter.Write(scmap with { TerrainShader = "TTerrain\0" }));
        }
    }
}
