using System.Text;
using System.Text.RegularExpressions;
using FAForever.FileFormats.Lua;
using FAForever.FileFormats.Map;

namespace FAForever.MapEditor.Project
{
    /// <summary>
    /// Turns a project (<see cref="MapProjectLayout"/>) into a map folder the game reads: the
    /// <c>.scmap</c> with every image baked in, and the files of <see cref="MapProjectLayout.MapFiles"/>.
    /// </summary>
    /// <remarks>
    /// The folder is named after the map's <c>_scenario.lua</c>: the name from its <c>map</c> path and
    /// the version from its <c>map_version</c> (<see cref="MapFolderName"/>). Paths into the map's own
    /// folder, in the Lua files and in the <c>.scmap</c>, are pointed at that folder, whatever version
    /// they named before.
    /// </remarks>
    public static partial class MapPacker
    {
        /// <summary>
        /// Lua files are read and written as Latin-1, which keeps every byte: map files are not always UTF-8.
        /// </summary>
        private static readonly Encoding LuaEncoding = Encoding.Latin1;

        /// <summary>
        /// Packs the project into a map folder in <paramref name="outputFolder"/>, replacing the files
        /// it writes; other files in that folder are left alone.
        /// </summary>
        /// <param name="version">The version to give the map; when set, it replaces the
        /// <c>map_version</c> of the <c>_scenario.lua</c> in the packed map.</param>
        /// <returns>The map folder that was written.</returns>
        /// <exception cref="MapProjectException">A file of the project is missing or wrong.</exception>
        public static string Pack(string projectFolder, string outputFolder, int? version = null)
        {
            string mapFiles = Path.Combine(projectFolder, MapProjectLayout.MapFiles);
            string scenarioPath = FindScenario(mapFiles);
            string scenarioText = File.ReadAllText(scenarioPath, LuaEncoding);
            MapScenario scenario;
            try
            {
                scenario = MapScenarioParser.Parse(scenarioText);
            }
            catch (Exception exception) when (exception is FormatException or LuaSyntaxException)
            {
                throw new MapProjectException($"{scenarioPath}: {exception.Message}", exception);
            }

            if (scenario.Map is not string mapPath || mapPath.Split('/', StringSplitOptions.RemoveEmptyEntries) is not [.., string folder, string scmapFile])
            {
                throw new MapProjectException($"{scenarioPath} names no map file (map = '/maps/<folder>/<name>.scmap')");
            }
            if (version is not null && scenario.MapVersion is null)
            {
                throw new MapProjectException($"{scenarioPath} has no map_version to change; add one");
            }

            MapFolderName name = MapFolderName.Parse(folder) with { Version = version ?? scenario.MapVersion };
            Scmap scmap = RewritePaths(BuildScmap(projectFolder), name.RewritePaths);

            string target = Path.Combine(outputFolder, name.ToString());
            Directory.CreateDirectory(target);
            WriteReplacing(Path.Combine(target, scmapFile), ScmapWriter.Write(scmap));

            foreach (string file in Directory.EnumerateFiles(mapFiles, "*", SearchOption.AllDirectories))
            {
                string destination = Path.Combine(target, Path.GetRelativePath(mapFiles, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                if (!file.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(file, destination, overwrite: true);
                    continue;
                }

                string text = name.RewritePaths(File.ReadAllText(file, LuaEncoding));
                if (version is not null && file == scenarioPath)
                {
                    text = MapVersion().Replace(text, $"${{1}}{version}", 1);
                }
                WriteReplacing(destination, LuaEncoding.GetBytes(text));
            }

            return target;
        }

        /// <summary>
        /// Reads the files of a project that make up the <c>.scmap</c>.
        /// </summary>
        /// <exception cref="MapProjectException">A file is missing or does not fit the map's size.</exception>
        public static Scmap BuildScmap(string projectFolder)
        {
            string Of(string file) => Path.Combine(projectFolder, file);

            MapProjectManifest manifest = MapProjectJson.Read<MapProjectManifest>(Of(MapProjectLayout.Manifest));
            if (manifest.FormatVersion > MapProjectManifest.CurrentFormatVersion)
            {
                throw new MapProjectException($"{MapProjectLayout.Manifest} is of format {manifest.FormatVersion}; this tool reads up to {MapProjectManifest.CurrentFormatVersion}");
            }
            IReadOnlyList<ScmapProp> props = MapProjectJson.Read<List<ScmapProp>>(Of(MapProjectLayout.Props));
            MapProjectDecals decals = MapProjectJson.Read<MapProjectDecals>(Of(MapProjectLayout.Decals));
            if (decals.Header.Count != 2)
            {
                throw new MapProjectException($"{MapProjectLayout.Decals}: the header holds two numbers");
            }

            int width = manifest.Width;
            int height = manifest.Height;
            ScmapHeightmap heightmap = new ScmapHeightmap(width, height, manifest.HeightScale, ReadHeightmap(projectFolder, width, height));

            (int normalWidth, int normalHeight) = SizeOf(Of(MapProjectLayout.NormalMap));
            byte[] normalMap = DdsTexture.Write(normalWidth, normalHeight, manifest.NormalMapFormat, ProjectImages.ReadRgba(Of(MapProjectLayout.NormalMap), normalWidth, normalHeight));
            (int waterWidth, int waterHeight) = SizeOf(Of(MapProjectLayout.WaterMap));
            byte[] waterMap = DdsTexture.Write(waterWidth, waterHeight, manifest.WaterMapFormat, ProjectImages.ReadRgba(Of(MapProjectLayout.WaterMap), waterWidth, waterHeight));
            byte[] preview = DdsTexture.Write(256, 256, ScmapTextureFormat.A8R8G8B8, ProjectImages.ReadRgba(Of(MapProjectLayout.Preview), 256, 256));

            int halfWidth = width / 2;
            int halfHeight = height / 2;

            return new Scmap
            {
                Version = manifest.ScmapVersion,
                Preview = preview,
                Heightmap = heightmap,
                TerrainShader = manifest.TerrainShader,
                BackgroundTexture = manifest.BackgroundTexture,
                SkyCubemap = manifest.SkyCubemap,
                EnvironmentCubemaps = manifest.EnvironmentCubemaps,
                Lighting = manifest.Lighting,
                Water = manifest.Water,
                WaveGenerators = manifest.WaveGenerators,
                Minimap = manifest.Minimap,
                Tileset = manifest.Tileset,
                Layers = manifest.Layers,
                DecalHeader = (decals.Header[0], decals.Header[1]),
                Decals = decals.Decals,
                DecalGroups = decals.Groups,
                NormalMaps = [normalMap],
                StratumMasks = ReadStratumMasks(projectFolder, manifest.ScmapVersion >= 56 ? 2 : 1),
                WaterMap = waterMap,
                WaterFoamMask = ProjectImages.ReadGray8(Of(MapProjectLayout.WaterFoam), halfWidth, halfHeight),
                WaterFlatnessMask = ProjectImages.ReadGray8(Of(MapProjectLayout.WaterFlatness), halfWidth, halfHeight),
                WaterDepthBiasMask = ProjectImages.ReadGray8(Of(MapProjectLayout.WaterDepthBias), halfWidth, halfHeight),
                TerrainTypes = ProjectImages.ReadGray8(Of(MapProjectLayout.TerrainTypes), width, height),
                Skybox = manifest.Skybox,
                Props = props,
            };
        }

        /// <summary>
        /// Applies <paramref name="rewrite"/> to every path in the map: textures, cubemaps, prop blueprints.
        /// </summary>
        public static Scmap RewritePaths(Scmap scmap, Func<string, string> rewrite) => scmap with
        {
            BackgroundTexture = rewrite(scmap.BackgroundTexture),
            SkyCubemap = rewrite(scmap.SkyCubemap),
            EnvironmentCubemaps = [.. scmap.EnvironmentCubemaps.Select(c => c with { Texture = rewrite(c.Texture) })],
            Water = scmap.Water with
            {
                Cubemap = rewrite(scmap.Water.Cubemap),
                WaterRamp = rewrite(scmap.Water.WaterRamp),
                WaveTextures = [.. scmap.Water.WaveTextures.Select(w => w with { Texture = rewrite(w.Texture) })],
            },
            WaveGenerators = [.. scmap.WaveGenerators.Select(g => g with { Texture = rewrite(g.Texture), Ramp = rewrite(g.Ramp) })],
            Layers = [.. scmap.Layers.Select(l => l with { Albedo = rewrite(l.Albedo), Normal = rewrite(l.Normal) })],
            Decals = [.. scmap.Decals.Select(d => d with { Textures = [.. d.Textures.Select(rewrite)] })],
            Skybox = scmap.Skybox is ScmapSkybox skybox
                ? skybox with { Albedo = rewrite(skybox.Albedo), Glow = rewrite(skybox.Glow), CirrusTexture = rewrite(skybox.CirrusTexture) }
                : null,
            Props = [.. scmap.Props.Select(p => p with { Blueprint = rewrite(p.Blueprint) })],
        };

        private static string FindScenario(string mapFiles)
        {
            string[] scenarios = Directory.Exists(mapFiles)
                ? Directory.GetFiles(mapFiles, "*_scenario.lua", SearchOption.TopDirectoryOnly)
                : [];
            return scenarios.Length == 1
                ? scenarios[0]
                : throw new MapProjectException($"{mapFiles} has {scenarios.Length} _scenario.lua files, expected one");
        }

        private static ushort[] ReadHeightmap(string projectFolder, int width, int height)
        {
            string png = Path.Combine(projectFolder, MapProjectLayout.Heightmap);
            if (File.Exists(png))
            {
                return ProjectImages.ReadGray16(png, width + 1, height + 1);
            }

            string raw = Path.Combine(projectFolder, MapProjectLayout.HeightmapRaw);
            if (!File.Exists(raw))
            {
                throw new MapProjectException($"The project has no {MapProjectLayout.Heightmap} or {MapProjectLayout.HeightmapRaw}");
            }
            byte[] bytes = File.ReadAllBytes(raw);
            int expected = (width + 1) * (height + 1) * 2;
            if (bytes.Length != expected)
            {
                throw new MapProjectException($"{MapProjectLayout.HeightmapRaw} has {bytes.Length} bytes, expected {expected}: {width + 1} by {height + 1} samples of 16 bits");
            }
            ushort[] samples = new ushort[(width + 1) * (height + 1)];
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] = (ushort)(bytes[i * 2] | bytes[i * 2 + 1] << 8);
            }
            return samples;
        }

