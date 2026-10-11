using System.Collections.Immutable;
using System.Numerics;
using System.Text;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project
{
    /// <summary>
    /// What a new map starts as (user story M-2).
    /// </summary>
    public sealed record NewMapSettings
    {
        /// <summary>
        /// The name players see; the folder and the files are named after it (<see cref="NewMap.FolderNameOf"/>).
        /// </summary>
        public string Name { get; init; } = "New map";

        /// <summary>
        /// The width and height in ogrids, one of the game's sizes (<see cref="NewMap.Sizes"/>):
        /// 256 is a 5 km map, 4096 an 81 km one.
        /// </summary>
        public int Size { get; init; } = 512;

        /// <summary>
        /// The height of the flat terrain, in game units (0 to 511).
        /// </summary>
        public float Height { get; init; } = 35;

        public bool Water { get; init; } = true;

        /// <summary>
        /// The height of the water surface, in game units; the deep and abyss elevations follow it
        /// unless given.
        /// </summary>
        public float WaterElevation { get; init; } = 30;

        public float? DeepElevation { get; init; }

        public float? AbyssElevation { get; init; }

        /// <summary>
        /// The biome whose textures cover the strata (<see cref="NewMap.Biomes"/>), by its id.
        /// </summary>
        public string Biome { get; init; } = "evergreen";

        /// <summary>
        /// How many armies players take: as many spawns are placed on a circle around the middle.
        /// </summary>
        public int Players { get; init; } = 2;

        /// <summary>
        /// The author, written into the scenario.
        /// </summary>
        public string Author { get; init; } = "";

        /// <summary>
        /// The terrain's layers (lower, strata, upper) in place of the biome's: a template's own.
        /// </summary>
        public ImmutableArray<TerrainLayer>? Layers { get; init; }

        /// <summary>
        /// The armies besides the players' in place of <c>ARMY_17</c> and <c>NEUTRAL_CIVILIAN</c>: a template's own.
        /// </summary>
        public ImmutableArray<string>? ExtraArmies { get; init; }

        /// <summary>
        /// The lighting in place of <see cref="NewMap.DefaultLighting"/>: a template's own.
        /// </summary>
        public Lighting? Lighting { get; init; }
    }

    /// <summary>
    /// The textures of the strata a new map of a biome starts with.
    /// </summary>
    /// <param name="Id">E.g. <c>evergreen</c>.</param>
    /// <param name="Layers">The ten layers of a version 56 map: the lower layer, eight strata (the
    /// first few filled, the rest empty) and the upper layer.</param>
    /// <param name="WaterCubemap">What the water reflects: the game's sky cube map of the biome.</param>
    public sealed record NewMapBiome(string Id, string Title, ImmutableArray<TerrainLayer> Layers, string WaterCubemap = NewMap.DefaultWaterCubemap);

    /// <summary>
    /// Makes a map from scratch (user story M-2): flat terrain at a height, water or none, the
    /// strata of a biome, spawns on a circle and the playable area over the whole map. It exports
    /// to a map the game reads, as the FAF map editor's new map does (<c>UI/Windows/NewMap.cs</c>,
    /// <c>HazardX SCMAP Code/Map.cs</c> in FAForeverMapEditor), as a version 56 <c>.scmap</c>.
    /// </summary>
    public static class NewMap
    {
        /// <summary>
        /// The sizes of the game's maps in ogrids: 5, 10, 20, 40 and 81 km.
        /// </summary>
        public static IReadOnlyList<int> Sizes { get; } = [256, 512, 1024, 2048, 4096];

        /// <summary>
        /// The version of the <c>.scmap</c> a new map is, made from a biome or a settings template alike.
        /// </summary>
        public const int ScmapVersion = 56;

        /// <summary>
        /// The terrain layers a map of <see cref="ScmapVersion"/> holds: the lower layer, eight
        /// strata and the upper layer.
        /// </summary>
        public const int LayerCount = 10;

        /// <summary>
        /// What a new map's water reflects when its biome names nothing else.
        /// </summary>
        public const string DefaultWaterCubemap = "/textures/environment/skycube_evergreen01a.dds";

        /// <summary>
        /// The script of a skirmish map, as the map editors write it.
        /// </summary>
        public const string DefaultScript = """
            local ScenarioUtils = import('/lua/sim/ScenarioUtilities.lua')
            local ScenarioFramework = import('/lua/ScenarioFramework.lua')

            function OnPopulate()
                ScenarioUtils.InitializeArmies()
            end

            function OnStart(self)
            end

            """;

        /// <summary>
        /// The biomes a new map can start with: the strata textures of the game's <c>env</c>
        /// folders that most maps of the biome use.
        /// </summary>
        public static IReadOnlyList<NewMapBiome> Biomes { get; } =
        [
            Biome("evergreen", "/textures/environment/skycube_evergreen01a.dds", "Evergreen", "/env/evergreen/layers/macrotexture000_albedo.dds",
                ("/env/evergreen/layers/sandlight_albedo.dds", "/env/evergreen/layers/sandlight_normals.dds", 4),
                ("/env/evergreen/layers/grass000_albedo.dds", "/env/evergreen/layers/grass000_normals.dds", 4),
                ("/env/evergreen/layers/grass001_albedo.dds", "/env/evergreen/layers/grass001_normals.dds", 6),
                ("/env/evergreen/layers/dirt001_albedo.dds", "/env/evergreen/layers/dirt001_normals.dds", 4),
                ("/env/evergreen/layers/rockmed_albedo.dds", "/env/evergreen/layers/rockmed_normals.dds", 8)),
            Biome("desert", "/textures/environment/skycube_desert01a.dds", "Desert", "/env/evergreen/layers/macrotexture000_albedo.dds",
                ("/env/desert/layers/des_sandlight_albedo.dds", "/env/desert/layers/des_sandlight_normal.dds", 4),
                ("/env/desert/layers/des_sandmed_albedo.dds", "/env/desert/layers/des_sandmed_normal.dds", 4),
                ("/env/desert/layers/des_sanddark_albedo.dds", "/env/desert/layers/des_sanddark_normal.dds", 4),
                ("/env/desert/layers/des_gravel_albedo.dds", "/env/desert/layers/des_gravel_normal.dds", 4),
                ("/env/desert/layers/des_rock_albedo.dds", "/env/desert/layers/des_rock_normal.dds", 8)),
            Biome("tundra", "/textures/environment/skycube_tundra02a.dds", "Tundra", "/env/tundra/layers/macroice_albedo.dds",
                ("/env/tundra/layers/tund_sandlight2_albedo.dds", "/env/tundra/layers/tund_sandlight2_normal.dds", 4),
                ("/env/tundra/layers/tund_grass_albedo.dds", "/env/tundra/layers/tund_sandmed_normal.dds", 4),
                ("/env/tundra/layers/tund_gravil_albedo.dds", "/env/tundra/layers/tund_sandwet_normal.dds", 4),
                ("/env/tundra/layers/tund_rock_albedo.dds", "/env/tundra/layers/tund_rock_normal.dds", 8),
                ("/env/tundra/layers/tund_snow_albedo.dds", "/env/tundra/layers/tund_snow_normal.dds", 6)),
            Biome("lava", "/textures/environment/skycube_lava01a.dds", "Lava", "/env/lava/layers/lav_macrotexture000_albedo.dds",
                ("/env/lava/layers/lav_gravel_albedo.dds", "/env/lava/layers/lav_gravel_normals.dds", 4),
                ("/env/lava/layers/lav_cracked02_albedo.dds", "/env/lava/layers/lav_cracked02_normals.dds", 4),
                ("/env/lava/layers/lav_rock01_albedo.dds", "/env/lava/layers/lav_rock01_normals.dds", 6),
                ("/env/lava/layers/lav_rock04_albedo.dds", "/env/lava/layers/lav_rock04_normals.dds", 8),
                ("/env/lava/layers/lav_ribbon_albedo.dds", "/env/lava/layers/lav_ribbon_normals.dds", 4)),
            Biome("tropical", "/textures/environment/skycube_tropical01a.dds", "Tropical", "/env/evergreen/layers/macrotexture000_albedo.dds",
                ("/env/tropical/layers/trop_sand_albedo.dds", "/env/tropical/layers/trop_sand_normal.dds", 4),
                ("/env/tropical/layers/trop_grass_albedo.dds", "/env/tropical/layers/trop_grassjung_normal.dds", 4),
                ("/env/tropical/layers/trop_grassjung_albedo.dds", "/env/tropical/layers/trop_grassjung_normal.dds", 6),
                ("/env/tropical/layers/trop_dirt_albedo.dds", "/env/tropical/layers/trop_none_normal.dds", 4),
                ("/env/tropical/layers/trop_rock_albedo.dds", "/env/tropical/layers/trop_rock_normal.dds", 8)),
            Biome("red-barrens", "/textures/environment/skycube_redrocks05a.dds", "Red Barrens", "/env/red barrens/layers/macrotexture001_albedo.dds",
                ("/env/red barrens/layers/rb_sand_albedo.dds", "/env/red barrens/layers/rb_sand_normal.dds", 4),
                ("/env/red barrens/layers/rb_redmud_albedo.dds", "/env/red barrens/layers/rb_redmud_normal.dds", 4),
                ("/env/red barrens/layers/rb_cracked_albedo.dds", "/env/red barrens/layers/rb_cracked_normal.dds", 4),
                ("/env/red barrens/layers/rb_rock06_albedo.dds", "/env/red barrens/layers/rb_rock06_normal.dds", 6),
                ("/env/red barrens/layers/rb_rock_albedo.dds", "/env/red barrens/layers/rb_rock_normal.dds", 8)),
        ];

        /// <summary>
        /// The game's lighting of a new map, the FAF map editor's default.
        /// </summary>
        public static Lighting DefaultLighting { get; } = new Lighting
        {
            Multiplier = 1.3f,
            SunDirection = Vector3.Normalize(new Vector3(-0.616f, 0.559f, -0.554f)),
            SunAmbience = new Vector3(0.41f, 0.44f, 0.55f),
            SunColor = new Vector3(0.95f, 0.84f, 0.76f),
            ShadowFillColor = Vector3.Zero,
            SpecularColor = new Vector4(0.1f, 0.1f, 0.1f, 0.1f),
            Bloom = 0.03f,
            FogColor = new Vector3(0.37f, 0.50f, 0.61f),
            FogStart = 0,
            FogEnd = 800,
        };

        /// <summary>
        /// A map folder's name for a map's name: lower case, words joined by underscores, only
        /// letters, digits, underscores and dashes (<c>My New Map!</c> becomes <c>my_new_map</c>).
        /// </summary>
        public static string FolderNameOf(string name)
        {
            StringBuilder folder = new StringBuilder();
            foreach (char c in name.Trim().ToLowerInvariant())
            {
                if (char.IsAsciiLetterOrDigit(c) || c is '_' or '-')
                {
                    folder.Append(c);
                }
                else if (char.IsWhiteSpace(c) && folder.Length > 0 && folder[^1] != '_')
                {
                    folder.Append('_');
                }
            }
            string result = folder.ToString().Trim('_');
            return result.Length == 0 ? "new_map" : result;
        }

        /// <summary>
        /// Makes the map.
        /// </summary>
        /// <exception cref="ArgumentException">The size is none of the game's, the biome is unknown,
        /// or a height or the number of players is out of range.</exception>
        public static MapDocument Create(NewMapSettings settings)
        {
            if (!Sizes.Contains(settings.Size))
            {
                throw new ArgumentException($"A map is {string.Join(", ", Sizes)} ogrids wide, not {settings.Size}", nameof(settings));
            }
            NewMapBiome biome = Biomes.FirstOrDefault(b => string.Equals(b.Id, settings.Biome, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"There is no biome '{settings.Biome}': {string.Join(", ", Biomes.Select(b => b.Id))}", nameof(settings));
            const float scale = 1f / 128;
            float highest = ushort.MaxValue * scale;
            if (settings.Height is < 0 || settings.Height > highest || settings.WaterElevation is < 0 || settings.WaterElevation > highest)
            {
                throw new ArgumentException($"Heights lie between 0 and {highest:0}", nameof(settings));
            }
            if (settings.Players is < 1 or > 16)
            {
                throw new ArgumentException("A map has 1 to 16 players", nameof(settings));
            }

            int size = settings.Size;
            float deep = Math.Clamp(settings.DeepElevation ?? settings.WaterElevation - 5, 0, settings.WaterElevation);
            float abyss = Math.Clamp(settings.AbyssElevation ?? settings.WaterElevation - 15, 0, deep);
            string folder = FolderNameOf(settings.Name);
            ushort sample = (ushort)Math.Clamp(MathF.Round(settings.Height / scale), 0, ushort.MaxValue);
            bool underWater = settings.Water && settings.Height <= settings.WaterElevation;

            string[] players = [.. Enumerable.Range(1, settings.Players).Select(i => $"ARMY_{i}")];
            string[] extra = settings.ExtraArmies is ImmutableArray<string> own ? [.. own.Where(name => !players.Contains(name)).Distinct()] : ["ARMY_17", "NEUTRAL_CIVILIAN"];
            // the first spawn above the middle on an ogrid's centre, the others its images under the
            // rotational symmetry of as many players, so that a new map is exactly symmetric
            float radius = size * 0.3f;
            Vector3 first = new Vector3(MathF.Floor(size / 2f) + 0.5f, settings.Height, MathF.Round(size / 2f - radius) + 0.5f);
            ImmutableArray<Spawn> spawns = [.. players.Select((army, i) =>
            {
                Vector3 place = MapSymmetryTransform.Rotation(2 * Math.PI * i / players.Length, size / 2f, size / 2f).Apply(first);
                return new Spawn { Id = i + 1, Name = army, Position = place with { Y = settings.Height } };
            })];

            return new MapDocument
            {
                Name = folder,
                FileName = folder,
                Version = 1,
                // the normal and shadow decals over the whole map, generated at export (as Kaali's)
                WideDecals = MapWideDecals.NewMap,
                Scenario = new ScenarioInfo
                {
                    Name = settings.Name.Trim().Length == 0 ? "New map" : settings.Name.Trim(),
                    Author = settings.Author,
                    Type = ScenarioType.Skirmish,
                    PlayableArmies = [.. players],
                    ExtraArmies = [.. extra],
                },
                Terrain = new Terrain
                {
                    ScmapVersion = ScmapVersion,
                    Heightmap = new Heightmap { Scale = scale, Samples = TiledBuffer<ushort>.Create(size + 1, size + 1, sample) },
                    Types = TiledBuffer<byte>.Create(size, size, 1),
                    Shader = "TTerrainXP",
                    Layers = settings.Layers is ImmutableArray<TerrainLayer> layers ? FitLayers(layers) : biome.Layers,
                    StratumMasks = new StratumMasks { Layers = [.. Enumerable.Range(0, 8).Select(_ => TiledBuffer<byte>.Create(size / 2, size / 2))] },
                    // flat: x and y of the normal at the middle (DXT5nm, x in alpha, y in green)
                    NormalMap = TiledBuffer<Color32>.Create(size, size, new Color32(128, 0, 128, 0)),
                    NormalMapFormat = TextureFormat.Dxt5,
                    Preview = TiledBuffer<Color32>.Create(256, 256, underWater ? new Color32(255, 71, 140, 181) : new Color32(255, 117, 99, 107)),
                },
                Lighting = settings.Lighting ?? DefaultLighting,
                Water = new Water
                {
                    HasWater = settings.Water,
                    Elevation = settings.WaterElevation,
                    ElevationDeep = deep,
                    ElevationAbyss = abyss,
                    // the FAF map editor's (HazardX SCMAP Code/WaterShader.cs, Defaults)
                    SurfaceColor = new Vector3(0.2f, 0.2f, 0.2f),
                    ColorLerp = new Vector2(0.064f, 0.064f),
                    RefractionScale = 0.38f,
                    FresnelBias = 0,
                    FresnelPower = 0,
                    UnitReflection = 0.5f,
                    SkyReflection = 1,
                    SunShininess = 80,
                    SunStrength = 0,
                    SunDirection = new Vector3(0.09954818f, -0.9626309f, 0.2518569f),
                    SunColor = new Vector3(0.52f, 0.47f, 0.35f),
                    SunReflection = 1,
                    SunGlow = 0,
                    Cubemap = biome.WaterCubemap,
                    Ramp = "/textures/engine/waterramp.dds",
                    WaveTextures =
                    [
                        new WaveTexture { Texture = "/textures/engine/waves1_400m.dds", NormalRepeat = 0.05f, NormalMovement = new Vector2(0, -0.012f) },
                        new WaveTexture { Texture = "/textures/engine/waves1_120m.dds", NormalRepeat = 0.167f, NormalMovement = new Vector2(-0.00130236f, 0.00738606f) },
                        new WaveTexture { Texture = "/textures/engine/waves1_40m.dds", NormalRepeat = 0.5f, NormalMovement = new Vector2(-0.003f, 0.00519615f) },
                        new WaveTexture { Texture = "/textures/engine/waves1_120m.dds", NormalRepeat = 0.8f, NormalMovement = new Vector2(-0.00105f, -0.006f) },
                    ],
                    Map = TiledBuffer<Color32>.Create(size / 2, size / 2, new Color32(0, 255, 0, 255)),
                    MapFormat = TextureFormat.Dxt5,
                    FoamMask = TiledBuffer<byte>.Create(size / 2, size / 2, 0),
                    FlatnessMask = TiledBuffer<byte>.Create(size / 2, size / 2, 255),
                    DepthBiasMask = TiledBuffer<byte>.Create(size / 2, size / 2, 127),
                },
                Sky = new Sky
                {
                    Background = "/textures/environment/defaultbackground.dds",
                    Cubemap = "/textures/environment/defaultskycube.dds",
                    EnvironmentCubemaps = [new Cubemap { Name = "<default>", Texture = "/textures/environment/defaultenvcube.dds" }],
                },
                Symmetry = settings.Players is >= 2 and <= MapSymmetry.MaximumCount ? new SymmetrySetting(MapSymmetryKind.Rotational, settings.Players) : null,
                Minimap = new Minimap
                {
                    ContourInterval = 24,
                    DeepWaterColor = new Color32(255, 71, 140, 181),
                    ContourColor = new Color32(255, 112, 112, 112),
                    ShoreColor = new Color32(255, 140, 201, 224),
                    LandStartColor = new Color32(255, 117, 99, 107),
                    LandEndColor = new Color32(255, 206, 206, 176),
                },
                Markers = new MarkerSet
                {
                    Spawns = spawns,
                    Areas = [new Area { Name = "AREA_1", X0 = 0, Z0 = 0, X1 = size, Z1 = size }],
                },
                Armies = [.. players.Concat(extra).Select(army => new Army
                {
                    Name = army,
                    Units = new UnitGroup { Name = "Units", Groups = [new UnitGroup { Name = "INITIAL" }] },
                })],
                Script = [.. Encoding.UTF8.GetBytes(DefaultScript.ReplaceLineEndings("\n"))],
            };
        }

        /// <summary>
        /// A template's layers as many as the new map's version 56 holds (ten: the lower layer,
        /// eight strata, the upper layer): the lower first and the upper last, the strata between
        /// kept in their order, filled with empty ones when the template has fewer (one saved
        /// from a version 53 map has four) and cut when it has more.
        /// </summary>
        internal static ImmutableArray<TerrainLayer> FitLayers(ImmutableArray<TerrainLayer> layers)
        {
            const int count = LayerCount;
            if (layers.Length == count)
            {
                return layers;
            }
            TerrainLayer empty = new TerrainLayer { AlbedoPath = "", NormalPath = "", AlbedoScale = 4, NormalScale = 4 };
            if (layers.IsEmpty)
            {
                return [.. Enumerable.Repeat(empty, count)];
            }
            TerrainLayer[] strata = [.. layers.Skip(1).Take(Math.Max(0, layers.Length - 2))];
            return [layers[0], .. Enumerable.Range(0, count - 2).Select(i => i < strata.Length ? strata[i] : empty), layers.Length > 1 ? layers[^1] : empty];
        }

        private static NewMapBiome Biome(string id, string water, string title, string upper, params (string Albedo, string Normal, float Scale)[] strata)
        {
            TerrainLayer Empty() => new TerrainLayer { AlbedoPath = "", NormalPath = "", AlbedoScale = 4, NormalScale = 4 };
            ImmutableArray<TerrainLayer>.Builder layers = ImmutableArray.CreateBuilder<TerrainLayer>(10);
            foreach ((string albedo, string normal, float scale) in strata)
            {
                layers.Add(new TerrainLayer { AlbedoPath = albedo, AlbedoScale = scale, NormalPath = normal, NormalScale = scale });
            }
            while (layers.Count < 9)
            {
                layers.Add(Empty());
            }
            layers.Add(new TerrainLayer { AlbedoPath = upper, AlbedoScale = 128, NormalPath = "", NormalScale = 0 });
            return new NewMapBiome(id, title, layers.MoveToImmutable(), water);
        }
    }
}
