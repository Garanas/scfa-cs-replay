namespace FAForever.MapEditor.Project.Files.Serialized
{
    /// <summary>
    /// A start position, named after its army.
    /// </summary>
    public sealed record SpawnDto
    {
        /// <summary>
        /// E.g. <c>ARMY_1</c>.
        /// </summary>
        public required string Army { get; init; }

        public required Vector3Dto Position { get; init; }

        /// <summary>
        /// The rotation around each axis, in radians.
        /// </summary>
        public Vector3Dto? Orientation { get; init; }

        /// <summary>
        /// Where the no-rush circle of the army lies relative to the start position; absent for a
        /// circle around it.
        /// </summary>
        public Vector2Dto? NoRushOffset { get; init; }
    }
}
