using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Files.Serialized
{
    /// <summary>
    /// <c>stacks/&lt;target&gt;.json</c> (format 6): a stack of layers that makes one grid of the map
    /// (<see cref="LayerStack"/>), lowest first; <c>stacks/heightmap.json</c> for the heightmap. A
    /// project without the file has no stack for that grid: the grid is simply what its own file
    /// holds. The layers' grids lie in the folder of the same name beside it
    /// (<c>stacks/heightmap/&lt;layer&gt;.png</c>): for heights 16-bit grey with sample 0 at
    /// <see cref="LayerDto.Offset"/> and the coverage as <c>&lt;layer&gt;.coverage.png</c>, 8-bit
    /// grey; for coverage 8-bit grey. A curve's or a mask reference's values follow from the source
    /// and have no file. The composite is written too (<c>heightmap.png</c>), so the project stays
    /// readable without the editor.
    /// </summary>
    public sealed record LayerStackDto
    {
        /// <summary>
        /// The target's key: <c>heightmap</c>, <c>stratum-2</c>, <c>mask-highlands</c>, <c>water-foam</c>.
        /// </summary>
        public required string Target { get; init; }

        public required IReadOnlyList<LayerDto> Layers { get; init; }

        /// <summary>
        /// For the heightmap: the draft heightmap's layers in their order, lowest first, each with
        /// whether it shows in the draft (<see cref="LayerDto.Hidden"/> is the final's choice for a
        /// shared layer). Every layer whose <see cref="LayerDto.Use"/> is not <c>final</c> is in it.
        /// Left out while the draft holds the same layers as the final in the same order, shown
        /// alike; a project without it (older ones too) has every layer in both.
        /// </summary>
        public IReadOnlyList<DraftLayerDto>? Draft { get; init; }
    }

    /// <summary>
    /// A layer's place in the draft heightmap's stack (<see cref="LayerStackDto.Draft"/>).
    /// </summary>
    public sealed record DraftLayerDto
    {
        /// <summary>
        /// The layer's name, as in <see cref="LayerStackDto.Layers"/>.
        /// </summary>
        public required string Name { get; init; }

        /// <summary>
        /// Left out while the layer shows in the draft.
        /// </summary>
        public bool? Hidden { get; init; }
    }

    /// <summary>
    /// One layer of a stack: its settings, and its source's own fields (only those of its kind are
    /// written).
    /// </summary>
    public sealed record LayerDto
    {
        public required string Name { get; init; }

        /// <summary>
        /// The kind of source: <c>painted</c>, <c>image</c>, <c>mask</c>, <c>rules</c>, <c>curve</c>, <c>decals</c>
        /// (<see cref="LayerSources"/>).
        /// </summary>
        public required string Source { get; init; }

        public required LayerCombine Combine { get; init; }

        /// <summary>
        /// 0 to 1; left out at 1.
        /// </summary>
        public float? Strength { get; init; }

        /// <summary>
        /// <c>mask:&lt;name&gt;</c> or <c>stratum:&lt;n&gt;</c>; left out for everywhere.
        /// </summary>
        public string? Clip { get; init; }

        /// <summary>
        /// Left out while the layer shows.
        /// </summary>
        public bool? Hidden { get; init; }

        /// <summary>
        /// Left out while it is not locked.
        /// </summary>
        public bool? Locked { get; init; }

        /// <summary>
        /// True for the stack's base; left out otherwise.
        /// </summary>
        public bool? Base { get; init; }

        /// <summary>
        /// In a stack of heights: the height in game units of a sample 0 in the layer's PNG; one step
        /// is the map's height scale. Layers of differences hold them around half the range (-256 at 1/128).
        /// </summary>
        public float? Offset { get; init; }

        /// <summary>
        /// An image layer's file, relative to the project folder when it lies in it.
        /// </summary>
        public string? Path { get; init; }

        /// <summary>
        /// A mask reference's mask: <c>mask:&lt;name&gt;</c> or <c>stratum:&lt;n&gt;</c>.
        /// </summary>
        public string? Mask { get; init; }

        /// <summary>
        /// A mask reference's threshold, 0 to 1; left out at 0.
        /// </summary>
        public float? Threshold { get; init; }

        /// <summary>
        /// Whether a mask reference is inverted; left out when not.
        /// </summary>
        public bool? Invert { get; init; }

        /// <summary>
        /// A mask reference's height in game units at full mask, in a stack of heights; left out at 0.
        /// </summary>
        public float? Height { get; init; }

        /// <summary>
        /// A rules layer's rules.
        /// </summary>
        public MaskRulesDto? Rules { get; init; }

        /// <summary>
        /// A curve layer's curve.
        /// </summary>
        public HeightCurveDto? Curve { get; init; }

        /// <summary>
        /// A decal-masks layer's mask name (<c>main</c> for the plain <c>_mask</c>). Its height in a
        /// stack of heights is <see cref="Height"/>; its grid's file holds what it drew last.
        /// </summary>
        public string? DecalMask { get; init; }

        /// <summary>
        /// The decal sets a decal-masks layer draws, by name; left out for every set.
        /// </summary>
        public IReadOnlyList<string>? DecalSets { get; init; }

        /// <summary>
        /// For a layer of the heightmap, which of its stacks hold it: <c>draft</c> (only the draft,
        /// not listed in the final's order) or <c>final</c> (only the final); left out for both. The
        /// layers listed without <c>draft</c> are the final's, in its order.
        /// </summary>
        public string? Use { get; init; }
    }

    public sealed record HeightCurveDto
    {
        public required CurveProfile Profile { get; init; }

        public required float Width { get; init; }

        public required float Falloff { get; init; }

        public bool? Closed { get; init; }

        /// <summary>
        /// The symmetry whose images of the curve the layer draws too; absent for the curve alone.
        /// </summary>
        public SymmetryDto? Symmetry { get; init; }

        /// <summary>
        /// The points as <c>[x, z, height]</c>.
        /// </summary>
        public required IReadOnlyList<float[]> Points { get; init; }

        /// <summary>
        /// The points that override the curve's values or are sharp, by index (format 9); absent
        /// when none does.
        /// </summary>
        public IReadOnlyList<CurveNodeDto>? Nodes { get; init; }

        /// <summary>
        /// The right falloff; absent: the same as <see cref="Falloff"/>.
        /// </summary>
        public float? FalloffRight { get; init; }

        /// <summary>
        /// The fade's shape; absent: the profile's.
        /// </summary>
        public CurveFalloffShape? FalloffShape { get; init; }

        public int? TerraceSteps { get; init; }

        public float? CliffFace { get; init; }

        public CurveRoadMode? RoadMode { get; init; }

        public float? Camber { get; init; }

        public float? NoiseAmplitude { get; init; }

        public float? NoiseScale { get; init; }

        public int? NoiseSeed { get; init; }

        /// <summary>
        /// The height variation along the curve as <c>[min, max]</c>.
        /// </summary>
        public float[]? HeightVariation { get; init; }

        /// <summary>
        /// The width variation along the curve as <c>[min, max]</c>.
        /// </summary>
        public float[]? WidthVariation { get; init; }

        public float? VariationScale { get; init; }

        /// <summary>
        /// Written for every closed curve (format 9): true when it shapes its line only, false when it
        /// fills its inside. A closed curve without it (written before format 9) is read as line
        /// only, so it keeps its look.
        /// </summary>
        public bool? LineOnly { get; init; }
    }

    /// <summary>
    /// One point's own values (<see cref="CurvePoint"/>): absent ones are the curve's.
    /// </summary>
    public sealed record CurveNodeDto
    {
        public required int Index { get; init; }

        public float? Width { get; init; }

        public float? FalloffLeft { get; init; }

        public float? FalloffRight { get; init; }

        public float? Strength { get; init; }

        public bool? Sharp { get; init; }
    }
}
