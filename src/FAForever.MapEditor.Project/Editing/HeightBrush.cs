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

        /// <summary>
        /// Sets each height to <see cref="HeightBrush.Target"/>, as far as the brush works there: all
        /// the way at the centre (and everywhere inside a sharp brush), whatever the strength.
        /// </summary>
        SetHeight,

        /// <summary>
        /// Adds noise of up to <see cref="HeightBrush.Strength"/> game units up or down, with
        /// features about <see cref="HeightBrush.NoiseScale"/> ogrids across. The noise is fixed by
        /// <see cref="HeightBrush.Seed"/> and the place, so the dabs of a stroke fit together.
        /// </summary>
        Noise,

        /// <summary>
        /// Wears slopes steeper than <see cref="HeightBrush.Talus"/> down, the earth sliding onto the
        /// lower neighbours (thermal erosion), by <see cref="HeightBrush.Strength"/> (0 to 1) at the
        /// centre. A stroke wears down a little more with every dab.
        /// </summary>
        Erode,

        /// <summary>
        /// Multiplies each height's distance from <see cref="HeightBrush.Target"/> by
        /// <see cref="HeightBrush.Factor"/> at the centre: above 1 hills grow and valleys deepen,
        /// below 1 the terrain flattens towards the base.
        /// </summary>
        Scale,
    }

    /// <summary>
    /// A brush that changes the heights of the terrain: one <see cref="Apply"/> is one dab, round or
    /// of a <see cref="Shape"/>. A stroke is a series of dabs in one <see cref="EditTransaction"/>,
    /// so that it is one undo step.
    /// </summary>
    public sealed record HeightBrush
    {
        public HeightBrushMode Mode { get; init; } = HeightBrushMode.Raise;

        /// <summary>
        /// The radius in ogrids (one ogrid is one sample of the heightmap).
        /// </summary>
        public float Radius { get; init; } = 8;

        /// <summary>
        /// For raising, lowering and noise, the change at the centre in game units; for smoothing,
        /// flattening and eroding, how far a dab goes at the centre, from 0 (nothing) to 1 (all the
        /// way).
        /// </summary>
        public float Strength { get; init; } = 1;

        /// <summary>
        /// The height in game units that <see cref="HeightBrushMode.Flatten"/> moves towards and
        /// <see cref="HeightBrushMode.SetHeight"/> sets; the base that <see cref="HeightBrushMode.Scale"/>
        /// scales around.
        /// </summary>
        public float Target { get; init; }

        /// <summary>
        /// What <see cref="HeightBrushMode.Scale"/> multiplies by at the centre.
        /// </summary>
        public float Factor { get; init; } = 1.1f;

        /// <summary>
        /// The size of the features of <see cref="HeightBrushMode.Noise"/>, in ogrids.
        /// </summary>
        public float NoiseScale { get; init; } = 16;

        /// <summary>
        /// Chooses the noise of <see cref="HeightBrushMode.Noise"/>: the same seed gives the same
        /// noise at the same place.
        /// </summary>
        public int Seed { get; init; }

        /// <summary>
        /// The steepest slope that <see cref="HeightBrushMode.Erode"/> leaves alone, in game units
        /// of height per ogrid (1 is 45 degrees).
        /// </summary>
        public float Talus { get; init; } = 0.6f;

        /// <summary>
        /// The brush's shape: null for round, with <see cref="Falloff"/>; a picture fills the
        /// square around the radius.
        /// </summary>
        public BrushShape? Shape { get; init; }

        /// <summary>
        /// How a round brush fades from its centre to its radius.
        /// </summary>
        public BrushFalloff Falloff { get; init; } = BrushFalloff.Smooth;

        /// <summary>
        /// How far the shape is turned, in degrees clockwise as the top view shows the map.
        /// </summary>
        public float Rotation { get; init; }

        /// <summary>
        /// The lowest height the brush makes, in game units; null for no limit. A height already
        /// below it is not raised to it, only kept from going lower.
        /// </summary>
        public float? MinHeight { get; init; }

        /// <summary>
        /// The highest height the brush makes, in game units; null for no limit. A height already
        /// above it is not lowered to it, only kept from going higher.
        /// </summary>
        public float? MaxHeight { get; init; }

        /// <summary>
        /// One dab centred on (<paramref name="x"/>, <paramref name="z"/>) in ogrids.
        /// </summary>
        /// <param name="scale">The height of one step of a sample (<see cref="Heightmap.Scale"/>).</param>
        public void Apply(TiledBuffer<ushort>.Builder samples, float scale, float x, float z)
        {
            BrushFootprint footprint = new BrushFootprint(Radius, Shape, Rotation, Falloff);
            int x0 = Math.Max(0, (int)MathF.Floor(x - footprint.Reach));
            int z0 = Math.Max(0, (int)MathF.Floor(z - footprint.Reach));
            int x1 = Math.Min(samples.Width - 1, (int)MathF.Ceiling(x + footprint.Reach));
            int z1 = Math.Min(samples.Height - 1, (int)MathF.Ceiling(z + footprint.Reach));
            if (x0 > x1 || z0 > z1 || Radius <= 0)
            {
                return;
            }

            // smoothing and eroding read the heights as they were before the dab, one sample around the area
            float[,]? before = Mode is HeightBrushMode.Smooth or HeightBrushMode.Erode ? Read(samples, x0 - 1, z0 - 1, x1 + 1, z1 + 1) : null;
            if (Mode == HeightBrushMode.Erode)
            {
                Erode(samples, scale, before!, x0, z0, x1, z1, footprint, x, z);
                return;
            }

            for (int sz = z0; sz <= z1; sz++)
            {
                for (int sx = x0; sx <= x1; sx++)
                {
                    float weight = footprint.Weight(sx - x, sz - z);
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
                        HeightBrushMode.SetHeight => Lerp(height, Target, weight),
                        HeightBrushMode.Noise => height + Strength * weight * HeightNoise.At(sx / NoiseScale, sz / NoiseScale, Seed),
                        HeightBrushMode.Scale => Target + (height - Target) * (1 + (Factor - 1) * weight),
                        _ => height,
                    };
                    samples[sx, sz] = Write(height, changed, scale);
                }
            }
        }

        /// <summary>
        /// A height as a sample, kept within the limits it was within.
        /// </summary>
        private ushort Write(float height, float changed, float scale)
        {
            float low = Math.Min(MinHeight ?? float.NegativeInfinity, height);
            float high = Math.Max(MaxHeight ?? float.PositiveInfinity, height);
            changed = Math.Clamp(changed, low, high);
            return (ushort)Math.Clamp(MathF.Round(changed / scale), 0, ushort.MaxValue);
        }

        /// <summary>
        /// One pass of thermal erosion under the brush: each sample whose drop to a neighbour is
        /// steeper than the talus gives part of the excess to its lower neighbours, so the earth
        /// stays (apart from the rounding to samples).
        /// </summary>
        private void Erode(TiledBuffer<ushort>.Builder samples, float scale, float[,] before, int x0, int z0, int x1, int z1, BrushFootprint footprint, float x, float z)
        {
            int columns = before.GetLength(0);
            int rows = before.GetLength(1);
            float[,] change = new float[columns, rows];
            Span<float> drops = stackalloc float[8];
            for (int sz = z0; sz <= z1; sz++)
            {
                for (int sx = x0; sx <= x1; sx++)
                {
                    float weight = footprint.Weight(sx - x, sz - z);
                    if (weight <= 0)
                    {
                        continue;
                    }
                    int cx = sx - x0 + 1;
                    int cz = sz - z0 + 1;
                    float height = before[cx, cz] * scale;
                    float excess = 0;
                    float steepest = 0;
                    for (int n = 0; n < 8; n++)
                    {
                        (int dx, int dz) = Neighbours[n];
                        float distance = dx != 0 && dz != 0 ? MathF.Sqrt(2) : 1;
                        float drop = height - before[cx + dx, cz + dz] * scale - Talus * distance;
                        drops[n] = drop > 0 ? drop : 0;
                        excess += drops[n];
                        steepest = Math.Max(steepest, drops[n]);
                    }
                    if (excess <= 0)
                    {
                        continue;
                    }
                    // half the steepest excess evens that slope out; the rest of the neighbours share by their drop
                    float moved = 0.5f * steepest * Math.Clamp(Strength * weight, 0, 1);
                    for (int n = 0; n < 8; n++)
                    {
                        if (drops[n] > 0)
                        {
                            (int dx, int dz) = Neighbours[n];
                            float share = moved * drops[n] / excess;
                            change[cx + dx, cz + dz] += share;
                            change[cx, cz] -= share;
                        }
                    }
                }
            }

            for (int cz = 0; cz < rows; cz++)
            {
                for (int cx = 0; cx < columns; cx++)
                {
                    int sx = x0 - 1 + cx;
                    int sz = z0 - 1 + cz;
                    if (change[cx, cz] == 0 || sx < 0 || sz < 0 || sx >= samples.Width || sz >= samples.Height)
                    {
                        continue;
                    }
                    float height = samples[sx, sz] * scale;
                    samples[sx, sz] = Write(height, height + change[cx, cz], scale);
                }
            }
        }

        private static readonly (int X, int Z)[] Neighbours = [(-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1)];

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

    /// <summary>
    /// Smooth noise from -1 to 1 for <see cref="HeightBrushMode.Noise"/>: three octaves of value
    /// noise on a grid of whole numbers, the same for the same seed and place.
    /// </summary>
    public static class HeightNoise
    {
        public static float At(float x, float z, int seed)
        {
            float sum = 0;
            float amplitude = 1;
            float total = 0;
            for (int octave = 0; octave < 3; octave++)
            {
                sum += amplitude * Value(x, z, seed + octave * 1013);
                total += amplitude;
                amplitude *= 0.5f;
                x *= 2;
                z *= 2;
            }
            return sum / total;
        }

        private static float Value(float x, float z, int seed)
        {
            int ix = (int)MathF.Floor(x);
            int iz = (int)MathF.Floor(z);
            float fx = Fade(x - ix);
            float fz = Fade(z - iz);
            float top = Corner(ix, iz, seed) + (Corner(ix + 1, iz, seed) - Corner(ix, iz, seed)) * fx;
            float bottom = Corner(ix, iz + 1, seed) + (Corner(ix + 1, iz + 1, seed) - Corner(ix, iz + 1, seed)) * fx;
            return top + (bottom - top) * fz;
        }

        private static float Fade(float t) => t * t * (3 - 2 * t);

        /// <summary>
        /// A value from -1 to 1 for a corner of the grid, from a hash of its place and the seed.
        /// </summary>
        private static float Corner(int x, int z, int seed)
        {
            uint hash = unchecked((uint)x * 0x8da6b343u ^ (uint)z * 0xd8163841u ^ (uint)seed * 0xcb1ab31fu);
            hash ^= hash >> 13;
            hash = unchecked(hash * 0x5bd1e995u);
            hash ^= hash >> 15;
            return (hash & 0xffffff) / (float)0x7fffff - 1;
        }
    }
}
