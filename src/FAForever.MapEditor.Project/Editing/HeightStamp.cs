using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// What a <see cref="HeightStamp"/> does to the terrain under it.
    /// </summary>
    public enum HeightStampMode
    {
        /// <summary>
        /// Adds the picture's value times <see cref="HeightStamp.Height"/>.
        /// </summary>
        Raise,

        /// <summary>
        /// Takes the picture's value times <see cref="HeightStamp.Height"/> away.
        /// </summary>
        Lower,

        /// <summary>
        /// Sets each height under the picture to <see cref="HeightStamp.Base"/> plus the picture's
        /// value times <see cref="HeightStamp.Height"/>: a stamp taken from a part of the map puts
        /// that part's shape back, the picture's black at the base.
        /// </summary>
        Set,
    }

    /// <summary>
    /// A height stamp (user story T-2): a greyscale picture pressed into the terrain at one place,
    /// turned and scaled, in one go. Unlike a brush it does not build up along a stroke: its value
    /// is the height (the picture stretched from 0 to 1, <see cref="BrushShape.Read"/>), so a stamp
    /// of a mountain is a mountain at a click. The outer <see cref="Edge"/> of the picture fades
    /// out, so it merges with the terrain around it.
    /// </summary>
    public sealed record HeightStamp
    {
        public HeightStampMode Mode { get; init; } = HeightStampMode.Raise;

        /// <summary>
        /// The picture; it fills the square of twice the radius, its longer side from edge to edge.
        /// </summary>
        public required BrushShape Shape { get; init; }

        /// <summary>
        /// Half the side of the stamp's square, in ogrids.
        /// </summary>
        public float Radius { get; init; } = 32;

        /// <summary>
        /// The height the picture's white stands for, in game units.
        /// </summary>
        public float Height { get; init; } = 20;

        /// <summary>
        /// The height of the picture's black for <see cref="HeightStampMode.Set"/>, in game units.
        /// </summary>
        public float Base { get; init; }

        /// <summary>
        /// How much of the picture fades out towards its sides, as a part of half its side: 0 for a
        /// hard edge, 0.5 fading from halfway.
        /// </summary>
        public float Edge { get; init; } = 0.15f;

        /// <summary>
        /// How far the picture is turned, in degrees clockwise as the top view shows the map.
        /// </summary>
        public float Rotation { get; init; }

        /// <summary>
        /// Whether the picture is flipped left to right before it is turned: its image in a mirrored
        /// part of a symmetric map (<see cref="SymmetricDabs"/>).
        /// </summary>
        public bool Mirrored { get; init; }

        /// <summary>
        /// Presses the stamp centred on (<paramref name="x"/>, <paramref name="z"/>) in ogrids.
        /// </summary>
        /// <param name="scale">The height of one step of a sample (<see cref="Heightmap.Scale"/>).</param>
        public void Apply(TiledBuffer<ushort>.Builder samples, float scale, float x, float z)
        {
            if (Radius <= 0)
            {
                return;
            }
            float reach = Radius * MathF.Sqrt(2);
            int x0 = Math.Max(0, (int)MathF.Floor(x - reach));
            int z0 = Math.Max(0, (int)MathF.Floor(z - reach));
            int x1 = Math.Min(samples.Width - 1, (int)MathF.Ceiling(x + reach));
            int z1 = Math.Min(samples.Height - 1, (int)MathF.Ceiling(z + reach));
            (float sin, float cos) = MathF.SinCos(Rotation * MathF.PI / 180);
            for (int sz = z0; sz <= z1; sz++)
            {
                for (int sx = x0; sx <= x1; sx++)
                {
                    float dx = sx - x;
                    float dz = sz - z;
                    // turned back by the rotation into the picture's square, as a brush's shape
                    float u = (dx * cos + dz * sin) / Radius;
                    float v = (dz * cos - dx * sin) / Radius;
                    if (Mirrored)
                    {
                        u = -u;
                    }
                    float border = Shape.Border(u, v);
                    if (border <= 0)
                    {
                        continue;
                    }
                    float fade = Edge <= 0 ? 1 : Smooth(Math.Clamp(border / Edge, 0, 1));
                    float value = Shape.Sample(u, v, clamp: true);
                    float height = samples[sx, sz] * scale;
                    float changed = Mode switch
                    {
                        HeightStampMode.Raise => height + value * Height * fade,
                        HeightStampMode.Lower => height - value * Height * fade,
                        _ => height + (Base + value * Height - height) * fade,
                    };
                    samples[sx, sz] = (ushort)Math.Clamp(MathF.Round(changed / scale), 0, ushort.MaxValue);
                }
            }
        }

        private static float Smooth(float t) => t * t * (3 - 2 * t);
    }

    /// <summary>
    /// A part of the heightmap taken as a stamp (user story T-2): its samples stretched from the
    /// lowest (0) to the highest (65535), to press elsewhere with <see cref="HeightStampMode.Set"/>,
    /// <see cref="HeightStamp.Base"/> <see cref="Lowest"/> and <see cref="HeightStamp.Height"/>
    /// <see cref="Highest"/> minus <see cref="Lowest"/>.
    /// </summary>
    /// <param name="Samples">Row by row from the north.</param>
    /// <param name="Lowest">In game units.</param>
    /// <param name="Highest">In game units.</param>
    public sealed record HeightStampCapture(int Width, int Height, ushort[] Samples, float Lowest, float Highest)
    {
        /// <summary>
        /// The samples of the heightmap between two corners (in ogrids, any order, kept inside the map).
        /// </summary>
        /// <exception cref="ArgumentException">The rectangle is smaller than 2 by 2 samples.</exception>
        public static HeightStampCapture Of(Heightmap heights, int x0, int z0, int x1, int z1)
        {
            int left = Math.Clamp(Math.Min(x0, x1), 0, heights.Width);
            int right = Math.Clamp(Math.Max(x0, x1), 0, heights.Width);
            int top = Math.Clamp(Math.Min(z0, z1), 0, heights.Height);
            int bottom = Math.Clamp(Math.Max(z0, z1), 0, heights.Height);
            int width = right - left + 1;
            int height = bottom - top + 1;
            if (width < 2 || height < 2)
            {
                throw new ArgumentException("A stamp needs a part of the map at least 2 by 2 ogrids");
            }
            ushort low = ushort.MaxValue;
            ushort high = 0;
            for (int z = top; z <= bottom; z++)
            {
                for (int x = left; x <= right; x++)
                {
                    ushort sample = heights.Samples[x, z];
                    low = Math.Min(low, sample);
                    high = Math.Max(high, sample);
                }
            }
            ushort[] samples = new ushort[width * height];
            float stretch = high == low ? 0 : 65535f / (high - low);
            for (int z = top; z <= bottom; z++)
            {
                for (int x = left; x <= right; x++)
                {
                    samples[(z - top) * width + x - left] = (ushort)MathF.Round((heights.Samples[x, z] - low) * stretch);
                }
            }
            return new HeightStampCapture(width, height, samples, low * heights.Scale, high * heights.Scale);
        }

        /// <summary>
        /// Writes it as a 16-bit grey PNG, a brush picture of the workspace.
        /// </summary>
        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            ProjectImages.WriteGray16(path, Width, Height, Samples);
        }
    }
}
