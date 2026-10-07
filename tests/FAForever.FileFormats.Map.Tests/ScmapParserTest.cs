using System.Numerics;
using FAForever.FileFormats.Map;

namespace FAForever.FileFormats.Map.Tests
{
    [TestClass]
    public class ScmapParserTest
    {
        public const string ThetaPassage = "assets/maps/theta_passage_-_faf_version.v0001/theta_passage_-_faf_version";
        public const string HardFfa = "assets/maps/HardFFA.v0001/HardFFA";
        public const string CoopR01 = "assets/maps/SCCA_Coop_R01.v0020/SCCA_Coop_R01";

        private static Scmap ParseAsset(string map) => ScmapParser.Parse(File.ReadAllBytes(map + ".scmap"));

        [TestMethod]
        [DataRow(ThetaPassage, 60, 256, 1009, 0, 842)]
        [DataRow(HardFfa, 56, 256, 752, 0, 2226)]
        [DataRow(CoopR01, 53, 512, 1036, 4, 3625)]
        public void ParsesEveryVersion(string map, int version, int size, int decals, int decalGroups, int props)
        {
            Scmap scmap = ParseAsset(map);

            Assert.AreEqual(version, scmap.Version);
            Assert.AreEqual(size, scmap.Width);
            Assert.AreEqual(size, scmap.Height);
            Assert.AreEqual(decals, scmap.Decals.Count);
            Assert.AreEqual(decalGroups, scmap.DecalGroups.Count);
            Assert.AreEqual(props, scmap.Props.Count);

            // the sizes of the arrays follow the map's size
            Assert.AreEqual((size + 1) * (size + 1), scmap.Heightmap.Samples.Length);
            Assert.AreEqual(size * size, scmap.TerrainTypes.Length);
            Assert.AreEqual(size / 2 * (size / 2), scmap.WaterFoamMask.Length);
            Assert.AreEqual(size / 2 * (size / 2), scmap.WaterFlatnessMask.Length);
            Assert.AreEqual(size / 2 * (size / 2), scmap.WaterDepthBiasMask.Length);

            // the embedded textures are DDS files
            foreach (byte[] texture in new[] { scmap.Preview, scmap.WaterMap }.Concat(scmap.NormalMaps).Concat(scmap.StratumMasks))
            {
                Assert.AreEqual("DDS ", System.Text.Encoding.ASCII.GetString(texture, 0, 4));
            }
        }

        [TestMethod]
        public void ReadsAVersion60Map()
        {
            Scmap scmap = ParseAsset(ThetaPassage);

            Assert.AreEqual("TTerrainXP", scmap.TerrainShader);
            Assert.AreEqual(1f / 128, scmap.Heightmap.Scale);
            Assert.AreEqual(953 / 128f, scmap.Heightmap.GetHeight(128, 128));
            Assert.IsNull(scmap.Tileset);

            Assert.AreEqual(10, scmap.Layers.Count);
            Assert.AreEqual(new ScmapTerrainLayer("/env/Red Barrens/Layers/RB_Sand_albedo.dds", 5, "/env/Desert/Layers/Des_sandLight_normal.dds", 4), scmap.Layers[0]);
            Assert.AreEqual("", scmap.Layers[9].Normal, "the top layer has no normal map");
            Assert.AreEqual(2, scmap.StratumMasks.Count);

            Assert.IsNotNull(scmap.Minimap);
            Assert.AreEqual(10, scmap.Minimap.ContourInterval);
            Assert.AreEqual(0f, scmap.Minimap.Unknown);

            Assert.IsFalse(scmap.Water.HasWater);
            Assert.AreEqual(4, scmap.Water.WaveTextures.Count);
            Assert.IsNotNull(scmap.Skybox);

            ScmapProp bridge = scmap.Props[0];
            Assert.AreEqual("/env/redrocks/props/thetabridge01_prop.bp", bridge.Blueprint);
            Assert.AreEqual(new Vector3(143.5f, 4.3535156f, 143.5f), bridge.Position);
            Assert.AreEqual(Vector3.One, bridge.Scale);

            ScmapDecal decal = scmap.Decals[0];
            Assert.AreEqual(2, decal.Type);
            Assert.AreEqual(600f, decal.CutOffLod);
            Assert.AreEqual(-1, decal.OwnerArmy);
        }

