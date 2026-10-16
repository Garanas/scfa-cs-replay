namespace FAForever.MapEditor.Project.Files.Serialized
{
    /// <summary>
    /// The symmetry the map is made by, which the editor mirrors edits by (user story F-12).
    /// </summary>
    public sealed record SymmetryDto
    {
        /// <summary>
        /// <c>Rotational</c>, <c>MirroredLeftRight</c>, <c>MirroredTopBottom</c>, <c>MirroredDiagonal</c>,
        /// <c>MirroredOtherDiagonal</c>, <c>MirroredBothWays</c>, <c>MirroredBothDiagonals</c> or
        /// <c>Kaleidoscope</c> (the names of <c>MapSymmetryKind</c>).
        /// </summary>
        public required string Kind { get; init; }

        /// <summary>
        /// The players of a rotational symmetry or the turns of a kaleidoscope; 2 for the other kinds.
        /// </summary>
        public int Count { get; init; } = 2;

        /// <summary>
        /// The rectangle the symmetry maps onto itself, <c>[x0, z0, x1, z1]</c> in ogrids; left out for
        /// the whole map.
        /// </summary>
        public float[]? Around { get; init; }
    }
}
