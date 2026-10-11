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
    /// The layout follows the loader of the FAF map editor
    /// (<c>Assets/Scripts/HazardX SCMAP Code/Map.cs</c> in FAForever/FAForeverMapEditor). Textures
    /// (the preview, the normal map, the stratum masks, the water map) are kept as the DDS files they
    /// are stored as.
    /// </remarks>
    public sealed record Scmap
    {
        /// <summary>
        /// The minor version of the format: 53 (Supreme Commander), 56 (Forged Alliance) or 60 (the
        /// FAF map editor, which adds the skybox).
        /// </summary>
        public required int Version { get; init; }

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
        /// The tileset of a version 53 map (always <c>No Tileset</c>); absent in later versions.
        /// </summary>
        public string? Tileset { get; init; }

        /// <summary>
        /// The terrain layers (strata), lowest first. Version 56 and later have ten (the last has no
        /// normal map); version 53 stores its own count, usually six.
        /// </summary>
        public required IReadOnlyList<ScmapTerrainLayer> Layers { get; init; }

        /// <summary>
        /// Two integers between the layers and the decals whose meaning is unknown. In most maps
        /// the first exceeds every decal id and the second every decal group id; some store zeros.
        /// </summary>
        public required (int First, int Second) DecalHeader { get; init; }

        public required IReadOnlyList<ScmapDecal> Decals { get; init; }

        public required IReadOnlyList<ScmapDecalGroup> DecalGroups { get; init; }

        /// <summary>
        /// The normal maps of the terrain as DDS files (always one).
        /// </summary>
        public required IReadOnlyList<byte[]> NormalMaps { get; init; }

        /// <summary>
        /// The masks that blend the terrain layers, as DDS files: one before version 56 (layers 1 to
        /// 4), two after (layers 1 to 4 and 5 to 8).
        /// </summary>
        public required IReadOnlyList<byte[]> StratumMasks { get; init; }

        /// <summary>
        /// The water map as a DDS file.
        /// </summary>
        public required byte[] WaterMap { get; init; }

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
        /// The skybox; only version 60 and later store it.
        /// </summary>
        public ScmapSkybox? Skybox { get; init; }

        public required IReadOnlyList<ScmapProp> Props { get; init; }
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
    /// The colours of the minimap, each a 32-bit colour as stored.
    /// </summary>
    /// <param name="Unknown">A number after the colours that version 60 adds; its meaning is unknown.</param>
    public sealed record ScmapMinimap(
        int ContourInterval,
        uint DeepWaterColor,
        uint ContourColor,
        uint ShoreColor,
        uint LandStartColor,
        uint LandEndColor,
        float? Unknown);

    /// <summary>
    /// A terrain layer (stratum): an albedo texture and a normal map, each with its tiling scale.
    /// The paths are empty for an unused layer.
    /// </summary>
    public sealed record ScmapTerrainLayer(string Albedo, float AlbedoScale, string Normal, float NormalScale);

    /// <summary>
    /// A terrain decal. The type numbers follow the engine's <c>ETerrainDecalType</c> (1 albedo,
    /// 2 normals, 3 water mask, 4 water albedo, 5 water normals, 6 glow, 7 alpha normals, 8 glow mask).
    /// </summary>
    /// <param name="Textures">The textures by slot; an unused slot is empty.</param>
    /// <param name="CutOffLod">The distance beyond which the decal is not drawn.</param>
    /// <param name="NearCutOffLod">The distance closer than which the decal is not drawn.</param>
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
    /// <param name="Blueprint">The path of the prop's blueprint, e.g. <c>/env/evergreen/props/trees/pine06_s2_prop.bp</c>.</param>
    public sealed record ScmapProp(
        string Blueprint,
        Vector3 Position,
        Vector3 RotationX,
        Vector3 RotationY,
        Vector3 RotationZ,
        Vector3 Scale);
}
