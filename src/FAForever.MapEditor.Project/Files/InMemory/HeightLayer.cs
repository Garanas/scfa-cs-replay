using System.Collections.Immutable;

namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// What a height layer holds and where its values come from (user story T-5).
    /// </summary>
    public enum HeightLayerKind
    {
        /// <summary>
        /// The map's heights as they were when the stack began: always the lowest layer, one per map.
        /// </summary>
        Base,

        /// <summary>
        /// Heights the sculpt tool writes into.
        /// </summary>
        Sculpt,

        /// <summary>
        /// The heightmap of another tool (World Machine, Gaea), read from its file again when the
        /// file changes.
        /// </summary>
        Imported,

        /// <summary>
        /// A curve that shapes the terrain along it (user story T-4); its values follow from the
        /// curve.
        /// </summary>
        Curve,
    }

    /// <summary>
    /// How a height layer combines with the heights of the layers below it.
    /// </summary>
    public enum HeightCombine
    {
        /// <summary>
        /// Adds its values: they are differences, 0 changes nothing.
        /// </summary>
        Add,

        /// <summary>
        /// The higher of its value and the height below.
        /// </summary>
        Maximum,

        /// <summary>
        /// The lower of its value and the height below.
        /// </summary>
        Minimum,

        /// <summary>
        /// Its value in place of the height below.
        /// </summary>
        Replace,
    }

    /// <summary>
    /// One layer of the stack of heights that add up to the heightmap (user story T-5,
    /// <see cref="MapDocument.HeightLayers"/>): its values, how they combine with the layers below,
    /// how strongly, and where (a mask).
    /// </summary>
    /// <remarks>
    /// The values are in game units, one per corner of an ogrid like the heightmap's samples
    /// ((width + 1) by (height + 1)), so the tiles of a layer and of the heightmap are the same
    /// places. Per corner a layer works with the weight <see cref="Strength"/> × the mask × the
    /// coverage; <see cref="Editing.HeightLayers"/> adds the layers up.
    /// </remarks>
    public sealed record HeightLayer
    {
        /// <summary>
        /// The layer's name, unique in the map without regard to case, since it names its files
        /// (<c>layers/&lt;name&gt;.png</c>).
        /// </summary>
        public required string Name { get; init; }

        public required HeightLayerKind Kind { get; init; }

        public HeightCombine Combine { get; init; } = HeightCombine.Add;

        /// <summary>
        /// How strongly it works, from 0 (not at all) to 1.
        /// </summary>
        public float Strength { get; init; } = 1;

        /// <summary>
        /// Where it works: <c>mask:&lt;name&gt;</c> for a mask for tools (<see cref="MapDocument.Masks"/>)
        /// or <c>stratum:&lt;n&gt;</c> for the mask of terrain layer n; null for everywhere.
        /// </summary>
        public string? Mask { get; init; }

        /// <summary>
        /// Whether it counts: a hidden layer is left out of the heightmap.
        /// </summary>
        public bool Visible { get; init; } = true;

        /// <summary>
        /// Whether the sculpt tool and other edits of its values leave it alone.
        /// </summary>
        public bool Locked { get; init; }

        /// <summary>
        /// The values in game units, (width + 1) by (height + 1).
        /// </summary>
        public required TiledBuffer<float> Values { get; init; }

        /// <summary>
        /// How much of the layer there is per corner, 0 to 255; null for all of it everywhere. A
        /// curve's soft edge, or the part of a replacing layer that was sculpted.
        /// </summary>
        public TiledBuffer<byte>? Coverage { get; init; }

        /// <summary>
        /// For an imported layer: the file it is read from, relative to the project folder when it
        /// lies in it (<c>procedural/temp/gaea.png</c>), else absolute.
        /// </summary>
        public string? Source { get; init; }

        /// <summary>
        /// For a curve layer: the curve its values are made from.
        /// </summary>
        public HeightCurve? Curve { get; init; }
    }

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
        /// the terrain over the falloff.
        /// </summary>
        Road,

        /// <summary>
        /// Raises the terrain on its left side (walking from the first point to the last, as seen
        /// in the top view) by the points' heights: a drop of about an ogrid at the curve, the high
        /// ground full over the width and fading back over the falloff.
        /// </summary>
        Cliff,
    }

    /// <summary>
    /// A point a curve passes through, in ogrids, with its height: the road's height there, or how
    /// much a ridge, valley or cliff rises or sinks there, in game units.
    /// </summary>
    public sealed record CurvePoint(float X, float Z, float Height);

    /// <summary>
    /// A curve through its points (a Catmull-Rom spline) that shapes the terrain along it.
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
        /// How far beyond the width it fades out on each side, in ogrids.
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

        public bool Equals(HeightCurve? other) =>
            other is not null && Profile == other.Profile && Width == other.Width && Falloff == other.Falloff && Closed == other.Closed
            && Equals(Symmetry, other.Symmetry) && Points.AsSpan().SequenceEqual(other.Points.AsSpan());

        public override int GetHashCode() => HashCode.Combine(Profile, Width, Falloff, Closed, Points.Length);
    }
}
