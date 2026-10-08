using System.Collections.Immutable;
using System.Numerics;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Files.Serialized;

namespace FAForever.MapEditor.Project.Files
{
    // The types that are the same in memory and on disk, property by property.
    internal static partial class MapProjectMapping
    {
        public static ScenarioInfoDto ToDto(ScenarioInfo source) => new ScenarioInfoDto
        {
            Name = source.Name,
            Description = source.Description,
            Author = source.Author,
            Type = source.Type,
            Starts = source.Starts,
            PreviewPath = source.PreviewPath,
            NoRushRadius = source.NoRushRadius,
            NoRushOffsets = source.NoRushOffsets.ToDictionary(entry => entry.Key, entry => ToDto(entry.Value)),
            AdaptiveMap = source.AdaptiveMap,
            Reclaim = source.Reclaim is null ? null : ToDto(source.Reclaim),
            Configurations = [.. source.Configurations.Select(item => ToDto(item))],
        };

        public static ScenarioInfo ToModel(ScenarioInfoDto source) => new ScenarioInfo
        {
            Name = source.Name,
            Description = source.Description,
            Author = source.Author,
            Type = source.Type,
            Starts = source.Starts,
            PreviewPath = source.PreviewPath,
            NoRushRadius = source.NoRushRadius,
            NoRushOffsets = source.NoRushOffsets.ToImmutableSortedDictionary(entry => entry.Key, entry => ToModel(entry.Value), StringComparer.Ordinal),
            AdaptiveMap = source.AdaptiveMap,
            Reclaim = source.Reclaim is null ? null : ToModel(source.Reclaim),
            Configurations = [.. source.Configurations.Select(item => ToModel(item))],
        };

        public static ReclaimDto ToDto(Reclaim source) => new ReclaimDto
        {
            Mass = source.Mass,
            Energy = source.Energy,
        };

        public static Reclaim ToModel(ReclaimDto source) => new Reclaim
        {
            Mass = source.Mass,
            Energy = source.Energy,
        };

        public static NoRushOffsetDto ToDto(NoRushOffset source) => new NoRushOffsetDto
        {
            X = source.X,
            Y = source.Y,
        };

        public static NoRushOffset ToModel(NoRushOffsetDto source) => new NoRushOffset
        {
            X = source.X,
            Y = source.Y,
        };

        public static ConfigurationDto ToDto(Configuration source) => new ConfigurationDto
        {
            Name = source.Name,
            Teams = [.. source.Teams.Select(item => ToDto(item))],
            CustomProperties = new Dictionary<string, string>(source.CustomProperties),
            Factions = source.Factions is null ? null : [.. source.Factions.Value.Select(list => list.ToList())],
        };

        public static Configuration ToModel(ConfigurationDto source) => new Configuration
        {
            Name = source.Name,
            Teams = [.. source.Teams.Select(item => ToModel(item))],
            CustomProperties = source.CustomProperties.ToImmutableSortedDictionary(StringComparer.Ordinal),
            Factions = source.Factions is null ? null : ImmutableArray.CreateRange(source.Factions.Select(list => list.ToImmutableArray())),
        };

        public static TeamDto ToDto(Team source) => new TeamDto
        {
            Name = source.Name,
            Armies = [.. source.Armies],
        };

        public static Team ToModel(TeamDto source) => new Team
        {
            Name = source.Name,
            Armies = [.. source.Armies],
        };

        public static TerrainLayerDto ToDto(TerrainLayer source) => new TerrainLayerDto
        {
            AlbedoPath = source.AlbedoPath,
            AlbedoScale = source.AlbedoScale,
            NormalPath = source.NormalPath,
            NormalScale = source.NormalScale,
        };

        public static TerrainLayer ToModel(TerrainLayerDto source) => new TerrainLayer
        {
            AlbedoPath = source.AlbedoPath,
            AlbedoScale = source.AlbedoScale,
            NormalPath = source.NormalPath,
            NormalScale = source.NormalScale,
        };

        public static LightingDto ToDto(Lighting source) => new LightingDto
        {
            Multiplier = source.Multiplier,
            SunDirection = Dto(source.SunDirection),
            SunAmbience = Dto(source.SunAmbience),
            SunColor = Dto(source.SunColor),
            ShadowFillColor = Dto(source.ShadowFillColor),
            SpecularColor = Dto(source.SpecularColor),
            Bloom = source.Bloom,
            FogColor = Dto(source.FogColor),
            FogStart = source.FogStart,
            FogEnd = source.FogEnd,
        };

