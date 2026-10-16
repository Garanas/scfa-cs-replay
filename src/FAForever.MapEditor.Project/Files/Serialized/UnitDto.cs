namespace FAForever.MapEditor.Project.Files.Serialized
{
    /// <summary>
    /// A unit placed on the map, e.g. a civilian building or a wreck.
    /// </summary>
    public sealed record UnitDto
    {
        /// <summary>
        /// E.g. <c>UNIT_5</c>.
        /// </summary>
        public required string Name { get; init; }

        /// <summary>
        /// The unit's blueprint id, e.g. <c>uel0307</c>.
        /// </summary>
        public required string Blueprint { get; init; }

        public required Vector3Dto Position { get; init; }

        /// <summary>
        /// The rotation around each axis, in radians.
        /// </summary>
        public required Vector3Dto Orientation { get; init; }
    }
}
