using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Files.Serialized
{
    /// <summary>
    /// <c>layers/layers.json</c> of projects of format 3 to 5: the height layers of the map (user
    /// story T-5), lowest first, read and turned into the heightmap's stack
    /// (<see cref="LayerStackDto"/>) when such a project opens; format 6 writes
    /// <c>stacks/heightmap.json</c> instead. Each layer's values are <c>layers/&lt;name&gt;.png</c>
    /// (16-bit grey) beside it, its coverage <c>layers/&lt;name&gt;.coverage.png</c> (8-bit grey)
    /// when it has one; a curve's follow from its curve and have no file.
    /// </summary>
    public sealed record HeightLayersDto
    {
        public required IReadOnlyList<HeightLayerDto> Layers { get; init; }
    }

    public sealed record HeightLayerDto
    {
        public required string Name { get; init; }

        /// <summary>
        /// <c>Base</c>, <c>Sculpt</c>, <c>Imported</c> or <c>Curve</c>.
        /// </summary>
        public required string Kind { get; init; }

        /// <summary>
        /// <c>Add</c>, <c>Maximum</c>, <c>Minimum</c> or <c>Replace</c>.
        /// </summary>
        public required LayerCombine Combine { get; init; }

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
}
