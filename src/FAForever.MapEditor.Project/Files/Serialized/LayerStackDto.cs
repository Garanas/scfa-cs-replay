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
    }

    /// <summary>
    /// One layer of a stack: its settings, and its source's own fields (only those of its kind are
    /// written).
    /// </summary>
    public sealed record LayerDto
    {
        public required string Name { get; init; }

        /// <summary>
        /// The kind of source: <c>painted</c>, <c>image</c>, <c>mask</c>, <c>rules</c>, <c>curve</c>
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
    }
}
