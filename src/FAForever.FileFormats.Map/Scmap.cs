using System.Numerics;

namespace FAForever.FileFormats.Map
{
    /// <summary>
    /// The binary part of a map (<c>.scmap</c>): terrain, textures, lighting, water, decals and props.
    /// Read it with <see cref="ScmapParser"/>. Markers, armies and the units placed at the start are
    /// in the map's <c>_save.lua</c> (<see cref="MapSave"/>), its name and teams in
    /// <c>_scenario.lua</c> (<see cref="MapScenario"/>).
    /// </summary>
    /// <remarks>
    /// The layout follows the engine's reader, <c>CWldMap::MapLoad</c> and <c>CWldTerrainRes::Load</c>
    /// as reconstructed in Draiget/faf-re (commit 2ba55b2, <c>src/sdk/moho/sim/CWldMap.cpp</c>), and
    /// the loader of the FAF map editor (<c>Assets/Scripts/HazardX SCMAP Code/Map.cs</c> in
    /// FAForever/FAForeverMapEditor). Textures (the preview, the normal map, the stratum masks, the
    /// water map) are kept as the DDS files they are stored as. The library's guide lists what the
    /// game does with each field.
    /// </remarks>
    public sealed record Scmap
    {
        /// <summary>
        /// The minor version of the format: 53 (Supreme Commander), 56 (Forged Alliance) or 60 (what
        /// the engine itself saves: the game's own maps and those of the FAF map editor).
        /// </summary>
        public required int Version { get; init; }

        /// <summary>
        /// The name in the preview's header, stored as UTF-16; empty in every map known. The engine
        /// reads it and does nothing with it (<c>RWldMapPreviewChunk::Load</c>).
        /// </summary>
        public string PreviewName { get; init; } = "";

        /// <summary>
        /// The entries after <see cref="PreviewName"/>, pairs of numbers the engine reads and discards;
        /// none in every map known.
        /// </summary>
        public IReadOnlyList<ScmapPreviewMetadata> PreviewMetadata { get; init; } = [];

        /// <summary>
        /// The width of the map in game units (ogrids), e.g. 256 for a 5 km map, 512 for 10 km.
        /// </summary>
        public int Width => Heightmap.Width;

        /// <summary>
        /// The height of the map in game units (ogrids).
        /// </summary>
        public int Height => Heightmap.Height;

        /// <summary>
        /// The preview image as a DDS file (256 by 256 pixels).
        /// </summary>
        public required byte[] Preview { get; init; }

        public required ScmapHeightmap Heightmap { get; init; }

        /// <summary>
        /// The terrain shader, usually <c>TTerrain</c> (<c>TTerrainXP</c> for the extended strata).
        /// </summary>
        public required string TerrainShader { get; init; }

        /// <summary>
        /// Whether the terrain shader is a composite one: the byte after the heightmap, from version 54
        /// on. The engine sets it from the <c>usage</c> annotation of the shader in
        /// <c>effects/terrain.fx</c> when it saves; false in every map known, the game's own maps with
        /// <c>TTerrainXP</c> included.
        /// </summary>
        public bool CompositeTerrainShader { get; init; }

        public required string BackgroundTexture { get; init; }

        public required string SkyCubemap { get; init; }

        /// <summary>
        /// The cubemaps that units reflect, by name. Version 53 stores only the default one.
        /// </summary>
        public required IReadOnlyList<ScmapCubemap> EnvironmentCubemaps { get; init; }

        public required ScmapLighting Lighting { get; init; }

        public required ScmapWater Water { get; init; }

        public required IReadOnlyList<ScmapWaveGenerator> WaveGenerators { get; init; }

        /// <summary>
        /// The colours of the minimap; absent in version 53.
        /// </summary>
        public ScmapMinimap? Minimap { get; init; }

        /// <summary>
        /// The tileset of a version 53 map (always <c>No Tileset</c>); absent in later versions. The
        /// engine reads it as a shader name and discards it.
        /// </summary>
        public string? Tileset { get; init; }

        /// <summary>
        /// The number a version 53 map stores after <see cref="Tileset"/> (6 in every map known): the
        /// FAF map editor reads it as the count of layers, the engine discards it and always reads six.
        /// Null in later versions; written as 6 when null.
        /// </summary>
        public int? StoredLayerCount { get; init; }

