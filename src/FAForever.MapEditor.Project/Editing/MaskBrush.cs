using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// A brush that paints a terrain layer's mask (<see cref="StratumMasks"/>): one
    /// <see cref="Apply"/> is one dab, round or of a <see cref="Shape"/>. A mask need not have the
    /// map's size (most are half of it); the brush works in the map's ogrids and paints the mask's
    /// pixels under them.
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
        /// The brush's shape: null for round, strongest at the centre and fading to nothing at the
        /// radius; a picture fills the square around the radius.
        /// </summary>
        public BrushShape? Shape { get; init; }

        /// <summary>
        /// How far the shape is turned, in degrees clockwise as the top view shows the map.
        /// </summary>
        public float Rotation { get; init; }

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
            BrushFootprint footprint = new BrushFootprint(Radius, Shape, Rotation);
            int x0 = Math.Max(0, (int)MathF.Floor(centreX - footprint.Reach * scaleX));
            int z0 = Math.Max(0, (int)MathF.Floor(centreZ - footprint.Reach * scaleZ));
            int x1 = Math.Min(mask.Width - 1, (int)MathF.Ceiling(centreX + footprint.Reach * scaleX));
            int z1 = Math.Min(mask.Height - 1, (int)MathF.Ceiling(centreZ + footprint.Reach * scaleZ));
            float target = Erase ? 0 : 255;

            for (int pz = z0; pz <= z1; pz++)
            {
                for (int px = x0; px <= x1; px++)
                {
                    float dx = (px + 0.5f) / scaleX - x;
                    float dz = (pz + 0.5f) / scaleZ - z;
                    float weight = footprint.Weight(dx, dz);
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

    /// <summary>
    /// Where a dab works and how strongly: the round falloff, or a shape turned and scaled to the
    /// radius.
    /// </summary>
    internal readonly struct BrushFootprint
    {
        private readonly float _radius;
        private readonly BrushShape? _shape;
        private readonly float _cos;
        private readonly float _sin;

        /// <param name="rotation">In degrees clockwise in the top view, where x runs east and z south.</param>
        public BrushFootprint(float radius, BrushShape? shape, float rotation)
        {
            _radius = radius;
            _shape = shape;
            (_sin, _cos) = MathF.SinCos(rotation * MathF.PI / 180);
        }

        /// <summary>
        /// How far from the centre the dab can reach: the radius, or the corner of a shape's square.
        /// </summary>
        public float Reach => _shape is null ? _radius : _radius * MathF.Sqrt(2);

        /// <summary>
        /// The weight at an offset from the centre, in ogrids, from 0 to 1.
        /// </summary>
        public float Weight(float dx, float dz)
        {
            if (_shape is null)
            {
                return BrushFalloff.Weight(MathF.Sqrt(dx * dx + dz * dz), _radius);
            }
            // turned back by the rotation into the shape's square
            float u = (dx * _cos + dz * _sin) / _radius;
            float v = (dz * _cos - dx * _sin) / _radius;
            return _shape.Sample(u, v);
        }
    }
}