        public static Lighting ToModel(LightingDto source) => new Lighting
        {
            Multiplier = source.Multiplier,
            SunDirection = Model(source.SunDirection),
            SunAmbience = Model(source.SunAmbience),
            SunColor = Model(source.SunColor),
            ShadowFillColor = Model(source.ShadowFillColor),
            SpecularColor = Model(source.SpecularColor),
            Bloom = source.Bloom,
            FogColor = Model(source.FogColor),
            FogStart = source.FogStart,
            FogEnd = source.FogEnd,
        };

        public static WaveTextureDto ToDto(WaveTexture source) => new WaveTextureDto
        {
            Texture = source.Texture,
            NormalRepeat = source.NormalRepeat,
            NormalMovement = Dto(source.NormalMovement),
        };

        public static WaveTexture ToModel(WaveTextureDto source) => new WaveTexture
        {
            Texture = source.Texture,
            NormalRepeat = source.NormalRepeat,
            NormalMovement = Model(source.NormalMovement),
        };

        public static WaveGeneratorDto ToDto(WaveGenerator source) => new WaveGeneratorDto
        {
            Texture = source.Texture,
            Ramp = source.Ramp,
            Position = Dto(source.Position),
            Rotation = source.Rotation,
            Velocity = Dto(source.Velocity),
            LifetimeFirst = source.LifetimeFirst,
            LifetimeSecond = source.LifetimeSecond,
            PeriodFirst = source.PeriodFirst,
            PeriodSecond = source.PeriodSecond,
            ScaleFirst = source.ScaleFirst,
            ScaleSecond = source.ScaleSecond,
            FrameCount = source.FrameCount,
            FrameRateFirst = source.FrameRateFirst,
            FrameRateSecond = source.FrameRateSecond,
            StripCount = source.StripCount,
        };

        public static WaveGenerator ToModel(WaveGeneratorDto source) => new WaveGenerator
        {
            Texture = source.Texture,
            Ramp = source.Ramp,
            Position = Model(source.Position),
            Rotation = source.Rotation,
            Velocity = Model(source.Velocity),
            LifetimeFirst = source.LifetimeFirst,
            LifetimeSecond = source.LifetimeSecond,
            PeriodFirst = source.PeriodFirst,
            PeriodSecond = source.PeriodSecond,
            ScaleFirst = source.ScaleFirst,
            ScaleSecond = source.ScaleSecond,
            FrameCount = source.FrameCount,
            FrameRateFirst = source.FrameRateFirst,
            FrameRateSecond = source.FrameRateSecond,
            StripCount = source.StripCount,
        };

        public static SkyDto ToDto(Sky source) => new SkyDto
        {
            Background = source.Background,
            Cubemap = source.Cubemap,
            EnvironmentCubemaps = [.. source.EnvironmentCubemaps.Select(item => ToDto(item))],
            Skybox = source.Skybox is null ? null : ToDto(source.Skybox),
        };

        public static Sky ToModel(SkyDto source) => new Sky
        {
            Background = source.Background,
            Cubemap = source.Cubemap,
            EnvironmentCubemaps = [.. source.EnvironmentCubemaps.Select(item => ToModel(item))],
            Skybox = source.Skybox is null ? null : ToModel(source.Skybox),
        };

        public static CubemapDto ToDto(Cubemap source) => new CubemapDto
        {
            Name = source.Name,
            Texture = source.Texture,
        };

        public static Cubemap ToModel(CubemapDto source) => new Cubemap
        {
            Name = source.Name,
            Texture = source.Texture,
        };

        public static SkyboxDto ToDto(Skybox source) => new SkyboxDto
        {
            Position = Dto(source.Position),
            HorizonHeight = source.HorizonHeight,
            Scale = source.Scale,
            SubtractHeight = source.SubtractHeight,
            SubdivisionsAxis = source.SubdivisionsAxis,
            SubdivisionsHeight = source.SubdivisionsHeight,
            ZenithHeight = source.ZenithHeight,
            HorizonColor = Dto(source.HorizonColor),
            ZenithColor = Dto(source.ZenithColor),
            DecalGlowMultiplier = source.DecalGlowMultiplier,
            Albedo = source.Albedo,
            Glow = source.Glow,
            Planets = [.. source.Planets.Select(item => ToDto(item))],
            MidColor = Rgb(source.MidColor),
            CirrusMultiplier = source.CirrusMultiplier,
            CirrusColor = Dto(source.CirrusColor),
            CirrusTexture = source.CirrusTexture,
            CirrusLayers = [.. source.CirrusLayers.Select(item => ToDto(item))],
            Clouds7 = source.Clouds7,
        };

