using System.Collections.Immutable;
using System.Numerics;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Generation
{
    /// <summary>
    /// Places the props of the rules (user story P-4) from the layers: per rule, places at random
    /// over the map (as many as its density asks), each kept where the layers allow, the slope and
    /// the water let it, the clustering keeps it and no prop of the rules lies closer than the
    /// rule's spacing; the rules place in order, so a later rule fills around an earlier one's props.
    /// Each place is drawn from the rule's seed and its number, so a change of a layer in one corner
    /// leaves the props elsewhere where they were. On a map with a symmetry (user story F-12) a rule
    /// that mirrors (<see cref="PropRule.Mirror"/>, the default) places each prop with its image in
    /// every other part, as the props brush does: a prop goes only with all its images, each inside
    /// the map, allowed by the layers, the slope and the water where it lies and as far from every
    /// other prop as the spacing asks; a place whose image is itself (on an axis) has none.
    /// </summary>
    public static class PropRules
    {
        /// <summary>
        /// The most places one rule tries, so a mistyped density cannot hang the editor.
        /// </summary>
        public const int MaxTries = 4_000_000;

        /// <summary>
        /// The props of every rule, made from <paramref name="document"/>: each carries its rule's name
        /// and an id of 0 (<see cref="PropRulesGenerator.Apply"/> numbers them).
        /// </summary>
        /// <exception cref="ArgumentException">A rule reads a layer the map has not.</exception>
        public static ImmutableArray<Prop> Generate(MapDocument document)
        {
            PropGrid grid = new PropGrid([]);
            ImmutableArray<Prop>.Builder made = ImmutableArray.CreateBuilder<Prop>();
            foreach (PropRule rule in document.PropRules)
            {
                made.AddRange(Generate(document, rule, grid));
            }
            return made.ToImmutable();
        }

        /// <summary>
        /// The other parts of the map a rule's props are mirrored into (the identity left out): none
        /// without a symmetry or for a rule that does not mirror.
        /// </summary>
        public static IReadOnlyList<MapSymmetryTransform> ImagesOf(MapDocument document, PropRule rule) =>
            rule.Mirror && document.Symmetry is SymmetrySetting symmetry
                ? [.. MapSymmetry.Transforms(symmetry.Kind, symmetry.Count, document.Width, document.Height).Skip(1)]
                : [];

        /// <summary>
        /// The props of one rule, away from the props <paramref name="grid"/> holds (which it adds its own to).
        /// </summary>
        /// <exception cref="ArgumentException">The rule reads a layer the map has not.</exception>
        public static List<Prop> Generate(MapDocument document, PropRule rule, PropGrid grid)
        {
            List<Prop> placed = [];
            PaletteEntry[] palette = [.. rule.Palette.Where(entry => entry.Weight > 0 && !string.IsNullOrWhiteSpace(entry.Blueprint))];
            if (palette.Length == 0 || rule.Density <= 0)
            {
                return placed;
            }
            double totalWeight = palette.Sum(entry => entry.Weight);
            (TiledBuffer<byte> Mask, float Threshold, bool Forbid)[] layers = [.. rule.Conditions.Select(condition => (
                MaskRuleSources.Read(document, condition.Layer)
                    ?? throw new ArgumentException(string.IsNullOrEmpty(condition.Layer)
                        ? $"A condition of the rule '{rule.Name}' names no layer"
                        : $"The rule '{rule.Name}' reads '{condition.Layer}', which the map has not"),
                Math.Clamp(condition.Threshold, 0, 1),
                condition.Forbid))];

            Heightmap heights = document.Terrain.Heightmap;
            int width = document.Width;
            int height = document.Height;
            float? water = document.Water.HasWater && rule.AboveWater ? document.Water.Elevation : null;
            bool slopes = rule.MinSlope > 0 || rule.MaxSlope < 90;
            IReadOnlyList<MapSymmetryTransform> images = ImagesOf(document, rule);
            // each place brings its images: as many places as one part of the map asks
            long wanted = (long)Math.Round(rule.Density * (double)width * height / 100 / (images.Count + 1));
            int tries = (int)Math.Min(MaxTries, wanted);
            List<Vector2> imagePlaces = new List<Vector2>(images.Count);
            List<MapSymmetryTransform> imageTransforms = new List<MapSymmetryTransform>(images.Count);

            // whether the layers, the water and the slope let a prop stand at a place; the chance the allowing layers give
            bool Allows(float x, float z, out double chance)
            {
                chance = 1;
                foreach ((TiledBuffer<byte> mask, float threshold, bool forbid) in layers)
                {
                    float value = ValueAt(mask, width, height, x, z);
                    bool covers = value > 0 && value >= threshold;
                    if (forbid ? covers : !covers)
                    {
                        return false;
                    }
                    if (!forbid && rule.Fade)
                    {
                        chance = Math.Min(chance, value);
                    }
                }
                if (water is float level && heights.HeightAt(x, z) < level)
                {
                    return false;
                }
                if (slopes)
                {
                    float slope = PaintLimits.SlopeAt(heights, x, z);
                    if (slope < rule.MinSlope || slope > rule.MaxSlope)
                    {
                        return false;
                    }
                }
                return true;
            }

            // the images of a place in the other parts, each where a prop may stand; false when one may not
            bool ImagesFit(float x, float z)
            {
                imagePlaces.Clear();
                imageTransforms.Clear();
                Vector2 place = new Vector2(x, z);
                foreach (MapSymmetryTransform transform in images)
                {
                    Vector2 image = transform.Apply(place);
                    float apart = Vector2.Distance(image, place);
                    if (apart < 1e-3f)
                    {
                        // on an axis: the prop is its own image
                        continue;
                    }
                    if (apart < rule.Spacing || image.X < 0 || image.Y < 0 || image.X >= width || image.Y >= height
                        || !Allows(image.X, image.Y, out _) || grid.AnyWithin(image.X, image.Y, rule.Spacing)
                        || imagePlaces.Any(other => Vector2.Distance(other, image) < Math.Max(rule.Spacing, 1e-3f)))
                    {
                        return false;
                    }
                    imagePlaces.Add(image);
                    imageTransforms.Add(transform);
                }
                return true;
            }

            ulong seed = Mix((ulong)(uint)rule.Seed ^ ((ulong)Hash(rule.Name) << 32));
            HashRandom random = new HashRandom();
            Clusters clusters = new Clusters(rule, seed);

            for (int i = 0; i < tries; i++)
            {
                random.Reset(seed ^ Mix((ulong)i + 1));
                float x = (float)(random.NextDouble() * width);
                float z = (float)(random.NextDouble() * height);
                if (!Allows(x, z, out double chance))
                {
                    continue;
                }
                chance *= clusters.At(x, z);
                if (chance <= 0 || (chance < 1 && random.NextDouble() >= chance))
                {
                    continue;
                }
                if (grid.AnyWithin(x, z, rule.Spacing))
                {
                    continue;
                }
                if (images.Count > 0 && !ImagesFit(x, z))
                {
                    continue;
                }
                Prop prop = rule.Placement.Make(Choose(palette, totalWeight, random.NextDouble()), x, z, heights, random) with { Rule = rule.Name };
                placed.Add(prop);
                grid.Add(prop);
                for (int image = 0; images.Count > 0 && image < imageTransforms.Count; image++)
                {
                    Prop mirrored = SymmetryEdits.ImageOf(prop, imageTransforms[image], heights);
                    placed.Add(mirrored);
                    grid.Add(mirrored);
                }
            }
            return placed;
        }

        /// <summary>
        /// A mask's value at a place from 0 to 1: the pixel under it.
        /// </summary>
        private static float ValueAt(TiledBuffer<byte> mask, int mapWidth, int mapHeight, float x, float z)
        {
            int px = Math.Clamp((int)(x * mask.Width / mapWidth), 0, mask.Width - 1);
            int pz = Math.Clamp((int)(z * mask.Height / mapHeight), 0, mask.Height - 1);
            return mask[px, pz] / 255f;
        }

        private static string Choose(PaletteEntry[] palette, double totalWeight, double pick)
        {
            pick *= totalWeight;
            foreach (PaletteEntry entry in palette)
            {
                pick -= entry.Weight;
                if (pick < 0)
                {
                    return entry.Blueprint;
                }
            }
            return palette[^1].Blueprint;
        }

        /// <summary>
        /// A hash of a text that stays the same between runs (<see cref="string.GetHashCode()"/> does not).
        /// </summary>
        private static uint Hash(string text)
        {
            uint hash = 2166136261;
            foreach (char c in text)
            {
                hash = unchecked((hash ^ c) * 16777619);
            }
            return hash;
        }

        /// <summary>
        /// SplitMix64's finaliser: spreads the bits of a number over all of them.
        /// </summary>
        internal static ulong Mix(ulong value)
        {
            value = unchecked((value ^ (value >> 30)) * 0xbf58476d1ce4e5b9UL);
            value = unchecked((value ^ (value >> 27)) * 0x94d049bb133111ebUL);
            return value ^ (value >> 31);
        }

        /// <summary>
        /// Where a rule's props gather: 1 everywhere without clustering; otherwise from 0 outside the
        /// patches or groups to 1 inside them.
        /// </summary>
        private sealed class Clusters(PropRule rule, ulong seed)
        {
            private readonly float _size = Math.Max(1, rule.ClusterSize);
            private readonly float _cover = Math.Clamp(rule.ClusterCover, 0.01f, 1);
            private readonly int _noiseSeed = (int)(seed & 0x7fffffff);

            public float At(float x, float z)
            {
                switch (rule.Clustering)
                {
                    case PropClustering.Noise:
                    {
                        // smooth noise spread over 0 to 1; the patches are where it is above 1 - cover
                        float noise = Math.Clamp(0.5f + 0.9f * HeightNoise.At(x / _size, z / _size, _noiseSeed), 0, 1);
                        return Smooth((noise - (1 - _cover)) / 0.08f + 0.5f);
                    }
                    case PropClustering.Clusters:
                    {
                        // a group of the cluster size around one centre per cell, the cells as large as the cover asks
                        float cell = _size * MathF.Sqrt(MathF.PI / _cover);
                        int cx = (int)MathF.Floor(x / cell);
                        int cz = (int)MathF.Floor(z / cell);
                        float best = 0;
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                ulong hash = Mix(seed ^ Mix(unchecked((ulong)(uint)(cx + dx) * 0x9e3779b97f4a7c15UL ^ (ulong)(uint)(cz + dz))));
                                float ox = (cx + dx + (hash & 0xffff) / 65536f) * cell;
                                float oz = (cz + dz + ((hash >> 16) & 0xffff) / 65536f) * cell;
                                float distance = MathF.Sqrt((x - ox) * (x - ox) + (z - oz) * (z - oz));
                                best = Math.Max(best, Smooth((_size - distance) / (0.3f * _size)));
                            }
                        }
                        return best;
                    }
                    default:
                        return 1;
                }
            }

            private static float Smooth(float t)
            {
                t = Math.Clamp(t, 0, 1);
                return t * t * (3 - 2 * t);
            }
        }

        /// <summary>
        /// A <see cref="Random"/> whose numbers follow from a seed set with <see cref="Reset"/>, cheap
        /// to set again for every place (xorshift of 64 bits).
        /// </summary>
        private sealed class HashRandom : Random
        {
            private ulong _state = 1;

            public void Reset(ulong seed) => _state = seed == 0 ? 0x9e3779b97f4a7c15UL : seed;

            private ulong NextBits()
            {
                _state ^= _state << 13;
                _state ^= _state >> 7;
                _state ^= _state << 17;
                return _state;
            }

            protected override double Sample() => (NextBits() >> 11) * (1.0 / (1UL << 53));

            public override double NextDouble() => Sample();

            public override int Next() => (int)(NextBits() >> 33);

            public override int Next(int maxValue) => (int)(Sample() * maxValue);

            public override int Next(int minValue, int maxValue) => minValue + (int)(Sample() * ((long)maxValue - minValue));

            public override float NextSingle() => (float)Sample();
        }
    }

    /// <summary>
    /// The props of the rules (user story P-4), made when asked and put in the map in place of the
    /// props the rules placed before: one step of undo, and the props placed by hand stay. Out of
    /// date when the terrain, the water level, the strata, the rules or the masks they read change.
    /// </summary>
    public sealed class PropRulesGenerator : IDocumentGenerator
    {
        public const string GeneratorId = "propsFromRules";

        public string Id => GeneratorId;

        public string Title => "Props from rules";

        public string Story => "P-4";

        public IReadOnlyList<GeneratedInput> Inputs { get; } =
            [GeneratedInput.Heightmap, GeneratedInput.WaterLevel, GeneratedInput.StratumMasks, RuleInputs.PropRules, RuleInputs.PropRuleMasks, GeneratedInput.Symmetry];

        public IReadOnlyList<GeneratedInput> Outputs { get; } = [GeneratedInput.Props];

        public object Generate(MapDocument document) => PropRules.Generate(document);

        /// <summary>
        /// The document without the props any rule placed, with <paramref name="value"/> (the props of
        /// <see cref="PropRules.Generate(MapDocument)"/>) numbered after the props that stay.
        /// </summary>
        public MapDocument Apply(MapDocument document, object value) => RuleEdits.ReplaceRuleProps(document, (ImmutableArray<Prop>)value);
    }
}
