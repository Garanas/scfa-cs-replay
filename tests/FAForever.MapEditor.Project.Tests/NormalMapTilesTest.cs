using System.Text;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;

namespace FAForever.MapEditor.Project.Tests
{
    [TestClass]
    public class NormalMapTilesTest
    {
        [ClassCleanup]
        public static void Cleanup() => ProjectFolders.Clean();

        private const string Map = ProjectFolders.HardFfa;

        [TestMethod]
        public void UsesTheEnginesTiles()
        {
            Assert.AreEqual((256, 256), NormalMapTiles.DefaultTileSize(256, 256));
            Assert.AreEqual((2048, 2048), NormalMapTiles.DefaultTileSize(4096, 4096));
            Assert.AreEqual((2048, 1024), NormalMapTiles.DefaultTileSize(4096, 1024));
            Assert.AreEqual((2, 1), NormalMapTiles.Grid(4096, 1024, 2048, 1024));
        }

        [TestMethod]
        public void ImportsAMapStoredInTilesAsOneImageAndExportsTheSameTiles()
        {
            // HardFFA (256 by 256) with its normal map in four tiles of 128, as the game's maps of 4096
            // by 4096 store theirs in four of 2048
            (Scmap scmap, MapSave save, MapScenario scenario) = Files();
            (int width, int height, _, byte[] rgba) = DdsTexture.Read(scmap.NormalMaps[0], "normal map");
            byte[][] quadrants = [.. Enumerable.Range(0, 4).Select(index => Quadrant(rgba, width, height, index % 2, index / 2))];
            Scmap tiled = scmap with
            {
                NormalMapTileWidth = 128,
                NormalMapTileHeight = 128,
                NormalMaps = [.. quadrants.Select(quadrant => DdsTexture.Write(width / 2, height / 2, TextureFormat.A8R8G8B8, quadrant))],
            };

            MapDocument document = MapImporter.Import(tiled, save, scenario, "HardFFA", "HardFFA");

            Assert.AreEqual((width, height), (document.Terrain.NormalMap.Width, document.Terrain.NormalMap.Height));
            CollectionAssert.AreEqual(rgba, Pixels.ToRgba(document.Terrain.NormalMap), "the tiles in their places, row by row");
            Assert.AreEqual((128, 128), (document.Terrain.NormalMapTileWidth, document.Terrain.NormalMapTileHeight), "a size other than the engine's is kept");

            Scmap exported = ScmapParser.Parse(MapExporter.ToBytes(document)["HardFFA.scmap"]);

            Assert.AreEqual((128, 128), (exported.NormalMapTileWidth, exported.NormalMapTileHeight));
            Assert.AreEqual(4, exported.NormalMaps.Count);
            for (int index = 0; index < 4; index++)
            {
                CollectionAssert.AreEqual(quadrants[index], DdsTexture.Read(exported.NormalMaps[index], "tile").Rgba, $"tile {index + 1}");
            }
        }

        [TestMethod]
        public void KeepsTheSizeOfTheTilesInTheProject()
        {
            (Scmap scmap, MapSave save, MapScenario scenario) = Files();
            byte[] tile = scmap.NormalMaps[0];
            MapDocument document = MapImporter.Import(scmap with { NormalMapTileWidth = 128, NormalMapTileHeight = 128, NormalMaps = [tile, tile, tile, tile] }, save, scenario, "HardFFA", "HardFFA");
            string project = ProjectFolders.New("tiles");

            MapProjectStore.Save(document, project);
            MapDocument loaded = MapProjectStore.Load(project);

            Assert.AreEqual((128, 128), (loaded.Terrain.NormalMapTileWidth, loaded.Terrain.NormalMapTileHeight));
            Assert.AreEqual((512, 512), (loaded.Terrain.NormalMap.Width, loaded.Terrain.NormalMap.Height));
        }

        [TestMethod]
        public void LeavesTheEnginesTilesOutOfTheProject()
        {
            MapDocument document = MapImporter.ImportFolder(Map);
            string project = ProjectFolders.New("engine-tiles");

            MapProjectStore.Save(document, project);

            Assert.IsNull(document.Terrain.NormalMapTileWidth);
            Assert.IsFalse(File.ReadAllText(Path.Combine(project, MapProjectLayout.Map)).Contains("normalMapTile", StringComparison.Ordinal));
        }

        [TestMethod]
        public void WritesAGeneratedNormalMapInTheEnginesTiles()
        {
            (Scmap scmap, MapSave save, MapScenario scenario) = Files();
            byte[] tile = scmap.NormalMaps[0];
            MapDocument document = MapImporter.Import(scmap with { NormalMapTileWidth = 128, NormalMapTileHeight = 128, NormalMaps = [tile, tile, tile, tile] }, save, scenario, "HardFFA", "HardFFA");
            NormalMapGenerator generator = new NormalMapGenerator();

            MapDocument generated = generator.Apply(document, generator.Generate(document));
            Scmap exported = MapExporter.Export(generated).Scmap;

            Assert.IsNull(generated.Terrain.NormalMapTileWidth);
            Assert.AreEqual((256, 256), (exported.NormalMapTileWidth, exported.NormalMapTileHeight));
            Assert.AreEqual(1, exported.NormalMaps.Count);
        }

        [TestMethod]
        public void SplitsALargeMapIntoTheEnginesTiles()
        {
            // a map of 4096 by 4096 whose normal map is held at a quarter of its size: four tiles of
            // 2048 ogrids, each 512 pixels
            byte[] rgba = new byte[1024 * 1024 * 4];
            rgba[(1023 * 1024 + 1023) * 4] = 200;

            (int tileWidth, int tileHeight, IReadOnlyList<byte[]> tiles) = NormalMapTiles.Split(4096, 4096, 1024, 1024, rgba, TextureFormat.A8R8G8B8, null, null);

            Assert.AreEqual((2048, 2048), (tileWidth, tileHeight));
            Assert.AreEqual(4, tiles.Count);
            (int width, int height, _, byte[] last) = DdsTexture.Read(tiles[3], "tile 4");
            Assert.AreEqual((512, 512), (width, height));
            Assert.AreEqual(200, last[(511 * 512 + 511) * 4], "the last tile is the bottom right one");
        }

        [TestMethod]
        public void RefusesTilesThatDoNotCoverTheMap()
        {
            (Scmap scmap, MapSave save, MapScenario scenario) = Files();
            byte[] tile = scmap.NormalMaps[0];

            MapProjectException exception = Assert.ThrowsException<MapProjectException>(() =>
                MapImporter.Import(scmap with { NormalMapTileWidth = 128, NormalMapTileHeight = 128, NormalMaps = [tile, tile, tile] }, save, scenario, "HardFFA", "HardFFA"));

            StringAssert.Contains(exception.Message, "do not cover");
        }

        private static (Scmap Scmap, MapSave Save, MapScenario Scenario) Files() => (
            ScmapParser.Parse(File.ReadAllBytes(Path.Combine(Map, "HardFFA.scmap"))),
            MapSaveParser.Parse(File.ReadAllText(Path.Combine(Map, "HardFFA_save.lua"), Encoding.Latin1)),
            MapScenarioParser.Parse(File.ReadAllText(Path.Combine(Map, "HardFFA_scenario.lua"), Encoding.Latin1)));

        private static byte[] Quadrant(byte[] rgba, int width, int height, int column, int row)
        {
            int w = width / 2;
            int h = height / 2;
            byte[] quadrant = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
            {
                rgba.AsSpan(((row * h + y) * width + column * w) * 4, w * 4).CopyTo(quadrant.AsSpan(y * w * 4));
            }
            return quadrant;
        }
    }
}
