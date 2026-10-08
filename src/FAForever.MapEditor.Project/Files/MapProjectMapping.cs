using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Files.Serialized;

namespace FAForever.MapEditor.Project.Files
{
    /// <summary>
    /// The images of a project as read from its files, for <see cref="MapProjectMapping.ToDocument"/>.
    /// </summary>
    internal sealed record MapProjectImages
    {
        public required TiledBuffer<ushort> Heightmap { get; init; }

        public required TiledBuffer<byte> TerrainTypes { get; init; }

        public required StratumMasks StratumMasks { get; init; }

        public required TiledBuffer<Color32> NormalMap { get; init; }

        public required TiledBuffer<Color32> Preview { get; init; }

        public required TiledBuffer<Color32> WaterMap { get; init; }

        public required TiledBuffer<byte> WaterFoamMask { get; init; }

        public required TiledBuffer<byte> WaterFlatnessMask { get; init; }

        public required TiledBuffer<byte> WaterDepthBiasMask { get; init; }
    }

    /// <summary>
    /// Translates between a document in memory (<see cref="InMemory"/>) and the records of its files
    /// (<see cref="Serialized"/>). The images are not part of it: <see cref="MapProjectStore"/> reads
    /// and writes them.
    /// </summary>
    internal static partial class MapProjectMapping
    {
        public static MapProjectDto ToDto(MapDocument document) => new MapProjectDto
        {
            FormatVersion = MapProjectDto.CurrentFormatVersion,
            Name = document.Name,
            FileName = document.FileName,
            Version = document.Version,
            Scenario = ToDto(document.Scenario),
            Terrain = ToDto(document.Terrain),
            Lighting = ToDto(document.Lighting),
            Water = ToDto(document.Water),
            Sky = ToDto(document.Sky),
            Minimap = document.Minimap is null ? null : ToDto(document.Minimap),
        };

        public static DecalsDto ToDecalsDto(MapDocument document) => new DecalsDto
        {
            Header = ToDto(document.DecalHeader),
            Decals = [.. document.Decals.Select(decal => ToDto(decal))],
            Groups = [.. document.DecalGroups.Select(group => ToDto(group))],
        };

        public static MapDocument ToDocument(
            MapProjectDto map,
            IReadOnlyList<PropDto> props,
            DecalsDto decals,
            MarkerSetDto markers,
            IReadOnlyList<ArmyDto> armies,
            MapProjectImages images,
            ImmutableArray<byte>? script,
            ImmutableSortedDictionary<string, ImmutableArray<byte>> files)
        {
            if (map.FormatVersion > MapProjectDto.CurrentFormatVersion)
            {
                throw new MapProjectException($"{MapProjectLayout.Map} is of format {map.FormatVersion}; this editor reads up to {MapProjectDto.CurrentFormatVersion}");
            }

            return new MapDocument
            {
                Name = map.Name,
                FileName = map.FileName,
                Version = map.Version,
                Scenario = ToModel(map.Scenario),
                Terrain = ToModel(map.Terrain, images),
                Lighting = ToModel(map.Lighting),
                Water = ToModel(map.Water, images),
                Sky = ToModel(map.Sky),
                Minimap = map.Minimap is null ? null : ToModel(map.Minimap),
                DecalHeader = ToModel(decals.Header),
                Decals = [.. decals.Decals.Select(decal => ToModel(decal))],
                DecalGroups = [.. decals.Groups.Select(group => ToModel(group))],
                Props = [.. props.Select((prop, index) => ToModel(prop, index + 1))],
                Markers = ToModel(markers),
                Armies = [.. armies.Select(army => ToModel(army))],
                Script = script,
                Files = files,
            };
        }

        private static TerrainDto ToDto(Terrain source) => new TerrainDto
        {
            ScmapVersion = source.ScmapVersion,
            Width = source.Heightmap.Width,
            Height = source.Heightmap.Height,
            HeightScale = source.Heightmap.Scale,
            Shader = source.Shader,
            Tileset = source.Tileset,
            Layers = [.. source.Layers.Select(layer => ToDto(layer))],
            NormalMapFormat = source.NormalMapFormat,
        };

        private static Terrain ToModel(TerrainDto source, MapProjectImages images) => new Terrain
        {
            ScmapVersion = source.ScmapVersion,
            Heightmap = new Heightmap { Scale = source.HeightScale, Samples = images.Heightmap },
            Types = images.TerrainTypes,
            Shader = source.Shader,
            Tileset = source.Tileset,
            Layers = [.. source.Layers.Select(layer => ToModel(layer))],
            StratumMasks = images.StratumMasks,
            NormalMap = images.NormalMap,
            NormalMapFormat = source.NormalMapFormat,
            Preview = images.Preview,
        };

