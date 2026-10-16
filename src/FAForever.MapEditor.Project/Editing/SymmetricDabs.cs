using System.Numerics;
using FAForever.FileFormats.Map;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// One dab of a brush in one part of a symmetric map: where it lands and how its shape lies
    /// there (user stories F-12, S-1).
    /// </summary>
    /// <param name="X">In ogrids.</param>
    /// <param name="Rotation">How far the shape is turned, in degrees clockwise in the top view (the
    /// brushes' <c>Rotation</c>).</param>
    /// <param name="Mirrored">Whether the shape is flipped left to right before it is turned (the
    /// brushes' <c>Mirrored</c>).</param>
    /// <param name="Image">The part's transform, which took the first dab here: a brush that reads
    /// more than its footprint (the noise of <see cref="HeightBrush"/>) reads it through its inverse.</param>
    public readonly record struct SymmetricDab(float X, float Z, float Rotation, bool Mirrored, MapSymmetryTransform Image);

    /// <summary>
    /// The images of a dab in every part of a symmetric map, so that a stroke paints them all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The image of a dab is the dab seen through the part's transform: its centre moved, its shape
    /// turned by the transform's turn and, in a mirrored part, flipped. A shape turned by θ is the
    /// turn <c>R(θ)</c>; an image under a turn <c>R(φ)</c> is <c>R(φ + θ)</c>, under a mirror
    /// <c>R(φ)·F</c> (F the flip) it is <c>R(φ)·F·R(θ) = R(φ − θ)·F</c>.
    /// </para>
    /// <para>
    /// Images that coincide (a dab on a mirror's axis or at the middle of a turn) are painted once:
    /// a round brush when the centres coincide, a shaped brush when its turn and flip also do. Images
    /// that only overlap (near the axis) each paint as two dabs would; a stroke that crosses an axis
    /// is painted twice there, once by itself and once by its image, as it would be by hand.
    /// </para>
    /// </remarks>
    public static class SymmetricDabs
    {
        /// <summary>
        /// How close, in ogrids, two images' centres are to count as one.
        /// </summary>
        public const float SamePlace = 0.01f;

        /// <summary>
        /// The images of a dab at (<paramref name="x"/>, <paramref name="z"/>) turned by
        /// <paramref name="rotation"/> degrees, in the order of <paramref name="transforms"/> (the
        /// identity first gives the dab itself first), without the ones that coincide with an earlier one.
        /// </summary>
        /// <param name="transforms">The parts of the map; empty for no symmetry, which gives the dab alone.</param>
        /// <param name="round">Whether the brush is round, so that its turn and flip do not matter.</param>
        public static IReadOnlyList<SymmetricDab> Of(IReadOnlyList<MapSymmetryTransform> transforms, float x, float z, float rotation, bool round)
        {
            if (transforms.Count == 0)
            {
                return [new SymmetricDab(x, z, rotation, false, MapSymmetryTransform.Identity)];
            }
            List<SymmetricDab> dabs = new List<SymmetricDab>(transforms.Count);
            foreach (MapSymmetryTransform transform in transforms)
            {
                SymmetricDab dab = Image(transform, x, z, rotation);
                if (!dabs.Any(other => Coincide(other, dab, round)))
                {
                    dabs.Add(dab);
                }
            }
            return dabs;
        }

        /// <summary>
        /// The image of a dab under one transform.
        /// </summary>
        public static SymmetricDab Image(MapSymmetryTransform transform, float x, float z, float rotation)
        {
            Vector2 at = transform.Apply(new Vector2(x, z));
            float turn = transform.TurnAngle * 180 / MathF.PI;
            float turned = transform.IsMirror ? turn - rotation : turn + rotation;
            return new SymmetricDab(at.X, at.Y, Degrees(turned), transform.IsMirror, transform);
        }

        private static bool Coincide(SymmetricDab a, SymmetricDab b, bool round)
        {
            if (MathF.Abs(a.X - b.X) > SamePlace || MathF.Abs(a.Z - b.Z) > SamePlace)
            {
                return false;
            }
            return round || (a.Mirrored == b.Mirrored && MathF.Abs(Degrees(a.Rotation - b.Rotation)) < 0.01f);
        }

        /// <summary>
        /// An angle in degrees between -180 and 180.
        /// </summary>
        private static float Degrees(float degrees)
        {
            float wrapped = MathF.IEEERemainder(degrees, 360);
            return wrapped <= -180 ? wrapped + 360 : wrapped;
        }
    }
}
