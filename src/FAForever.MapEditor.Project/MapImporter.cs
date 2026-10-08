using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using FAForever.FileFormats.Lua;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project
{
    /// <summary>
    /// Makes a <see cref="MapDocument"/> from the files of a map: the folder the game and the vault
    /// hold (<c>maps/theta_passage.v0004/</c>), or the parsed files themselves. The reverse is
    /// <see cref="MapExporter"/>.
    /// </summary>
    public static partial class MapImporter
    {
        /// <summary>
        /// Lua files are read as Latin-1, which keeps every byte: map files are not always UTF-8.
        /// </summary>
        internal static readonly Encoding LuaEncoding = Encoding.Latin1;

        /// <summary>
        /// Reads a map folder. The scenario names the save and the script; the folder's name gives
        /// the map's name, and its version when the scenario has none.
        /// </summary>
        /// <exception cref="MapProjectException">The folder is not a map, or one of its files cannot be read.</exception>
        public static MapDocument ImportFolder(string folder)
        {
            if (!Directory.Exists(folder))
            {
                throw new MapProjectException($"The map folder {folder} does not exist");
            }

            string scmapPath = Single(folder, "*.scmap");
            string scenarioPath = Single(folder, "*_scenario.lua");
            MapScenario scenario = Parse(scenarioPath, MapScenarioParser.Parse);

            string fileName = Path.GetFileNameWithoutExtension(scmapPath);
            string savePath = FindNamed(folder, scenario.Save, $"{fileName}_save.lua")
                ?? throw new MapProjectException($"{folder} has no save file ({Path.GetFileName(scenario.Save) ?? $"{fileName}_save.lua"})");
            string? scriptPath = FindNamed(folder, scenario.Script, $"{fileName}_script.lua");

            Scmap scmap;
            try
            {
                scmap = ScmapParser.Parse(File.ReadAllBytes(scmapPath));
            }
            catch (ScmapFormatException exception)
            {
                throw new MapProjectException($"{scmapPath}: {exception.Message}", exception);
            }
            MapSave save = Parse(savePath, MapSaveParser.Parse);

            string[] known = [scmapPath, scenarioPath, savePath, scriptPath ?? ""];
            Dictionary<string, byte[]> files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Where(file => !known.Contains(file))
                .ToDictionary(file => Path.GetRelativePath(folder, file).Replace('\\', '/'), File.ReadAllBytes);

            MapFolderName folderName = MapFolderName.Parse(Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder))));
            MapDocument document = Import(scmap, save, scenario, folderName.Name, fileName, scriptPath is null ? null : File.ReadAllBytes(scriptPath), files);
            return document.Version is null ? document with { Version = folderName.Version } : document;
        }

        /// <summary>
        /// Makes a document from the parsed files of a map.
        /// </summary>
        /// <param name="name">The name of the map's folder without its version.</param>
        /// <param name="fileName">The name the map's files start with.</param>
        /// <param name="script">The <c>_script.lua</c>, if the map has one.</param>
        /// <param name="files">The other files of the folder, by their path in it.</param>
        public static MapDocument Import(
            Scmap scmap,
            MapSave save,
            MapScenario scenario,
            string name,
            string fileName,
            byte[]? script = null,
            IReadOnlyDictionary<string, byte[]>? files = null)
        {
            return new MapDocument
            {
                Name = name,
                FileName = fileName,
                Version = scenario.MapVersion,
                Scenario = ToScenario(scenario),
                Terrain = ToTerrain(scmap),
                Lighting = ToLighting(scmap.Lighting),
                Water = ToWater(scmap),
                Sky = new Sky
                {
                    Background = scmap.BackgroundTexture,
                    Cubemap = scmap.SkyCubemap,
                    EnvironmentCubemaps = [.. scmap.EnvironmentCubemaps.Select(c => new Cubemap { Name = c.Name, Texture = c.Texture })],
                    Skybox = scmap.Skybox is ScmapSkybox skybox ? ToSkybox(skybox) : null,
                },
                Minimap = scmap.Minimap is ScmapMinimap m
                    ? new Minimap
                    {
                        ContourInterval = m.ContourInterval,
                        DeepWaterColor = Color32.FromArgb(m.DeepWaterColor),
                        ContourColor = Color32.FromArgb(m.ContourColor),
                        ShoreColor = Color32.FromArgb(m.ShoreColor),
                        LandStartColor = Color32.FromArgb(m.LandStartColor),
                        LandEndColor = Color32.FromArgb(m.LandEndColor),
                        Unknown = m.Unknown,
                    }
                    : null,
                DecalHeader = new DecalHeader { First = scmap.DecalHeader.First, Second = scmap.DecalHeader.Second },
                Decals = [.. scmap.Decals.Select(d => new Decal
                {
                    Id = d.Id,
                    Type = (DecalType)d.Type,
                    Textures = [.. d.Textures],
                    Scale = d.Scale,
                    Position = d.Position,
                    Rotation = d.Rotation,
                    CutOffLod = d.CutOffLod,
                    NearCutOffLod = d.NearCutOffLod,
                    OwnerArmy = d.OwnerArmy,
                })],
                DecalGroups = [.. scmap.DecalGroups.Select(g => new DecalGroup { Id = g.Id, Name = g.Name, DecalIds = [.. g.DecalIds] })],
                Props = [.. scmap.Props.Select((p, index) => new Prop
                {
                    Id = index + 1,
                    Blueprint = p.Blueprint,
                    Position = p.Position,
                    RotationX = p.RotationX,
                    RotationY = p.RotationY,
                    RotationZ = p.RotationZ,
                    Scale = p.Scale,
                })],
                Markers = MarkerTranslation.ToMarkerSet(save.Markers, save.Areas, save.Chains),
                Armies = [.. save.Armies.Select(ToArmy)],
                Script = script is null ? null : ImmutableCollectionsMarshal.AsImmutableArray(script),
                Files = (files ?? new Dictionary<string, byte[]>()).ToImmutableSortedDictionary(
                    entry => entry.Key,
                    entry => ImmutableCollectionsMarshal.AsImmutableArray(entry.Value),
                    StringComparer.Ordinal),
            };
        }

        private static ScenarioInfo ToScenario(MapScenario scenario)
        {
            LuaTableReader raw = new LuaTableReader(scenario.Raw);
            Dictionary<string, NoRushOffset> offsets = new Dictionary<string, NoRushOffset>();
            foreach ((string key, LuaData value) in scenario.Raw.Value)
            {
                if (NoRushOffsetKey().Match(key) is { Success: true } match && value is LuaData.Number number)
                {
                    string army = match.Groups[2].Value;
                    NoRushOffset offset = offsets.GetValueOrDefault(army) ?? new NoRushOffset();
                    offsets[army] = match.Groups[1].Value == "X" ? offset with { X = number.Value } : offset with { Y = number.Value };
                }
            }

            return new ScenarioInfo
            {
                Name = scenario.Name,
                Description = scenario.Description,
                Author = raw.String("author"),
                Type = scenario.Type,
                Starts = scenario.Starts,
                PreviewPath = scenario.Preview,
                NoRushRadius = scenario.NoRushRadius,
                NoRushOffsets = offsets.ToImmutableSortedDictionary(StringComparer.Ordinal),
                AdaptiveMap = scenario.AdaptiveMap,
                Reclaim = scenario.Reclaim is (double mass, double energy) ? new Reclaim { Mass = mass, Energy = energy } : null,
                Configurations = [.. scenario.Configurations.Select(c => new Configuration
                {
                    Name = c.Name,
                    Teams = [.. c.Teams.Select(t => new Team { Name = t.Name, Armies = [.. t.Armies] })],
                    CustomProperties = c.CustomProperties.ToImmutableSortedDictionary(StringComparer.Ordinal),
                    Factions = ToFactions(c.Raw),
                })],
            };
        }

        /// <summary>
        /// The factions each army may play (<c>factions = { { 'cybran' }, { 'uef', 'aeon' }, ... }</c>).
        /// </summary>
        private static ImmutableArray<ImmutableArray<string>>? ToFactions(LuaData.Table configuration) =>
            configuration.TryGetTableValue("factions", out LuaData.Table? factions)
                ? ImmutableArray.CreateRange(Positional(factions!).Select(army => army is LuaData.Table table
                    ? Positional(table).OfType<LuaData.String>().Select(faction => faction.Value).ToImmutableArray()
                    : []))
                : null;

        private static IEnumerable<LuaData> Positional(LuaData.Table table)
        {
            for (int i = 1; table.Value.TryGetValue(i.ToString(System.Globalization.CultureInfo.InvariantCulture), out LuaData? value); i++)
            {
                yield return value;
            }
        }

        private static Terrain ToTerrain(Scmap scmap)
        {
            if (scmap.NormalMaps.Count != 1)
            {
                throw new MapProjectException($"The map has {scmap.NormalMaps.Count} normal maps; the editor holds one");
            }
            (int normalWidth, int normalHeight, TextureFormat normalFormat, byte[] normal) = DdsTexture.Read(scmap.NormalMaps[0], "normal map");
            (int previewWidth, int previewHeight, _, byte[] preview) = DdsTexture.Read(scmap.Preview, "preview");

            return new Terrain
            {
                ScmapVersion = scmap.Version,
                Heightmap = new Heightmap
                {
                    Scale = scmap.Heightmap.Scale,
                    Samples = TiledBuffer<ushort>.FromArray(scmap.Width + 1, scmap.Height + 1, scmap.Heightmap.Samples),
                },
                Types = TiledBuffer<byte>.FromArray(scmap.Width, scmap.Height, scmap.TerrainTypes),
                Shader = scmap.TerrainShader,
                Tileset = scmap.Tileset,
                Layers = [.. scmap.Layers.Select(l => new TerrainLayer { AlbedoPath = l.Albedo, AlbedoScale = l.AlbedoScale, NormalPath = l.Normal, NormalScale = l.NormalScale })],
                StratumMasks = SplitMasks(scmap.StratumMasks),
                NormalMap = Pixels.FromRgba(normalWidth, normalHeight, normal),
                NormalMapFormat = normalFormat,
                Preview = Pixels.FromRgba(previewWidth, previewHeight, preview),
            };
        }

        private static Lighting ToLighting(ScmapLighting l) => new Lighting
        {
            Multiplier = l.LightingMultiplier,
            SunDirection = l.SunDirection,
            SunAmbience = l.SunAmbience,
            SunColor = l.SunColor,
            ShadowFillColor = l.ShadowFillColor,
            SpecularColor = l.SpecularColor,
            Bloom = l.Bloom,
            FogColor = l.FogColor,
            FogStart = l.FogStart,
            FogEnd = l.FogEnd,
        };

        private static Water ToWater(Scmap scmap)
        {
            ScmapWater w = scmap.Water;
            (int width, int height, TextureFormat format, byte[] rgba) = DdsTexture.Read(scmap.WaterMap, "water map");
            return new Water
            {
                HasWater = w.HasWater,
                Elevation = w.Elevation,
                ElevationDeep = w.ElevationDeep,
                ElevationAbyss = w.ElevationAbyss,
                SurfaceColor = w.SurfaceColor,
                ColorLerp = w.ColorLerp,
                RefractionScale = w.RefractionScale,
                FresnelBias = w.FresnelBias,
                FresnelPower = w.FresnelPower,
                UnitReflection = w.UnitReflection,
                SkyReflection = w.SkyReflection,
                SunShininess = w.SunShininess,
                SunStrength = w.SunStrength,
                SunDirection = w.SunDirection,
                SunColor = w.SunColor,
                SunReflection = w.SunReflection,
                SunGlow = w.SunGlow,
                Cubemap = w.Cubemap,
                Ramp = w.WaterRamp,
                WaveTextures = [.. w.WaveTextures.Select(t => new WaveTexture { Texture = t.Texture, NormalRepeat = t.NormalRepeat, NormalMovement = t.NormalMovement })],
                WaveGenerators = [.. scmap.WaveGenerators.Select(g => new WaveGenerator
                {
                    Texture = g.Texture,
                    Ramp = g.Ramp,
                    Position = g.Position,
                    Rotation = g.Rotation,
                    Velocity = g.Velocity,
                    LifetimeFirst = g.LifetimeFirst,
                    LifetimeSecond = g.LifetimeSecond,
                    PeriodFirst = g.PeriodFirst,
                    PeriodSecond = g.PeriodSecond,
                    ScaleFirst = g.ScaleFirst,
                    ScaleSecond = g.ScaleSecond,
                    FrameCount = g.FrameCount,
                    FrameRateFirst = g.FrameRateFirst,
                    FrameRateSecond = g.FrameRateSecond,
                    StripCount = g.StripCount,
                })],
                Map = Pixels.FromRgba(width, height, rgba),
                MapFormat = format,
                FoamMask = TiledBuffer<byte>.FromArray(scmap.Width / 2, scmap.Height / 2, scmap.WaterFoamMask),
                FlatnessMask = TiledBuffer<byte>.FromArray(scmap.Width / 2, scmap.Height / 2, scmap.WaterFlatnessMask),
                DepthBiasMask = TiledBuffer<byte>.FromArray(scmap.Width / 2, scmap.Height / 2, scmap.WaterDepthBiasMask),
            };
        }

        private static Skybox ToSkybox(ScmapSkybox s) => new Skybox
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
            Planets = [.. s.Planets.Select(p => new Planet { Position = p.Position, Rotation = p.Rotation, Scale = p.Scale, Uv = p.Uv })],
            MidColor = new Color32(255, s.MidColor.Red, s.MidColor.Green, s.MidColor.Blue),
            CirrusMultiplier = s.CirrusMultiplier,
            CirrusColor = s.CirrusColor,
            CirrusTexture = s.CirrusTexture,
            CirrusLayers = [.. s.CirrusLayers.Select(c => new CirrusLayer { Frequency = c.Frequency, Speed = c.Speed, Direction = c.Direction })],
            Clouds7 = s.Clouds7,
        };

        private static Army ToArmy(MapArmy a) => new Army
        {
            Name = a.Name,
            Personality = a.Personality,
            Plans = a.Plans,
            Color = a.Color,
            Faction = a.Faction,
            Economy = a.Economy is MapEconomy e ? new Economy { Mass = e.Mass, Energy = e.Energy } : null,
            Alliances = a.Alliances.ToImmutableSortedDictionary(StringComparer.Ordinal),
            Units = ToGroup(a.Units),
        };

        private static UnitGroup ToGroup(MapUnitGroup g) => new UnitGroup
        {
            Name = g.Name,
            Groups = [.. g.Groups.Select(ToGroup)],
            Units = [.. g.Units.Select(u => new Unit
            {
                Name = u.Name,
                Blueprint = u.BlueprintId,
                Position = u.Position,
                Orientation = u.Orientation,
            })],
        };

        /// <summary>
        /// Each mask texture holds four layers, one per channel: red, green, blue, alpha.
        /// </summary>
        private static StratumMasks SplitMasks(IReadOnlyList<byte[]> textures)
        {
            List<TiledBuffer<byte>> layers = new List<TiledBuffer<byte>>();
            for (int texture = 0; texture < textures.Count; texture++)
            {
                (int width, int height, _, byte[] rgba) = DdsTexture.Read(textures[texture], $"stratum mask {texture + 1}");
                for (int channel = 0; channel < 4; channel++)
                {
                    byte[] layer = new byte[width * height];
                    for (int i = 0; i < layer.Length; i++)
                    {
                        layer[i] = rgba[i * 4 + channel];
                    }
                    layers.Add(TiledBuffer<byte>.FromArray(width, height, layer));
                }
            }
            return new StratumMasks { Layers = [.. layers] };
        }

        private static string Single(string folder, string pattern)
        {
            string[] files = Directory.GetFiles(folder, pattern, SearchOption.TopDirectoryOnly);
            return files.Length == 1
                ? files[0]
                : throw new MapProjectException($"{folder} has {files.Length} files named {pattern}, expected one");
        }

        /// <summary>
        /// The file in the folder that a path of the scenario names (in any case: the game's file
        /// system ignores it), else the file of the usual name, else null.
        /// </summary>
        private static string? FindNamed(string folder, string? path, string usual)
        {
            string[] names = [Path.GetFileName(path ?? ""), usual];
            return Directory.GetFiles(folder, "*.lua", SearchOption.TopDirectoryOnly)
                .Select(file => (file, rank: Array.FindIndex(names, name => name.Length > 0 && string.Equals(name, Path.GetFileName(file), StringComparison.OrdinalIgnoreCase))))
                .Where(match => match.rank >= 0)
                .OrderBy(match => match.rank)
                .Select(match => match.file)
                .FirstOrDefault();
        }

        private static T Parse<T>(string path, Func<string, T> parse)
        {
            try
            {
                return parse(File.ReadAllText(path, LuaEncoding));
            }
            catch (FormatException exception)
            {
                throw new MapProjectException($"{path}: {exception.Message}", exception);
            }
        }

        [GeneratedRegex(@"^norushoffset([XY])_(.+)$")]
        private static partial Regex NoRushOffsetKey();
    }
}
