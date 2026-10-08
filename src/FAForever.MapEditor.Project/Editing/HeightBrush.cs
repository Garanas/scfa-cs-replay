using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// What a <see cref="HeightBrush"/> does to the terrain under it.
    /// </summary>
    public enum HeightBrushMode
    {
        /// <summary>
        /// Adds <see cref="HeightBrush.Strength"/> game units at the centre.
        /// </summary>
        Raise,

        /// <summary>
        /// Takes <see cref="HeightBrush.Strength"/> game units away at the centre.
        /// </summary>
        Lower,

        /// <summary>
        /// Moves each height towards the mean of its neighbours, by <see cref="HeightBrush.Strength"/>
        /// (0 to 1) at the centre.
        /// </summary>
        Smooth,

        /// <summary>
        /// Moves each height towards <see cref="HeightBrush.Target"/>, by <see cref="HeightBrush.Strength"/>
        /// (0 to 1) at the centre.
        /// </summary>
        Flatten,
    }

    /// <summary>
    /// A round brush that changes the heights of the terrain: one <see cref="Apply"/> is one dab,
    /// strongest at the centre and fading to nothing at <see cref="Radius"/>. A stroke is a series
    /// of dabs in one <see cref="EditTransaction"/>, so that it is one undo step.
    /// </summary>
    public sealed record HeightBrush
    {
        public HeightBrushMode Mode { get; init; } = HeightBrushMode.Raise;

        /// <summary>
        /// The radius in ogrids (one ogrid is one sample of the heightmap).
        /// </summary>
        public float Radius { get; init; } = 8;

        /// <summary>
        /// For raising and lowering, the change at the centre in game units; for smoothing and
        /// flattening, how far a dab goes at the centre, from 0 (nothing) to 1 (all the way).
        /// </summary>
        public float Strength { get; init; } = 1;

        /// <summary>
        /// The height in game units that <see cref="HeightBrushMode.Flatten"/> moves towards.
        /// </summary>
        public float Target { get; init; }

        /// <summary>
        /// One dab centred on (<paramref name="x"/>, <paramref name="z"/>) in ogrids.
        /// </summary>
        /// <param name="scale">The height of one step of a sample (<see cref="Heightmap.Scale"/>).</param>
        public void Apply(TiledBuffer<ushort>.Builder samples, float scale, float x, float z)
        {
            int x0 = Math.Max(0, (int)MathF.Floor(x - Radius));
            int z0 = Math.Max(0, (int)MathF.Floor(z - Radius));
            int x1 = Math.Min(samples.Width - 1, (int)MathF.Ceiling(x + Radius));
            int z1 = Math.Min(samples.Height - 1, (int)MathF.Ceiling(z + Radius));
            if (x0 > x1 || z0 > z1 || Radius <= 0)
            {
                return;
            }

            // smoothing reads the heights as they were before the dab, one sample around the area
            float[,]? before = Mode == HeightBrushMode.Smooth ? Read(samples, x0 - 1, z0 - 1, x1 + 1, z1 + 1) : null;

            for (int sz = z0; sz <= z1; sz++)
            {
                for (int sx = x0; sx <= x1; sx++)
                {
                    float weight = Falloff(MathF.Sqrt((sx - x) * (sx - x) + (sz - z) * (sz - z)));
                    if (weight <= 0)
                    {
                        continue;
                    }

                    float height = samples[sx, sz] * scale;
                    float changed = Mode switch
                    {
                        HeightBrushMode.Raise => height + Strength * weight,
                        HeightBrushMode.Lower => height - Strength * weight,
                        HeightBrushMode.Smooth => Lerp(height, Mean(before!, sx - x0 + 1, sz - z0 + 1) * scale, Strength * weight),
                        HeightBrushMode.Flatten => Lerp(height, Target, Strength * weight),
                        _ => height,
                    };
                    samples[sx, sz] = (ushort)Math.Clamp(MathF.Round(changed / scale), 0, ushort.MaxValue);
                }
            }
        }

        /// <summary>
        /// A smooth bell: 1 at the centre, 0 from the radius on.
        /// </summary>
        private float Falloff(float distance) =>
            distance >= Radius ? 0 : 0.5f * (1 + MathF.Cos(MathF.PI * distance / Radius));

        private static float Lerp(float from, float to, float amount) => from + (to - from) * Math.Clamp(amount, 0, 1);

        /// <summary>
        /// The samples of a rectangle, the edge repeated where it reaches past the heightmap.
        /// </summary>
        private static float[,] Read(TiledBuffer<ushort>.Builder samples, int x0, int z0, int x1, int z1)
        {
            float[,] values = new float[x1 - x0 + 1, z1 - z0 + 1];
            for (int z = z0; z <= z1; z++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    values[x - x0, z - z0] = samples[Math.Clamp(x, 0, samples.Width - 1), Math.Clamp(z, 0, samples.Height - 1)];
                }
            }
            return values;
        }

        /// <summary>
        /// The mean of a sample and its eight neighbours.
        /// </summary>
        private static float Mean(float[,] values, int x, int z)
        {
            float sum = 0;
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    sum += values[x + dx, z + dz];
                }
            }
            return sum / 9;
        }
    }
}
