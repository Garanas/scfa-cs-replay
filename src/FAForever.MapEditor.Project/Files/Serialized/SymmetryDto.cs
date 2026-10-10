namespace FAForever.MapEditor.Project.Files.Serialized
{
    /// <summary>
    /// The symmetry the map is made by, which the editor mirrors edits by (user story F-12).
    /// </summary>
    public sealed record SymmetryDto
    {
        /// <summary>
        /// <c>Rotational</c>, <c>MirroredLeftRight</c>, <c>MirroredTopBottom</c>, <c>MirroredDiagonal</c>
        /// or <c>MirroredOtherDiagonal</c> (the names of <c>MapSymmetryKind</c>).
        /// </summary>
        public required string Kind { get; init; }

        /// <summary>
        /// The players of a rotational symmetry; 2 for a mirror.
        /// </summary>
        public int Count { get; init; } = 2;
    }
}