        /// <summary>
        /// The terrain layers (strata), lowest first. Version 56 and later have ten (the last has no
        /// normal map), version 53 six.
        /// </summary>
        public required IReadOnlyList<ScmapTerrainLayer> Layers { get; init; }

        /// <summary>
        /// The id the engine gives the next decal it makes (its decal manager's counter): one more
        /// than the highest decal id in most maps, 0 or too small in some made by the FAF map editor.
        /// </summary>
        public required int NextDecalId { get; init; }

        /// <summary>
        /// The id the engine gives the next decal group it makes (named <c>Group_&lt;id&gt;</c>): one
        /// more than the highest group id in most maps.
        /// </summary>
        public required int NextDecalGroupId { get; init; }

        public required IReadOnlyList<ScmapDecal> Decals { get; init; }

        public required IReadOnlyList<ScmapDecalGroup> DecalGroups { get; init; }

        /// <summary>
        /// The width of a tile of the normal map in <see cref="NormalMaps"/>: the map's width, at most
        /// <see cref="MaxNormalMapTile"/> (the engine's tiles). A version 53 map of 4096 by 4096 in the
        /// vault stores 4096 with a single tile, so keep what a map stores.
        /// </summary>
        public required int NormalMapTileWidth { get; init; }

        /// <summary>
        /// The height of a tile of the normal map; see <see cref="NormalMapTileWidth"/>.
        /// </summary>
        public required int NormalMapTileHeight { get; init; }

        /// <summary>
        /// The largest tile of the normal map the engine makes.
        /// </summary>
        public const int MaxNormalMapTile = 2048;

        /// <summary>
        /// The tiles of the terrain's normal map as DDS files, row by row (the rows along z, the tiles
        /// of a row along x): one for a map up to 2048 ogrids wide and high, four for a map of 4096.
        /// The engine skips them: it makes the normal map again from the heightmap.
        /// </summary>
        public required IReadOnlyList<byte[]> NormalMaps { get; init; }

        /// <summary>
        /// The masks that blend the terrain layers, as DDS files: two from version 54 on (layers 1 to
        /// 4 and 5 to 8); version 53 stores a count, 1 in every map known, and the engine uses the first
        /// for both.
        /// </summary>
        public required IReadOnlyList<byte[]> StratumMasks { get; init; }

        /// <summary>
        /// The water map as a DDS file: the first of the water maps the file stores. The engine
        /// writes every texel again from the heightmap, the water's elevations and the foam and
        /// flatness masks when it loads the map.
        /// </summary>
        public required byte[] WaterMap { get; init; }

        /// <summary>
        /// The water maps stored after <see cref="WaterMap"/>, which the engine reads and ignores; none
        /// in every map known.
        /// </summary>
        public IReadOnlyList<byte[]> ExtraWaterMaps { get; init; } = [];

        /// <summary>
        /// One byte per two by two ogrids (<see cref="Width"/> / 2 by <see cref="Height"/> / 2).
        /// </summary>
        public required byte[] WaterFoamMask { get; init; }

        /// <inheritdoc cref="WaterFoamMask"/>
        public required byte[] WaterFlatnessMask { get; init; }

        /// <inheritdoc cref="WaterFoamMask"/>
        public required byte[] WaterDepthBiasMask { get; init; }

        /// <summary>
        /// The terrain type of every ogrid (<see cref="Width"/> by <see cref="Height"/>), an index
        /// into the terrain types of the game (<c>lua/TerrainTypes.lua</c>).
        /// </summary>
        public required byte[] TerrainTypes { get; init; }

        /// <summary>
        /// The skybox; only version 58 and later store it (of the versions read, 60). The engine
        /// makes a default dome from the map's size for an earlier map.
        /// </summary>
        public ScmapSkybox? Skybox { get; init; }

        /// <summary>
        /// The batches of decals of the strategic (cartographic) view, after the skybox from version 59
        /// on; none in every map known.
        /// </summary>
        public IReadOnlyList<ScmapCartographicDecalBatch> CartographicDecals { get; init; } = [];

        public required IReadOnlyList<ScmapProp> Props { get; init; }
    }

    /// <summary>
    /// An entry of the preview's header: two numbers the engine reads and discards.
    /// </summary>
    public sealed record ScmapPreviewMetadata(uint Tag, uint Value);

