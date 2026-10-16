namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// How the sky cube map is generated from the map (the Procedural mode): six faces seen from a
    /// point above the map's middle, the map's sky above the horizon and the map itself below it.
    /// Kept in <c>map.json</c> (<c>procedural.skyCube</c>); the cube map itself is one of the map's
    /// own files (<see cref="Generation.SkyCubeMaps.FilePath"/>).
    /// </summary>
    public sealed record SkyCubeSettings
    {
        public static SkyCubeSettings Default { get; } = new SkyCubeSettings();

        public const int SmallestResolution = 512;

        public const int LargestResolution = 2048;

        /// <summary>
        /// The size of a face in pixels, a power of two from 512 to 2048.
        /// </summary>
        public int Resolution { get; init; } = 1024;

        /// <summary>
        /// How high the eye is above the terrain or the water at the map's middle (whichever is
        /// higher), in ogrids.
        /// </summary>
        public float EyeHeight { get; init; } = 40;

        /// <summary>
        /// Whether the faces show the map below the horizon; off, the horizon's colour fills the
        /// lower half as the sky dome does.
        /// </summary>
        public bool IncludeTerrain { get; init; } = true;

        /// <summary>
        /// Whether the sun shows as a disc in its direction (<see cref="Lighting.SunDirection"/>).
        /// </summary>
        public bool SunDisc { get; init; } = true;

        public bool IsDefault => Equals(Default);

        /// <summary>
        /// A resolution made valid: the power of two nearest to it from 512 to 2048.
        /// </summary>
        public static int ValidResolution(int resolution)
        {
            int clamped = Math.Clamp(resolution, SmallestResolution, LargestResolution);
            int lower = (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)clamped);
            if (lower != clamped)
            {
                int below = lower / 2;
                lower = clamped - below < lower - clamped ? below : lower;
            }
            return Math.Clamp(lower, SmallestResolution, LargestResolution);
        }
    }
}
