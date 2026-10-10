using System.Collections.Immutable;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// A terrain shader the game knows (user story S-4): a technique of <c>effects/terrain.fx</c>,
    /// the game's or FAF's (<c>effects.nx2</c>), with what it does with the layers and settings.
    /// </summary>
    /// <param name="Name">The technique's name, as the map's <c>.scmap</c> names it (<c>TTerrainXP</c>).</param>
    /// <param name="Summary">What it is, in a line.</param>
    /// <param name="Strata">How many strata it blends: 4 (<c>TTerrain</c>) or 8.</param>
    /// <param name="FullMaskRange">Whether a stratum's mask counts over its whole range; otherwise
    /// only its upper half does (<c>saturate(mask * 2 - 1)</c>), as the game's own shaders read them.</param>
    /// <param name="Stratum8IsData">Whether stratum 8 holds map-wide data (water depth, ambient
    /// occlusion, shadows, normals) instead of a texture that is blended.</param>
    /// <param name="UpperIsAlbedo">Whether the upper layer is a texture blended over the rest by
    /// its alpha; otherwise it holds data (specularity, heights and roughness).</param>
    /// <param name="IsFaf">Whether only FAF's game has it.</param>
    /// <param name="Uses">The layers and settings it reads, each in a line.</param>
    public sealed record TerrainShaderInfo(
        string Name,
        string Summary,
        int Strata,
        bool FullMaskRange,
        bool Stratum8IsData,
        bool UpperIsAlbedo,
        bool IsFaf,
        ImmutableArray<string> Uses);

    /// <summary>
    /// The terrain shaders a map can name (user story S-4), from the techniques of FAF's
    /// <c>effects/terrain.fx</c> (<c>effects.nx2</c>) and the comments that describe their layout
    /// there (the naming conventions: <c>Terrain0XX</c> legacy specularity, <c>1XX</c> PBR roughness,
    /// <c>2XX</c> PBR roughness and splatting by height; <c>X0X</c> the whole mask range, <c>X5X</c>
    /// its upper half; a <c>B</c> biplanar mapping of strata 2 and 3).
    /// </summary>
    public static class TerrainShaderCatalog
    {
        private const string Stratum8Data = "Stratum 8 holds map-wide data: its albedo the water depth, ambient occlusion and shadows, its normal map the map's normals";
        private const string UpperHeights = "The upper layer holds heights and roughness: of the lower layer to stratum 3 in red and green, of strata 4 to 7 in blue and alpha";
        private const string RoughnessMask = "Stratum 8's mask multiplies the roughness, 0.5 leaves it";
        private const string Splatting = "Strata blend by their heights; specular red blurs the splatting, specular green sharpens the height's contrast";
        private const string Scales = "The albedo scales set the near scale of the heights, the normal scales the far one";

        public static ImmutableArray<TerrainShaderInfo> All { get; } =
        [
            new TerrainShaderInfo("TTerrain", "the game's first: four strata", 4, false, false, true, false,
                ["The lower layer, strata 1 to 4 and the upper layer by its alpha", "Strata 5 to 8 and their masks are not drawn", "A stratum's mask counts from its middle up"]),
            new TerrainShaderInfo("TTerrainXP", "Forged Alliance's: eight strata", 8, false, false, true, false,
                ["The lower layer, strata 1 to 8 and the upper layer by its alpha", "The albedos' alpha is their specularity, times the lighting's specular colour", "A stratum's mask counts from its middle up"]),
            new TerrainShaderInfo("TTerrainXPExt", "TTerrainXP with map-wide normals and shadows", 8, false, true, true, true,
                ["The lower layer and strata 1 to 7; the upper layer a macro texture by its alpha", Stratum8Data, "The albedos' alpha is their specularity", "A stratum's albedo mask counts from its middle up, its normal mask whole"]),
            new TerrainShaderInfo("Terrain000", "map-wide normals and shadows, whole masks", 8, true, true, true, true,
                ["The lower layer and strata 1 to 7; the upper layer a macro texture by its alpha", Stratum8Data, "The albedos' alpha is their specularity", "A stratum's mask counts over its whole range"]),
            new TerrainShaderInfo("Terrain050", "map-wide normals and shadows, half masks", 8, false, true, true, true,
                ["The lower layer and strata 1 to 7; the upper layer a macro texture by its alpha", Stratum8Data, "The albedos' alpha is their specularity", "A stratum's mask counts from its middle up"]),
            new TerrainShaderInfo("Terrain001", "a specularity texture, whole masks", 8, true, true, false, true,
                ["The lower layer, blended over everything again by its alpha, and strata 1 to 7", Stratum8Data, "The upper layer is the specular colour, its alpha the sharpness", "A stratum's mask counts over its whole range"]),
            new TerrainShaderInfo("Terrain051", "a specularity texture, half masks", 8, false, true, false, true,
                ["The lower layer, blended over everything again by its alpha, and strata 1 to 7", Stratum8Data, "The upper layer is the specular colour, its alpha the sharpness", "A stratum's mask counts from its middle up"]),
            new TerrainShaderInfo("Terrain100", "PBR roughness, whole masks", 8, true, true, false, true,
                ["The lower layer and strata 1 to 7", Stratum8Data, UpperHeights, RoughnessMask, "A stratum's mask counts over its whole range"]),
            new TerrainShaderInfo("Terrain150", "PBR roughness, half masks", 8, false, true, false, true,
                ["The lower layer and strata 1 to 7", Stratum8Data, UpperHeights, RoughnessMask, "A stratum's mask counts from its middle up"]),
            new TerrainShaderInfo("Terrain100B", "PBR roughness, biplanar cliffs, whole masks", 8, true, true, false, true,
                ["The lower layer and strata 1 to 7; strata 2 and 3 mapped biplanar for cliffs", Stratum8Data, UpperHeights, RoughnessMask, "A stratum's mask counts over its whole range"]),
            new TerrainShaderInfo("Terrain150B", "PBR roughness, biplanar cliffs, half masks", 8, false, true, false, true,
                ["The lower layer and strata 1 to 7; strata 2 and 3 mapped biplanar for cliffs", Stratum8Data, UpperHeights, RoughnessMask, "A stratum's mask counts from its middle up"]),
            new TerrainShaderInfo("Terrain200", "PBR roughness and splatting by height, whole masks", 8, true, true, false, true,
                ["The lower layer and strata 1 to 7; every second stratum may be turned to break up repetition", Stratum8Data, UpperHeights, Splatting, Scales, RoughnessMask, "A stratum's mask counts over its whole range"]),
            new TerrainShaderInfo("Terrain250", "PBR roughness and splatting by height, half masks", 8, false, true, false, true,
                ["The lower layer and strata 1 to 7; every second stratum may be turned to break up repetition", Stratum8Data, UpperHeights, Splatting, Scales, RoughnessMask, "A stratum's mask counts from its middle up"]),
            new TerrainShaderInfo("Terrain200B", "splatting by height, biplanar cliffs, whole masks", 8, true, true, false, true,
                ["The lower layer and strata 1 to 7; strata 2 and 3 mapped biplanar for cliffs", Stratum8Data, UpperHeights, Splatting, Scales, RoughnessMask, "A stratum's mask counts over its whole range"]),
            new TerrainShaderInfo("Terrain250B", "splatting by height, biplanar cliffs, half masks", 8, false, true, false, true,
                ["The lower layer and strata 1 to 7; strata 2 and 3 mapped biplanar for cliffs", Stratum8Data, UpperHeights, Splatting, Scales, RoughnessMask, "A stratum's mask counts from its middle up"]),
            new TerrainShaderInfo("Terrain301", "the map generator's old shader, for old maps only", 8, false, true, false, true,
                ["Kept for maps the generator made in its versions 1.11 to 1.14; choose Terrain250 for a new map", "Stratum 8: its albedo a texture with transparency, its normal map heights and roughness", "The upper layer: the map's normals, the water depth and shadows"]),
        ];

        /// <summary>
        /// The shader of this name (in any case), or null for one the editor does not know.
        /// </summary>
        public static TerrainShaderInfo? Find(string? name) =>
            All.FirstOrDefault(shader => string.Equals(shader.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// The shader of this name, or what an unknown one is taken as: <c>TTerrainXP</c>'s way of
        /// blending, which the views draw.
        /// </summary>
        public static TerrainShaderInfo Of(string? name) => Find(name) ?? All[1];
    }
}
