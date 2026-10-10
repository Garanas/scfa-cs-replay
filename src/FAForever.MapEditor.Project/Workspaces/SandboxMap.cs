using System.Collections.Immutable;
using System.Numerics;
using System.Security.Cryptography;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Workspaces
{
    /// <summary>
    /// The workspace's sandbox: a small map (5 km, two armies) with one of every kind of thing a map
    /// holds, in <c>projects/Sandbox</c>, for trying the editor out. The editor opens it when started
    /// without a folder, so a new user and an agent always have a map to work on. It is not
    /// publishable (<see cref="MapDocument.IsPublishable"/>): it can be exported to play in the
    /// game, never uploaded to the vault.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is made from the workspace's own converted assets (<see cref="Build"/>): the stratum
    /// textures of one biome, a few of its props and decals, a few units, all named by their game
    /// paths; no file of the game is copied into it. A workspace without converted assets gets a
    /// plain sandbox: the terrain, water, markers, an area and a chain, without textures, props,
    /// decals or units.
    /// </para>
    /// <para>
    /// <see cref="Ensure"/> writes it when it is missing, so it comes back after it is deleted, the
    /// workspace is set up again or the assets are converted again. A plain sandbox nobody changed
    /// is made again once the workspace has assets (a hidden file, <c>.sandbox</c>, remembers how it
    /// was written); a sandbox someone saved is never overwritten.
    /// </para>
    /// </remarks>
    public static class SandboxMap
    {
        /// <summary>
        /// The sandbox's folder in the workspace's projects.
        /// </summary>
        public const string FolderName = "Sandbox";

        /// <summary>
        /// The map's size in ogrids.
        /// </summary>
        public const int Size = 256;

        private const string MarkFile = ".sandbox";
        private const float WaterElevation = 17.5f;

        public static string FolderOf(Workspace workspace) => Path.Combine(workspace.ProjectsFolder, FolderName);

        /// <summary>
        /// Writes the sandbox into the workspace's projects when it is missing, or when it is a
        /// plain one nobody changed and the workspace now has assets.
        /// </summary>
        /// <returns>The sandbox's folder.</returns>
        public static string Ensure(Workspace workspace)
        {
            string folder = FolderOf(workspace);
            string project = Path.Combine(folder, MapProjectLayout.Map);
            if (File.Exists(project) && !IsUntouchedPlain(folder))
            {
                return folder;
            }

            AssetCatalog catalog = AssetCatalog.Load(workspace.AssetsFolder);
            bool plain = !catalog.Entries.Any(entry => entry.Kind is AssetKind.Texture or AssetKind.Prop or AssetKind.Unit);
            if (File.Exists(project) && plain)
            {
                // still no assets: the plain sandbox stays
                return folder;
            }
            Write(Build(catalog), folder, plain);
            return folder;
        }

        /// <summary>
        /// Writes the sandbox as a project into <paramref name="folder"/>, replacing what is there.
        /// </summary>
        private static void Write(MapDocument document, string folder, bool plain)
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
            MapProjectStore.Save(document, folder);
            File.WriteAllText(Path.Combine(folder, MarkFile), plain ? $"plain {HashOf(folder)}\n" : "assets\n");
        }

        private static bool IsUntouchedPlain(string folder)
        {
            string mark = Path.Combine(folder, MarkFile);
            if (!File.Exists(mark))
            {
                return false;
            }
            string[] words = File.ReadAllText(mark).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return words is ["plain", string hash] && hash == HashOf(folder);
        }

        /// <summary>
        /// The project's files as they are, apart from the mark: a save changes it.
        /// </summary>
        private static string HashOf(string folder)
        {
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Where(file => Path.GetFileName(file) != MarkFile)
                .OrderBy(file => file, StringComparer.Ordinal))
            {
                hash.AppendData(System.Text.Encoding.UTF8.GetBytes(Path.GetRelativePath(folder, file).Replace(Path.DirectorySeparatorChar, '/')));
                hash.AppendData(File.ReadAllBytes(file));
            }
            return Convert.ToHexString(hash.GetHashAndReset());
        }

        /// <summary>
        /// Makes the sandbox from the assets a workspace has (props with a model, trees and rocks
        /// first, so they show): what <paramref name="catalog"/> lacks
        /// is left out, and an empty catalogue gives the plain sandbox.
        /// </summary>
        public static MapDocument Build(AssetCatalog catalog)
        {
            Heightmap heightmap = new Heightmap { Scale = 1f / 128, Samples = Heights() };
            Vector3 At(float x, float z) => new Vector3(x, heightmap.HeightAt(x, z), z);

            // the biome with the most stratum textures, the evergreen one first
            AssetEntry[] strata = [.. catalog.Entries.Where(entry => entry.Kind == AssetKind.Texture && entry.Category == "stratum")
                .GroupBy(entry => entry.Group)
                .OrderByDescending(group => group.Key == "evergreen")
                .ThenByDescending(group => group.Count())
                .FirstOrDefault()?.OrderBy(entry => entry.Key, StringComparer.Ordinal).Take(4) ?? []];
            string biome = strata.FirstOrDefault()?.Group ?? "evergreen";
            AssetEntry[] props = [.. Pick(catalog, AssetKind.Prop, biome, 4)];
            AssetEntry[] decals = [.. Pick(catalog, AssetKind.Decal, biome, 1).Where(entry => entry.Category == "albedo")];
            AssetEntry[] units = [.. catalog.Entries.Where(entry => entry.Kind == AssetKind.Unit)
                .OrderByDescending(entry => entry.IsCivilian).ThenBy(entry => entry.Key, StringComparer.Ordinal).Take(2)];

            return new MapDocument
            {
                Name = "sandbox",
                FileName = "sandbox",
                Version = 1,
                Scenario = new ScenarioInfo
                {
                    Name = "Sandbox",
                    Description = "The workspace's sandbox, for trying the editor out. It can be exported to play in the game, not uploaded to the vault.",
                    Author = "FAF Map Editor",
                    PlayableArmies = ["ARMY_1", "ARMY_2"],
                    ExtraArmies = ["NEUTRAL_CIVILIAN"],
                    NoRushRadius = 50,
                    Publishable = false,
                },
                Terrain = new Terrain
                {
                    ScmapVersion = 56,
                    Heightmap = heightmap,
                    Types = TiledBuffer<byte>.Create(Size, Size),
                    Shader = "TTerrainXP",
                    Layers = [.. Layers(strata)],
                    StratumMasks = new StratumMasks { Layers = [.. Enumerable.Range(1, 8).Select(layer => Mask(layer, layer < strata.Length))] },
                    NormalMap = TiledBuffer<Color32>.Create(Size, Size, Color32.FromArgb(0xff8080ff)),
                    NormalMapFormat = TextureFormat.A8R8G8B8,
                    Preview = TiledBuffer<Color32>.Create(Size, Size, Color32.FromArgb(0xff4d5a3a)),
                },
                Lighting = new Lighting
                {
                    Multiplier = 1.5f,
                    SunDirection = Vector3.Normalize(new Vector3(0.7f, 0.5f, -0.5f)),
                    SunAmbience = new Vector3(0.2f),
                    SunColor = new Vector3(1.2f),
                    ShadowFillColor = new Vector3(0.4f),
                    SpecularColor = new Vector4(0, 0, 0, 0),
                    Bloom = 0.08f,
                    FogColor = new Vector3(0.37f, 0.39f, 0.4f),
                    FogStart = 0,
                    FogEnd = 1000,
                },
                Water = Water(),
                Sky = new Sky
                {
                    Background = "/textures/environment/defaultbackground.dds",
                    Cubemap = "/textures/environment/defaultskycube.dds",
                    EnvironmentCubemaps = [new Cubemap { Name = "<default>", Texture = "/textures/environment/defaultenvcube.dds" }],
                },
                Minimap = new Minimap
                {
                    ContourInterval = 10,
                    DeepWaterColor = Color32.FromArgb(0xff47689c),
                    ContourColor = Color32.FromArgb(0xff70707a),
                    ShoreColor = Color32.FromArgb(0xff8da6c4),
                    LandStartColor = Color32.FromArgb(0xff657145),
                    LandEndColor = Color32.FromArgb(0xffb2b28c),
                },
                Decals = [.. decals.Select(decal => new Decal
                {
                    Id = 1,
                    Type = DecalType.Albedo,
                    Textures = ["/" + decal.Key],
                    Scale = new Vector3(24, 24, 24),
                    Position = At(116, 132),
                    Rotation = new Vector3(0, 0.4f, 0),
                    CutOffLod = 1000,
                    NearCutOffLod = 0,
                    OwnerArmy = -1,
                })],
                DecalGroups = decals.Length == 0 ? [] : [new DecalGroup { Id = 1, Name = "Mud", DecalIds = [1] }],
                Props = [.. props.Select((prop, index) => new Prop
                {
                    Id = index + 1,
                    Blueprint = "/" + prop.Key,
                    Position = At(118 + index * 6, 112 + (index % 2) * 5),
                    RotationX = Vector3.UnitX,
                    RotationY = Vector3.UnitY,
                    RotationZ = Vector3.UnitZ,
                    Scale = Vector3.One,
                })],
                Markers = Markers(At),
                Armies =
                [
                    Army("ARMY_1", []),
                    Army("ARMY_2", []),
                    Army("NEUTRAL_CIVILIAN", [.. units.Select((unit, index) => new Unit
                    {
                        Name = $"UNIT_{index + 1}",
                        Blueprint = unit.Key,
                        Position = At(140 + index * 10, 150),
                        Orientation = Vector3.Zero,
                    })]),
                ],
            };
        }

        private static IEnumerable<AssetEntry> Pick(AssetCatalog catalog, AssetKind kind, string biome, int count) =>
            catalog.Entries.Where(entry => entry.Kind == kind)
                .OrderByDescending(entry => entry.Group == biome)
                .ThenByDescending(entry => entry.Model is not null || kind == AssetKind.Decal)
                .ThenBy(entry => entry.Category switch { "trees" => 0, "rocks" => 1, _ => 2 })
                .ThenBy(entry => entry.Key, StringComparer.Ordinal)
                .Take(count);

        /// <summary>
        /// Gentle hills around a plateau in the middle, and a lake in the north-west corner below the water.
        /// </summary>
        private static TiledBuffer<ushort> Heights()
        {
            ushort[] samples = new ushort[(Size + 1) * (Size + 1)];
            for (int z = 0; z <= Size; z++)
            {
                for (int x = 0; x <= Size; x++)
                {
                    double hills = 3 * Math.Sin(x * 0.05) * Math.Cos(z * 0.04);
                    double lake = Math.Max(0, 1 - Math.Sqrt((x - 40.0) * (x - 40) + (z - 40.0) * (z - 40)) / 40);
                    double height = 25 + hills - 16 * lake * lake * (3 - 2 * lake);
                    samples[z * (Size + 1) + x] = (ushort)Math.Clamp(height * 128, 0, ushort.MaxValue);
                }
            }
            return TiledBuffer<ushort>.FromArray(Size + 1, Size + 1, samples);
        }

        /// <summary>
        /// The ten layers of a version 56 map: the lower stratum, eight strata, the upper (macro) one.
        /// </summary>
        private static IEnumerable<TerrainLayer> Layers(AssetEntry[] strata)
        {
            for (int layer = 0; layer < 10; layer++)
            {
                AssetEntry? texture = layer < strata.Length && layer < 9 ? strata[layer] : null;
                yield return new TerrainLayer
                {
                    AlbedoPath = texture is null ? "" : "/" + texture.Key,
                    AlbedoScale = layer == 0 ? 4 : 10,
                    NormalPath = texture?.Keys.Skip(1).FirstOrDefault() is string normal ? "/" + normal : "",
                    NormalScale = layer == 0 ? 4 : 10,
                };
            }
        }

        /// <summary>
        /// A stratum's mask at half the map's size: a band per layer, so each shows somewhere.
        /// </summary>
        private static TiledBuffer<byte> Mask(int layer, bool painted)
        {
            int size = Size / 2;
            if (!painted)
            {
                return TiledBuffer<byte>.Create(size, size);
            }
            byte[] mask = new byte[size * size];
            float centreX = size * (0.25f + 0.25f * ((layer - 1) % 3));
            float centreZ = size * (0.7f - 0.2f * ((layer - 1) % 2));
            for (int z = 0; z < size; z++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = MathF.Sqrt((x - centreX) * (x - centreX) + (z - centreZ) * (z - centreZ));
                    mask[z * size + x] = (byte)Math.Clamp(255 * (1 - distance / (size * 0.2f)) * 2, 0, 255);
                }
            }
            return TiledBuffer<byte>.FromArray(size, size, mask);
        }

        private static Water Water()
        {
            int half = Size / 2;
            return new Water
            {
                HasWater = true,
                Elevation = WaterElevation,
                ElevationDeep = 15,
                ElevationAbyss = 10,
                SurfaceColor = new Vector3(0, 0.7f, 1.5f),
                ColorLerp = new Vector2(0.064f, 0.119f),
                RefractionScale = 0.375f,
                FresnelBias = 0.15f,
                FresnelPower = 1.5f,
                UnitReflection = 0.5f,
                SkyReflection = 1.5f,
                SunShininess = 50,
                SunStrength = 10,
                SunDirection = Vector3.Normalize(new Vector3(0.09f, -0.95f, 0.29f)),
                SunColor = new Vector3(0.81f, 0.47f, 0.34f),
                SunReflection = 5,
                SunGlow = 0.1f,
                Cubemap = "/textures/engine/waterCubemap.dds",
                Ramp = "/textures/engine/waterramp.dds",
                WaveTextures = [.. new[] { 0.0009f, 0.009f, 0.05f, 0.5f }.Select((repeat, index) => new WaveTexture
                {
                    Texture = "/textures/engine/waves.dds",
                    NormalRepeat = repeat,
                    NormalMovement = new Vector2(0.5f - index * 0.2f, -0.05f + index * 0.03f),
                })],
                Map = TiledBuffer<Color32>.Create(half, half, Color32.FromArgb(0x00000000)),
                MapFormat = TextureFormat.A8R8G8B8,
                FoamMask = TiledBuffer<byte>.Create(half, half),
                FlatnessMask = TiledBuffer<byte>.Create(half, half, 255),
                DepthBiasMask = TiledBuffer<byte>.Create(half, half, 127),
            };
        }

        /// <summary>
        /// Two spawns, mass and hydrocarbon, three land path nodes in a line with their links, an AI
        /// marker, an area and a chain through the path nodes. Ids are given here, from 1.
        /// </summary>
        private static MarkerSet Markers(Func<float, float, Vector3> at)
        {
            PathNode Node(int id, int index, ImmutableArray<int> links) => new PathNode
            {
                Id = id,
                Name = $"LandPN{index}",
                Position = at(80 + index * 48, 128),
                Layer = PathLayer.Land,
                AdjacentTo = links,
            };
            return new MarkerSet
            {
                Spawns =
                [
                    new Spawn { Id = 1, Name = "ARMY_1", Position = at(64, 192) },
                    new Spawn { Id = 2, Name = "ARMY_2", Position = at(192, 64) },
                ],
                Resources =
                [
                    new ResourceMarker { Id = 3, Name = "Mass 00", Kind = ResourceKind.Mass, Position = at(72, 200) },
                    new ResourceMarker { Id = 4, Name = "Mass 01", Kind = ResourceKind.Mass, Position = at(184, 56) },
                    new ResourceMarker { Id = 5, Name = "Hydrocarbon 00", Kind = ResourceKind.Hydrocarbon, Position = at(128, 168) },
                ],
                PathNodes = [Node(6, 0, [7]), Node(7, 1, [6, 8]), Node(8, 2, [7])],
                AiMarkers = [new AiMarker { Id = 9, Name = "Expansion Area 00", Type = "Expansion Area", Position = at(176, 176) }],
                Areas = [new Area { Name = "AREA_1", X0 = 16, Z0 = 16, X1 = 240, Z1 = 240 }],
                Chains = [new Chain { Name = "CHAIN_1", MarkerIds = [6, 7, 8] }],
            };
        }

        private static Army Army(string name, ImmutableArray<Unit> units) => new Army
        {
            Name = name,
            Units = new UnitGroup
            {
                Name = "Units",
                Groups = units.IsEmpty ? [] : [new UnitGroup { Name = "INITIAL", Units = units }],
            },
        };
    }
}
