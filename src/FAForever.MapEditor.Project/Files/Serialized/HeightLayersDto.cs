using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Files.Serialized
{
    /// <summary>
    /// <c>layers/layers.json</c>: the height layers of the map (user story T-5), lowest first. A
    /// project without the file has no layers: its heightmap is simply its heights. Each layer's
    /// values are <c>layers/&lt;name&gt;.png</c> (16-bit grey) beside it, its coverage
    /// <c>layers/&lt;name&gt;.coverage.png</c> (8-bit grey) when it has one; a curve's follow from
    /// its curve and have no file.
    /// </summary>
    public sealed record HeightLayersDto
    {
        public required IReadOnlyList<HeightLayerDto> Layers { get; init; }
    }

    public sealed record HeightLayerDto
    {
        public required string Name { get; init; }

        public required HeightLayerKind Kind { get; init; }

        public required HeightCombine Combine { get; init; }

        /// <summary>
        /// 0 to 1; left out at 1.
        /// </summary>
        public float? Strength { get; init; }

        /// <summary>
        /// <c>mask:&lt;name&gt;</c> or <c>stratum:&lt;n&gt;</c>; left out for everywhere.
        /// </summary>
        public string? Mask { get; init; }

        /// <summary>
        /// Left out while the layer shows.
        /// </summary>
        public bool? Hidden { get; init; }

        /// <summary>
        /// Left out while it is not locked.
        /// </summary>
        public bool? Locked { get; init; }

        /// <summary>
        /// The height in game units of a sample 0 in the layer's PNG; one step is the map's height
        /// scale. Adding layers hold differences around half the range (-256 at 1/128).
        /// </summary>
        public float? Offset { get; init; }

        /// <summary>
        /// An imported layer's file, relative to the project folder when it lies in it.
        /// </summary>
        public string? Source { get; init; }

        public HeightCurveDto? Curve { get; init; }
    }

    public sealed record HeightCurveDto
    {
        public required CurveProfile Profile { get; init; }

        public required float Width { get; init; }

        public required float Falloff { get; init; }

        public bool? Closed { get; init; }

        /// <summary>
        /// The points as <c>[x, z, height]</c>.
        /// </summary>
        public required IReadOnlyList<float[]> Points { get; init; }
    }
}
