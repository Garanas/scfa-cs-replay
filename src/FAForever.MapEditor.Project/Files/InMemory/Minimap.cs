namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// The settings of the strategic view (the minimap): its contour bands and colours
    /// (<see cref="FileFormats.Map.ScmapMinimap"/>).
    /// </summary>
    public sealed record Minimap
    {
        /// <summary>
        /// The number of contour bands over the land's heights, 1 to 100 (0 counts as 1).
        /// </summary>
        public int ContourInterval { get; init; }

        public Color32 DeepWaterColor { get; init; }

        public Color32 ContourColor { get; init; }

        public Color32 ShoreColor { get; init; }

        public Color32 LandStartColor { get; init; }

        public Color32 LandEndColor { get; init; }

        /// <summary>
        /// How far the borders of the map imager are lifted above the terrain's middle height; stored
        /// from version 57 on.
        /// </summary>
        public float? ImagerElevationOffset { get; init; }
    }
}
