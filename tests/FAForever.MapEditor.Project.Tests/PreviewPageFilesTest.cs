using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;
using ImageMagick;

namespace FAForever.MapEditor.Project.Tests
{
    [TestClass]
    public class PreviewPageFilesTest
    {
        [ClassCleanup]
        public static void Cleanup() => ProjectFolders.Clean();

        private static (int Width, int Height, byte[] Rgba) Read(byte[] png)
        {
            using MagickImage image = new MagickImage(png);
            return ((int)image.Width, (int)image.Height, image.GetPixels().ToByteArray(PixelMapping.RGBA)!);
        }

        private static byte[] Pixel(byte[] rgba, int size, int column, int row) => rgba.AsSpan((row * size + column) * 4, 4).ToArray();

        /// <summary>
        /// The pixel over a place of the map, in ogrids.
        /// </summary>
        private static (int Column, int Row) At(MapDocument document, int size, float x, float z)
        {
            float perPixel = Math.Max(document.Width, document.Height) / (float)size;
            return ((int)(x / perPixel), (int)(z / perPixel));
        }

        [TestMethod]
        public void DrawsEveryPictureAtEverySize()
        {
            MapDocument document = MapImporter.ImportFolder(ProjectFolders.ThetaPassage);

            IReadOnlyDictionary<string, byte[]> files = PreviewPageFiles.Render(document, null);

            string[] kinds = ["preview", "cliffs", "water", "zones_land", "zones_naval", "zones_hover", "zones_amphibious"];
            CollectionAssert.AreEquivalent(
                kinds.SelectMany(kind => new[] { 1024, 512, 256 }.Select(size => $"assets/{kind}_{size}.png")).Append("index.html").ToList(),
                files.Keys.ToList(), "the page and its pictures; no icons without wrecks or a catalogue");
            foreach ((string path, byte[] png) in files.Where(file => file.Key.EndsWith(".png", StringComparison.Ordinal)))
            {
                (int width, int height, _) = Read(png);
                int size = int.Parse(path[(path.LastIndexOf('_') + 1)..^".png".Length]);
                Assert.AreEqual((size, size), (width, height), path);
            }
        }

        [TestMethod]
        public void ShowsTheWaterInBlueAndTheRestTransparent()
        {
            // the water halfway up Theta Passage's ground, which has none
            MapDocument document = MapImporter.ImportFolder(ProjectFolders.ThetaPassage);
            float[] heights = [.. document.Terrain.Heightmap.Samples.ToArray().Select(sample => sample * document.Terrain.Heightmap.Scale).Order()];
            document = document with { Water = document.Water with { HasWater = true, Elevation = heights[heights.Length / 2] } };

            (_, _, byte[] water) = Read(PreviewPageFiles.Render(document, null)["assets/water_256.png"]);

            int wet = 0, dry = 0;
            for (int row = 0; row < 256; row++)
            {
                for (int column = 0; column < 256; column++)
                {
                    byte[] pixel = Pixel(water, 256, column, row);
                    float perPixel = Math.Max(document.Width, document.Height) / 256f;
                    float height = document.Terrain.Heightmap.HeightAt((column + 0.5f) * perPixel, (row + 0.5f) * perPixel);
                    if (pixel[3] == 255)
                    {
                        wet++;
                        Assert.IsTrue(pixel[2] > pixel[0] && pixel[2] > pixel[1], "blue");
                    }
                    else if (pixel[3] == 0)
                    {
                        dry++;
                    }
                    // a pixel wholly under or above the water, by the middle and a margin
                    if (height < document.Water.Elevation - 2)
                    {
                        Assert.AreNotEqual(0, pixel[3], $"under water at {column}, {row}");
                    }
                    else if (height > document.Water.Elevation + 2)
                    {
                        Assert.AreNotEqual(255, pixel[3], $"dry at {column}, {row}");
                    }
                }
            }
            Assert.IsTrue(wet > 0 && dry > 0, $"{wet} wet, {dry} dry");
        }

        [TestMethod]
        public void ShowsTheCliffsInRed()
        {
            // HardFFA's middle is too steep for land units
            MapDocument document = MapImporter.ImportFolder(ProjectFolders.HardFfa);
            ScmapHeightmap heightmap = Walkability.HeightmapOf(document.Terrain.Heightmap);

            (_, _, byte[] cliffs) = Read(PreviewPageFiles.Render(document, null)["assets/cliffs_1024.png"]);

            int steep = 0;
            for (int z = 0; z < document.Height; z++)
            {
                for (int x = 0; x < document.Width; x++)
                {
                    (int column, int row) = At(document, 1024, x + 0.5f, z + 0.5f);
                    byte[] pixel = Pixel(cliffs, 1024, column, row);
                    if (NavGenerator.IsFlat(heightmap, x, z))
                    {
                        Assert.AreEqual(0, pixel[3], $"flat at {x}, {z}");
                    }
                    else
                    {
                        steep++;
                        Assert.AreEqual(255, pixel[3], $"steep at {x}, {z}");
                        Assert.IsTrue(pixel[0] > 200 && pixel[1] < 60 && pixel[2] < 60, "red");
                    }
                }
            }
            Assert.IsTrue(steep > 0);
        }

        [TestMethod]
        public void ColoursEachRegionOfALayerAndLeavesTheRestTransparent()
        {
            MapDocument document = MapImporter.ImportFolder(ProjectFolders.ThetaPassage);
            MapNavigation navigation = Walkability.Generate(document);

            (_, _, byte[] zones) = Read(PreviewPageFiles.Render(document, null)["assets/zones_land_1024.png"]);

            NavGrid land = navigation.Land;
            HashSet<string> colours = [];
            for (int z = 0; z < land.Height; z++)
            {
                for (int x = 0; x < land.Width; x++)
                {
                    (int column, int row) = At(document, 1024, x + 0.5f, z + 0.5f);
                    byte[] pixel = Pixel(zones, 1024, column, row);
                    int label = land.Cells[z * land.Width + x];
                    if (label > 0)
                    {
                        (byte r, byte g, byte b) = PreviewPageFiles.RegionColour(label);
                        CollectionAssert.AreEqual(new byte[] { r, g, b, 255 }, pixel, $"region {label} at {x}, {z}");
                        colours.Add($"{r},{g},{b}");
                    }
                    else
                    {
                        Assert.AreEqual(0, pixel[3], $"no region at {x}, {z}");
                    }
                }
            }
            Assert.AreEqual(land.Labels.Count, colours.Count, "a colour per region");
        }

        [TestMethod]
        public void WritesThePageAndItsPicturesIntoTheChosenFolder()
        {
            MapDocument document = MapImporter.ImportFolder(ProjectFolders.HardFfa);
            string folder = Path.Combine(ProjectFolders.New("preview"), "made when missing");

            string page = PreviewPageFiles.Write(document, folder, null);

            Assert.AreEqual(Path.GetFullPath(Path.Combine(folder, "index.html")), page);
            Assert.IsTrue(File.Exists(page));
            Assert.AreEqual(21, Directory.GetFiles(Path.Combine(folder, "assets")).Length);
            Assert.AreEqual(0, Directory.GetFiles(folder, "*.tmp", SearchOption.AllDirectories).Length, "no file left half written");
        }
    }
}