    /// <summary>
    /// A batch of decals drawn in the strategic view with one technique and texture
    /// (<c>CartographicDecalBatch::Read</c> in Draiget/faf-re, <c>src/sdk/moho/render/Cartographic.cpp</c>).
    /// </summary>
    /// <param name="Technique">The technique; version 59 stores none (the engine uses <c>Decal</c>).</param>
    public sealed record ScmapCartographicDecalBatch(string Technique, string Texture, IReadOnlyList<ScmapCartographicDecal> Decals)
    {
        /// <summary>
        /// The technique of a batch in a version 59 map, which stores none.
        /// </summary>
        public const string DefaultTechnique = "Decal";
    }

    /// <summary>
    /// A decal of the strategic view: nine numbers of vertex data, whose meaning is not recovered.
    /// </summary>
    public sealed record ScmapCartographicDecal(IReadOnlyList<float> Values)
    {
        /// <summary>
        /// How many numbers a decal has.
        /// </summary>
        public const int ValueCount = 9;
    }

    /// <summary>
    /// The heights of the terrain: (<see cref="Width"/> + 1) by (<see cref="Height"/> + 1) samples,
    /// row by row, one at every corner of an ogrid.
    /// </summary>
    /// <param name="Width">The width of the map in ogrids.</param>
    /// <param name="Height">The height of the map in ogrids.</param>
    /// <param name="Scale">The height of one step of a sample, usually 1/128.</param>
    /// <param name="Samples">The raw samples; multiply by <paramref name="Scale"/> for the height.</param>
    public sealed record ScmapHeightmap(int Width, int Height, float Scale, ushort[] Samples)
    {
        /// <summary>
        /// The height of the terrain at a corner, in game units: 0 &lt;= x &lt;= Width, 0 &lt;= z &lt;= Height.
        /// </summary>
        public float GetHeight(int x, int z)
        {
            if ((uint)x > (uint)Width)
            {
                throw new ArgumentOutOfRangeException(nameof(x));
            }
            if ((uint)z > (uint)Height)
            {
                throw new ArgumentOutOfRangeException(nameof(z));
            }
            return Samples[z * (Width + 1) + x] * Scale;
        }
    }

    public sealed record ScmapCubemap(string Name, string Texture);

    public sealed record ScmapLighting(
        float LightingMultiplier,
        Vector3 SunDirection,
        Vector3 SunAmbience,
        Vector3 SunColor,
        Vector3 ShadowFillColor,
        Vector4 SpecularColor,
        float Bloom,
        Vector3 FogColor,
        float FogStart,
        float FogEnd);

    /// <summary>
    /// The water shader settings. The elevations are stored whether or not the map has water.
    /// </summary>
    public sealed record ScmapWater
    {
        /// <summary>
        /// Whether the map has water: any byte other than 0, as the engine reads it (written as 1).
        /// </summary>
        public required bool HasWater { get; init; }

        /// <summary>
        /// The height of the water surface.
        /// </summary>
        public required float Elevation { get; init; }

        public required float ElevationDeep { get; init; }

        public required float ElevationAbyss { get; init; }

        public required Vector3 SurfaceColor { get; init; }

        public required Vector2 ColorLerp { get; init; }

        public required float RefractionScale { get; init; }

        public required float FresnelBias { get; init; }

        public required float FresnelPower { get; init; }

        public required float UnitReflection { get; init; }

        public required float SkyReflection { get; init; }

        public required float SunShininess { get; init; }

        public required float SunStrength { get; init; }

        public required Vector3 SunDirection { get; init; }

        public required Vector3 SunColor { get; init; }

        public required float SunReflection { get; init; }

        public required float SunGlow { get; init; }

        public required string Cubemap { get; init; }

        public required string WaterRamp { get; init; }

        /// <summary>
        /// The four normal map layers of the surface.
        /// </summary>
        public required IReadOnlyList<ScmapWaveTexture> WaveTextures { get; init; }
    }

    public sealed record ScmapWaveTexture(float NormalRepeat, Vector2 NormalMovement, string Texture);

    /// <summary>
    /// A generator of the waves that roll onto a shore.
    /// </summary>
    public sealed record ScmapWaveGenerator(
        string Texture,
        string Ramp,
        Vector3 Position,
        float Rotation,
        Vector3 Velocity,
        float LifetimeFirst,
        float LifetimeSecond,
        float PeriodFirst,
        float PeriodSecond,
        float ScaleFirst,
        float ScaleSecond,
        float FrameCount,
        float FrameRateFirst,
        float FrameRateSecond,
        float StripCount);

