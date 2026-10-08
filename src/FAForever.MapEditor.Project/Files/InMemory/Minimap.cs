namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// The colours of the minimap.
    /// </summary>
    public sealed record Minimap
    {
        public int ContourInterval { get; init; }

        public Color32 DeepWaterColor { get; init; }

        public Color32 ContourColor { get; init; }

        public Color32 ShoreColor { get; init; }

        public Color32 LandStartColor { get; init; }

        public Color32 LandEndColor { get; init; }

        /// <summary>
        /// A number after the colours in version 60, whose meaning is unknown.
        /// </summary>
        public float? Unknown { get; init; }
    }
}
