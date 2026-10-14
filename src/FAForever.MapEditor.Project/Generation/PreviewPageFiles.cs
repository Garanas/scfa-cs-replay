using System.Text;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.Project.Generation
{
    /// <summary>
    /// The preview page of a map, generated into a folder the map maker chooses: a page about the map,
    /// <c>index.html</c> (<see cref="PreviewPage"/>), and in <c>assets</c> its pictures, each at
    /// <see cref="Sizes"/> pixels square: the map from above (<see cref="TexturedPreview"/>), its
    /// cliffs in red, its water in blue, and per layer of the navigational mesh its regions, each in
    /// a colour of its own, everything else transparent; and in <c>assets/icons</c> the icons of the
    /// units whose wrecks lie on the map, only those.
    /// </summary>
    /// <remarks>
    /// The pictures cover the map as <see cref="TexturedPreview"/> does: a map that is not square fills
    /// the picture's width or height from the top left, and the margin is transparent (black in the
    /// preview). Where a pixel covers more than one ogrid it samples the ogrids it covers, and its
    /// alpha is the share of them that is cliff, water or region (a region pixel takes the colour of
    /// the region most of them belong to); where it covers less, it shows the ogrid under it.
    /// <list type="bullet">
    /// <item>Cliffs: the ogrids too steep for land units (<see cref="NavGenerator.IsFlat"/>), under
    /// the water too; terrain types and walkability marks do not count.</item>
    /// <item>Water: the ground under the water's surface, the heights between the heightmap's
    /// samples interpolated, so the shore is smooth at every size.</item>
    /// <item>Regions: the labels the game gives a layer (<see cref="Walkability.Generate"/>, with the
    /// marks and the playable area); ground no unit of the layer can reach and the small regions the
    /// game culls are transparent. A region's colour follows from its number, so the same map gives
    /// the same colours.</item>
    /// </list>
    /// </remarks>
    public static class PreviewPageFiles
    {
        /// <summary>
        /// The page, by its path in the folder.
        /// </summary>
        public const string Page = "index.html";

        /// <summary>
        /// The sizes of every picture, square, largest first.
        /// </summary>
        public static readonly IReadOnlyList<int> Sizes = [1024, 512, 256];

        /// <summary>
        /// The layers that get a picture of their regions, by the name in the file's name.
        /// </summary>
        public static readonly IReadOnlyList<(NavLayer Layer, string Name)> Layers =
            [(NavLayer.Land, "land"), (NavLayer.Water, "naval"), (NavLayer.Hover, "hover"), (NavLayer.Amphibious, "amphibious")];

        private static readonly (byte R, byte G, byte B) Cliff = (220, 30, 30);

        private static readonly (byte R, byte G, byte B) WaterColour = (30, 90, 220);

        /// <summary>
        /// The page and its pictures by their path in the folder: <c>index.html</c>,
        /// <c>assets/water_512.png</c> and so on (<c>preview_</c>, <c>cliffs_</c>, <c>water_</c>
        /// and <c>zones_&lt;layer&gt;_</c> for land, naval, hover and amphibious, each followed by its
        /// size) and <c>assets/icons/&lt;unit&gt;.png</c>.
        /// </summary>
        /// <param name="workspace">The workspace whose assets hold the game's textures for the preview,
        /// as the 3D view finds them (<see cref="GameTextures"/>); null for the map's own textures only,
        /// the others drawn grey.</param>
        /// <param name="catalog">The workspace's assets, for the reclaim of props and wrecks and the units'
        /// names and icons; null (or empty) for none, and the page leaves those out.</param>
        /// <param name="stamp">The editor and moment the page names; null for neither.</param>
        public static IReadOnlyDictionary<string, byte[]> Render(MapDocument document, Workspace? workspace, AssetCatalog? catalog = null, EditorStamp? stamp = null,
            CancellationToken cancel = default)
        {
            catalog = catalog is { Entries.Count: > 0 } ? catalog : null;
            Dictionary<string, byte[]> files = new Dictionary<string, byte[]>(StringComparer.Ordinal);

            // the preview is drawn once, at the largest size, and made smaller from it
            int largest = Sizes[0];
            byte[] preview = Pixels.ToRgba(TexturedPreview.Render(document, path => GameTextures.Find(path, document, workspace)?.Read(), largest));
            foreach (int size in Sizes)
            {
                cancel.ThrowIfCancellationRequested();
                files[Name("preview", size)] = ProjectImages.RgbaPng(size, size, Reduce(preview, largest, size));
            }

            Heightmap heightmap = document.Terrain.Heightmap;
            ScmapHeightmap samples = Walkability.HeightmapOf(heightmap);
            bool[] steep = new bool[document.Width * document.Height];
            Parallel.For(0, document.Height, z =>
            {
                for (int x = 0; x < document.Width; x++)
                {
                    steep[z * document.Width + x] = !NavGenerator.IsFlat(samples, x, z);
                }
            });
            float? water = Walkability.WaterLevel(document);
            MapNavigation navigation = Walkability.Generate(document, cancel);
            NavigationMesh mesh = NavigationMesh.Of(document, navigation, cancel);

            foreach (int size in Sizes)
            {
                cancel.ThrowIfCancellationRequested();
                files[Name("cliffs", size)] = Png(Mask(document, size, (x, z) => steep[(int)z * document.Width + (int)x], Cliff), size);
                files[Name("water", size)] = Png(Mask(document, size, (x, z) => water is float level && heightmap.HeightAt(x, z) < level, WaterColour), size);
                foreach ((NavLayer layer, string name) in Layers)
                {
                    files[Name($"zones_{name}", size)] = Png(Regions(document, size, navigation[layer]), size);
                }
            }

            // the icons of the units whose wrecks lie on the map
            HashSet<string> icons = new HashSet<string>(StringComparer.Ordinal);
            if (catalog is not null)
            {
                foreach (string blueprint in document.AllUnits().Where(UnitWrecks.IsWreck).Select(unit => unit.Unit.Blueprint).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    string id = ReclaimValues.UnitId(blueprint);
                    if (ReclaimValues.UnitOf(blueprint, catalog)?.Icon is string icon && File.Exists(icon) && icons.Add(id))
                    {
                        files[$"assets/icons/{id}.png"] = File.ReadAllBytes(icon);
                    }
                }
            }

            files[Page] = new UTF8Encoding(false).GetBytes(PreviewPage.Write(document, mesh, catalog, icons, stamp));
            return files;
        }

        /// <summary>
        /// Generates the page into a folder (made when missing), each file written next to its place and
        /// then moved there, so a browser showing the page never reads half a file; other files in the
        /// folder are left alone.
        /// </summary>
        /// <returns>The page's file.</returns>
        /// <inheritdoc cref="Render" path="/param"/>
        public static string Write(MapDocument document, string folder, Workspace? workspace, AssetCatalog? catalog = null, EditorStamp? stamp = null,
            CancellationToken cancel = default)
        {
            IReadOnlyDictionary<string, byte[]> files = Render(document, workspace, catalog, stamp, cancel);
            foreach ((string path, byte[] bytes) in files)
            {
                string destination = Path.Combine(folder, path);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                string temporary = destination + ".tmp";
                File.WriteAllBytes(temporary, bytes);
                File.Move(temporary, destination, overwrite: true);
            }
            return Path.GetFullPath(Path.Combine(folder, Page));
        }

        private static string Name(string kind, int size) => $"assets/{kind}_{size}.png";

        private static byte[] Png(byte[] rgba, int size) => ProjectImages.RgbaPng(size, size, rgba);

        /// <summary>
        /// The colour of a region by its number: hues a golden angle apart, so neighbouring numbers
        /// differ, among the hues that are neither the cliffs' red nor the water's blue (orange to
        /// teal, violet to magenta), so a region never reads as cliff or water.
        /// </summary>
        public static (byte R, byte G, byte B) RegionColour(int label)
        {
            // 145 degrees from orange to teal, then 75 from violet to magenta
            double place = (label * 0.6180339887498949) % 1.0 * 220;
            double hue = (place < 145 ? 25 + place : 260 + (place - 145)) / 60;
            double saturation = label % 2 == 0 ? 0.65 : 0.85;
            double value = label % 3 == 0 ? 0.8 : 0.95;
            int sector = (int)hue;
            double f = hue - sector;
            double p = value * (1 - saturation);
            double q = value * (1 - saturation * f);
            double t = value * (1 - saturation * (1 - f));
            (double r, double g, double b) = sector switch
            {
                0 => (value, t, p),
                1 => (q, value, p),
                2 => (p, value, t),
                3 => (p, q, value),
                4 => (t, p, value),
                _ => (value, p, q),
            };
            return ((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
        }

        /// <summary>
        /// How many places a pixel samples along each axis, spread evenly over it: about one per ogrid
        /// it covers, at least one.
        /// </summary>
        private static int SamplesPerAxis(MapDocument document, int size) =>
            Math.Max(1, (int)Math.Ceiling(Math.Max(document.Width, document.Height) / (double)size));

        private static byte[] Mask(MapDocument document, int size, Func<float, float, bool> covers, (byte R, byte G, byte B) colour)
        {
            byte[] rgba = new byte[size * size * 4];
            float perPixel = Math.Max(document.Width, document.Height) / (float)size;
            int k = SamplesPerAxis(document, size);
            Parallel.For(0, size, row =>
            {
                for (int column = 0; column < size; column++)
                {
                    int hits = 0;
                    for (int j = 0; j < k; j++)
                    {
                        float z = (row + (j + 0.5f) / k) * perPixel;
                        for (int i = 0; i < k; i++)
                        {
                            float x = (column + (i + 0.5f) / k) * perPixel;
                            if (x < document.Width && z < document.Height && covers(x, z))
                            {
                                hits++;
                            }
                        }
                    }
                    if (hits > 0)
                    {
                        int at = (row * size + column) * 4;
                        rgba[at] = colour.R;
                        rgba[at + 1] = colour.G;
                        rgba[at + 2] = colour.B;
                        rgba[at + 3] = (byte)Math.Round(255.0 * hits / (k * k));
                    }
                }
            });
            return rgba;
        }

        private static byte[] Regions(MapDocument document, int size, NavGrid grid)
        {
            byte[] rgba = new byte[size * size * 4];
            float perPixel = Math.Max(document.Width, document.Height) / (float)size;
            int k = SamplesPerAxis(document, size);
            Parallel.For(0, size, () => new int[k * k], (row, _, labels) =>
            {
                for (int column = 0; column < size; column++)
                {
                    int count = 0;
                    for (int j = 0; j < k; j++)
                    {
                        float z = (row + (j + 0.5f) / k) * perPixel;
                        for (int i = 0; i < k; i++)
                        {
                            float x = (column + (i + 0.5f) / k) * perPixel;
                            if (x < grid.Width && z < grid.Height && grid.Cells[(int)z * grid.Width + (int)x] > 0)
                            {
                                labels[count++] = grid.Cells[(int)z * grid.Width + (int)x];
                            }
                        }
                    }
                    if (count == 0)
                    {
                        continue;
                    }
                    (int most, int _) = MostCommon(labels.AsSpan(0, count));
                    (byte r, byte g, byte b) = RegionColour(most);
                    int at = (row * size + column) * 4;
                    rgba[at] = r;
                    rgba[at + 1] = g;
                    rgba[at + 2] = b;
                    rgba[at + 3] = (byte)Math.Round(255.0 * count / (k * k));
                }
                return labels;
            }, _ => { });
            return rgba;
        }

        /// <summary>
        /// The value that occurs most often, the lowest of those that occur equally often; sorts the span.
        /// </summary>
        private static (int Value, int Count) MostCommon(Span<int> values)
        {
            values.Sort();
            (int best, int bestCount) = (values[0], 0);
            for (int start = 0; start < values.Length;)
            {
                int end = start;
                while (end < values.Length && values[end] == values[start])
                {
                    end++;
                }
                if (end - start > bestCount)
                {
                    (best, bestCount) = (values[start], end - start);
                }
                start = end;
            }
            return (best, bestCount);
        }

        /// <summary>
        /// A square RGBA picture made smaller: each pixel the average of the pixels it covers.
        /// </summary>
        private static byte[] Reduce(byte[] rgba, int from, int to)
        {
            if (from == to)
            {
                return rgba;
            }
            int factor = from / to;
            byte[] reduced = new byte[to * to * 4];
            Parallel.For(0, to, row =>
            {
                for (int column = 0; column < to; column++)
                {
                    for (int channel = 0; channel < 4; channel++)
                    {
                        int sum = 0;
                        for (int y = 0; y < factor; y++)
                        {
                            for (int x = 0; x < factor; x++)
                            {
                                sum += rgba[((row * factor + y) * from + column * factor + x) * 4 + channel];
                            }
                        }
                        reduced[(row * to + column) * 4 + channel] = (byte)((sum + factor * factor / 2) / (factor * factor));
                    }
                }
            });
            return reduced;
        }
    }
}