    /// <summary>
    /// The settings of the strategic view (the cartographic map), each colour a 32-bit colour as
    /// stored. The engine reads them in <c>CWldTerrainRes::Load</c> and builds the view in
    /// <c>Cartographic::InitializeTerrain</c> (Draiget/faf-re, <c>src/sdk/moho/render/Cartographic.cpp</c>).
    /// </summary>
    /// <param name="ContourInterval">The number of contour bands over the land's range of heights
    /// (<c>mTopographicSamples</c>), 1 to 100; the engine takes 0 as 1 (FAF maps of version 56 store 0,
    /// the game's maps 20 or 24).</param>
    /// <param name="DeepWaterColor">The colour at and below the deep water elevation.</param>
    /// <param name="ContourColor">The second colour of the ramp; in faf-re's code its threshold equals
    /// the first one's, so it is never seen there (likely a recovery error: treat its meaning as
    /// uncertain).</param>
    /// <param name="ShoreColor">The colour between the deep water elevation and the surface.</param>
    /// <param name="LandStartColor">The colour of the lowest land, blended into <paramref name="LandEndColor"/>.</param>
    /// <param name="LandEndColor">The colour of the highest land.</param>
    /// <param name="ImagerElevationOffset">How far the borders of the map imager are lifted above the
    /// terrain's middle height (<c>mImagerElevationOffset</c>, <c>MapImager::UpdateMeshStances</c>); from
    /// version 57 on, null before. The game's maps store -19.5 to 6.3, FAF maps 0.</param>
    public sealed record ScmapMinimap(
        int ContourInterval,
        uint DeepWaterColor,
        uint ContourColor,
        uint ShoreColor,
        uint LandStartColor,
        uint LandEndColor,
        float? ImagerElevationOffset)
    {
        /// <summary>
        /// The most contour bands the engine draws.
        /// </summary>
        public const int MaxContourBands = 100;
    }

    /// <summary>
    /// A terrain layer (stratum): an albedo texture and a normal map, each with its tiling scale.
    /// The paths are empty for an unused layer.
    /// </summary>
    public sealed record ScmapTerrainLayer(string Albedo, float AlbedoScale, string Normal, float NormalScale);

    /// <summary>
    /// A terrain decal. The type numbers follow the engine's <c>ETerrainDecalType</c> (0 undefined,
    /// 1 albedo, 2 normals, 3 water mask, 4 water albedo, 5 water normals, 6 glow, 7 alpha normals,
    /// 8 glow mask, 9 albedo XP).
    /// </summary>
    /// <param name="Textures">The textures by slot, always two (the engine's decal holds two); an
    /// unused slot is empty.</param>
    /// <param name="Position">A corner of the decal, not its middle: the footprint runs from it along
    /// the scale's x and z, turned by the rotation around y (<c>ProjectDecalBoundsXZ</c>).</param>
    /// <param name="CutOffLod">The distance beyond which the decal is not drawn: when
    /// <paramref name="NearCutOffLod"/> is 0, it fades out from 0.75 of this distance.</param>
    /// <param name="NearCutOffLod">The distance closer than which the decal is not drawn: when above 0,
    /// it fades in from 0.75 of this distance up to it, and <paramref name="CutOffLod"/> does not fade.</param>
    /// <param name="OwnerArmy">The army that owns the decal, -1 for none.</param>
    public sealed record ScmapDecal(
        int Id,
        int Type,
        IReadOnlyList<string> Textures,
        Vector3 Scale,
        Vector3 Position,
        Vector3 Rotation,
        float CutOffLod,
        float NearCutOffLod,
        int OwnerArmy);

    /// <summary>
    /// A named group of decals, by id.
    /// </summary>
    public sealed record ScmapDecalGroup(int Id, string Name, IReadOnlyList<int> DecalIds);

    /// <summary>
    /// The skybox of a version 60 map: the sky dome the game draws, its sky objects and its clouds.
    /// The engine reads it in <c>Moho::SkyDome::Load</c> and draws it with <c>effects/sky.fx</c>
    /// (reconstructed in Draiget/faf-re, <c>src/sdk/moho/render/SkyDome.cpp</c>); the names are the
    /// FAF map editor's, the remarks say what the engine does with each.
    /// </summary>
    public sealed record ScmapSkybox
    {
        /// <summary>
        /// The middle of the dome's bottom ring.
        /// </summary>
        public required Vector3 Position { get; init; }