        public static Skybox ToModel(SkyboxDto source) => new Skybox
        {
            Position = Model(source.Position),
            HorizonHeight = source.HorizonHeight,
            Scale = source.Scale,
            SubtractHeight = source.SubtractHeight,
            SubdivisionsAxis = source.SubdivisionsAxis,
            SubdivisionsHeight = source.SubdivisionsHeight,
            ZenithHeight = source.ZenithHeight,
            HorizonColor = Model(source.HorizonColor),
            ZenithColor = Model(source.ZenithColor),
            DecalGlowMultiplier = source.DecalGlowMultiplier,
            Albedo = source.Albedo,
            Glow = source.Glow,
            Planets = [.. source.Planets.Select(item => ToModel(item))],
            MidColor = ParseRgb(source.MidColor, "Skybox.MidColor"),
            CirrusMultiplier = source.CirrusMultiplier,
            CirrusColor = Model(source.CirrusColor),
            CirrusTexture = source.CirrusTexture,
            CirrusLayers = [.. source.CirrusLayers.Select(item => ToModel(item))],
            Clouds7 = source.Clouds7,
        };

        public static PlanetDto ToDto(Planet source) => new PlanetDto
        {
            Position = Dto(source.Position),
            Rotation = source.Rotation,
            Scale = Dto(source.Scale),
            Uv = Dto(source.Uv),
        };

        public static Planet ToModel(PlanetDto source) => new Planet
        {
            Position = Model(source.Position),
            Rotation = source.Rotation,
            Scale = Model(source.Scale),
            Uv = Model(source.Uv),
        };

        public static CirrusLayerDto ToDto(CirrusLayer source) => new CirrusLayerDto
        {
            Frequency = Dto(source.Frequency),
            Speed = source.Speed,
            Direction = Dto(source.Direction),
        };

        public static CirrusLayer ToModel(CirrusLayerDto source) => new CirrusLayer
        {
            Frequency = Model(source.Frequency),
            Speed = source.Speed,
            Direction = Model(source.Direction),
        };

        public static MinimapDto ToDto(Minimap source) => new MinimapDto
        {
            ContourInterval = source.ContourInterval,
            DeepWaterColor = Argb(source.DeepWaterColor),
            ContourColor = Argb(source.ContourColor),
            ShoreColor = Argb(source.ShoreColor),
            LandStartColor = Argb(source.LandStartColor),
            LandEndColor = Argb(source.LandEndColor),
            Unknown = source.Unknown,
        };

        public static Minimap ToModel(MinimapDto source) => new Minimap
        {
            ContourInterval = source.ContourInterval,
            DeepWaterColor = ParseArgb(source.DeepWaterColor, "Minimap.DeepWaterColor"),
            ContourColor = ParseArgb(source.ContourColor, "Minimap.ContourColor"),
            ShoreColor = ParseArgb(source.ShoreColor, "Minimap.ShoreColor"),
            LandStartColor = ParseArgb(source.LandStartColor, "Minimap.LandStartColor"),
            LandEndColor = ParseArgb(source.LandEndColor, "Minimap.LandEndColor"),
            Unknown = source.Unknown,
        };

        public static DecalDto ToDto(Decal source) => new DecalDto
        {
            Id = source.Id,
            Type = source.Type,
            Textures = [.. source.Textures],
            Scale = Dto(source.Scale),
            Position = Dto(source.Position),
            Rotation = Dto(source.Rotation),
            CutOffLod = source.CutOffLod,
            NearCutOffLod = source.NearCutOffLod,
            OwnerArmy = source.OwnerArmy,
        };

        public static Decal ToModel(DecalDto source) => new Decal
        {
            Id = source.Id,
            Type = source.Type,
            Textures = [.. source.Textures],
            Scale = Model(source.Scale),
            Position = Model(source.Position),
            Rotation = Model(source.Rotation),
            CutOffLod = source.CutOffLod,
            NearCutOffLod = source.NearCutOffLod,
            OwnerArmy = source.OwnerArmy,
        };

        public static DecalGroupDto ToDto(DecalGroup source) => new DecalGroupDto
        {
            Id = source.Id,
            Name = source.Name,
            DecalIds = [.. source.DecalIds],
        };

        public static DecalGroup ToModel(DecalGroupDto source) => new DecalGroup
        {
            Id = source.Id,
            Name = source.Name,
            DecalIds = [.. source.DecalIds],
        };

        public static DecalHeaderDto ToDto(DecalHeader source) => new DecalHeaderDto
        {
            First = source.First,
            Second = source.Second,
        };

