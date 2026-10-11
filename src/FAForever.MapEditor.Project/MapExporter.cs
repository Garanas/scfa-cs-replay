using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
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
        /// <param name="stamp">The editor to name in the scenario (<see cref="EditorStamp"/>); null names none.</param>
        public static MapGameFiles Export(MapDocument document, EditorStamp? stamp = null)
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
                    Chains = [.. MarkerTranslation.ToMapChains(document.Markers)],
                    Armies = [.. document.Armies.Select(ToMapArmy)],
                },
                Scenario = ToMapScenario(document, root, stamp),
                Script = AdaptiveScript.Writes(document) ? AdaptiveScriptOf(document, root) : ScriptOf(document),
                Files = FilesOf(document),
            };
        }

        /// <summary>
        /// The map's own files, without its decal sets' masks (<see cref="Editing.DecalSets.UnexportedMasks"/>:
        /// they feed decal-masks layers and the game never reads them); for a map whose adaptive files the
        /// export writes (user story R-5, <see cref="AdaptiveScript.Writes"/>) with its <c>_options.lua</c>
        /// and <c>_tables.lua</c>.
        /// </summary>
        private static Dictionary<string, byte[]> FilesOf(MapDocument document)
        {
            IReadOnlySet<string> masks = Editing.DecalSets.UnexportedMasks(document);
            Dictionary<string, byte[]> files = document.Files.Where(entry => !masks.Contains(entry.Key))
                .ToDictionary(entry => entry.Key, entry => ImmutableCollectionsMarshal.AsArray(entry.Value)!);
            if (AdaptiveScript.Writes(document))
            {
                files[$"{document.FileName}_options.lua"] = MapImporter.LuaEncoding.GetBytes(AdaptiveScript.Options(document));
                files[$"{document.FileName}_tables.lua"] = MapImporter.LuaEncoding.GetBytes(AdaptiveScript.Tables(document));
            }
            return files;
        }

        /// <summary>
        /// The adaptive script of a map (user story R-5), which sets the playable area the scenario names.
        /// </summary>
        private static byte[] AdaptiveScriptOf(MapDocument document, string root)
        {
            string text = AdaptiveScript.Script(document, $"{root}_tables.lua");
            if (document.Scenario.PlayableArea is string area && document.Markers.Areas.Any(a => a.Name == area) && MapScript.WithPlayableArea(text, area) is string changed)
            {
                text = changed;
            }
            return MapImporter.LuaEncoding.GetBytes(text);
        }

        /// <summary>
        /// The script, which sets the playable area the scenario names (<see cref="ScenarioInfo.PlayableArea"/>):
        /// its call changed or added (<see cref="MapScript.WithPlayableArea"/>). A map that keeps the
        /// convention, or whose script has no place for the call, keeps its script as it is.
        /// </summary>
        private static byte[]? ScriptOf(MapDocument document)
        {
            if (document.Script is not { } script)
            {
                return null;
            }
            byte[] bytes = ImmutableCollectionsMarshal.AsArray(script)!;
            if (document.Scenario.PlayableArea is not string area || !document.Markers.Areas.Any(a => a.Name == area))
            {
                return bytes;
            }
            string text = MapImporter.LuaEncoding.GetString(bytes);
            return MapScript.WithPlayableArea(text, area) is string changed && changed != text ? MapImporter.LuaEncoding.GetBytes(changed) : bytes;
        }

        /// <summary>
        /// The files of the map folder by their path in it, with the paths in them pointed at the folder.
        /// </summary>
        /// <exception cref="ArgumentException">The document does not make a valid map (see <see cref="ScmapWriter.Write(Scmap)"/>).</exception>
        /// <param name="stamp">The editor to name in <c>_scenario.lua</c>: its comment line at the top
        /// and its <c>map_editor</c> table; null names none, so two exports give the same bytes.</param>
        public static IReadOnlyDictionary<string, byte[]> ToBytes(MapDocument document, EditorStamp? stamp = null)
        {
            MapGameFiles game = Export(document, stamp);
            Func<string, string> rewrite = game.Folder.RewritePaths;

            Dictionary<string, byte[]> files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach ((string path, byte[] bytes) in game.Files)
            {
                files[path] = path.EndsWith(".lua", StringComparison.OrdinalIgnoreCase) ? RewriteLua(bytes, rewrite) : bytes;
            }
            files[$"{game.FileName}.scmap"] = ScmapWriter.Write(RewritePaths(game.Scmap, rewrite));
            files[$"{game.FileName}_save.lua"] = MapImporter.LuaEncoding.GetBytes(rewrite(MapSaveWriter.Write(game.Save)));
            string scenario = rewrite(MapScenarioWriter.Write(game.Scenario));
            files[$"{game.FileName}_scenario.lua"] = MapImporter.LuaEncoding.GetBytes(stamp is null ? scenario : stamp.Comment + "\n" + scenario);
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
        /// <param name="stamp">The editor to name in <c>_scenario.lua</c> (<see cref="ToBytes"/>).</param>
        public static string WriteFolder(MapDocument document, string outputFolder, EditorStamp? stamp = null)
        {
            IReadOnlyDictionary<string, byte[]> files = ToBytes(document, stamp);
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
            // a decal set is one decal per texture in the game (its albedo, then its normals)
            d = Editing.DecalSets.Expand(d);
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
                DecalHeader = DecalHeader(d),
                Decals = [.. d.Decals.Select(x => new ScmapDecal(x.Id, (int)x.Type, [.. x.Textures], x.Scale, x.Position, x.Rotation, x.CutOffLod, x.NearCutOffLod, x.OwnerArmy))],
                DecalGroups = [.. d.DecalGroups.Select(g => new ScmapDecalGroup(g.Id, g.Name, [.. g.DecalIds]))],
                NormalMaps = [Texture(terrain.NormalMap, terrain.NormalMapFormat)],
                StratumMasks = JoinMasks(terrain.StratumMasks),
                WaterMap = Texture(water.Map, water.MapFormat),
                WaterFoamMask = water.FoamMask.ToArray(),
                WaterFlatnessMask = water.FlatnessMask.ToArray(),
                WaterDepthBiasMask = water.DepthBiasMask.ToArray(),
                // with the walkability marked by hand (T-7): blocked ogrids on a blocking type
                TerrainTypes = Editing.WalkabilityMarks.TerrainTypes(d),
                Skybox = d.Sky.Skybox is Skybox s ? ToScmapSkybox(s) : null,
                Props = [.. d.Props.Select(p => new ScmapProp(p.Blueprint, p.Position, p.RotationX, p.RotationY, p.RotationZ, p.Scale))],
            };
        }

        /// <summary>
        /// The two numbers before the decals, which the map editors keep as counters (the FAF map
        /// editor calls them Unknown7 and Unknown8): one more than the highest decal id and group id.
        /// </summary>
        private static (int, int) DecalHeader(MapDocument d) =>
            (d.Decals.IsEmpty ? 0 : d.Decals.Max(decal => decal.Id) + 1, d.DecalGroups.IsEmpty ? 0 : d.DecalGroups.Max(group => group.Id) + 1);

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

        /// <summary>
        /// The factions any army may play, for the armies of a map that restricts some.
        /// </summary>
        private static readonly string[] AllFactions = ["uef", "aeon", "cybran", "seraphim"];

        /// <summary>
        /// The scenario as the game reads it: one configuration, <c>standard</c>, with one team,
        /// <c>FFA</c>, of the playable armies, and the other armies in <c>customprops.ExtraArmies</c>.
        /// The map maker's teams follow the team <c>FFA</c> (the lobby reads only the first team),
        /// each with its playable armies; a team without any is left out.
        /// </summary>
        private static MapScenario ToMapScenario(MapDocument d, string root, EditorStamp? stamp)
        {
            ScenarioInfo s = d.Scenario;
            Dictionary<string, LuaData> extra = new Dictionary<string, LuaData>();
            if (s.Author.Length > 0)
            {
                extra["author"] = new LuaData.String(s.Author);
            }
            foreach (Spawn spawn in d.Markers.Spawns)
            {
                if (spawn.NoRushOffset is Vector2 offset)
                {
                    extra[$"norushoffsetX_{spawn.Army}"] = new LuaData.Number(offset.X);
                    extra[$"norushoffsetY_{spawn.Army}"] = new LuaData.Number(offset.Y);
                }
            }
            if (stamp is not null)
            {
                extra[EditorStamp.ScenarioKey] = stamp.ToLua();
            }

            Dictionary<string, LuaData> configuration = new Dictionary<string, LuaData>();
            if (!s.ArmyFactions.IsEmpty)
            {
                // per playable army in order, up to the last that is restricted
                int count = s.PlayableArmies.LastIndexOf(s.PlayableArmies.LastOrDefault(s.ArmyFactions.ContainsKey) ?? "") + 1;
                configuration["factions"] = List(s.PlayableArmies.Take(count).Select(army =>
                    List((s.ArmyFactions.TryGetValue(army, out ImmutableArray<string> allowed) ? allowed.AsEnumerable() : AllFactions).Select(f => new LuaData.String(f)))));
            }
            Dictionary<string, string> customProperties = new Dictionary<string, string>();
            if (!s.ExtraArmies.IsEmpty)
            {
                customProperties["ExtraArmies"] = string.Join(' ', s.ExtraArmies);
            }

            return new MapScenario
            {
                Raw = new LuaData.Table(extra),
                Name = s.Name,
                Description = s.Description,
                Type = s.Type == ScenarioType.CampaignCoop ? "campaign_coop" : "skirmish",
                Starts = true,
                Preview = s.PreviewPath,
                Size = (d.Width, d.Height),
                Map = root + ".scmap",
                Save = root + "_save.lua",
                Script = root + "_script.lua",
                MapVersion = d.Version,
                NoRushRadius = s.NoRushRadius,
                Reclaim = s.Reclaim is Reclaim r ? (r.Mass, r.Energy) : null,
                AdaptiveMap = s.AdaptiveMap ? true : null,
                Configurations = [new MapConfiguration("standard", Teams(s), customProperties, new LuaData.Table(configuration))],
            };
        }

        /// <summary>
        /// The team <c>FFA</c> of the playable armies, which the game reads, then the map maker's teams.
        /// </summary>
        private static List<MapTeam> Teams(ScenarioInfo s)
        {
            HashSet<string> playable = new HashSet<string>(s.PlayableArmies, StringComparer.Ordinal);
            List<MapTeam> teams = [new MapTeam("FFA", [.. s.PlayableArmies])];
            foreach (Team team in s.Teams)
            {
                string[] armies = [.. team.Armies.Where(playable.Contains)];
                if (armies.Length > 0)
                {
                    teams.Add(new MapTeam(team.Name, armies));
                }
            }
            return teams;
        }

        /// <summary>
        /// An army of the save file, with the economy (0 mass and energy) and personality (empty)
        /// that every map of the vault gives its armies.
        /// </summary>
        private static MapArmy ToMapArmy(Army a) => new MapArmy(
            a.Name,
            "",
            a.Plans ?? "",
            a.Color,
            a.Faction,
            new MapEconomy(0, 0),
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