        /// <summary>
        /// How far the dome's bottom ring lies above <see cref="Position"/>; also the height where the
        /// horizon's colour starts to give way (the shader's <c>horizonBegin</c>).
        /// </summary>
        public required float HorizonHeight { get; init; }

        /// <summary>
        /// The radius of the dome's bottom ring.
        /// </summary>
        public required float Scale { get; init; }

        /// <summary>
        /// The angle in radians where the dome starts on its sphere (1.2566, 72°, in the game's maps):
        /// the dome is the cap of a sphere of radius <c>Scale / cos(angle)</c> above that angle, so a
        /// larger angle makes it flatter.
        /// </summary>
        public required float SubtractHeight { get; init; }

        /// <summary>
        /// How many pieces the dome is built of around its axis.
        /// </summary>
        public required int SubdivisionsAxis { get; init; }

        /// <summary>
        /// How many rings the dome is built of from its bottom to its top.
        /// </summary>
        public required int SubdivisionsHeight { get; init; }

        /// <summary>
        /// How far above <see cref="HorizonHeight"/> the sky has the zenith's colour (the shader's
        /// <c>horizonEnd</c> is their sum).
        /// </summary>
        public required float ZenithHeight { get; init; }

        public required Vector3 HorizonColor { get; init; }

        public required Vector3 ZenithColor { get; init; }

        /// <summary>
        /// How strongly the glow atlas's alpha brightens the sky objects (written into the frame's
        /// alpha for the bloom).
        /// </summary>
        public required float DecalGlowMultiplier { get; init; }

        /// <summary>
        /// The atlas the sky objects are cut from (alpha-blended); none are drawn unless it and
        /// <see cref="Glow"/> load.
        /// </summary>
        public required string Albedo { get; init; }

        /// <summary>
        /// The glow atlas, in the layout of <see cref="Albedo"/>; only its alpha is read.
        /// </summary>
        public required string Glow { get; init; }

        public required IReadOnlyList<ScmapPlanet> Planets { get; init; }

        /// <summary>
        /// The light ramp of the cumulus clouds (<c>cumulusLightRamp</c>); empty in every map known.
        /// </summary>
        public required string CumulusLightRamp { get; init; }

        /// <summary>
        /// The dispersion ramp of the cumulus clouds (<c>cumulusDispersionRamp</c>); empty in every map known.
        /// </summary>
        public required string CumulusDispersionRamp { get; init; }

        /// <summary>
        /// The texture of the cumulus clouds (<c>cumulusTexture</c>); empty in every map known.
        /// </summary>
        public required string CumulusTexture { get; init; }

        public required float CirrusMultiplier { get; init; }

        public required Vector3 CirrusColor { get; init; }

        /// <summary>
        /// The cirrus texture: a layer in each of its channels (red, green, blue, alpha), tiled over the dome.
        /// </summary>
        public required string CirrusTexture { get; init; }

        /// <summary>
        /// The four cirrus layers. The file stores a count before them, which the engine ignores
        /// (it always reads four and writes 4), so there are always <see cref="CirrusLayerCount"/>.
        /// </summary>
        public required IReadOnlyList<ScmapCirrusLayer> CirrusLayers { get; init; }

        /// <summary>
        /// How many cirrus layers a skybox has.
        /// </summary>
        public const int CirrusLayerCount = 4;
    }

    public sealed record ScmapPlanet(Vector3 Position, float Rotation, Vector2 Scale, Vector4 Uv);

    public sealed record ScmapCirrusLayer(Vector2 Frequency, float Speed, Vector2 Direction);

    /// <summary>
    /// A prop placed on the map, e.g. a tree or a rock, with its orientation as three axes.
    /// </summary>
    /// <remarks>
    /// The engine (<c>CWldProps::Load</c> in Draiget/faf-re, <c>src/sdk/moho/sim/CWldMap.cpp</c>) turns
    /// the three axes into a normalised quaternion, so skew and scale in them are lost, and ignores
    /// <see cref="Scale"/> (it writes 1, 1, 1 when it saves).
    /// </remarks>
    /// <param name="Blueprint">The path of the prop's blueprint, e.g. <c>/env/evergreen/props/trees/pine06_s2_prop.bp</c>.</param>
    public sealed record ScmapProp(
        string Blueprint,
        Vector3 Position,
        Vector3 RotationX,
        Vector3 RotationY,
        Vector3 RotationZ,
        Vector3 Scale);
}
