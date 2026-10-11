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
            Assert.AreEqual(0f, scmap.Minimap.ImagerElevationOffset);

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
            Assert.IsNull(scmap.Minimap.ImagerElevationOffset, "version 56 stores no imager elevation offset");
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
        [DataRow(ThetaPassage, 256)]
        [DataRow(HardFfa, 256)]
        [DataRow(CoopR01, 512)]
        public void ReadsTheSizeOfTheNormalMapsTiles(string map, int size)
        {
            Scmap scmap = ParseAsset(map);

            Assert.AreEqual((size, size), (scmap.NormalMapTileWidth, scmap.NormalMapTileHeight));
            Assert.AreEqual(1, scmap.NormalMaps.Count);
        }

        [TestMethod]
        public void SizesTheMasksAndTerrainTypesFromTheHeightmapNotFromTheNormalMap()
        {
            // as the game's maps of 4096 by 4096 (SCMP_029, SCMP_030, X1MP_012): four tiles of half the
            // map's size, and grids of the map's size after them
            Scmap scmap = ParseAsset(HardFfa);
            byte[] tile = scmap.NormalMaps[0];
            byte[] bytes = ScmapWriter.Write(scmap with { NormalMapTileWidth = 128, NormalMapTileHeight = 128, NormalMaps = [tile, tile, tile, tile] });

            Scmap tiled = ScmapParser.Parse(bytes);

            Assert.AreEqual((128, 128), (tiled.NormalMapTileWidth, tiled.NormalMapTileHeight));
            Assert.AreEqual(4, tiled.NormalMaps.Count);
            Assert.AreEqual(128 * 128, tiled.WaterFoamMask.Length);
            Assert.AreEqual(256 * 256, tiled.TerrainTypes.Length);
            CollectionAssert.AreEqual(scmap.TerrainTypes, tiled.TerrainTypes);
            CollectionAssert.AreEqual(scmap.WaterDepthBiasMask, tiled.WaterDepthBiasMask);
            Assert.AreEqual(scmap.Props.Count, tiled.Props.Count);
            CollectionAssert.AreEqual(bytes, ScmapWriter.Write(tiled), "the tile size is written back");
        }

        [TestMethod]
        public void ReadsAnyByteButZeroAsWater()
        {
            Scmap scmap = ParseAsset(ThetaPassage);
            byte[] dry = ScmapWriter.Write(scmap);
            byte[] wet = ScmapWriter.Write(scmap with { Water = scmap.Water with { HasWater = true } });
            int flag = dry.AsSpan().CommonPrefixLength(wet);
            Assert.AreEqual((0, 1), (dry[flag], wet[flag]));

            dry[flag] = 2;

            Assert.IsTrue(ScmapParser.Parse(dry).Water.HasWater, "the engine reads the flag as not 0");
        }

        [TestMethod]
        public void ReadsTheCompositeShaderFlag()
        {
            Scmap scmap = ParseAsset(ThetaPassage);
            Assert.IsFalse(scmap.CompositeTerrainShader);

            Scmap reparsed = ScmapParser.Parse(ScmapWriter.Write(scmap with { CompositeTerrainShader = true }));

            Assert.IsTrue(reparsed.CompositeTerrainShader);
        }

        [TestMethod]
        public void ReadsThePreviewsNameAndEntries()
        {
            Scmap scmap = ParseAsset(HardFfa);
            Assert.AreEqual("", scmap.PreviewName);
            Assert.AreEqual(0, scmap.PreviewMetadata.Count);
            byte[] bytes = ScmapWriter.Write(scmap with { PreviewName = "Hård", PreviewMetadata = [new ScmapPreviewMetadata(7, 0xdeadbeef)] });

            Scmap reparsed = ScmapParser.Parse(bytes);

            Assert.AreEqual("Hård", reparsed.PreviewName);
            CollectionAssert.AreEqual(new[] { new ScmapPreviewMetadata(7, 0xdeadbeef) }, reparsed.PreviewMetadata.ToArray());
            // the name in UTF-16 up to a zero character, then the count of entries
            Assert.AreEqual("Hård\0", System.Text.Encoding.Unicode.GetString(bytes, 24, 10));
            Assert.AreEqual(1, BitConverter.ToInt32(bytes, 34));
            CollectionAssert.AreEqual(scmap.Preview, reparsed.Preview);
        }

        [TestMethod]
        public void ReadsTheDecalCounters()
        {
            Scmap scmap = ParseAsset(ThetaPassage);

            Assert.IsTrue(scmap.NextDecalId > scmap.Decals.Max(decal => decal.Id), "the next decal id comes after every decal's");
            Assert.AreEqual(0, scmap.DecalGroups.Count);
        }

        [TestMethod]
        public void ReadsTheDecalsOfTheStrategicView()
        {
            Scmap scmap = ParseAsset(ThetaPassage);
            Assert.AreEqual(0, scmap.CartographicDecals.Count);
            ScmapCartographicDecalBatch batch = new ScmapCartographicDecalBatch("Decal", "/textures/ui/common/game/cartographic/x.dds",
                [new ScmapCartographicDecal([1, 2, 3, 4, 5, 6, 7, 8, 9]), new ScmapCartographicDecal([9, 8, 7, 6, 5, 4, 3, 2, 1])]);

            Scmap reparsed = ScmapParser.Parse(ScmapWriter.Write(scmap with { CartographicDecals = [batch] }));

            ScmapCartographicDecalBatch read = reparsed.CartographicDecals.Single();
            Assert.AreEqual((batch.Technique, batch.Texture), (read.Technique, read.Texture));
            CollectionAssert.AreEqual(new float[] { 9, 8, 7, 6, 5, 4, 3, 2, 1 }, read.Decals[1].Values.ToArray());
            Assert.AreEqual(scmap.Props.Count, reparsed.Props.Count);
        }

        [TestMethod]
        public void ReadsAVersion53MapsCountsAsTheEngineDoes()
        {
            Scmap scmap = ParseAsset(CoopR01);
            Assert.AreEqual(6, scmap.StoredLayerCount);

            // the engine reads six layers whatever the number after the tileset, and every stratum mask counted
            Scmap reparsed = ScmapParser.Parse(ScmapWriter.Write(scmap with
            {
                StoredLayerCount = 3,
                StratumMasks = [scmap.StratumMasks[0], scmap.StratumMasks[0]],
                ExtraWaterMaps = [scmap.WaterMap],
            }));

            Assert.AreEqual(3, reparsed.StoredLayerCount);
            Assert.AreEqual(6, reparsed.Layers.Count);
            Assert.AreEqual(scmap.Layers[5], reparsed.Layers[5]);
            Assert.AreEqual(2, reparsed.StratumMasks.Count);
            Assert.AreEqual(1, reparsed.ExtraWaterMaps.Count);
            Assert.AreEqual(scmap.Props.Count, reparsed.Props.Count);
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
