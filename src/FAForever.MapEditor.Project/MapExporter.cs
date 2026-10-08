using System.Globalization;
using System.Runtime.InteropServices;
using FAForever.FileFormats.Lua;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project
{
    /// <summary>
    /// The files of a map as the game reads them, made from a document by <see cref="MapExporter.Export"/>.
    /// </summary>
    public sealed record MapGameFiles
    {
        /// <summary>
        /// The name of the map's folder, e.g. <c>theta_passage.v0004</c>.
        /// </summary>
        public required MapFolderName Folder { get; init; }

        /// <summary>
        /// The name the map's files start with.
        /// </summary>
        public required string FileName { get; init; }

        public required Scmap Scmap { get; init; }

        public required MapSave Save { get; init; }

        public required MapScenario Scenario { get; init; }

        /// <summary>
        /// The <c>_script.lua</c>, if the map has one.
        /// </summary>
        public byte[]? Script { get; init; }

        /// <summary>
        /// The other files of the folder, by their path in it.
        /// </summary>
        public required IReadOnlyDictionary<string, byte[]> Files { get; init; }
    }

    /// <summary>
    /// Makes the game's files of a map from a <see cref="MapDocument"/>, the reverse of
    /// <see cref="MapImporter"/>.
    /// </summary>
    /// <remarks>
    /// The scenario names the map's files after the document (<c>/maps/&lt;folder&gt;/&lt;file&gt;.scmap</c>,
    /// ...) and gets its size from the heightmap; markers get the decoration of their type
    /// (<see cref="MarkerTranslation"/>). When the files are written (<see cref="ToBytes"/>,
    /// <see cref="WriteFolder"/>), every path into the map's own folder, under whatever version it was
    /// written, is pointed at the folder of this version: in the Lua files and in the textures and
    /// props of the <c>.scmap</c>.
    /// </remarks>
    public static class MapExporter
    {
        public static MapGameFiles Export(MapDocument document)
        {
            MapFolderName folder = document.Folder;
            string root = $"/maps/{folder}/{document.FileName}";

            return new MapGameFiles
            {
                Folder = folder,
                FileName = document.FileName,
                Scmap = ToScmap(document),
                Save = new MapSave
                {
                    Raw = new LuaData.Table([]),
                    Markers = [.. MarkerTranslation.ToMapMarkers(document.Markers)],
                    Areas = [.. document.Markers.Areas.Select(a => new MapArea(a.Name, a.X0, a.Z0, a.X1, a.Z1))],
                    Chains = [.. document.Markers.Chains.Select(c => new MapChain(c.Name, [.. c.Markers]))],
                    Armies = [.. document.Armies.Select(ToMapArmy)],
                },
                Scenario = ToMapScenario(document, root),
                Script = document.Script is { } script ? ImmutableCollectionsMarshal.AsArray(script) : null,
                Files = document.Files.ToDictionary(entry => entry.Key, entry => ImmutableCollectionsMarshal.AsArray(entry.Value)!),
            };
        }

        /// <summary>
        /// The files of the map folder by their path in it, with the paths in them pointed at the folder.
        /// </summary>
        /// <exception cref="ArgumentException">The document does not make a valid map (see <see cref="ScmapWriter.Write(Scmap)"/>).</exception>
        public static IReadOnlyDictionary<string, byte[]> ToBytes(MapDocument document)
        {
            MapGameFiles game = Export(document);
            Func<string, string> rewrite = game.Folder.RewritePaths;

            Dictionary<string, byte[]> files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach ((string path, byte[] bytes) in game.Files)
            {
                files[path] = path.EndsWith(".lua", StringComparison.OrdinalIgnoreCase) ? RewriteLua(bytes, rewrite) : bytes;
            }
            files[$"{game.FileName}.scmap"] = ScmapWriter.Write(RewritePaths(game.Scmap, rewrite));
            files[$"{game.FileName}_save.lua"] = MapImporter.LuaEncoding.GetBytes(rewrite(MapSaveWriter.Write(game.Save)));
            files[$"{game.FileName}_scenario.lua"] = MapImporter.LuaEncoding.GetBytes(rewrite(MapScenarioWriter.Write(game.Scenario)));
            if (game.Script is byte[] script)
            {
                files[$"{game.FileName}_script.lua"] = RewriteLua(script, rewrite);
            }
            return files;
        }

        /// <summary>
        /// Writes the map folder into <paramref name="outputFolder"/>, e.g. the game's <c>maps</c>
        /// folder, replacing the files it writes; other files in the map folder are left alone. Each
        /// file is written next to its place and then moved there, so that the game or a file
        /// watcher never reads half a file.
        /// </summary>
        /// <returns>The map folder.</returns>
        public static string WriteFolder(MapDocument document, string outputFolder)
        {
            IReadOnlyDictionary<string, byte[]> files = ToBytes(document);
            string target = Path.Combine(outputFolder, document.Folder.ToString());
            foreach ((string path, byte[] bytes) in files)
            {
                string destination = Path.Combine(target, path);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                string temporary = destination + ".tmp";
                File.WriteAllBytes(temporary, bytes);
                File.Move(temporary, destination, overwrite: true);
            }
            return target;
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

        private static Scmap ToScmap(MapDocument d)
        {
            Terrain terrain = d.Terrain;
            Water water = d.Water;
            Lighting l = d.Lighting;
            return new Scmap
            {
                Version = terrain.ScmapVersion,
                Preview = Texture(terrain.Preview, TextureFormat.A8R8G8B8),
                Heightmap = new ScmapHeightmap(terrain.Heightmap.Width, terrain.Heightmap.Height, terrain.Heightmap.Scale, terrain.Heightmap.Samples.ToArray()),
                TerrainShader = terrain.Shader,
                BackgroundTexture = d.Sky.Background,
                SkyCubemap = d.Sky.Cubemap,
                EnvironmentCubemaps = [.. d.Sky.EnvironmentCubemaps.Select(c => new ScmapCubemap(c.Name, c.Texture))],
                Lighting = new ScmapLighting(l.Multiplier, l.SunDirection, l.SunAmbience, l.SunColor, l.ShadowFillColor, l.SpecularColor, l.Bloom, l.FogColor, l.FogStart, l.FogEnd),
                Water = new ScmapWater
                {
                    HasWater = water.HasWater,
                    Elevation = water.Elevation,
                    ElevationDeep = water.ElevationDeep,
                    ElevationAbyss = water.ElevationAbyss,
                    SurfaceColor = water.SurfaceColor,
                    ColorLerp = water.ColorLerp,
                    RefractionScale = water.RefractionScale,
                    FresnelBias = water.FresnelBias,
                    FresnelPower = water.FresnelPower,
                    UnitReflection = water.UnitReflection,
                    SkyReflection = water.SkyReflection,
                    SunShininess = water.SunShininess,
                    SunStrength = water.SunStrength,
                    SunDirection = water.SunDirection,
                    SunColor = water.SunColor,
                    SunReflection = water.SunReflection,
                    SunGlow = water.SunGlow,
                    Cubemap = water.Cubemap,
                    WaterRamp = water.Ramp,
                    WaveTextures = [.. water.WaveTextures.Select(t => new ScmapWaveTexture(t.NormalRepeat, t.NormalMovement, t.Texture))],
                },
                WaveGenerators = [.. water.WaveGenerators.Select(g => new ScmapWaveGenerator(
                    g.Texture, g.Ramp, g.Position, g.Rotation, g.Velocity, g.LifetimeFirst, g.LifetimeSecond, g.PeriodFirst, g.PeriodSecond,
                    g.ScaleFirst, g.ScaleSecond, g.FrameCount, g.FrameRateFirst, g.FrameRateSecond, g.StripCount))],
                Minimap = d.Minimap is Minimap m
                    ? new ScmapMinimap(m.ContourInterval, m.DeepWaterColor.ToArgb(), m.ContourColor.ToArgb(), m.ShoreColor.ToArgb(), m.LandStartColor.ToArgb(), m.LandEndColor.ToArgb(), m.Unknown)
                    : null,
                Tileset = terrain.Tileset,
                Layers = [.. terrain.Layers.Select(x => new ScmapTerrainLayer(x.AlbedoPath, x.AlbedoScale, x.NormalPath, x.NormalScale))],
                DecalHeader = (d.DecalHeader.First, d.DecalHeader.Second),
                Decals = [.. d.Decals.Select(x => new ScmapDecal(x.Id, (int)x.Type, [.. x.Textures], x.Scale, x.Position, x.Rotation, x.CutOffLod, x.NearCutOffLod, x.OwnerArmy))],
                DecalGroups = [.. d.DecalGroups.Select(g => new ScmapDecalGroup(g.Id, g.Name, [.. g.DecalIds]))],
                NormalMaps = [Texture(terrain.NormalMap, terrain.NormalMapFormat)],
                StratumMasks = JoinMasks(terrain.StratumMasks),
                WaterMap = Texture(water.Map, water.MapFormat),
                WaterFoamMask = water.FoamMask.ToArray(),
                WaterFlatnessMask = water.FlatnessMask.ToArray(),
                WaterDepthBiasMask = water.DepthBiasMask.ToArray(),
                TerrainTypes = terrain.Types.ToArray(),
                Skybox = d.Sky.Skybox is Skybox s ? ToScmapSkybox(s) : null,
                Props = [.. d.Props.Select(p => new ScmapProp(p.Blueprint, p.Position, p.RotationX, p.RotationY, p.RotationZ, p.Scale))],
            };
        }

        private static ScmapSkybox ToScmapSkybox(Skybox s) => new ScmapSkybox
        {
            Position = s.Position,
            HorizonHeight = s.HorizonHeight,
            Scale = s.Scale,
            SubtractHeight = s.SubtractHeight,
            SubdivisionsAxis = s.SubdivisionsAxis,
            SubdivisionsHeight = s.SubdivisionsHeight,
            ZenithHeight = s.ZenithHeight,
            HorizonColor = s.HorizonColor,
            ZenithColor = s.ZenithColor,
            DecalGlowMultiplier = s.DecalGlowMultiplier,
            Albedo = s.Albedo,
            Glow = s.Glow,
            Planets = [.. s.Planets.Select(p => new ScmapPlanet(p.Position, p.Rotation, p.Scale, p.Uv))],
            MidColor = (s.MidColor.Red, s.MidColor.Green, s.MidColor.Blue),
            CirrusMultiplier = s.CirrusMultiplier,
            CirrusColor = s.CirrusColor,
            CirrusTexture = s.CirrusTexture,
            CirrusLayers = [.. s.CirrusLayers.Select(c => new ScmapCirrusLayer(c.Frequency, c.Speed, c.Direction))],
            Clouds7 = s.Clouds7,
        };

        private static MapScenario ToMapScenario(MapDocument d, string root)
        {
            ScenarioInfo s = d.Scenario;
            Dictionary<string, LuaData> extra = new Dictionary<string, LuaData>();
            if (s.Author is string author)
            {
                extra["author"] = new LuaData.String(author);
            }
            foreach ((string army, NoRushOffset offset) in s.NoRushOffsets)
            {
                if (offset.X is double x)
                {
                    extra[$"norushoffsetX_{army}"] = new LuaData.Number(x);
                }
                if (offset.Y is double y)
                {
                    extra[$"norushoffsetY_{army}"] = new LuaData.Number(y);
                }
            }

            return new MapScenario
            {
                Raw = new LuaData.Table(extra),
                Name = s.Name,
                Description = s.Description,
                Type = s.Type,
                Starts = s.Starts,
                Preview = s.PreviewPath,
                Size = (d.Width, d.Height),
                Map = root + ".scmap",
                Save = root + "_save.lua",
                Script = root + "_script.lua",
                MapVersion = d.Version,
                NoRushRadius = s.NoRushRadius,
                Reclaim = s.Reclaim is Reclaim r ? (r.Mass, r.Energy) : null,
                AdaptiveMap = s.AdaptiveMap,
                Configurations = [.. s.Configurations.Select(c => new MapConfiguration(
                    c.Name,
                    [.. c.Teams.Select(t => new MapTeam(t.Name, [.. t.Armies]))],
                    new Dictionary<string, string>(c.CustomProperties),
                    new LuaData.Table(c.Factions is null ? [] : new Dictionary<string, LuaData> { ["factions"] = List(c.Factions.Value.Select(army => List(army.Select(f => new LuaData.String(f))))) })))],
            };
        }

        private static MapArmy ToMapArmy(Army a) => new MapArmy(
            a.Name,
            a.Personality,
            a.Plans,
            a.Color,
            a.Faction,
            a.Economy is Economy e ? new MapEconomy(e.Mass, e.Energy) : null,
            new Dictionary<string, string>(a.Alliances),
            ToMapGroup(a.Units),
            new LuaData.Table([]));

        /// <summary>
        /// A group of the save file; the orders and platoon of groups and units are empty, as the
        /// map editors write them.
        /// </summary>
        private static MapUnitGroup ToMapGroup(UnitGroup g) => new MapUnitGroup(
            g.Name,
            "",
            "",
            [.. g.Groups.Select(ToMapGroup)],
            [.. g.Units.Select(u => new MapUnit(u.Name, u.Blueprint, u.Position, u.Orientation, "", ""))]);

        private static byte[] Texture(TiledBuffer<Color32> image, TextureFormat format) =>
            DdsTexture.Write(image.Width, image.Height, format, Pixels.ToRgba(image));

        /// <summary>
        /// A Lua list: a table of "1" to "n".
        /// </summary>
        private static LuaData.Table List(IEnumerable<LuaData> values) =>
            new LuaData.Table(values.Select((value, index) => (value, index)).ToDictionary(entry => (entry.index + 1).ToString(CultureInfo.InvariantCulture), entry => entry.value));

        private static byte[] RewriteLua(byte[] bytes, Func<string, string> rewrite) =>
            MapImporter.LuaEncoding.GetBytes(rewrite(MapImporter.LuaEncoding.GetString(bytes)));

        /// <summary>
        /// Four layers per texture, one per channel: red, green, blue, alpha.
        /// </summary>
        private static IReadOnlyList<byte[]> JoinMasks(StratumMasks masks)
        {
            int pixels = masks.Width * masks.Height;
            List<byte[]> textures = new List<byte[]>();
            for (int texture = 0; texture < masks.Layers.Length / 4; texture++)
            {
                byte[] rgba = new byte[pixels * 4];
                for (int channel = 0; channel < 4; channel++)
                {
                    byte[] layer = masks.Layers[texture * 4 + channel].ToArray();
                    for (int i = 0; i < pixels; i++)
                    {
                        rgba[i * 4 + channel] = layer[i];
                    }
                }
                textures.Add(DdsTexture.Write(masks.Width, masks.Height, TextureFormat.A8R8G8B8, rgba));
            }
            return textures;
        }
    }
}
