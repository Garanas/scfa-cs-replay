namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// The heights of the terrain: (<see cref="Width"/> + 1) by (<see cref="Height"/> + 1) samples,
    /// one at every corner of an ogrid.
    /// </summary>
    public sealed record Heightmap
    {
        /// <summary>
        /// The height of one step of a sample, usually 1/128.
        /// </summary>
        public required float Scale { get; init; }

        public required TiledBuffer<ushort> Samples { get; init; }

        /// <summary>
        /// The width of the map in ogrids, e.g. 256 for a 5 km map.
        /// </summary>
        public int Width => Samples.Width - 1;

        /// <summary>
        /// The height of the map in ogrids.
        /// </summary>
        public int Height => Samples.Height - 1;

        /// <summary>
        /// The height of the terrain at a corner, in game units: 0 &lt;= x &lt;= Width, 0 &lt;= z &lt;= Height.
        /// </summary>
        public float GetHeight(int x, int z) => Samples[x, z] * Scale;
    }
}
