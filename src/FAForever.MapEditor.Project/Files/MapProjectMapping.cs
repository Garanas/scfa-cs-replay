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

        /// <summary>
        /// The masks for tools by name; none in a project without them.
        /// </summary>
        public ImmutableSortedDictionary<string, TiledBuffer<byte>> Masks { get; init; } =
            ImmutableSortedDictionary.Create<string, TiledBuffer<byte>>(StringComparer.Ordinal);
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
            Symmetry = ToDto(document.Symmetry),
            Procedural = ToDto(document.Procedural),
        };

        /// <summary>
        /// The Procedural mode's settings as <c>map.json</c> keeps them; null for the defaults.
        /// </summary>
        internal static ProceduralDto? ToDto(ProceduralSettings settings) => settings.IsDefault
            ? null
            : new ProceduralDto
            {
                AutoGenerate = settings.AutoGenerate.IsEmpty ? null : [.. settings.AutoGenerate],
                NotOnExport = settings.NotOnExport.IsEmpty ? null : [.. settings.NotOnExport],
                DraftMasks = settings.DraftMasks.IsEmpty ? null : [.. settings.DraftMasks],
                WriteDraft = settings.WriteDraftOnChange ? true : null,
                PathNodes = ToDto(settings.PathNodes),
                SkyCube = settings.SkyCube.IsDefault ? null : new SkyCubeDto
                {
                    Resolution = settings.SkyCube.Resolution,
                    EyeHeight = settings.SkyCube.EyeHeight,
                    IncludeTerrain = settings.SkyCube.IncludeTerrain,
                    SunDisc = settings.SkyCube.SunDisc,
                },
            };

        /// <summary>
        /// The Procedural mode's settings of <c>map.json</c>; what this editor does not know (the routes
        /// of older editors) is left out.
        /// </summary>
        internal static ProceduralSettings ToModel(ProceduralDto? settings) => settings is null
            ? ProceduralSettings.Default
            : new ProceduralSettings
            {
                AutoGenerate = ImmutableSortedSet.CreateRange(StringComparer.Ordinal, settings.AutoGenerate ?? []),
                NotOnExport = ImmutableSortedSet.CreateRange(StringComparer.Ordinal, settings.NotOnExport ?? []),
                DraftMasks = ImmutableSortedSet.CreateRange(StringComparer.Ordinal, settings.DraftMasks ?? []),
                WriteDraftOnChange = settings.WriteDraft == true,
                PathNodes = ToModel(settings.PathNodes),
                SkyCube = settings.SkyCube is SkyCubeDto sky
                    ? new SkyCubeSettings
                    {
                        Resolution = SkyCubeSettings.ValidResolution(sky.Resolution ?? SkyCubeSettings.Default.Resolution),
                        EyeHeight = sky.EyeHeight ?? SkyCubeSettings.Default.EyeHeight,
                        IncludeTerrain = sky.IncludeTerrain ?? SkyCubeSettings.Default.IncludeTerrain,
                        SunDisc = sky.SunDisc ?? SkyCubeSettings.Default.SunDisc,
                    }
                    : SkyCubeSettings.Default,
            };

        /// <summary>
        /// The path networks' settings and edits as <c>map.json</c> keeps them; null for the defaults.
        /// </summary>
        internal static PathNodesDto? ToDto(PathNodeSettings settings) => settings.IsDefault
            ? null
            : new PathNodesDto
            {
                Layers = settings.Layers.SequenceEqual(PathNodeSettings.Default.Layers) ? null : [.. settings.Layers.Select(layer => layer.ToString())],
                Spacing = settings.Spacing,
                AirSpacing = settings.AirSpacing,
                Smoothing = settings.Smoothing == PathNodeSettings.DefaultSmoothing ? null : settings.Smoothing,
                MinimumDistance = settings.MinimumDistance,
                Fill = settings.Fill ? null : false,
                Replace = settings.Replace ? true : null,
                OnExport = settings.OnExport,
                Edits = settings.Edits.IsEmpty ? null : [.. settings.Edits.Select(edit => new PathEditDto
                {
                    Kind = edit.Kind.ToString(),
                    Layer = edit.Layer.ToString(),
                    X = edit.Node.X,
                    Z = edit.Node.Z,
                    Added = edit.Node.IsAdded ? edit.Node.Added : null,
                    OtherX = edit.Kind is PathEditKind.Link or PathEditKind.Cut ? edit.Other.X : null,
                    OtherZ = edit.Kind is PathEditKind.Link or PathEditKind.Cut ? edit.Other.Z : null,
                    OtherAdded = edit.Kind is PathEditKind.Link or PathEditKind.Cut && edit.Other.IsAdded ? edit.Other.Added : null,
                    ToX = edit.Kind == PathEditKind.Move ? edit.ToX : null,
                    ToZ = edit.Kind == PathEditKind.Move ? edit.ToZ : null,
                })],
            };

        /// <summary>
        /// The path networks' settings and edits of <c>map.json</c>; a layer or an edit this editor
        /// does not know is left out.
        /// </summary>
        internal static PathNodeSettings ToModel(PathNodesDto? settings)
        {
            if (settings is null)
            {
                return PathNodeSettings.Default;
            }
            static PathLayer? LayerOf(string? name) => Enum.TryParse(name, ignoreCase: true, out PathLayer layer) && Enum.IsDefined(layer) ? layer : null;
            return new PathNodeSettings
            {
                Layers = settings.Layers is null ? PathNodeSettings.Default.Layers : [.. settings.Layers.Select(LayerOf).OfType<PathLayer>().Distinct()],
                Spacing = settings.Spacing,
                AirSpacing = settings.AirSpacing,
                Smoothing = Math.Clamp(settings.Smoothing ?? PathNodeSettings.DefaultSmoothing, 0, 10),
                MinimumDistance = settings.MinimumDistance,
                Fill = settings.Fill ?? true,
                Replace = settings.Replace ?? false,
                OnExport = settings.OnExport,
                Edits = [.. (settings.Edits ?? []).Select(edit =>
                    Enum.TryParse(edit.Kind, ignoreCase: true, out PathEditKind kind) && Enum.IsDefined(kind) && LayerOf(edit.Layer) is PathLayer layer
                        ? new PathEdit(kind, layer, new PathNodeRef(edit.X, edit.Z, edit.Added ?? 0))
                        {
                            Other = new PathNodeRef(edit.OtherX ?? 0, edit.OtherZ ?? 0, edit.OtherAdded ?? 0),
                            ToX = edit.ToX ?? 0,
                            ToZ = edit.ToZ ?? 0,
                        }
                        : null).OfType<PathEdit>()],
            };
        }

        /// <summary>
        /// A symmetry as <c>map.json</c> and <c>layers.json</c> keep it; null for none.
        /// </summary>
        internal static SymmetryDto? ToDto(SymmetrySetting? symmetry) =>
            symmetry is null ? null : new SymmetryDto
            {
                Kind = symmetry.Kind.ToString(),
                Count = symmetry.Count,
                Around = symmetry.Around is FileFormats.Map.MapSymmetryArea area ? [area.X0, area.Z0, area.X1, area.Z1] : null,
            };

        /// <summary>
        /// The symmetry of <c>map.json</c>; null for none, or for a kind or count this editor does not know.
        /// A rectangle that is not one (four numbers, the second corner beyond the first) reads as the whole map.
        /// </summary>
        internal static SymmetrySetting? ToModel(SymmetryDto? symmetry) =>
            symmetry is not null && Enum.TryParse(symmetry.Kind, ignoreCase: true, out FileFormats.Map.MapSymmetryKind kind) && Enum.IsDefined(kind)
                && (!FileFormats.Map.MapSymmetry.HasCount(kind) || symmetry.Count is >= 2 and <= FileFormats.Map.MapSymmetry.MaximumCount)
                ? new SymmetrySetting(kind, FileFormats.Map.MapSymmetry.HasCount(kind) ? symmetry.Count : 2)
                {
                    Around = symmetry.Around is [float x0, float z0, float x1, float z1] && x1 > x0 && z1 > z0
                        ? new FileFormats.Map.MapSymmetryArea(x0, z0, x1, z1)
                        : null,
                }
                : null;

        public static DecalsDto ToDecalsDto(MapDocument document) => new DecalsDto
        {
            Decals = [.. document.Decals.Select(decal => ToDto(decal))],
            Groups = [.. document.DecalGroups.Select(group => ToDto(group))],
            MapWide = ToDto(document.WideDecals),
        };

        /// <summary>
        /// The map-wide decals as <c>decals.json</c> keeps them; null for none.
        /// </summary>
        internal static MapWideDecalsDto? ToDto(MapWideDecals wide)
        {
            if (wide.IsNone)
            {
                return null;
            }
            static MapWideSlotDto? Slot(MapWideSlot slot) => slot.Source == MapWideSource.None ? null : new MapWideSlotDto { Source = slot.Source.ToString(), File = slot.File };
            ShadowDecalSettings shadow = wide.ShadowSettings;
            ShadowDecalSettings defaults = ShadowDecalSettings.Default;
            return new MapWideDecalsDto
            {
                Albedo = Slot(wide.Albedo),
                Specular = wide.Specular,
                Normal = Slot(wide.Normal),
                Shadow = Slot(wide.Shadow),
                ShadowSettings = shadow == defaults ? null : new ShadowDecalSettingsDto
                {
                    Radius = shadow.Radius == defaults.Radius ? null : shadow.Radius,
                    Strength = shadow.Strength == defaults.Strength ? null : shadow.Strength,
                    Darkest = shadow.Darkest == defaults.Darkest ? null : shadow.Darkest,
                    SunShadows = shadow.SunShadows ? true : null,
                },
            };
        }

        /// <summary>
        /// The map-wide decals of <c>decals.json</c>; a slot of a source this editor does not know,
        /// or a file slot without its file, is none.
        /// </summary>
        internal static MapWideDecals ToModel(MapWideDecalsDto? wide)
        {
            if (wide is null)
            {
                return MapWideDecals.None;
            }
            static MapWideSlot Slot(MapWideSlotDto? slot, bool generates) =>
                slot is null || !Enum.TryParse(slot.Source, ignoreCase: true, out MapWideSource source) || !Enum.IsDefined(source) ? MapWideSlot.None
                : source == MapWideSource.File ? (string.IsNullOrWhiteSpace(slot.File) ? MapWideSlot.None : MapWideSlot.Of(slot.File))
                : source == MapWideSource.Generated && !generates ? MapWideSlot.None
                : new MapWideSlot(source);
            ShadowDecalSettings defaults = ShadowDecalSettings.Default;
            return new MapWideDecals
            {
                Albedo = Slot(wide.Albedo, generates: false),
                Specular = string.IsNullOrWhiteSpace(wide.Specular) ? null : wide.Specular,
                Normal = Slot(wide.Normal, generates: true),
                Shadow = Slot(wide.Shadow, generates: true),
                ShadowSettings = wide.ShadowSettings is ShadowDecalSettingsDto shadow ? new ShadowDecalSettings
                {
                    Radius = shadow.Radius is float radius && radius > 0 ? radius : defaults.Radius,
                    Strength = shadow.Strength ?? defaults.Strength,
                    Darkest = shadow.Darkest is float darkest ? Math.Clamp(darkest, 0, 1) : defaults.Darkest,
                    SunShadows = shadow.SunShadows ?? false,
                } : defaults,
            };
        }

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
                Symmetry = ToModel(map.Symmetry),
                Procedural = ToModel(map.Procedural),
                Decals = [.. decals.Decals.Select(decal => ToModel(decal))],
                DecalGroups = [.. decals.Groups.Select(group => ToModel(group))],
                WideDecals = ToModel(decals.MapWide),
                Props = [.. props.Select((prop, index) => ToModel(prop, index + 1))],
                Markers = ToModel(markers),
                Armies = [.. armies.Select(army => ToModel(army))],
                Script = script,
                Files = files,
                Masks = images.Masks,
            };
        }

        /// <summary>
        /// The document with what <c>map.json</c> holds replaced: name, scenario and the settings of
        /// terrain, light, water and sky. The grids stay, so the size cannot change.
        /// </summary>
        /// <exception cref="MapProjectException">The file gives the map another size.</exception>
        public static MapDocument WithMap(MapDocument document, MapProjectDto map)
        {
            if (map.Terrain.Width != document.Width || map.Terrain.Height != document.Height)
            {
                throw new MapProjectException($"{MapProjectLayout.Map} makes the map {map.Terrain.Width} by {map.Terrain.Height}, not {document.Width} by {document.Height}: open the project again to change its size");
            }

            Terrain terrain = document.Terrain;
            Water water = document.Water;
            MapProjectImages images = new MapProjectImages
            {
                Heightmap = terrain.Heightmap.Samples,
                TerrainTypes = terrain.Types,
                StratumMasks = terrain.StratumMasks,
                NormalMap = terrain.NormalMap,
                Preview = terrain.Preview,
                WaterMap = water.Map,
                WaterFoamMask = water.FoamMask,
                WaterFlatnessMask = water.FlatnessMask,
                WaterDepthBiasMask = water.DepthBiasMask,
            };
            return document with
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
                Symmetry = ToModel(map.Symmetry),
                Procedural = ToModel(map.Procedural),
            };
        }

        /// <summary>
        /// The document with the props of <c>props.json</c>, numbered anew.
        /// </summary>
        public static MapDocument WithProps(MapDocument document, List<PropDto> props) =>
            document with { Props = [.. props.Select((prop, index) => ToModel(prop, index + 1))] };

        /// <summary>
        /// The document with the decals and groups of <c>decals.json</c>.
        /// </summary>
        public static MapDocument WithDecals(MapDocument document, DecalsDto decals) => document with
        {
            Decals = [.. decals.Decals.Select(decal => ToModel(decal))],
            DecalGroups = [.. decals.Groups.Select(group => ToModel(group))],
            WideDecals = ToModel(decals.MapWide),
        };

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
            NormalMapTileWidth = source.NormalMapTileWidth,
            NormalMapTileHeight = source.NormalMapTileHeight,
        };

        private static Terrain ToModel(TerrainDto source, MapProjectImages images) => new Terrain
        {
            ScmapVersion = source.ScmapVersion,
            Heightmap = new Heightmap { Scale = source.HeightScale, Samples = images.Heightmap },
            Types = images.TerrainTypes,
            Shader = source.Shader,
            Tileset = source.Tileset,
            // a version 56 map holds ten layers; a project made from a settings template by an
            // earlier editor kept six, which the game's file cannot hold: the strata it lacks are empty
            Layers = source.ScmapVersion >= NewMap.TenLayersFrom && source.Layers.Count < NewMap.LayerCount
                ? NewMap.FitLayers([.. source.Layers.Select(layer => ToModel(layer))])
                : [.. source.Layers.Select(layer => ToModel(layer))],
            StratumMasks = images.StratumMasks,
            NormalMap = images.NormalMap,
            NormalMapFormat = source.NormalMapFormat,
            NormalMapTileWidth = source.NormalMapTileWidth,
            NormalMapTileHeight = source.NormalMapTileHeight,
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
            Rule = source.Rule,
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
            Rule = source.Rule,
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
