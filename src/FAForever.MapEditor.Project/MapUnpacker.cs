using FAForever.FileFormats.Map;

namespace FAForever.MapEditor.Project
{
    /// <summary>
    /// Turns a map folder into a project (<see cref="MapProjectLayout"/>): the <c>.scmap</c> becomes
    /// JSON and PNG files, the other files of the folder are copied to <see cref="MapProjectLayout.MapFiles"/>.
    /// </summary>
    /// <remarks>
    /// The project has no version in its name: the version lives in the <c>_scenario.lua</c>
    /// (<c>map_version</c>) and <see cref="MapPacker"/> puts it back in the folder's name.
    /// </remarks>
    public static class MapUnpacker
    {
        /// <summary>
        /// Unpacks the map in <paramref name="mapFolder"/> into <paramref name="projectFolder"/>,
        /// which must be empty or absent.
        /// </summary>
        /// <exception cref="MapProjectException">The folder holds no map, or the project folder is not empty.</exception>
        public static void Unpack(string mapFolder, string projectFolder)
        {
            if (!Directory.Exists(mapFolder))
            {
                throw new MapProjectException($"The map folder {mapFolder} does not exist");
            }

            string[] scmaps = Directory.GetFiles(mapFolder, "*.scmap", SearchOption.TopDirectoryOnly);
            if (scmaps.Length != 1)
            {
                throw new MapProjectException($"The map folder {mapFolder} has {scmaps.Length} .scmap files, expected one");
            }

            if (Directory.Exists(projectFolder) && Directory.EnumerateFileSystemEntries(projectFolder).Any())
            {
                throw new MapProjectException($"The project folder {projectFolder} is not empty");
            }

            Scmap scmap;
            try
            {
                scmap = ScmapParser.Parse(File.ReadAllBytes(scmaps[0]));
            }
            catch (ScmapFormatException exception)
            {
                throw new MapProjectException($"{scmaps[0]}: {exception.Message}", exception);
            }

            UnpackScmap(scmap, projectFolder);

            string mapFiles = Path.Combine(projectFolder, MapProjectLayout.MapFiles);
            foreach (string file in Directory.EnumerateFiles(mapFolder, "*", SearchOption.AllDirectories))
            {
                if (Path.GetFullPath(file) == Path.GetFullPath(scmaps[0]))
                {
                    continue;
                }
                string target = Path.Combine(mapFiles, Path.GetRelativePath(mapFolder, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
        }

        /// <summary>
        /// Writes the files of a project that come from the <c>.scmap</c>.
        /// </summary>
        public static void UnpackScmap(Scmap scmap, string projectFolder)
        {
            if (scmap.NormalMaps.Count != 1)
            {
                throw new MapProjectException($"The map has {scmap.NormalMaps.Count} normal maps; projects hold one");
            }

            Directory.CreateDirectory(projectFolder);
            string Of(string file) => Path.Combine(projectFolder, file);

            (int normalWidth, int normalHeight, ScmapTextureFormat normalFormat, byte[] normal) = DdsTexture.Read(scmap.NormalMaps[0], "normal map");
            (int waterWidth, int waterHeight, ScmapTextureFormat waterFormat, byte[] water) = DdsTexture.Read(scmap.WaterMap, "water map");
            (int previewWidth, int previewHeight, _, byte[] preview) = DdsTexture.Read(scmap.Preview, "preview");

            MapProjectManifest manifest = new MapProjectManifest
            {
                ScmapVersion = scmap.Version,
                Width = scmap.Width,
                Height = scmap.Height,
                HeightScale = scmap.Heightmap.Scale,
                TerrainShader = scmap.TerrainShader,
                BackgroundTexture = scmap.BackgroundTexture,
                SkyCubemap = scmap.SkyCubemap,
                EnvironmentCubemaps = scmap.EnvironmentCubemaps,
                Lighting = scmap.Lighting,
                Water = scmap.Water,
                WaveGenerators = scmap.WaveGenerators,
                Minimap = scmap.Minimap,
                Tileset = scmap.Tileset,
                Layers = scmap.Layers,
                Skybox = scmap.Skybox,
                NormalMapFormat = normalFormat,
                WaterMapFormat = waterFormat,
            };
            MapProjectJson.Write(Of(MapProjectLayout.Manifest), manifest);
            MapProjectJson.Write(Of(MapProjectLayout.Props), scmap.Props);
            MapProjectJson.Write(Of(MapProjectLayout.Decals), new MapProjectDecals([scmap.DecalHeader.First, scmap.DecalHeader.Second], scmap.Decals, scmap.DecalGroups));

            ProjectImages.WriteGray16(Of(MapProjectLayout.Heightmap), scmap.Width + 1, scmap.Height + 1, scmap.Heightmap.Samples);
            ProjectImages.WriteGray8(Of(MapProjectLayout.TerrainTypes), scmap.Width, scmap.Height, scmap.TerrainTypes);
            ProjectImages.WriteRgba(Of(MapProjectLayout.NormalMap), normalWidth, normalHeight, normal);
            ProjectImages.WriteRgba(Of(MapProjectLayout.Preview), previewWidth, previewHeight, preview);
            ProjectImages.WriteRgba(Of(MapProjectLayout.WaterMap), waterWidth, waterHeight, water);

            int halfWidth = scmap.Width / 2;
            int halfHeight = scmap.Height / 2;
            ProjectImages.WriteGray8(Of(MapProjectLayout.WaterFoam), halfWidth, halfHeight, scmap.WaterFoamMask);
            ProjectImages.WriteGray8(Of(MapProjectLayout.WaterFlatness), halfWidth, halfHeight, scmap.WaterFlatnessMask);
            ProjectImages.WriteGray8(Of(MapProjectLayout.WaterDepthBias), halfWidth, halfHeight, scmap.WaterDepthBiasMask);

            // each mask holds four layers, one per channel: red, green, blue, alpha
            for (int mask = 0; mask < scmap.StratumMasks.Count; mask++)
            {
                (int width, int height, _, byte[] rgba) = DdsTexture.Read(scmap.StratumMasks[mask], $"stratum mask {mask + 1}");
                for (int channel = 0; channel < 4; channel++)
                {
                    byte[] layer = new byte[width * height];
                    for (int i = 0; i < layer.Length; i++)
                    {
                        layer[i] = rgba[i * 4 + channel];
                    }
                    ProjectImages.WriteGray8(Of(MapProjectLayout.StratumMask(1 + mask * 4 + channel)), width, height, layer);
                }
            }
        }
    }
}
