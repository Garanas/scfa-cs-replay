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

        /// <summary>
        /// The height of the terrain at any place, in game units: between the four corners around
        /// it, and at the nearest edge for a place outside the map.
        /// </summary>
        public float HeightAt(float x, float z)
        {
            x = Math.Clamp(x, 0, Width);
            z = Math.Clamp(z, 0, Height);
            int x0 = Math.Min((int)x, Math.Max(0, Width - 1));
            int z0 = Math.Min((int)z, Math.Max(0, Height - 1));
            int x1 = Math.Min(x0 + 1, Width);
            int z1 = Math.Min(z0 + 1, Height);
            float u = x - x0;
            float v = z - z0;
            float top = GetHeight(x0, z0) * (1 - u) + GetHeight(x1, z0) * u;
            float bottom = GetHeight(x0, z1) * (1 - u) + GetHeight(x1, z1) * u;
            return top * (1 - v) + bottom * v;
        }
    }
}
