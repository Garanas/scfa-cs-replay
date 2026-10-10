namespace FAForever.MapEditor.Project.Files.Serialized
{
    /// <summary>
    /// A team the map maker puts playable armies in.
    /// </summary>
    public sealed record TeamDto
    {
        public required string Name { get; init; }

        /// <summary>
        /// The playable armies in the team, in order.
        /// </summary>
        public required IReadOnlyList<string> Armies { get; init; }
    }
}
