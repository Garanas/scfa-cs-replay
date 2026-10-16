namespace FAForever.MapEditor.Project.Files.Serialized
{
    /// <summary>
    /// How the sky cube map is generated (<c>procedural.skyCube</c> in <c>map.json</c>); every
    /// property is optional.
    /// </summary>
    public sealed record SkyCubeDto
    {
        /// <summary>
        /// The size of a face in pixels, 512 to 2048.
        /// </summary>
        public int? Resolution { get; init; }

        /// <summary>
        /// How high the eye is above the map's middle, in ogrids.
        /// </summary>
        public float? EyeHeight { get; init; }

        /// <summary>
        /// Whether the faces show the map below the horizon.
        /// </summary>
        public bool? IncludeTerrain { get; init; }

        /// <summary>
        /// Whether the sun shows as a disc.
        /// </summary>
        public bool? SunDisc { get; init; }
    }
}
