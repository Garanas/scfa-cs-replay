using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// How strongly a round brush works from its centre to its radius. A brush with the shape of a
    /// picture (<see cref="BrushShape"/>) takes its strength from the picture.
    /// </summary>
    public enum BrushFalloff
    {
        /// <summary>
        /// A smooth bell: 1 at the centre, fading to 0 at the radius.
        /// </summary>
        Smooth,

        /// <summary>
        /// A cone: 1 at the centre, falling evenly to 0 at the radius.
        /// </summary>
        Linear,

        /// <summary>
        /// The same strength everywhere inside the radius, with a hard edge.
        /// </summary>
        Sharp,
    }

    /// <summary>
    /// Where a dab works and how strongly: the round falloff, or a shape turned and scaled to the
    /// radius.
    /// </summary>
    internal readonly struct BrushFootprint
    {
        private readonly float _radius;
        private readonly BrushShape? _shape;
        private readonly BrushFalloff _falloff;
        private readonly float _cos;
        private readonly float _sin;

        /// <param name="rotation">In degrees clockwise in the top view, where x runs east and z south.</param>
        public BrushFootprint(float radius, BrushShape? shape, float rotation, BrushFalloff falloff = BrushFalloff.Smooth)
        {
            _radius = radius;
            _shape = shape;
            _falloff = falloff;
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
                return Falloff(MathF.Sqrt(dx * dx + dz * dz) / _radius, _falloff);
            }
            // turned back by the rotation into the shape's square
            float u = (dx * _cos + dz * _sin) / _radius;
            float v = (dz * _cos - dx * _sin) / _radius;
            return _shape.Sample(u, v);
        }

        /// <summary>
        /// The weight of a round brush at a distance from its centre, in radii.
        /// </summary>
        public static float Falloff(float distance, BrushFalloff falloff) => distance >= 1 ? 0 : falloff switch
        {
            BrushFalloff.Linear => 1 - distance,
            BrushFalloff.Sharp => 1,
            _ => 0.5f * (1 + MathF.Cos(MathF.PI * distance)),
        };
    }

    /// <summary>
    /// Where a mask brush may paint: only where the terrain's slope and height are within limits
    /// (user story S-1). A limit left out does not limit.
    /// </summary>
    /// <param name="MinSlope">In degrees from flat.</param>
    /// <param name="MaxSlope">In degrees from flat.</param>
    /// <param name="MinHeight">In game units.</param>
    /// <param name="MaxHeight">In game units.</param>
    public sealed record PaintLimits(float? MinSlope = null, float? MaxSlope = null, float? MinHeight = null, float? MaxHeight = null)
    {
        /// <summary>
        /// Whether any limit is set.
        /// </summary>
        public bool Limits => MinSlope is not null || MaxSlope is not null || MinHeight is not null || MaxHeight is not null;

        /// <summary>
        /// Whether the terrain at a place, in ogrids, is within the limits.
        /// </summary>
        public bool Allows(Heightmap terrain, float x, float z)
        {
            if (MinHeight is not null || MaxHeight is not null)
            {
                float height = terrain.HeightAt(x, z);
                if (height < (MinHeight ?? float.NegativeInfinity) || height > (MaxHeight ?? float.PositiveInfinity))
                {
                    return false;
                }
            }
            if (MinSlope is not null || MaxSlope is not null)
            {
                float slope = SlopeAt(terrain, x, z);
                if (slope < (MinSlope ?? float.NegativeInfinity) || slope > (MaxSlope ?? float.PositiveInfinity))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// The slope of the terrain at a place, in degrees from flat, over one ogrid to each side.
        /// </summary>
        public static float SlopeAt(Heightmap terrain, float x, float z)
        {
            float dx = (terrain.HeightAt(x + 1, z) - terrain.HeightAt(x - 1, z)) / 2;
            float dz = (terrain.HeightAt(x, z + 1) - terrain.HeightAt(x, z - 1)) / 2;
            return MathF.Atan(MathF.Sqrt(dx * dx + dz * dz)) * 180 / MathF.PI;
        }
    }

    /// <summary>
    /// The pixels of a grid that one dab covers. A grid need not have the map's size (stratum and
    /// water masks are mostly half of it): the dab is in the map's ogrids, and each pixel counts
    /// at its centre.
    /// </summary>
    internal static class GridDab
    {
        /// <summary>
        /// A pixel the dab reaches with a weight above 0, and the place of its centre in ogrids.
        /// </summary>
        public delegate void Visit(int px, int pz, float weight);

        public static void Paint(int gridWidth, int gridHeight, int mapWidth, int mapHeight, float x, float z, BrushFootprint footprint,
            PaintLimits? limits, Heightmap? terrain, Visit visit)
        {
            // pixels per ogrid
            float scaleX = gridWidth / (float)mapWidth;
            float scaleZ = gridHeight / (float)mapHeight;
            float centreX = x * scaleX;
            float centreZ = z * scaleZ;
            int x0 = Math.Max(0, (int)MathF.Floor(centreX - footprint.Reach * scaleX));
            int z0 = Math.Max(0, (int)MathF.Floor(centreZ - footprint.Reach * scaleZ));
            int x1 = Math.Min(gridWidth - 1, (int)MathF.Ceiling(centreX + footprint.Reach * scaleX));
            int z1 = Math.Min(gridHeight - 1, (int)MathF.Ceiling(centreZ + footprint.Reach * scaleZ));
            bool limited = limits is { Limits: true } && terrain is not null;

            for (int pz = z0; pz <= z1; pz++)
            {
                for (int px = x0; px <= x1; px++)
                {
                    float ox = (px + 0.5f) / scaleX;
                    float oz = (pz + 0.5f) / scaleZ;
                    float weight = footprint.Weight(ox - x, oz - z);
                    if (weight <= 0 || (limited && !limits!.Allows(terrain!, ox, oz)))
                    {
                        continue;
                    }
                    visit(px, pz, weight);
                }
            }
        }
    }
}
