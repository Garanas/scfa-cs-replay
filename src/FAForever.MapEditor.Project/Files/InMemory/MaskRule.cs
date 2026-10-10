using System.Collections.Immutable;

namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// What a rule of a mask does (user story K-2). The first kinds each make a value from 0 to 1 at
    /// every pixel that combines with what the rules before made (<see cref="MaskRule.Combine"/>);
    /// the last ones change what the rules before made.
    /// </summary>
    public enum MaskRuleKind
    {
        /// <summary>The terrain's slope in degrees, between <see cref="MaskRule.Min"/> and <see cref="MaskRule.Max"/>.</summary>
        Slope,

        /// <summary>The terrain's height in game units, between <see cref="MaskRule.Min"/> and <see cref="MaskRule.Max"/>.</summary>
        Height,

        /// <summary>
        /// The distance to the water in ogrids, between <see cref="MaskRule.Min"/> and
        /// <see cref="MaskRule.Max"/>: 0 under the water's surface. A map without water is far from it everywhere.
        /// </summary>
        WaterDistance,

        /// <summary>Another mask for tools, or a stratum's mask (<see cref="MaskRule.Source"/>).</summary>
        Layer,

        /// <summary>Smooth noise from 0 to 1, features about <see cref="MaskRule.Scale"/> ogrids across, fixed by <see cref="MaskRule.Seed"/>.</summary>
        Noise,

        /// <summary>Blurs what the rules before made over <see cref="MaskRule.Radius"/> ogrids.</summary>
        Blur,

        /// <summary>Widens what the rules before made by <see cref="MaskRule.Radius"/> ogrids.</summary>
        Grow,

        /// <summary>Narrows what the rules before made by <see cref="MaskRule.Radius"/> ogrids.</summary>
        Shrink,

        /// <summary>
        /// Makes what the rules before made sharp: empty below <see cref="MaskRule.Min"/> (0 to 1),
        /// full from <see cref="MaskRule.Min"/> plus <see cref="MaskRule.Soft"/> on, a ramp between.
        /// </summary>
        Threshold,

        /// <summary>Turns what the rules before made around: full becomes empty.</summary>
        Invert,
    }

    /// <summary>
    /// How a rule's value meets what the rules before it made.
    /// </summary>
    public enum MaskCombine
    {
        /// <summary>Both: the product (where this rule and the ones before allow).</summary>
        Multiply,

        /// <summary>The smaller of the two.</summary>
        Min,

        /// <summary>The larger of the two (where this rule or the ones before allow).</summary>
        Max,

        /// <summary>The sum, at most full.</summary>
        Add,

        /// <summary>The difference, at least empty (the ones before, without this rule's).</summary>
        Subtract,

        /// <summary>This rule's value alone.</summary>
        Replace,
    }

    /// <summary>
    /// One rule of a mask made from rules (user story K-2). A rule has every parameter; its
    /// <see cref="Kind"/> says which it reads.
    /// </summary>
    public sealed record MaskRule
    {
        public MaskRuleKind Kind { get; init; }

        /// <summary>
        /// How its value meets what the rules before made; the first rule's value is the start.
        /// Not read by <see cref="MaskRuleKind.Blur"/>, <see cref="MaskRuleKind.Grow"/>,
        /// <see cref="MaskRuleKind.Shrink"/> and <see cref="MaskRuleKind.Invert"/>.
        /// </summary>
        public MaskCombine Combine { get; init; } = MaskCombine.Multiply;

        /// <summary>
        /// Whether its own value is turned around before it combines: a slope rule of 0 to 30
        /// degrees inverted holds where it is steeper.
        /// </summary>
        public bool Invert { get; init; }

        /// <summary>
        /// The lower end of the range of a slope (degrees), height (game units) or distance to the
        /// water (ogrids); for a layer, the value from which on it counts as full (0 for as it is);
        /// for a threshold, the value below which the mask is empty.
        /// </summary>
        public float Min { get; init; }

        /// <summary>
        /// The upper end of the range of a slope, height or distance to the water.
        /// </summary>
        public float Max { get; init; } = 30;

        /// <summary>
        /// How far beyond the ends of the range the value fades to nothing, in the range's unit: 0
        /// for a hard edge. For a threshold, how far above <see cref="Min"/> the mask becomes full.
        /// </summary>
        public float Soft { get; init; }

        /// <summary>
        /// The layer of a <see cref="MaskRuleKind.Layer"/> rule: a mask for tools by name, or
        /// <c>stratum:N</c> for the mask of terrain layer N (1 to 8).
        /// </summary>
        public string Source { get; init; } = "";

        /// <summary>
        /// The size of a noise's features in ogrids.
        /// </summary>
        public float Scale { get; init; } = 32;

        /// <summary>
        /// What fixes a noise: the same seed gives the same noise.
        /// </summary>
        public int Seed { get; init; }

        /// <summary>
        /// The radius of a blur, grow or shrink in ogrids.
        /// </summary>
        public float Radius { get; init; } = 2;

        /// <summary>
        /// Whether the kind makes a value of its own (and combines it), rather than changing what the
        /// rules before made.
        /// </summary>
        public bool MakesValue => Kind is MaskRuleKind.Slope or MaskRuleKind.Height or MaskRuleKind.WaterDistance or MaskRuleKind.Layer or MaskRuleKind.Noise;

        /// <summary>
        /// A rule of a kind with the parameters it starts with in the editor.
        /// </summary>
        public static MaskRule Of(MaskRuleKind kind) => kind switch
        {
            MaskRuleKind.Slope => new MaskRule { Kind = kind, Min = 0, Max = 20, Soft = 5 },
            MaskRuleKind.Height => new MaskRule { Kind = kind, Min = 0, Max = 100, Soft = 5 },
            MaskRuleKind.WaterDistance => new MaskRule { Kind = kind, Min = 4, Max = 10000, Soft = 4 },
            MaskRuleKind.Layer => new MaskRule { Kind = kind, Min = 0, Max = 1 },
            MaskRuleKind.Noise => new MaskRule { Kind = kind, Scale = 32, Seed = 1 },
            MaskRuleKind.Blur => new MaskRule { Kind = kind, Radius = 2 },
            MaskRuleKind.Grow or MaskRuleKind.Shrink => new MaskRule { Kind = kind, Radius = 2 },
            MaskRuleKind.Threshold => new MaskRule { Kind = kind, Min = 0.5f, Soft = 0.1f },
            _ => new MaskRule { Kind = kind },
        };
    }

    /// <summary>
    /// The rules a mask for tools is made from (user story K-2), in order: the mask is a result the
    /// editor generates from them when asked (<see cref="Generation.MaskRulesGenerator"/>).
    /// </summary>
    public sealed record MaskRuleSet
    {
        public ImmutableArray<MaskRule> Rules { get; init; } = [];

        /// <summary>
        /// The masks for tools the rules read (by name; strata are left out).
        /// </summary>
        public IEnumerable<string> Sources => Rules.Where(rule => rule.Kind == MaskRuleKind.Layer && !MaskRuleSources.IsStratum(rule.Source, out _) && rule.Source.Length > 0).Select(rule => rule.Source);
    }

    /// <summary>
    /// How a rule names the layer it reads: a mask for tools by its name, a stratum as <c>stratum:N</c>.
    /// </summary>
    public static class MaskRuleSources
    {
        public const string StratumPrefix = "stratum:";

        /// <summary>
        /// How a rule names the mask of terrain layer <paramref name="layer"/> (1 to 8).
        /// </summary>
        public static string Stratum(int layer) => $"{StratumPrefix}{layer}";

        /// <summary>
        /// Whether a source names a stratum, and which.
        /// </summary>
        public static bool IsStratum(string source, out int layer)
        {
            layer = 0;
            return source.StartsWith(StratumPrefix, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(source.AsSpan(StratumPrefix.Length), out layer);
        }

        /// <summary>
        /// The grid a source names in a version of the document; null when it has no such layer.
        /// </summary>
        public static TiledBuffer<byte>? Read(MapDocument document, string source)
        {
            if (IsStratum(source, out int layer))
            {
                ImmutableArray<TiledBuffer<byte>> layers = document.Terrain.StratumMasks.Layers;
                return layer >= 1 && layer <= layers.Length ? layers[layer - 1] : null;
            }
            return document.Masks.GetValueOrDefault(source);
        }
    }
}