        /// <summary>
        /// The masks of the layers, four per texture. A missing mask leaves its layer unused; the
        /// masks that exist must all have the same size.
        /// </summary>
        private static IReadOnlyList<byte[]> ReadStratumMasks(string projectFolder, int textures)
        {
            string[] paths = Enumerable.Range(1, textures * 4).Select(layer => Path.Combine(projectFolder, MapProjectLayout.StratumMask(layer))).ToArray();
            string? first = paths.FirstOrDefault(File.Exists)
                ?? throw new MapProjectException($"The project has no stratum masks in {MapProjectLayout.StratumMasks}/");
            (int width, int height) = ProjectImages.Size(first);

            byte[][] masks = new byte[textures][];
            for (int texture = 0; texture < textures; texture++)
            {
                byte[] rgba = new byte[width * height * 4];
                for (int channel = 0; channel < 4; channel++)
                {
                    string path = paths[texture * 4 + channel];
                    if (!File.Exists(path))
                    {
                        continue;
                    }
                    byte[] layer = ProjectImages.ReadGray8(path, width, height);
                    for (int i = 0; i < layer.Length; i++)
                    {
                        rgba[i * 4 + channel] = layer[i];
                    }
                }
                masks[texture] = DdsTexture.Write(width, height, ScmapTextureFormat.A8R8G8B8, rgba);
            }
            return masks;
        }

        private static (int Width, int Height) SizeOf(string path) =>
            File.Exists(path) ? ProjectImages.Size(path) : throw new MapProjectException($"{path} is missing");

        /// <summary>
        /// Writes to a file next to the target, then moves it in place: the game or a file watcher never
        /// sees half a file.
        /// </summary>
        private static void WriteReplacing(string path, byte[] bytes)
        {
            string temporary = path + ".tmp";
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, overwrite: true);
        }

        [GeneratedRegex(@"(map_version\s*=\s*)\d+")]
        private static partial Regex MapVersion();
    }
}
