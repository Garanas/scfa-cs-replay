using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Files.Serialized;

namespace FAForever.MapEditor.Project
{
    /// <summary>
    /// Keeps a <see cref="MapDocument"/> on disk as a project (<see cref="MapProjectLayout"/>): its
    /// records as JSON, its images as PNG, its script and other files as they are.
    /// </summary>
    public static class MapProjectStore
    {
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            // the files are read by people and their tools, never put in a web page: <LOC ...> stays as it is
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            RespectNullableAnnotations = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            Converters = { new JsonStringEnumConverter() },
        };

        /// <summary>
        /// Writes the document into <paramref name="folder"/>, which must be absent, empty or a
        /// project. Files of an earlier save that the document no longer has are removed from
        /// <see cref="MapProjectLayout.Files"/>.
        /// </summary>
        /// <exception cref="MapProjectException">The folder holds something other than a project.</exception>
        public static void Save(MapDocument document, string folder)
        {
            if (Directory.Exists(folder)
                && Directory.EnumerateFileSystemEntries(folder).Any()
                && !File.Exists(Path.Combine(folder, MapProjectLayout.Map)))
            {
                throw new MapProjectException($"{folder} is not empty and holds no project ({MapProjectLayout.Map})");
            }
            Directory.CreateDirectory(folder);
            string Of(string file) => Path.Combine(folder, file);

            WriteJson(Of(MapProjectLayout.Map), MapProjectMapping.ToDto(document));
            WriteJson(Of(MapProjectLayout.Props), document.Props.Select(MapProjectMapping.ToDto).ToList());
            WriteJson(Of(MapProjectLayout.Decals), MapProjectMapping.ToDecalsDto(document));
            WriteJson(Of(MapProjectLayout.Markers), MapProjectMapping.ToDto(document.Markers));
            WriteJson(Of(MapProjectLayout.Armies), document.Armies.Select(MapProjectMapping.ToDto).ToList());

            Terrain terrain = document.Terrain;
            TiledBuffer<ushort> heights = terrain.Heightmap.Samples;
            ProjectImages.WriteGray16(Of(MapProjectLayout.Heightmap), heights.Width, heights.Height, heights.ToArray());
            WriteGray8(Of(MapProjectLayout.TerrainTypes), terrain.Types);
            WriteRgba(Of(MapProjectLayout.NormalMap), terrain.NormalMap);
            WriteRgba(Of(MapProjectLayout.Preview), terrain.Preview);
            for (int layer = 0; layer < terrain.StratumMasks.Layers.Length; layer++)
            {
                WriteGray8(Of(MapProjectLayout.StratumMask(layer + 1)), terrain.StratumMasks.Layers[layer]);
            }

            Water water = document.Water;
            WriteRgba(Of(MapProjectLayout.WaterMap), water.Map);
            WriteGray8(Of(MapProjectLayout.WaterFoam), water.FoamMask);
            WriteGray8(Of(MapProjectLayout.WaterFlatness), water.FlatnessMask);
            WriteGray8(Of(MapProjectLayout.WaterDepthBias), water.DepthBiasMask);

            if (document.Script is ImmutableArray<byte> script)
            {
                File.WriteAllBytes(Of(MapProjectLayout.Script), ImmutableCollectionsMarshal.AsArray(script)!);
            }
            else
            {
                File.Delete(Of(MapProjectLayout.Script));
            }

            string files = Of(MapProjectLayout.Files);
            foreach ((string path, ImmutableArray<byte> bytes) in document.Files)
            {
                string target = Path.Combine(files, path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllBytes(target, ImmutableCollectionsMarshal.AsArray(bytes)!);
            }
            foreach ((string path, string file) in FilesIn(files))
            {
                if (!document.Files.ContainsKey(path))
                {
                    File.Delete(file);
                }
            }
        }

        /// <summary>
        /// Reads the project in <paramref name="folder"/>.
        /// </summary>
        /// <exception cref="MapProjectException">A file is missing, does not read, or does not fit the map's size.</exception>
        public static MapDocument Load(string folder)
        {
            string Of(string file) => Path.Combine(folder, file);

            MapProjectDto map = ReadJson<MapProjectDto>(Of(MapProjectLayout.Map));
            List<PropDto> props = ReadJson<List<PropDto>>(Of(MapProjectLayout.Props));
            DecalsDto decals = ReadJson<DecalsDto>(Of(MapProjectLayout.Decals));
            MarkerSetDto markers = ReadJson<MarkerSetDto>(Of(MapProjectLayout.Markers));
            List<ArmyDto> armies = ReadJson<List<ArmyDto>>(Of(MapProjectLayout.Armies));

            int width = map.Terrain.Width;
            int height = map.Terrain.Height;
            MapProjectImages images = new MapProjectImages
            {
                Heightmap = TiledBuffer<ushort>.FromArray(width + 1, height + 1, ReadHeightmap(folder, width, height)),
                TerrainTypes = ReadGray8(Of(MapProjectLayout.TerrainTypes), width, height),
                StratumMasks = ReadStratumMasks(folder, map.Terrain.ScmapVersion >= 56 ? 8 : 4, width, height),
                NormalMap = ReadRgba(Of(MapProjectLayout.NormalMap)),
                Preview = Pixels.FromRgba(256, 256, ProjectImages.ReadRgba(Of(MapProjectLayout.Preview), 256, 256)),
                WaterMap = ReadRgba(Of(MapProjectLayout.WaterMap)),
                WaterFoamMask = ReadGray8(Of(MapProjectLayout.WaterFoam), width / 2, height / 2),
                WaterFlatnessMask = ReadGray8(Of(MapProjectLayout.WaterFlatness), width / 2, height / 2),
                WaterDepthBiasMask = ReadGray8(Of(MapProjectLayout.WaterDepthBias), width / 2, height / 2),
            };

            string scriptPath = Of(MapProjectLayout.Script);
            ImmutableArray<byte>? script = File.Exists(scriptPath) ? ImmutableCollectionsMarshal.AsImmutableArray(File.ReadAllBytes(scriptPath)) : null;
            ImmutableSortedDictionary<string, ImmutableArray<byte>> files = FilesIn(Of(MapProjectLayout.Files))
                .ToImmutableSortedDictionary(entry => entry.Path, entry => ImmutableCollectionsMarshal.AsImmutableArray(File.ReadAllBytes(entry.File)), StringComparer.Ordinal);

            try
            {
                return MapProjectMapping.ToDocument(map, props, decals, markers, armies, images, script, files);
            }
            catch (ArgumentException exception)
            {
                throw new MapProjectException($"{folder}: {exception.Message}", exception);
            }
        }

        private static void WriteJson<T>(string path, T value) => File.WriteAllText(path, JsonSerializer.Serialize(value, Json) + "\n");

        private static T ReadJson<T>(string path)
        {
            if (!File.Exists(path))
            {
                throw new MapProjectException($"{path} is missing");
            }
            try
            {
                return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json)
                    ?? throw new MapProjectException($"{path} is empty");
            }
            catch (JsonException exception)
            {
                throw new MapProjectException($"{path}: {exception.Message}", exception);
            }
        }

