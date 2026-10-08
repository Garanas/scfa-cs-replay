namespace FAForever.MapEditor.Project.Files.Serialized
{
    /// <summary>
    /// The colours of the minimap.
    /// </summary>
    public sealed record MinimapDto
    {
        public required int ContourInterval { get; init; }

        public required string DeepWaterColor { get; init; }

        public required string ContourColor { get; init; }

        public required string ShoreColor { get; init; }

        public required string LandStartColor { get; init; }

        public required string LandEndColor { get; init; }

        /// <summary>
        /// A number after the colours in version 60, whose meaning is unknown.
        /// </summary>
        public float? Unknown { get; init; }
    }
}
