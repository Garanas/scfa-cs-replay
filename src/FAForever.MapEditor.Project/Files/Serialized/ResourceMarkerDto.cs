namespace FAForever.MapEditor.Project.Files.Serialized
{
    /// <summary>
    /// A mass or hydrocarbon deposit.
    /// </summary>
    public sealed record ResourceMarkerDto
    {
        public required string Name { get; init; }

        public required ResourceKind Kind { get; init; }

        public required Vector3Dto Position { get; init; }

        /// <summary>
        /// The rotation around each axis, in radians.
        /// </summary>
        public Vector3Dto? Orientation { get; init; }

        /// <summary>
        /// The army without which an adaptive map leaves the deposit out.
        /// </summary>
        public string? SpawnWithArmy { get; init; }
    }
}
