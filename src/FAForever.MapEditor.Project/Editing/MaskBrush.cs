using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// A brush that paints a mask, a grid of bytes from 0 (nothing) to 255 (full): a terrain
    /// layer's (<see cref="StratumMasks"/>), the water's, or a mask for tools
    /// (<see cref="MapDocument.Masks"/>). One <see cref="Apply"/> is one dab, round or of a
    /// <see cref="Shape"/>. A mask need not have the map's size (most are half of it); the brush
    /// works in the map's ogrids and paints the mask's pixels under them.
    /// </summary>
    public sealed record MaskBrush
    {
        /// <summary>
        /// The radius in ogrids.
        /// </summary>
        public float Radius { get; init; } = 8;

        /// <summary>
        /// How far a dab moves the mask towards full (or, erasing, towards empty) at the centre,
        /// from 0 (nothing) to 1 (all the way).
        /// </summary>
        public float Strength { get; init; } = 0.5f;

        /// <summary>
        /// Whether the brush takes the layer away instead of painting it.
        /// </summary>
        public bool Erase { get; init; }

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
        /// Whether the shape is flipped left to right before it is turned by <see cref="Rotation"/>:
        /// the image of a dab in a mirrored part of a symmetric map (<see cref="SymmetricDabs"/>).
        /// </summary>
        public bool Mirrored { get; init; }

        /// <summary>
        /// Where it may paint, by the terrain's slope and height; null paints everywhere.
        /// </summary>
        public PaintLimits? Limits { get; init; }

        /// <summary>
        /// One dab centred on (<paramref name="x"/>, <paramref name="z"/>) in ogrids of a map of
        /// <paramref name="mapWidth"/> by <paramref name="mapHeight"/> ogrids.
        /// </summary>
        /// <param name="terrain">The heights that <see cref="Limits"/> are checked against; without
        /// them the limits do not apply.</param>
        public void Apply(TiledBuffer<byte>.Builder mask, int mapWidth, int mapHeight, float x, float z, Heightmap? terrain = null)
        {
            if (Radius <= 0)
            {
                return;
            }
            float target = Erase ? 0 : 255;
            float strength = Strength;
            GridDab.Paint(mask.Width, mask.Height, mapWidth, mapHeight, x, z, new BrushFootprint(Radius, Shape, Rotation, Falloff, Mirrored), Limits, terrain,
                (px, pz, weight) =>
                {
                    float value = mask[px, pz];
                    float changed = value + (target - value) * Math.Clamp(strength * weight, 0, 1);
                    mask[px, pz] = (byte)Math.Clamp(MathF.Round(changed), 0, 255);
                });
        }
    }

    /// <summary>
    /// A brush that sets a grid of codes, such as the terrain's types (user story T-6): no blending,
    /// every pixel where the brush works at least <see cref="Threshold"/> gets <see cref="Value"/>.
    /// The grid may have another size than the map, as with <see cref="MaskBrush"/>.
    /// </summary>
    public sealed record CodeBrush
    {
        /// <summary>
        /// The radius in ogrids.
        /// </summary>
        public float Radius { get; init; } = 8;

        /// <summary>
        /// The code it sets.
        /// </summary>
        public byte Value { get; init; }

        /// <summary>
        /// The weight from which on a pixel is set, from 0 to 1: with a smooth round brush, 0.5 sets
        /// the inner half of the radius.
        /// </summary>
        public float Threshold { get; init; } = 0.5f;

        /// <inheritdoc cref="MaskBrush.Shape"/>
        public BrushShape? Shape { get; init; }

        /// <inheritdoc cref="MaskBrush.Falloff"/>
        public BrushFalloff Falloff { get; init; } = BrushFalloff.Sharp;

        /// <inheritdoc cref="MaskBrush.Rotation"/>
        public float Rotation { get; init; }

        /// <inheritdoc cref="MaskBrush.Mirrored"/>
        public bool Mirrored { get; init; }

        /// <inheritdoc cref="MaskBrush.Limits"/>
        public PaintLimits? Limits { get; init; }

        /// <inheritdoc cref="MaskBrush.Apply"/>
        public void Apply(TiledBuffer<byte>.Builder grid, int mapWidth, int mapHeight, float x, float z, Heightmap? terrain = null)
        {
            if (Radius <= 0)
            {
                return;
            }
            byte value = Value;
            float threshold = Math.Max(Threshold, float.Epsilon);
            GridDab.Paint(grid.Width, grid.Height, mapWidth, mapHeight, x, z, new BrushFootprint(Radius, Shape, Rotation, Falloff, Mirrored), Limits, terrain,
                (px, pz, weight) =>
                {
                    if (weight >= threshold)
                    {
                        grid[px, pz] = value;
                    }
                });
        }
    }
}