        [TestMethod]
        public void ReadsAVersion56Map()
        {
            Scmap scmap = ParseAsset(HardFfa);

            Assert.AreEqual("TTerrain", scmap.TerrainShader);
            Assert.IsTrue(scmap.Water.HasWater);
            Assert.AreEqual(6.0625f, scmap.Water.Elevation);
            Assert.IsNotNull(scmap.Minimap);
            Assert.IsNull(scmap.Minimap.Unknown, "version 56 stores no number after the minimap colours");
            Assert.IsNull(scmap.Skybox, "only version 60 stores a skybox");
            Assert.AreEqual(10, scmap.Layers.Count);
            Assert.AreEqual(2, scmap.StratumMasks.Count);
            Assert.AreEqual("/env/geothermal/props/rocks/georockgroup01_prop.bp", scmap.Props[0].Blueprint);
        }

        [TestMethod]
        public void ReadsAVersion53Map()
        {
            Scmap scmap = ParseAsset(CoopR01);

            Assert.AreEqual("No Tileset", scmap.Tileset);
            Assert.IsNull(scmap.Minimap);
            Assert.IsNull(scmap.Skybox);
            Assert.AreEqual(1, scmap.StratumMasks.Count);
            Assert.AreEqual(1, scmap.EnvironmentCubemaps.Count);
            Assert.AreEqual("<default>", scmap.EnvironmentCubemaps[0].Name);

            Assert.AreEqual(6, scmap.Layers.Count);
            Assert.AreEqual(new ScmapTerrainLayer("/env/desert/layers/des_gravel01_albedo.dds", 4, "/env/evergreen2/layers/eg_gravel2_normal.dds", 4), scmap.Layers[0]);

            ScmapDecalGroup group = scmap.DecalGroups[0];
            Assert.AreEqual("erosionSml", group.Name);
            Assert.AreEqual(3, group.DecalIds.Count);
            Assert.AreEqual("/env/common/decals/tarmacs/tar8x_cybran_01_albedo.dds", scmap.Decals[0].Textures[0]);
            Assert.AreEqual("", scmap.Decals[0].Textures[1]);
        }

        [TestMethod]
        public void ParsesFromAStream()
        {
            using FileStream stream = File.OpenRead(HardFfa + ".scmap");

            Scmap scmap = ScmapParser.Parse(stream);

            Assert.AreEqual(2226, scmap.Props.Count);
        }

        [TestMethod]
        public void RejectsATruncatedFile()
        {
            byte[] bytes = File.ReadAllBytes(HardFfa + ".scmap");

            ScmapFormatException exception = Assert.ThrowsException<ScmapFormatException>(() => ScmapParser.Parse(bytes.AsSpan(0, bytes.Length - 10)));

            StringAssert.Contains(exception.Message, "Unexpected end of the file");
        }

        [TestMethod]
        public void RejectsTrailingBytes()
        {
            byte[] bytes = [.. File.ReadAllBytes(HardFfa + ".scmap"), 0];

            ScmapFormatException exception = Assert.ThrowsException<ScmapFormatException>(() => ScmapParser.Parse(bytes));

            Assert.AreEqual(bytes.Length - 1, exception.Offset);
        }

        [TestMethod]
        public void RejectsOtherFiles()
        {
            byte[] bytes = File.ReadAllBytes(HardFfa + "_scenario.lua");

            ScmapFormatException exception = Assert.ThrowsException<ScmapFormatException>(() => ScmapParser.Parse(bytes));

            Assert.AreEqual(0, exception.Offset);
        }

        [TestMethod]
        public void RejectsAnUnknownVersion()
        {
            byte[] bytes = File.ReadAllBytes(HardFfa + ".scmap");
            int previewLength = BitConverter.ToInt32(bytes, 30);
            BitConverter.GetBytes(57).CopyTo(bytes, 34 + previewLength);

            ScmapFormatException exception = Assert.ThrowsException<ScmapFormatException>(() => ScmapParser.Parse(bytes));

            StringAssert.Contains(exception.Message, "57");
        }

        [TestMethod]
        public void ReadsHeightsAtTheCorners()
        {
            ScmapHeightmap heightmap = new ScmapHeightmap(2, 1, 0.5f, [0, 1, 2, 3, 4, 5]);

            Assert.AreEqual(0f, heightmap.GetHeight(0, 0));
            Assert.AreEqual(1f, heightmap.GetHeight(2, 0));
            Assert.AreEqual(1.5f, heightmap.GetHeight(0, 1));
            Assert.AreEqual(2.5f, heightmap.GetHeight(2, 1));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => heightmap.GetHeight(3, 0));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => heightmap.GetHeight(0, -1));
        }
    }
}
