using System.Collections.Immutable;
using System.Numerics;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Files.Serialized;

namespace FAForever.MapEditor.Project.Files
{
    // The types that are the same in memory and on disk, property by property.
    internal static partial class MapProjectMapping
    {
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