        private static WaterDto ToDto(Water source) => new WaterDto
        {
            HasWater = source.HasWater,
            Elevation = source.Elevation,
            ElevationDeep = source.ElevationDeep,
            ElevationAbyss = source.ElevationAbyss,
            SurfaceColor = Dto(source.SurfaceColor),
            ColorLerp = Dto(source.ColorLerp),
            RefractionScale = source.RefractionScale,
            FresnelBias = source.FresnelBias,
            FresnelPower = source.FresnelPower,
            UnitReflection = source.UnitReflection,
            SkyReflection = source.SkyReflection,
            SunShininess = source.SunShininess,
            SunStrength = source.SunStrength,
            SunDirection = Dto(source.SunDirection),
            SunColor = Dto(source.SunColor),
            SunReflection = source.SunReflection,
            SunGlow = source.SunGlow,
            Cubemap = source.Cubemap,
            Ramp = source.Ramp,
            WaveTextures = [.. source.WaveTextures.Select(texture => ToDto(texture))],
            WaveGenerators = [.. source.WaveGenerators.Select(generator => ToDto(generator))],
            MapFormat = source.MapFormat,
        };

        private static Water ToModel(WaterDto source, MapProjectImages images) => new Water
        {
            HasWater = source.HasWater,
            Elevation = source.Elevation,
            ElevationDeep = source.ElevationDeep,
            ElevationAbyss = source.ElevationAbyss,
            SurfaceColor = Model(source.SurfaceColor),
            ColorLerp = Model(source.ColorLerp),
            RefractionScale = source.RefractionScale,
            FresnelBias = source.FresnelBias,
            FresnelPower = source.FresnelPower,
            UnitReflection = source.UnitReflection,
            SkyReflection = source.SkyReflection,
            SunShininess = source.SunShininess,
            SunStrength = source.SunStrength,
            SunDirection = Model(source.SunDirection),
            SunColor = Model(source.SunColor),
            SunReflection = source.SunReflection,
            SunGlow = source.SunGlow,
            Cubemap = source.Cubemap,
            Ramp = source.Ramp,
            WaveTextures = [.. source.WaveTextures.Select(texture => ToModel(texture))],
            WaveGenerators = [.. source.WaveGenerators.Select(generator => ToModel(generator))],
            Map = images.WaterMap,
            MapFormat = source.MapFormat,
            FoamMask = images.WaterFoamMask,
            FlatnessMask = images.WaterFlatnessMask,
            DepthBiasMask = images.WaterDepthBiasMask,
        };

        public static PropDto ToDto(Prop source) => new PropDto
        {
            Blueprint = source.Blueprint,
            Position = Dto(source.Position),
            RotationX = Dto(source.RotationX),
            RotationY = Dto(source.RotationY),
            RotationZ = Dto(source.RotationZ),
            Scale = Dto(source.Scale),
        };

        /// <summary>
        /// A prop of the file, with the id it gets in memory.
        /// </summary>
        public static Prop ToModel(PropDto source, int id) => new Prop
        {
            Id = id,
            Blueprint = source.Blueprint,
            Position = Model(source.Position),
            RotationX = Model(source.RotationX),
            RotationY = Model(source.RotationY),
            RotationZ = Model(source.RotationZ),
            Scale = Model(source.Scale),
        };

        private static Vector2Dto Dto(Vector2 v) => new Vector2Dto { X = v.X, Y = v.Y };

        private static Vector3Dto Dto(Vector3 v) => new Vector3Dto { X = v.X, Y = v.Y, Z = v.Z };

        private static Vector4Dto Dto(Vector4 v) => new Vector4Dto { X = v.X, Y = v.Y, Z = v.Z, W = v.W };

        /// <summary>
        /// A vector that is usually zero, such as the orientation of a marker: left out when it is.
        /// </summary>
        private static Vector3Dto? Optional(Vector3 v) => v == Vector3.Zero ? null : Dto(v);

        private static Vector2 Model(Vector2Dto v) => new Vector2(v.X, v.Y);

        private static Vector3 Model(Vector3Dto v) => new Vector3(v.X, v.Y, v.Z);

        private static Vector4 Model(Vector4Dto v) => new Vector4(v.X, v.Y, v.Z, v.W);

        private static string Argb(Color32 color) => $"#{color.ToArgb():x8}";

        private static string Rgb(Color32 color) => $"#{color.Red:x2}{color.Green:x2}{color.Blue:x2}";

        /// <summary>
        /// A colour written as <c>#aarrggbb</c>.
        /// </summary>
        private static Color32 ParseArgb(string text, string field) => Color32.FromArgb(ParseHex(text, 8, field));

        /// <summary>
        /// A colour written as <c>#rrggbb</c>, which is opaque.
        /// </summary>
        private static Color32 ParseRgb(string text, string field) => Color32.FromArgb(0xff000000 | ParseHex(text, 6, field));

        private static uint ParseHex(string text, int digits, string field) =>
            text.Length == digits + 1 && text[0] == '#' && uint.TryParse(text.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint value)
                ? value
                : throw new MapProjectException($"{field}: '{text}' is not a colour of # and {digits} hexadecimal digits");
    }
}
