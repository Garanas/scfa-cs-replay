namespace FAForever.MapEditor.Project.Files.Serialized
{
    /// <summary>
    /// A node of the graph the AI finds its way with on one layer.
    /// </summary>
    public sealed record PathNodeDto
    {
        public required string Name { get; init; }

        public required PathLayer Layer { get; init; }

        public required Vector3Dto Position { get; init; }

        /// <summary>
        /// The rotation around each axis, in radians.
        /// </summary>
        public Vector3Dto? Orientation { get; init; }

        /// <summary>
        /// The names of the nodes this one links to.
        /// </summary>
        public required IReadOnlyList<string> AdjacentTo { get; init; }
    }
}
