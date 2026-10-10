using System.Collections.Immutable;
using System.Globalization;
using FAForever.MapEditor.Project.Editing;

namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// The grid a stack of layers makes (<see cref="LayerStack"/>): the heightmap, or a grid of
    /// coverage from 0 to 255 (a stratum's mask, a mask for tools, one of the water's masks).
    /// </summary>
    /// <param name="Key">The name of the stack's files: <c>heightmap</c>, <c>stratum-2</c>,
    /// <c>mask-highlands</c>, <c>water-foam</c> (<c>stacks/&lt;key&gt;.json</c>).</param>
    /// <param name="Grid">The coverage grid the stack makes; null for the heightmap.</param>
    public sealed record LayerTarget(string Key, PaintTarget? Grid)
    {
        /// <summary>
        /// The heightmap: values in game units at the corners of the ogrids.
        /// </summary>
        public static LayerTarget Heightmap { get; } = new LayerTarget("heightmap", null);

        /// <summary>
        /// The mask of stratum <paramref name="layer"/> (1 to 8).
        /// </summary>
        public static LayerTarget Stratum(int layer) => new LayerTarget("stratum-" + layer.ToString(CultureInfo.InvariantCulture), PaintTarget.Stratum(layer));

        /// <summary>
        /// The mask for tools of that name (<see cref="MapDocument.Masks"/>).
        /// </summary>
        public static LayerTarget Mask(string name) => new LayerTarget("mask-" + name, PaintTarget.Mask(name));

        /// <summary>
        /// One of the water's masks.
        /// </summary>
        public static LayerTarget Water(WaterMask mask) => new LayerTarget(mask switch
        {
            WaterMask.Foam => "water-foam",
            WaterMask.Flatness => "water-flatness",
            _ => "water-depth-bias",
        }, PaintTarget.Water(mask));

        /// <summary>
        /// Whether the stack makes heights (float values, game units) rather than coverage (bytes).
        /// </summary>
        public bool IsHeights => Grid is null;

        /// <summary>
        /// The target a key names (<see cref="Key"/>: <c>stratum-2</c>, <c>mask-highlands</c>,
        /// <c>water-foam</c>, <c>heightmap</c>), or null for a key of no target.
        /// </summary>
        public static LayerTarget? FromKey(string key)
        {
            if (key == Heightmap.Key)
            {
                return Heightmap;
            }
            if (key.StartsWith("stratum-", StringComparison.Ordinal)
                && int.TryParse(key["stratum-".Length..], NumberStyles.None, CultureInfo.InvariantCulture, out int layer) && layer >= 1)
            {
                return Stratum(layer);
            }
            if (key.StartsWith("mask-", StringComparison.Ordinal) && key.Length > "mask-".Length)
            {
                return Mask(key["mask-".Length..]);
            }
            return key switch
            {
                "water-foam" => Water(WaterMask.Foam),
                "water-flatness" => Water(WaterMask.Flatness),
                "water-depth-bias" => Water(WaterMask.DepthBias),
                _ => null,
            };
        }

        public override string ToString() => Key;
    }

    /// <summary>
    /// How a layer works on what the layers below it made (<see cref="Editing.LayerStacks"/>). With
    /// h what the layers below made, v the layer's value and w its weight (strength × clip ×
    /// coverage) at a place.
    /// </summary>
    /// <remarks>
    /// The names stay as they are: they are written in the project's JSON.
    /// </remarks>
    public enum LayerCombine
    {
        /// <summary>
        /// h + w × v: its values are differences, 0 changes nothing.
        /// </summary>
        Add,

        /// <summary>
        /// The higher of its value and h, w of the way.
        /// </summary>
        Maximum,

        /// <summary>
        /// The lower of its value and h, w of the way.
        /// </summary>
        Minimum,

        /// <summary>
        /// Its value in place of h, w of the way.
        /// </summary>
        Replace,

        /// <summary>
        /// h − w × v.
        /// </summary>
        Subtract,

        /// <summary>
        /// h × v, w of the way: the value is a factor (coverage from 0 to 1; 1 changes nothing).
        /// </summary>
        Multiply,
    }

    /// <summary>
    /// A stack of layers that makes one grid of the map (<see cref="LayerTarget"/>): the layers,
    /// lowest first, each working on what the ones below made (<see cref="Editing.LayerStacks"/>).
    /// An empty stack makes nothing: its target is simply the grid as it is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The heightmap's stack is <see cref="MapDocument.HeightStack"/>; stratum masks, masks for tools
    /// and the water's masks get theirs in the same shape. A layer's name is its identity in the
    /// stack (unique without regard to case, and the name of its files), so edits, the editor's
    /// choice and the project name layers by it.
    /// </para>
    /// <para>
    /// Two stacks of one target (the heightmap's draft and final) can share a layer: the same name
    /// in both, with the same source and grids (the same objects), at a place of its own in each and
    /// with its own <see cref="Layer.Visible"/>. An edit of a shared layer's content changes it in
    /// both.
    /// </para>
    /// </remarks>
    public sealed record LayerStack
    {
        public required LayerTarget Target { get; init; }

        /// <summary>
        /// The layers, lowest first.
        /// </summary>
        public ImmutableArray<Layer> Layers { get; init; } = [];

        public bool IsEmpty => Layers.IsEmpty;

        public int Count => Layers.Length;

        /// <summary>
        /// The empty stack of a target.
        /// </summary>
        public static LayerStack Empty(LayerTarget target) => new LayerStack { Target = target };

        /// <summary>
        /// The layer of that name (exactly, else without regard to case), or null.
        /// </summary>
        public Layer? Find(string? name) =>
            name is null ? null
            : Layers.FirstOrDefault(layer => layer.Name == name) ?? Layers.FirstOrDefault(layer => layer.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// The place of the layer of that name, or -1.
        /// </summary>
        public int IndexOf(string? name) => Find(name) is Layer layer ? Layers.IndexOf(layer) : -1;

        /// <summary>
        /// The stack with the layer at <paramref name="index"/> replaced; itself when it is the same.
        /// </summary>
        public LayerStack With(int index, Layer layer) =>
            ReferenceEquals(Layers[index], layer) ? this : this with { Layers = Layers.SetItem(index, layer) };

        public bool Equals(LayerStack? other) =>
            other is not null && Target == other.Target && Layers.AsSpan().SequenceEqual(other.Layers.AsSpan());

        public override int GetHashCode() => HashCode.Combine(Target, Layers.Length);
    }

    /// <summary>
    /// One layer of a <see cref="LayerStack"/>: where its values come from (<see cref="Source"/>),
    /// how they combine with the layers below, how strongly and where (a clip mask), whether it
    /// counts and whether it can be changed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A layer holds the grids the compositor reads, whatever its source: what a brush painted, what
    /// an image file held when it was read, what a curve or rules made when they last changed. A
    /// source whose values follow from something else (a curve, rules, decals) makes them again in
    /// the edit that changes it, reusing the tiles that come out the same, so the compositor only
    /// ever reads grids; a <see cref="MaskRefSource"/> alone is read from its mask while compositing.
    /// </para>
    /// <para>
    /// In a stack of heights <see cref="Values"/> holds game units at the heightmap's corners
    /// ((width + 1) by (height + 1), so its tiles are the heightmap's), and <see cref="Coverage"/>,
    /// when present, how much of the layer there is per corner (a road's soft edge, the part of a
    /// replacing sculpt layer that was sculpted). In a stack of coverage <see cref="Values"/> is null
    /// and <see cref="Coverage"/> is the layer's values, 0 to 255, at the target grid's size.
    /// </para>
    /// </remarks>
    public sealed record Layer
    {
        /// <summary>
        /// The layer's name: unique in its stack without regard to case, since it names its files.
        /// </summary>
        public required string Name { get; init; }

        public required LayerSource Source { get; init; }

        public LayerCombine Combine { get; init; } = LayerCombine.Add;

        /// <summary>
        /// How strongly it works, from 0 (not at all) to 1.
        /// </summary>
        public float Strength { get; init; } = 1;

        /// <summary>
        /// Where it works: a mask key (<see cref="Editing.LayerStacks.MaskKey"/>: <c>mask:&lt;name&gt;</c>
        /// for a mask for tools, <c>stratum:&lt;n&gt;</c> for a stratum's mask); null for everywhere.
        /// A key whose mask is missing counts as none.
        /// </summary>
        public string? Clip { get; init; }

        /// <summary>
        /// Whether it counts: a hidden layer is left out.
        /// </summary>
        public bool Visible { get; init; } = true;

        /// <summary>
        /// Whether brushes and other edits of its values leave it alone.
        /// </summary>
        public bool Locked { get; init; }

        /// <summary>
        /// Whether it is its stack's base: the lowest layer, at full strength, unclipped, replacing.
        /// The heightmap's stack always has one (the heights as they were when the stack began).
        /// </summary>
        public bool IsBase { get; init; }

        /// <summary>
        /// In a stack of heights the values in game units, (width + 1) by (height + 1); null in a
        /// stack of coverage.
        /// </summary>
        public TiledBuffer<float>? Values { get; init; }

        /// <summary>
        /// In a stack of heights how much of the layer there is per corner, 0 to 255, or null for
        /// all of it everywhere; in a stack of coverage the layer's values.
        /// </summary>
        public TiledBuffer<byte>? Coverage { get; init; }

        /// <summary>
        /// In a stack of coverage, how much of the layer there is per pixel, 0 to 255 (the target
        /// grid's size), or null for all of it everywhere; null in a stack of heights, whose
        /// <see cref="Coverage"/> does this. A painted layer added to a stack of coverage starts
        /// with an alpha of 0 everywhere ("untouched"), and a brush raises it to 255 where it paints,
        /// so a replacing, minimum or multiplying painted layer works only where it was painted.
        /// </summary>
        public TiledBuffer<byte>? Alpha { get; init; }

        /// <summary>
        /// For a curve layer, its curve; else null.
        /// </summary>
        public HeightCurve? Curve => (Source as CurveSource)?.Curve;

        /// <summary>
        /// For a layer read from a file, the file; else null.
        /// </summary>
        public string? Path => (Source as ImageSource)?.Path;
    }

    /// <summary>
    /// Where a layer's values come from. Each kind of source is a record of its own; a new kind (the
    /// decal masks of wave 9) adds a record here, its name in <see cref="LayerSources"/>, how its
    /// grid is made in its own edit, and its fields in <c>LayerDto</c>.
    /// </summary>
    public abstract record LayerSource
    {
        /// <summary>
        /// Painted by a brush (sculpt, strata, mask or water brush) into the layer's own grid.
        /// </summary>
        public static PaintedSource Painted { get; } = new PaintedSource();
    }

    /// <summary>
    /// A grid of its own, written by a brush: the layer a brush writes into while it is chosen.
    /// </summary>
    public sealed record PaintedSource : LayerSource;

    /// <summary>
    /// An image file in or outside the project (World Machine, Gaea, Krita), read again when it
    /// changes.
    /// </summary>
    /// <param name="Path">The file, relative to the project folder when it lies in it
    /// (<c>procedural/temp/gaea.png</c>), else absolute.</param>
    public sealed record ImageSource(string Path) : LayerSource;

    /// <summary>
    /// Another mask, read while compositing: a mask for tools or a stratum's mask by key
    /// (<see cref="Editing.LayerStacks.MaskKey"/>), with a threshold below which it counts as
    /// nothing and an invert. In a stack of heights its value is the mask times
    /// <see cref="Height"/> game units.
    /// </summary>
    public sealed record MaskRefSource(string Key, float Threshold = 0, bool Invert = false, float Height = 0) : LayerSource;

    /// <summary>
    /// Rules on the map (slope, height, distance to water, noise, other masks; the rules of masks
    /// for tools of today). The layer's grid holds what they made when last generated; stacks of
    /// coverage only.
    /// </summary>
    public sealed record RulesSource(MaskRuleSet Rules) : LayerSource;

    /// <summary>
    /// A curve that shapes the terrain along it (user story T-4); the layer's values are rasterized
    /// from it whenever it changes. Stacks of heights only.
    /// </summary>
    public sealed record CurveSource(HeightCurve Curve) : LayerSource;

    /// <summary>
    /// The masks of the map's decal sets (<see cref="DecalSet.Masks"/>): the greyscale mask of a
    /// name of every placed set, or of the sets named, drawn at each decal's place, turn and size
    /// (where decals overlap, the higher value). The layer's grid holds what they made when a decal
    /// last moved (<see cref="Editing.DecalMaskLayers"/>). In a stack of heights its value is the
    /// mask times <see cref="Height"/> game units.
    /// </summary>
    /// <param name="Mask">The mask's name: <c>main</c> for the plain <c>_mask</c>, else the name after <c>_mask_</c>.</param>
    /// <param name="Sets">The sets whose masks it draws, by name (<see cref="DecalSet.Name"/>); empty for every set.</param>
    public sealed record DecalMasksSource(string Mask, ImmutableArray<string> Sets, float Height = 0) : LayerSource
    {
        /// <summary>
        /// The main mask of every set.
        /// </summary>
        public static DecalMasksSource Every { get; } = new DecalMasksSource(Editing.DecalSets.MainMask, []);

        public ImmutableArray<string> Sets { get; init; } = Sets.IsDefault ? [] : Sets;

        public bool Equals(DecalMasksSource? other) =>
            other is not null && Mask == other.Mask && Height == other.Height && Sets.AsSpan().SequenceEqual(other.Sets.AsSpan());

        public override int GetHashCode() => HashCode.Combine(Mask, Height, Sets.Length);
    }

    /// <summary>
    /// The names of the kinds of source, as the project writes them and the editor shows them.
    /// </summary>
    public static class LayerSources
    {
        public const string PaintedName = "painted";

        public const string ImageName = "image";

        public const string MaskName = "mask";

        public const string RulesName = "rules";

        public const string CurveName = "curve";

        public const string DecalMasksName = "decals";

        /// <summary>
        /// The name of a source's kind: <c>painted</c>, <c>image</c>, <c>mask</c>, <c>rules</c>, <c>curve</c>, <c>decals</c>.
        /// </summary>
        public static string NameOf(LayerSource source) => source switch
        {
            PaintedSource => PaintedName,
            ImageSource => ImageName,
            MaskRefSource => MaskName,
            RulesSource => RulesName,
            CurveSource => CurveName,
            DecalMasksSource => DecalMasksName,
            _ => source.GetType().Name,
        };

        /// <summary>
        /// Whether a brush can write into a layer of this source.
        /// </summary>
        public static bool CanPaint(LayerSource source) => source is PaintedSource;

        /// <summary>
        /// Whether a source fits a stack of heights (<paramref name="heights"/>) or of coverage.
        /// </summary>
        public static bool Fits(LayerSource source, bool heights) => source switch
        {
            CurveSource => heights,
            RulesSource => !heights,
            _ => true,
        };
    }
}
