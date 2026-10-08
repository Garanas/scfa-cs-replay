using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// A round brush that paints a terrain layer's mask (<see cref="StratumMasks"/>): one
    /// <see cref="Apply"/> is one dab, strongest at the centre and fading to nothing at
    /// <see cref="Radius"/>. A mask need not have the map's size (most are half of it); the brush
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
        /// One dab centred on (<paramref name="x"/>, <paramref name="z"/>) in ogrids of a map of
        /// <paramref name="mapWidth"/> by <paramref name="mapHeight"/> ogrids.
        /// </summary>
        public void Apply(TiledBuffer<byte>.Builder mask, int mapWidth, int mapHeight, float x, float z)
        {
            if (Radius <= 0)
            {
                return;
            }

            // pixels per ogrid; a pixel's value is the one at its centre
            float scaleX = mask.Width / (float)mapWidth;
            float scaleZ = mask.Height / (float)mapHeight;
            float centreX = x * scaleX;
            float centreZ = z * scaleZ;
            int x0 = Math.Max(0, (int)MathF.Floor(centreX - Radius * scaleX));
            int z0 = Math.Max(0, (int)MathF.Floor(centreZ - Radius * scaleZ));
            int x1 = Math.Min(mask.Width - 1, (int)MathF.Ceiling(centreX + Radius * scaleX));
            int z1 = Math.Min(mask.Height - 1, (int)MathF.Ceiling(centreZ + Radius * scaleZ));
            float target = Erase ? 0 : 255;

            for (int pz = z0; pz <= z1; pz++)
            {
                for (int px = x0; px <= x1; px++)
                {
                    float dx = (px + 0.5f) / scaleX - x;
                    float dz = (pz + 0.5f) / scaleZ - z;
                    float weight = BrushFalloff.Weight(MathF.Sqrt(dx * dx + dz * dz), Radius);
                    if (weight <= 0)
                    {
                        continue;
                    }
                    float value = mask[px, pz];
                    float changed = value + (target - value) * Math.Clamp(Strength * weight, 0, 1);
                    mask[px, pz] = (byte)Math.Clamp(MathF.Round(changed), 0, 255);
                }
            }
        }
    }

    /// <summary>
    /// How strongly a brush works at a distance from its centre: a smooth bell, 1 at the centre and
    /// 0 from the radius on.
    /// </summary>
    internal static class BrushFalloff
    {
        public static float Weight(float distance, float radius) =>
            distance >= radius ? 0 : 0.5f * (1 + MathF.Cos(MathF.PI * distance / radius));
    }
}