        public static DecalHeader ToModel(DecalHeaderDto source) => new DecalHeader
        {
            First = source.First,
            Second = source.Second,
        };

        public static MarkerSetDto ToDto(MarkerSet source) => new MarkerSetDto
        {
            Spawns = [.. source.Spawns.Select(item => ToDto(item))],
            Resources = [.. source.Resources.Select(item => ToDto(item))],
            PathNodes = [.. source.PathNodes.Select(item => ToDto(item))],
            AiMarkers = [.. source.AiMarkers.Select(item => ToDto(item))],
            WeatherDefinitions = [.. source.WeatherDefinitions.Select(item => ToDto(item))],
            WeatherGenerators = [.. source.WeatherGenerators.Select(item => ToDto(item))],
            Areas = [.. source.Areas.Select(item => ToDto(item))],
            Chains = [.. source.Chains.Select(item => ToDto(item))],
        };

        public static MarkerSet ToModel(MarkerSetDto source) => new MarkerSet
        {
            Spawns = [.. source.Spawns.Select(item => ToModel(item))],
            Resources = [.. source.Resources.Select(item => ToModel(item))],
            PathNodes = [.. source.PathNodes.Select(item => ToModel(item))],
            AiMarkers = [.. source.AiMarkers.Select(item => ToModel(item))],
            WeatherDefinitions = [.. source.WeatherDefinitions.Select(item => ToModel(item))],
            WeatherGenerators = [.. source.WeatherGenerators.Select(item => ToModel(item))],
            Areas = [.. source.Areas.Select(item => ToModel(item))],
            Chains = [.. source.Chains.Select(item => ToModel(item))],
        };

        public static SpawnDto ToDto(Spawn source) => new SpawnDto
        {
            Army = source.Army,
            Position = Dto(source.Position),
            Orientation = Optional(source.Orientation),
        };

        public static Spawn ToModel(SpawnDto source) => new Spawn
        {
            Army = source.Army,
            Position = Model(source.Position),
            Orientation = source.Orientation is null ? Vector3.Zero : Model(source.Orientation),
        };

        public static ResourceMarkerDto ToDto(ResourceMarker source) => new ResourceMarkerDto
        {
            Name = source.Name,
            Kind = source.Kind,
            Position = Dto(source.Position),
            Orientation = Optional(source.Orientation),
            SpawnWithArmy = source.SpawnWithArmy,
        };

        public static ResourceMarker ToModel(ResourceMarkerDto source) => new ResourceMarker
        {
            Name = source.Name,
            Kind = source.Kind,
            Position = Model(source.Position),
            Orientation = source.Orientation is null ? Vector3.Zero : Model(source.Orientation),
            SpawnWithArmy = source.SpawnWithArmy,
        };

        public static PathNodeDto ToDto(PathNode source) => new PathNodeDto
        {
            Name = source.Name,
            Layer = source.Layer,
            Position = Dto(source.Position),
            Orientation = Optional(source.Orientation),
            AdjacentTo = [.. source.AdjacentTo],
        };

        public static PathNode ToModel(PathNodeDto source) => new PathNode
        {
            Name = source.Name,
            Layer = source.Layer,
            Position = Model(source.Position),
            Orientation = source.Orientation is null ? Vector3.Zero : Model(source.Orientation),
            AdjacentTo = [.. source.AdjacentTo],
        };

        public static AiMarkerDto ToDto(AiMarker source) => new AiMarkerDto
        {
            Name = source.Name,
            Type = source.Type,
            Position = Dto(source.Position),
            Orientation = Optional(source.Orientation),
        };

        public static AiMarker ToModel(AiMarkerDto source) => new AiMarker
        {
            Name = source.Name,
            Type = source.Type,
            Position = Model(source.Position),
            Orientation = source.Orientation is null ? Vector3.Zero : Model(source.Orientation),
        };

        public static WeatherDefinitionDto ToDto(WeatherDefinition source) => new WeatherDefinitionDto
        {
            Name = source.Name,
            Position = Dto(source.Position),
            Orientation = Optional(source.Orientation),
            MapStyle = source.MapStyle,
            Description = source.Description,
            DriftDirection = Dto(source.DriftDirection),
            Types = [.. source.Types.Select(item => ToDto(item))],
        };

        public static WeatherDefinition ToModel(WeatherDefinitionDto source) => new WeatherDefinition
        {
            Name = source.Name,
            Position = Model(source.Position),
            Orientation = source.Orientation is null ? Vector3.Zero : Model(source.Orientation),
            MapStyle = source.MapStyle,
            Description = source.Description,
            DriftDirection = Model(source.DriftDirection),
            Types = [.. source.Types.Select(item => ToModel(item))],
        };

