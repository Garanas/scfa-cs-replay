using System.Collections.Immutable;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Generation
{
    /// <summary>
    /// Makes the masks for tools that have rules (user story K-2) from the terrain, the water and
    /// the other layers. A mask keeps its size (the map's or half); each pixel counts at its centre.
    /// The rules run in order on a value from 0 to 1 per pixel (<see cref="MaskRule"/>), which ends as
    /// the mask's bytes. A mask whose rules read another mask with rules is made after it, from what
    /// that one becomes.
    /// </summary>
    public static class MaskRules
    {
        /// <summary>
        /// Every mask with rules, made from <paramref name="document"/>, by name.
        /// </summary>
        /// <exception cref="ArgumentException">A rule reads a layer the map has not, or masks read each other in a circle.</exception>
        public static ImmutableSortedDictionary<string, TiledBuffer<byte>> GenerateAll(MapDocument document)
        {
            ImmutableSortedDictionary<string, TiledBuffer<byte>>.Builder made = ImmutableSortedDictionary.CreateBuilder<string, TiledBuffer<byte>>(StringComparer.Ordinal);
            MapDocument working = document;
            foreach (string name in Order(document))
            {
                TiledBuffer<byte> mask = Generate(working, name, document.MaskRules[name]);
                made[name] = mask;
                working = working with { Masks = working.Masks.SetItem(name, mask) };
            }
            return made.ToImmutable();
        }

        /// <summary>
        /// The masks with rules in the order they are made: each after the masks with rules it reads.
        /// </summary>
        /// <exception cref="ArgumentException">Masks read each other in a circle.</exception>
        public static IReadOnlyList<string> Order(MapDocument document)
        {
            List<string> order = [];
            HashSet<string> done = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> visiting = new HashSet<string>(StringComparer.Ordinal);
            void Visit(string name, string? from)
            {
                if (done.Contains(name))
                {
                    return;
                }
                if (!visiting.Add(name))
                {
                    throw new ArgumentException($"The rules of the masks '{from}' and '{name}' read each other: a mask cannot be made from itself");
                }
                foreach (string source in document.MaskRules[name].Sources.Where(document.MaskRules.ContainsKey))
                {
                    Visit(source, name);
                }
                visiting.Remove(name);
                done.Add(name);
                order.Add(name);
            }
            foreach (string name in document.MaskRules.Keys)
            {
                Visit(name, null);
            }
            return order;
        }

        /// <summary>
        /// One mask made from its rules, at the size the document's mask of that name has (the map's
        /// size when it has none). The layers its rules read are the document's as they are.
        /// </summary>
        /// <exception cref="ArgumentException">A rule reads a layer the map has not.</exception>
        public static TiledBuffer<byte> Generate(MapDocument document, string name, MaskRuleSet rules)
        {
            TiledBuffer<byte>? existing = document.Masks.GetValueOrDefault(name);
            int width = existing?.Width ?? document.Width;
            int height = existing?.Height ?? document.Height;
            float[] values = Evaluate(document, rules, width, height, name);
            byte[] bytes = new byte[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                bytes[i] = (byte)Math.Clamp(MathF.Round(values[i] * 255), 0, 255);
            }
            return TiledBuffer<byte>.FromArray(width, height, bytes);
        }

        /// <summary>
        /// The value of every pixel of a grid of <paramref name="width"/> by <paramref name="height"/>
        /// after the rules, from 0 to 1, row by row.
        /// </summary>
        /// <param name="name">The mask's name, for what a failure says.</param>
        public static float[] Evaluate(MapDocument document, MaskRuleSet rules, int width, int height, string name = "")
        {
            float[] values = new float[width * height];
            Array.Fill(values, 1f);
            // ogrids per pixel
            float scaleX = document.Width / (float)width;
            float scaleZ = document.Height / (float)height;
            float[]? waterDistance = null;
            for (int index = 0; index < rules.Rules.Length; index++)
            {
                MaskRule rule = rules.Rules[index];
                switch (rule.Kind)
                {
                    case MaskRuleKind.Blur:
                        BoxBlur(values, width, height, rule.Radius / scaleX, rule.Radius / scaleZ);
                        break;
                    case MaskRuleKind.Grow:
                        Extreme(values, width, height, (int)MathF.Round(rule.Radius / scaleX), (int)MathF.Round(rule.Radius / scaleZ), max: true);
                        break;
                    case MaskRuleKind.Shrink:
                        Extreme(values, width, height, (int)MathF.Round(rule.Radius / scaleX), (int)MathF.Round(rule.Radius / scaleZ), max: false);
                        break;
                    case MaskRuleKind.Invert:
                        for (int i = 0; i < values.Length; i++)
                        {
                            values[i] = 1 - values[i];
                        }
                        break;
                    case MaskRuleKind.Threshold:
                    {
                        float low = rule.Min;
                        float soft = Math.Max(0, rule.Soft);
                        for (int i = 0; i < values.Length; i++)
                        {
                            values[i] = soft <= 0 ? (values[i] >= low && values[i] > 0 ? 1 : 0) : Smooth((values[i] - low) / soft);
                        }
                        break;
                    }
                    default:
                    {
                        if (rule.Kind == MaskRuleKind.WaterDistance)
                        {
                            waterDistance ??= WaterDistance(document, width, height, scaleX, scaleZ);
                        }
                        Func<int, int, float> field = Field(document, rule, width, height, scaleX, scaleZ, waterDistance, name);
                        MaskCombine combine = index == 0 ? MaskCombine.Replace : rule.Combine;
                        bool invert = rule.Invert;
                        Parallel.For(0, height, pz =>
                        {
                            int row = pz * width;
                            for (int px = 0; px < width; px++)
                            {
                                float value = field(px, pz);
                                if (invert)
                                {
                                    value = 1 - value;
                                }
                                values[row + px] = Combine(values[row + px], value, combine);
                            }
                        });
                        break;
                    }
                }
            }
            return values;
        }

        private static float Combine(float before, float value, MaskCombine combine) => combine switch
        {
            MaskCombine.Multiply => before * value,
            MaskCombine.Min => Math.Min(before, value),
            MaskCombine.Max => Math.Max(before, value),
            MaskCombine.Add => Math.Min(1, before + value),
            MaskCombine.Subtract => Math.Max(0, before - value),
            _ => value,
        };

        /// <summary>
        /// What a rule that makes a value gives at a pixel.
        /// </summary>
        private static Func<int, int, float> Field(MapDocument document, MaskRule rule, int width, int height, float scaleX, float scaleZ, float[]? waterDistance, string name)
        {
            Heightmap heights = document.Terrain.Heightmap;
            switch (rule.Kind)
            {
                case MaskRuleKind.Slope:
                    return (px, pz) => Range(PaintLimits.SlopeAt(heights, (px + 0.5f) * scaleX, (pz + 0.5f) * scaleZ), rule);
                case MaskRuleKind.Height:
                    return (px, pz) => Range(heights.HeightAt((px + 0.5f) * scaleX, (pz + 0.5f) * scaleZ), rule);
                case MaskRuleKind.WaterDistance:
                    return (px, pz) => Range(waterDistance![pz * width + px], rule);
                case MaskRuleKind.Noise:
                {
                    float size = Math.Max(0.5f, rule.Scale);
                    int seed = rule.Seed;
                    // three octaves of value noise stay near the middle: spread them over 0 to 1
                    return (px, pz) => Math.Clamp(0.5f + 0.9f * HeightNoise.At((px + 0.5f) * scaleX / size, (pz + 0.5f) * scaleZ / size, seed), 0, 1);
                }
                case MaskRuleKind.Layer:
                {
                    TiledBuffer<byte> layer = MaskRuleSources.Read(document, rule.Source)
                        ?? throw new ArgumentException(string.IsNullOrEmpty(rule.Source)
                            ? $"A rule of the mask '{name}' reads a layer but names none"
                            : $"A rule of the mask '{name}' reads '{rule.Source}', which the map has not");
                    float full = rule.Min > 0 ? rule.Min : 1;
                    float layerX = layer.Width / (float)document.Width;
                    float layerZ = layer.Height / (float)document.Height;
                    return (px, pz) =>
                    {
                        float x = (px + 0.5f) * scaleX * layerX - 0.5f;
                        float z = (pz + 0.5f) * scaleZ * layerZ - 0.5f;
                        return Math.Min(1, Sample(layer, x, z) / 255f / full);
                    };
                }
                default:
                    throw new ArgumentException($"A rule of kind {rule.Kind} makes no value");
            }
        }

        /// <summary>
        /// 1 inside the rule's range, fading to 0 over <see cref="MaskRule.Soft"/> beyond its ends.
        /// </summary>
        public static float Range(float value, MaskRule rule)
        {
            float low = Math.Min(rule.Min, rule.Max);
            float high = Math.Max(rule.Min, rule.Max);
            if (value >= low && value <= high)
            {
                return 1;
            }
            float soft = rule.Soft;
            if (soft <= 0)
            {
                return 0;
            }
            float beyond = value < low ? low - value : value - high;
            return Smooth(1 - beyond / soft);
        }

        private static float Smooth(float t)
        {
            t = Math.Clamp(t, 0, 1);
            return t * t * (3 - 2 * t);
        }

        /// <summary>
        /// A grid read between its pixels' centres (a place in pixels, the centre of the first at 0).
        /// </summary>
        private static float Sample(TiledBuffer<byte> grid, float x, float z)
        {
            x = Math.Clamp(x, 0, grid.Width - 1);
            z = Math.Clamp(z, 0, grid.Height - 1);
            int x0 = (int)x;
            int z0 = (int)z;
            int x1 = Math.Min(x0 + 1, grid.Width - 1);
            int z1 = Math.Min(z0 + 1, grid.Height - 1);
            float u = x - x0;
            float v = z - z0;
            float top = grid[x0, z0] + (grid[x1, z0] - grid[x0, z0]) * u;
            float bottom = grid[x0, z1] + (grid[x1, z1] - grid[x0, z1]) * u;
            return top + (bottom - top) * v;
        }

        /// <summary>
        /// The distance from each pixel's centre to the nearest pixel under the water, in ogrids; 0
        /// under it. Without water, or with every pixel dry, everything is far away.
        /// </summary>
        public static float[] WaterDistance(MapDocument document, int width, int height, float scaleX, float scaleZ)
        {
            float[] distance = new float[width * height];
            if (!document.Water.HasWater)
            {
                Array.Fill(distance, Far);
                return distance;
            }
            Heightmap heights = document.Terrain.Heightmap;
            float level = document.Water.Elevation;
            // squared distances in pixels: 0 under the water, "infinite" elsewhere
            float[] squared = new float[width * height];
            bool any = false;
            Parallel.For(0, height, pz =>
            {
                for (int px = 0; px < width; px++)
                {
                    bool wet = heights.HeightAt((px + 0.5f) * scaleX, (pz + 0.5f) * scaleZ) < level;
                    squared[pz * width + px] = wet ? 0 : Infinity;
                    if (wet)
                    {
                        any = true;
                    }
                }
            });
            if (!any)
            {
                Array.Fill(distance, Far);
                return distance;
            }
            // the exact Euclidean distance transform of Felzenszwalb and Huttenlocher: rows, then columns, in ogrids
            Parallel.For(0, height, () => (new float[width], new float[width], new int[width], new float[width + 1]), (pz, _, buffers) =>
            {
                (float[] f, float[] d, int[] v, float[] z) = buffers;
                for (int px = 0; px < width; px++)
                {
                    f[px] = squared[pz * width + px];
                }
                Transform(f, d, v, z, width, scaleX);
                for (int px = 0; px < width; px++)
                {
                    squared[pz * width + px] = d[px];
                }
                return buffers;
            }, _ => { });
            Parallel.For(0, width, () => (new float[height], new float[height], new int[height], new float[height + 1]), (px, _, buffers) =>
            {
                (float[] f, float[] d, int[] v, float[] z) = buffers;
                for (int pz = 0; pz < height; pz++)
                {
                    f[pz] = squared[pz * width + px];
                }
                Transform(f, d, v, z, height, scaleZ);
                for (int pz = 0; pz < height; pz++)
                {
                    distance[pz * width + px] = MathF.Sqrt(d[pz]);
                }
                return buffers;
            }, _ => { });
            return distance;
        }

        /// <summary>
        /// How far a map without water is from it, in ogrids.
        /// </summary>
        public const float Far = 1e6f;

        private const float Infinity = 1e20f;

        /// <summary>
        /// One dimension of the distance transform: <paramref name="d"/>[q] is the least of
        /// f[p] + ((q - p) · step)² over p.
        /// </summary>
        private static void Transform(float[] f, float[] d, int[] v, float[] z, int n, float step)
        {
            double step2 = (double)step * step;
            double Meet(int q, int p) => ((f[q] + step2 * q * q) - (f[p] + step2 * p * p)) / (2 * step2 * (q - p));
            int k = 0;
            v[0] = 0;
            z[0] = float.NegativeInfinity;
            z[1] = float.PositiveInfinity;
            for (int q = 1; q < n; q++)
            {
                double s = Meet(q, v[k]);
                while (s <= z[k])
                {
                    k--;
                    s = Meet(q, v[k]);
                }
                k++;
                v[k] = q;
                z[k] = (float)s;
                z[k + 1] = float.PositiveInfinity;
            }
            k = 0;
            for (int q = 0; q < n; q++)
            {
                while (z[k + 1] < q)
                {
                    k++;
                }
                int p = v[k];
                d[q] = (float)(step2 * (q - p) * (q - p) + f[p]);
            }
        }

        /// <summary>
        /// Three passes of a box blur in each direction, close to a Gaussian of the radius.
        /// </summary>
        private static void BoxBlur(float[] values, int width, int height, float radiusX, float radiusZ)
        {
            int rx = Math.Max(0, (int)MathF.Round(radiusX / 1.7f));
            int rz = Math.Max(0, (int)MathF.Round(radiusZ / 1.7f));
            for (int pass = 0; pass < 3; pass++)
            {
                if (rx > 0)
                {
                    Lines(values, width, height, horizontal: true, (line, n) => BoxLine(line, n, rx));
                }
                if (rz > 0)
                {
                    Lines(values, width, height, horizontal: false, (line, n) => BoxLine(line, n, rz));
                }
            }
        }

        private static void BoxLine(float[] line, int n, int radius)
        {
            float[] copy = line[..n];
            float sum = 0;
            int count = 0;
            for (int i = -radius; i <= radius; i++)
            {
                if (i >= 0 && i < n)
                {
                    sum += copy[i];
                    count++;
                }
            }
            for (int i = 0; i < n; i++)
            {
                line[i] = sum / count;
                int leaving = i - radius;
                int entering = i + radius + 1;
                if (leaving >= 0)
                {
                    sum -= copy[leaving];
                    count--;
                }
                if (entering < n)
                {
                    sum += copy[entering];
                    count++;
                }
            }
        }

        /// <summary>
        /// The largest (grow) or smallest (shrink) value within a square of the radius, in each
        /// direction one after the other.
        /// </summary>
        private static void Extreme(float[] values, int width, int height, int radiusX, int radiusZ, bool max)
        {
            if (radiusX > 0)
            {
                Lines(values, width, height, horizontal: true, (line, n) => ExtremeLine(line, n, radiusX, max));
            }
            if (radiusZ > 0)
            {
                Lines(values, width, height, horizontal: false, (line, n) => ExtremeLine(line, n, radiusZ, max));
            }
        }

        /// <summary>
        /// A sliding window's largest or smallest value, with a queue of the candidates (each value
        /// enters and leaves once).
        /// </summary>
        private static void ExtremeLine(float[] line, int n, int radius, bool max)
        {
            float[] copy = line[..n];
            int[] queue = new int[n];
            int head = 0;
            int tail = 0;
            int next = 0;
            for (int i = 0; i < n; i++)
            {
                int last = Math.Min(n - 1, i + radius);
                for (; next <= last; next++)
                {
                    while (tail > head && (max ? copy[queue[tail - 1]] <= copy[next] : copy[queue[tail - 1]] >= copy[next]))
                    {
                        tail--;
                    }
                    queue[tail++] = next;
                }
                while (queue[head] < i - radius)
                {
                    head++;
                }
                line[i] = copy[queue[head]];
            }
        }

        /// <summary>
        /// Runs a change on every row or column of a grid, in parallel.
        /// </summary>
        private static void Lines(float[] values, int width, int height, bool horizontal, Action<float[], int> change)
        {
            int lines = horizontal ? height : width;
            int length = horizontal ? width : height;
            Parallel.For(0, lines, () => new float[length], (index, _, line) =>
            {
                for (int i = 0; i < length; i++)
                {
                    line[i] = horizontal ? values[index * width + i] : values[i * width + index];
                }
                change(line, length);
                for (int i = 0; i < length; i++)
                {
                    if (horizontal)
                    {
                        values[index * width + i] = line[i];
                    }
                    else
                    {
                        values[i * width + index] = line[i];
                    }
                }
                return line;
            }, _ => { });
        }
    }

    /// <summary>
    /// The masks for tools that have rules (user story K-2), made when asked and put in their place:
    /// one step of undo. Out of date when the terrain, the water level, the strata, the rules or the
    /// masks they read change.
    /// </summary>
    public sealed class MaskRulesGenerator : IDocumentGenerator
    {
        public const string GeneratorId = "masksFromRules";

        public string Id => GeneratorId;

        public string Title => "Masks from rules";

        public string Story => "K-2";

        public IReadOnlyList<GeneratedInput> Inputs { get; } =
            [GeneratedInput.Heightmap, GeneratedInput.WaterLevel, GeneratedInput.StratumMasks, RuleInputs.MaskRules, RuleInputs.MaskRuleSources];

        public object Generate(MapDocument document) => MaskRules.GenerateAll(document);

        /// <summary>
        /// The document with the generated masks in place of the masks of those names that still have
        /// rules and the same size.
        /// </summary>
        public MapDocument Apply(MapDocument document, object value)
        {
            ImmutableSortedDictionary<string, TiledBuffer<byte>> masks = document.Masks;
            foreach ((string name, TiledBuffer<byte> mask) in (ImmutableSortedDictionary<string, TiledBuffer<byte>>)value)
            {
                if (document.MaskRules.ContainsKey(name)
                    && masks.TryGetValue(name, out TiledBuffer<byte>? held)
                    && held.Width == mask.Width && held.Height == mask.Height)
                {
                    masks = masks.SetItem(name, mask);
                }
            }
            return ReferenceEquals(masks, document.Masks) ? document : document with { Masks = masks };
        }
    }
}
