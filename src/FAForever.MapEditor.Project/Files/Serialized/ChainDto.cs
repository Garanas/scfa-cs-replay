namespace FAForever.MapEditor.Project.Files.Serialized
{
    /// <summary>
    /// A named list of markers, e.g. a patrol route.
    /// </summary>
    public sealed record ChainDto
    {
        public required string Name { get; init; }

        /// <summary>
        /// The names of the markers, in order.
        /// </summary>
        public required IReadOnlyList<string> Markers { get; init; }
    }
}