        public static WeatherChanceDto ToDto(WeatherChance source) => new WeatherChanceDto
        {
            Type = source.Type,
            Chance = source.Chance,
        };

        public static WeatherChance ToModel(WeatherChanceDto source) => new WeatherChance
        {
            Type = source.Type,
            Chance = source.Chance,
        };

        public static WeatherGeneratorDto ToDto(WeatherGenerator source) => new WeatherGeneratorDto
        {
            Name = source.Name,
            Position = Dto(source.Position),
            Orientation = Optional(source.Orientation),
            ForceType = source.ForceType,
            SpawnChance = source.SpawnChance,
            CloudCount = source.CloudCount,
            CloudCountRange = source.CloudCountRange,
            CloudEmitterScale = source.CloudEmitterScale,
            CloudEmitterScaleRange = source.CloudEmitterScaleRange,
            CloudHeight = source.CloudHeight,
            CloudHeightRange = source.CloudHeightRange,
            CloudSpread = source.CloudSpread,
        };

        public static WeatherGenerator ToModel(WeatherGeneratorDto source) => new WeatherGenerator
        {
            Name = source.Name,
            Position = Model(source.Position),
            Orientation = source.Orientation is null ? Vector3.Zero : Model(source.Orientation),
            ForceType = source.ForceType,
            SpawnChance = source.SpawnChance,
            CloudCount = source.CloudCount,
            CloudCountRange = source.CloudCountRange,
            CloudEmitterScale = source.CloudEmitterScale,
            CloudEmitterScaleRange = source.CloudEmitterScaleRange,
            CloudHeight = source.CloudHeight,
            CloudHeightRange = source.CloudHeightRange,
            CloudSpread = source.CloudSpread,
        };

        public static AreaDto ToDto(Area source) => new AreaDto
        {
            Name = source.Name,
            X0 = source.X0,
            Z0 = source.Z0,
            X1 = source.X1,
            Z1 = source.Z1,
        };

        public static Area ToModel(AreaDto source) => new Area
        {
            Name = source.Name,
            X0 = source.X0,
            Z0 = source.Z0,
            X1 = source.X1,
            Z1 = source.Z1,
        };

        public static ChainDto ToDto(Chain source) => new ChainDto
        {
            Name = source.Name,
            Markers = [.. source.Markers],
        };

        public static Chain ToModel(ChainDto source) => new Chain
        {
            Name = source.Name,
            Markers = [.. source.Markers],
        };

        public static ArmyDto ToDto(Army source) => new ArmyDto
        {
            Name = source.Name,
            Personality = source.Personality,
            Plans = source.Plans,
            Color = source.Color,
            Faction = source.Faction,
            Economy = source.Economy is null ? null : ToDto(source.Economy),
            Alliances = new Dictionary<string, string>(source.Alliances),
            Units = ToDto(source.Units),
        };

        public static Army ToModel(ArmyDto source) => new Army
        {
            Name = source.Name,
            Personality = source.Personality,
            Plans = source.Plans,
            Color = source.Color,
            Faction = source.Faction,
            Economy = source.Economy is null ? null : ToModel(source.Economy),
            Alliances = source.Alliances.ToImmutableSortedDictionary(StringComparer.Ordinal),
            Units = ToModel(source.Units),
        };

        public static EconomyDto ToDto(Economy source) => new EconomyDto
        {
            Mass = source.Mass,
            Energy = source.Energy,
        };

        public static Economy ToModel(EconomyDto source) => new Economy
        {
            Mass = source.Mass,
            Energy = source.Energy,
        };

        public static UnitGroupDto ToDto(UnitGroup source) => new UnitGroupDto
        {
            Name = source.Name,
            Groups = [.. source.Groups.Select(item => ToDto(item))],
            Units = [.. source.Units.Select(item => ToDto(item))],
        };

        public static UnitGroup ToModel(UnitGroupDto source) => new UnitGroup
        {
            Name = source.Name,
            Groups = [.. source.Groups.Select(item => ToModel(item))],
            Units = [.. source.Units.Select(item => ToModel(item))],
        };

        public static UnitDto ToDto(Unit source) => new UnitDto
        {
            Name = source.Name,
            Blueprint = source.Blueprint,
            Position = Dto(source.Position),
            Orientation = Dto(source.Orientation),
        };

        public static Unit ToModel(UnitDto source) => new Unit
        {
            Name = source.Name,
            Blueprint = source.Blueprint,
            Position = Model(source.Position),
            Orientation = Model(source.Orientation),
        };
    }
}