        private static ushort[] ReadHeightmap(string folder, int width, int height)
        {
            string png = Path.Combine(folder, MapProjectLayout.Heightmap);
            if (File.Exists(png))
            {
                return ProjectImages.ReadGray16(png, width + 1, height + 1);
            }

            string raw = Path.Combine(folder, MapProjectLayout.HeightmapRaw);
            if (!File.Exists(raw))
            {
                throw new MapProjectException($"{folder} has no {MapProjectLayout.Heightmap} or {MapProjectLayout.HeightmapRaw}");
            }
            byte[] bytes = File.ReadAllBytes(raw);
            int expected = (width + 1) * (height + 1) * 2;
            if (bytes.Length != expected)
            {
                throw new MapProjectException($"{raw} has {bytes.Length} bytes, expected {expected}: {width + 1} by {height + 1} samples of 16 bits");
            }
            ushort[] samples = new ushort[(width + 1) * (height + 1)];
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] = (ushort)(bytes[i * 2] | bytes[i * 2 + 1] << 8);
            }
            return samples;
        }

        /// <summary>
        /// The masks of the layers. A missing mask leaves its layer unused; the masks that exist must
        /// all have the same size (the map's size when there are none).
        /// </summary>
        private static StratumMasks ReadStratumMasks(string folder, int count, int mapWidth, int mapHeight)
        {
            string[] paths = [.. Enumerable.Range(1, count).Select(layer => Path.Combine(folder, MapProjectLayout.StratumMask(layer)))];
            (int width, int height) = paths.FirstOrDefault(File.Exists) is string first ? ProjectImages.Size(first) : (mapWidth, mapHeight);
            return new StratumMasks
            {
                Layers = [.. paths.Select(path => File.Exists(path) ? ReadGray8(path, width, height) : TiledBuffer<byte>.Create(width, height))],
            };
        }

        private static TiledBuffer<byte> ReadGray8(string path, int width, int height) =>
            TiledBuffer<byte>.FromArray(width, height, ProjectImages.ReadGray8(path, width, height));

        /// <summary>
        /// An image of any size.
        /// </summary>
        private static TiledBuffer<Color32> ReadRgba(string path)
        {
            if (!File.Exists(path))
            {
                throw new MapProjectException($"{path} is missing");
            }
            (int width, int height) = ProjectImages.Size(path);
            return Pixels.FromRgba(width, height, ProjectImages.ReadRgba(path, width, height));
        }

        private static void WriteGray8(string path, TiledBuffer<byte> buffer) =>
            ProjectImages.WriteGray8(path, buffer.Width, buffer.Height, buffer.ToArray());

        private static void WriteRgba(string path, TiledBuffer<Color32> image) =>
            ProjectImages.WriteRgba(path, image.Width, image.Height, Pixels.ToRgba(image));

        /// <summary>
        /// The files under a folder by their path in it, with <c>/</c> between folders.
        /// </summary>
        private static IEnumerable<(string Path, string File)> FilesIn(string folder) =>
            Directory.Exists(folder)
                ? Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Select(file => (Path.GetRelativePath(folder, file).Replace('\\', '/'), file)).ToList()
                : [];
    }
}
