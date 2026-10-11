using System.Collections.Immutable;

namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// How a curve shapes the terrain across it (user story T-4).
    /// </summary>
    public enum CurveProfile
    {
        /// <summary>
        /// Raises a ridge along it by the points' heights, full over the width, fading over the falloff.
        /// </summary>
        Ridge,

        /// <summary>
        /// Lowers a valley along it by the points' heights.
        /// </summary>
        Valley,

        /// <summary>
        /// Flattens a road to the points' heights (absolute), full over the width, blending into
        /// the terrain over the falloff (<see cref="HeightCurve.RoadMode"/>: cutting and filling, or one of them).
        /// </summary>
        Road,

        /// <summary>
        /// Raises the terrain on its left side (walking from the first point to the last, as seen
        /// in the top view) by the points' heights: a drop (<see cref="HeightCurve.CliffFace"/>
        /// ogrids wide) at the curve, the high ground full over the width and fading back over the
        /// (left) falloff.
        /// </summary>
        Cliff,

        /// <summary>
        /// A road that only cuts (its layer combines by minimum): a river's bed at the points'
        /// heights (absolute, like a road's), its banks easing out unless another shape is set.
        /// </summary>
        River,
    }

    /// <summary>
    /// The shape of a curve's fade over its falloff and of a cliff's face (user story T-4).
    /// </summary>
    public enum CurveFalloffShape
    {
        /// <summary>
        /// A smoothstep: gentle at both ends.
        /// </summary>
        Smooth,

        /// <summary>
        /// A straight slope.
        /// </summary>
        Linear,

        /// <summary>
        /// Gentle at the outer edge, steep where the full part begins.
        /// </summary>
        EaseIn,

        /// <summary>
        /// Steep at the outer edge, gentle where the full part begins: a river's banks.
        /// </summary>
        EaseOut,

        /// <summary>
        /// Steps (<see cref="HeightCurve.TerraceSteps"/>), each with a short steep face: stepped
        /// banks, a terraced cliff.
        /// </summary>
        Terraced,
    }

    /// <summary>
    /// What a road does to the terrain it crosses (user story T-4): its layer's way of combining.
    /// </summary>
    public enum CurveRoadMode
    {
        /// <summary>
        /// Cuts through hills and fills hollows: the layer replaces.
        /// </summary>
        Both,

        /// <summary>
        /// Only cuts: the layer combines by minimum, hollows stay.
        /// </summary>
        CutOnly,

        /// <summary>
        /// Only fills: the layer combines by maximum, hills stay.
        /// </summary>
        FillOnly,
    }

    /// <summary>
    /// A range of values, <paramref name="Min"/> to <paramref name="Max"/>: a curve's variation
    /// along it picks one in the range at each place.
    /// </summary>
    public readonly record struct CurveRange(float Min, float Max)
    {
        public static CurveRange None { get; } = new CurveRange(0, 0);

        public bool IsNone => Min == 0 && Max == 0;

        /// <summary>
        /// The value a fraction <paramref name="t"/> (0 to 1) of the way from the least to the most.
        /// </summary>
        public float At(float t) => Min + (Max - Min) * t;
    }

    /// <summary>
    /// A point a curve passes through, in ogrids, with its height: the road's or river bed's height
    /// there, or how much a ridge, valley or cliff rises or sinks there, in game units. The other
    /// values override the curve's at this node (null: the curve's); between nodes they blend
    /// smoothly.
    /// </summary>
    /// <param name="Width">The width at full height here, in ogrids.</param>
    /// <param name="FalloffLeft">How far it fades out on the left (walking from the first point to the last, in the top view).</param>
    /// <param name="FalloffRight">How far it fades out on the right.</param>
    /// <param name="Strength">How much of the profile there is here, 0 to 1 (null is 1): a ridge that dies away, a road that merges into the terrain.</param>
    /// <param name="Sharp">Whether the path makes a corner here rather than a smooth bend.</param>
    public sealed record CurvePoint(float X, float Z, float Height, float? Width = null, float? FalloffLeft = null, float? FalloffRight = null,
        float? Strength = null, bool Sharp = false)
    {
        /// <summary>
        /// Whether the node overrides none of the curve's values and is not sharp.
        /// </summary>
        public bool IsPlain => Width is null && FalloffLeft is null && FalloffRight is null && Strength is null && !Sharp;
    }

    /// <summary>
    /// A curve through its points (a Catmull-Rom spline, heights by a monotone cubic) that shapes the
    /// terrain along it (<see cref="Editing.HeightCurves"/>).
    /// </summary>
    public sealed record HeightCurve
    {
        public ImmutableArray<CurvePoint> Points { get; init; } = [];

        public CurveProfile Profile { get; init; } = CurveProfile.Ridge;

        /// <summary>
        /// The width of the part at full height, in ogrids.
        /// </summary>
        public float Width { get; init; } = 8;

        /// <summary>
        /// How far beyond the width it fades out on the left, and on the right too unless
        /// <see cref="FalloffRight"/> is set, in ogrids.
        /// </summary>
        public float Falloff { get; init; } = 12;

        /// <summary>
        /// Whether the last point joins the first.
        /// </summary>
        public bool Closed { get; init; }

        /// <summary>
        /// The symmetry the curve is drawn by (user story F-12): its layer shapes the terrain along
        /// the curve's images in the other parts of the map too, so one curve makes a symmetric
        /// map's every river or ridge. Null for the curve alone.
        /// </summary>
        public SymmetrySetting? Symmetry { get; init; }

        /// <summary>
        /// How far it fades out on the right, in ogrids; null: <see cref="Falloff"/>.
        /// </summary>
        public float? FalloffRight { get; init; }

        /// <summary>
        /// The shape of the fade and of a cliff's face; null: the profile's (<see cref="CurveFalloffShape.EaseOut"/>
        /// for a river, <see cref="CurveFalloffShape.Smooth"/> otherwise).
        /// </summary>
        public CurveFalloffShape? FalloffShape { get; init; }

        /// <summary>
        /// The steps of a <see cref="CurveFalloffShape.Terraced"/> shape, 1 or more.
        /// </summary>
        public int TerraceSteps { get; init; } = 3;

        /// <summary>
        /// A cliff's drop: how wide its face is, in ogrids (1: about vertical).
        /// </summary>
        public float CliffFace { get; init; } = 1;

        /// <summary>
        /// A road's way with the terrain: cut and fill, cut only or fill only; sets its layer's way of combining.
        /// </summary>
        public CurveRoadMode RoadMode { get; init; } = CurveRoadMode.Both;

        /// <summary>
        /// A road's or river's tilt across it: its left edge this much higher than its right, in game units.
        /// </summary>
        public float Camber { get; init; }

        /// <summary>
        /// Edge noise: how far noise moves the distance to the curve, in ogrids (0: none). Ragged
        /// banks, broken cliff lines, a mesa's rim.
        /// </summary>
        public float NoiseAmplitude { get; init; }

        /// <summary>
        /// The size of the edge noise's features, in ogrids.
        /// </summary>
        public float NoiseScale { get; init; } = 16;

        /// <summary>
        /// The seed of the edge noise and of the variation along the curve.
        /// </summary>
        public int NoiseSeed { get; init; }

        /// <summary>
        /// Variation along the curve: an offset in the range added to the height at each place, in
        /// game units (-1 to 0 on a ridge of 5: between 4 and 5).
        /// </summary>
        public CurveRange HeightVariation { get; init; }

        /// <summary>
        /// Variation along the curve: an offset in the range added to the width at each place, in ogrids.
        /// </summary>
        public CurveRange WidthVariation { get; init; }

        /// <summary>
        /// How far along the curve the variation changes, in ogrids.
        /// </summary>
        public float VariationScale { get; init; } = 32;

        /// <summary>
        /// For a closed curve: shape only along its line, leaving its inside as it is; otherwise a
        /// closed curve fills its inside (a plateau, a basin, a pad, a lake bed, a mesa).
        /// </summary>
        public bool LineOnly { get; init; }

        /// <summary>
        /// Whether a closed curve fills its inside.
        /// </summary>
        public bool Fills => Closed && !LineOnly && Points.Length >= 3;

        public bool Equals(HeightCurve? other) =>
            other is not null && Profile == other.Profile && Width == other.Width && Falloff == other.Falloff && Closed == other.Closed
            && Equals(Symmetry, other.Symmetry) && FalloffRight == other.FalloffRight && FalloffShape == other.FalloffShape
            && TerraceSteps == other.TerraceSteps && CliffFace == other.CliffFace && RoadMode == other.RoadMode && Camber == other.Camber
            && NoiseAmplitude == other.NoiseAmplitude && NoiseScale == other.NoiseScale && NoiseSeed == other.NoiseSeed
            && HeightVariation == other.HeightVariation && WidthVariation == other.WidthVariation && VariationScale == other.VariationScale
            && LineOnly == other.LineOnly && Points.AsSpan().SequenceEqual(other.Points.AsSpan());

        public override int GetHashCode() => HashCode.Combine(Profile, Width, Falloff, Closed, Points.Length, NoiseAmplitude, LineOnly);
    }
}
