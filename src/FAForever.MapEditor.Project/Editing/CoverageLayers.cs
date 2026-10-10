using System.Collections.Immutable;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// The stacks of layers of the grids of coverage (<see cref="MapDocument.CoverageStacks"/>): the
    /// stratum masks, the masks for tools and the water's three maps. Each grid is what its stack
    /// makes (<see cref="LayerStacks"/>), kept so after every change (<see cref="Follow"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A grid without a stack in the document is its own one painted layer: <see cref="StackOf"/>
    /// gives that stack (<see cref="Trivial"/>, a layer named <see cref="PaintedName"/> holding the
    /// grid itself), and the first edit of the stack (a layer added) keeps it in the document. A stack
    /// that comes back to that shape (one painted layer as the grid holds it) leaves the document
    /// again, so an older project, which has no stacks, opens with one painted layer per grid holding
    /// what it held, and saves as it was.
    /// </para>
    /// <para>
    /// A painted layer of coverage has an alpha (<see cref="Layer.Alpha"/>): it starts at 0, so a new
    /// painted layer changes nothing until a brush paints it, and a brush raises it where it paints.
    /// The default painted layer of a grid (the trivial stack's) has none: it is all of the grid.
    /// </para>
    /// </remarks>
    public static class CoverageLayers
    {
        /// <summary>
        /// The name of a grid's own painted layer (<see cref="Trivial"/>).
        /// </summary>
        public const string PaintedName = "Painted";

        /// <summary>
        /// What a layer of a target is called in messages: <c>stratum layer</c>, <c>mask layer</c>,
        /// <c>water layer</c>.
        /// </summary>
        public static string What(LayerTarget target) =>
            target.Key.StartsWith("stratum-", StringComparison.Ordinal) ? "stratum layer"
            : target.Key.StartsWith("water-", StringComparison.Ordinal) ? "water layer"
            : "mask layer";

        /// <summary>
        /// Whether the document has the target's grid: the stratum exists, the mask is there.
        /// </summary>
        public static bool Exists(MapDocument document, LayerTarget target)
        {
            if (target.Grid is not PaintTarget grid)
            {
                return false;
            }
            try
            {
                grid.Read(document);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        /// <summary>
        /// Every target of coverage the document has: its strata (1 to 8, or 4), its masks for tools,
        /// the water's three maps.
        /// </summary>
        public static IEnumerable<LayerTarget> Targets(MapDocument document) =>
            Enumerable.Range(1, document.Terrain.StratumMasks.Layers.Length).Select(LayerTarget.Stratum)
                .Concat(document.Masks.Keys.Select(LayerTarget.Mask))
                .Concat(Enum.GetValues<WaterMask>().Select(LayerTarget.Water));

        /// <summary>
        /// A target's stack: the document's, else its grid as its one painted layer.
        /// </summary>
        /// <exception cref="ArgumentException">The document has no such grid.</exception>
        public static LayerStack StackOf(MapDocument document, LayerTarget target) =>
            document.CoverageStacks.TryGetValue(target.Key, out LayerStack? stack) ? stack : Trivial(document, target);

        /// <summary>
        /// The stack of a grid that has none of its own: one painted layer holding the grid.
        /// </summary>
        /// <exception cref="ArgumentException">The document has no such grid, or the target makes heights.</exception>
        public static LayerStack Trivial(MapDocument document, LayerTarget target)
        {
            PaintTarget grid = target.Grid ?? throw new ArgumentException($"The stack of {target} makes heights, not coverage", nameof(target));
            return new LayerStack
            {
                Target = target,
                Layers = [new Layer { Name = PaintedName, Source = LayerSource.Painted, Combine = LayerCombine.Replace, Coverage = grid.Read(document) }],
            };
        }

        /// <summary>
        /// Whether a stack is of the trivial shape: one painted layer named <see cref="PaintedName"/>,
        /// replacing at full strength everywhere, shown and not locked, without an alpha.
        /// </summary>
        public static bool IsTrivialShape(LayerStack stack) =>
            stack.Layers.Length == 1 && stack.Layers[0] is
            {
                Name: PaintedName, Source: PaintedSource, Combine: LayerCombine.Replace, Strength: 1, Clip: null, Visible: true, Locked: false,
                Alpha: null, Coverage: not null,
            };

        /// <summary>
        /// The document with a target's stack in place (an edit of the stack); <see cref="Follow"/> then
        /// makes the grid what it makes.
        /// </summary>
        public static MapDocument WithStack(MapDocument document, LayerStack stack)
        {
            if (stack.Target.IsHeights)
            {
                throw new ArgumentException($"The stack of {stack.Target} makes heights, not coverage", nameof(stack));
            }
            if (document.CoverageStacks.TryGetValue(stack.Target.Key, out LayerStack? held) && ReferenceEquals(held, stack))
            {
                return document;
            }
            return document with { CoverageStacks = document.CoverageStacks.SetItem(stack.Target.Key, stack) };
        }

        /// <summary>
        /// The painted layer a brush's change of a target's grid goes into: <paramref name="chosen"/>
        /// when it can be painted, else the highest that can; null for none.
        /// </summary>
        public static string? PaintLayerOf(LayerStack stack, string? chosen) =>
            LayerStacks.PaintProblem(stack, chosen) is null
                ? stack.Find(chosen)!.Name
                : stack.Layers.LastOrDefault(layer => LayerStacks.PaintProblem(stack, layer.Name) is null)?.Name;

        /// <summary>
        /// Why a brush cannot paint a target's grid, or null when it can: a stack without a layer that
        /// can be painted refuses (its grid is what its layers make).
        /// </summary>
        public static string? PaintProblem(MapDocument document, LayerTarget target, string? chosen)
        {
            if (!document.CoverageStacks.TryGetValue(target.Key, out LayerStack? stack))
            {
                return null;
            }
            return PaintLayerOf(stack, chosen) is null ? LayerStacks.PaintProblem(stack, chosen, What(target)) : null;
        }

        /// <summary>
        /// The document with the stacks of strata following the strata to other places (a stratum
        /// removed, moved or given an empty mask): <paramref name="place"/> gives a stratum's new
        /// place, or null when its stack goes. The composites move with their strata, so no grid changes.
        /// </summary>
        public static MapDocument MoveStrata(MapDocument document, Func<int, int?> place)
        {
            if (!document.CoverageStacks.Keys.Any(key => key.StartsWith("stratum-", StringComparison.Ordinal)))
            {
                return document;
            }
            ImmutableSortedDictionary<string, LayerStack>.Builder stacks = ImmutableSortedDictionary.CreateBuilder<string, LayerStack>(StringComparer.Ordinal);
            foreach ((string key, LayerStack stack) in document.CoverageStacks)
            {
                if (!key.StartsWith("stratum-", StringComparison.Ordinal))
                {
                    stacks[key] = stack;
                    continue;
                }
                int layer = int.Parse(key["stratum-".Length..], System.Globalization.CultureInfo.InvariantCulture);
                if (place(layer) is int to)
                {
                    LayerTarget target = LayerTarget.Stratum(to);
                    stacks[target.Key] = to == layer ? stack : stack with { Target = target };
                }
            }
            return document with { CoverageStacks = stacks.ToImmutable() };
        }

        /// <summary>
        /// The rules of every mask for tools with a rules layer: the rules of its lowest one.
        /// </summary>
        public static ImmutableSortedDictionary<string, MaskRuleSet> MaskRulesOf(MapDocument document)
        {
            ImmutableSortedDictionary<string, MaskRuleSet>.Builder rules = ImmutableSortedDictionary.CreateBuilder<string, MaskRuleSet>(StringComparer.Ordinal);
            foreach ((string key, LayerStack stack) in document.CoverageStacks)
            {
                if (key.StartsWith("mask-", StringComparison.Ordinal) && stack.Layers.FirstOrDefault(layer => layer.Source is RulesSource) is { Source: RulesSource own })
                {
                    rules[key["mask-".Length..]] = own.Rules;
                }
            }
            return rules.ToImmutable();
        }

        /// <summary>
        /// The document with every stack's grid what its layers make, every tile composited again
        /// (after opening a project).
        /// </summary>
        public static MapDocument Recomposite(MapDocument document)
        {
            MapDocument result = document;
            foreach (string key in Order(document))
            {
                LayerStack stack = result.CoverageStacks[key];
                if (!Exists(result, stack.Target))
                {
                    result = result with { CoverageStacks = result.CoverageStacks.Remove(key) };
                    continue;
                }
                result = Composite(result, stack, null);
            }
            return result;
        }

        /// <summary>
        /// The document after a change, with every grid of coverage that has a stack what its layers
        /// make again: the tiles whose layers or masks changed are composited again, and a change of
        /// the grid itself (a brush) goes into the target's chosen painted layer first
        /// (<paramref name="chosen"/>, by target key), or the highest that can be painted; without one
        /// the composite wins and the change is gone. Stacks that read other masks follow after them.
        /// </summary>
        /// <param name="previous">The document before the change, whose grids were what their stacks made.</param>
        /// <returns><paramref name="next"/> itself when it holds the composites already.</returns>
        public static MapDocument Follow(MapDocument previous, MapDocument next, Func<string, string?>? chosen = null)
        {
            if (next.CoverageStacks.IsEmpty)
            {
                return next;
            }
            if (ReferenceEquals(previous.CoverageStacks, next.CoverageStacks) && ReferenceEquals(previous.Masks, next.Masks)
                && ReferenceEquals(previous.Terrain.StratumMasks, next.Terrain.StratumMasks) && SameWater(previous.Water, next.Water))
            {
                return next;
            }
            MapDocument result = next;
            foreach (string key in Order(next))
            {
                LayerStack stack = result.CoverageStacks[key];
                LayerTarget target = stack.Target;
                if (!Exists(result, target))
                {
                    // the stratum or mask is gone: its stack goes with it
                    result = result with { CoverageStacks = result.CoverageStacks.Remove(key) };
                    continue;
                }
                LayerStack followed = LayerStacks.FollowRenamedMasks(previous, result, stack);
                TiledBuffer<byte> now = target.Grid!.Read(result);
                TiledBuffer<byte>? before = Exists(previous, target) ? target.Grid.Read(previous) : null;
                LayerStack? was = previous.CoverageStacks.GetValueOrDefault(key);
                bool sameSize = before is not null && before.Width == now.Width && before.Height == now.Height;
                if (was is not null && ReferenceEquals(was, followed) && ReferenceEquals(before, now) && !MasksChanged(previous, result, followed))
                {
                    continue;
                }
                bool[]? dirty = null;
                if (was is not null && sameSize && was.Target == followed.Target)
                {
                    dirty = LayerStacks.ChangedTiles(previous, was, result, followed);
                    if (!ReferenceEquals(before, now))
                    {
                        followed = LayerStacks.TakeChanges(result, followed, before!, now, 1, PaintLayerOf(followed, chosen?.Invoke(key)), dirty);
                    }
                }
                result = Composite(result, followed, dirty);
            }
            return result;
        }

        /// <summary>
        /// The keys of the document's stacks in the order they follow: each after the stacks of the
        /// masks it reads (a circle is broken where it closes).
        /// </summary>
        public static IReadOnlyList<string> Order(MapDocument document)
        {
            List<string> order = [];
            HashSet<string> done = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> visiting = new HashSet<string>(StringComparer.Ordinal);
            void Visit(string key)
            {
                if (done.Contains(key) || !visiting.Add(key))
                {
                    return;
                }
                foreach (string read in ReadsOf(document.CoverageStacks[key]))
                {
                    if (LayerStacks.StackKeyOf(read) is string stackKey && document.CoverageStacks.ContainsKey(stackKey))
                    {
                        Visit(stackKey);
                    }
                }
                visiting.Remove(key);
                done.Add(key);
                order.Add(key);
            }
            foreach (string key in document.CoverageStacks.Keys)
            {
                Visit(key);
            }
            return order;
        }

        /// <summary>
        /// The masks a stack reads, as mask keys or names: its clips, mask references and the layers
        /// its rules read.
        /// </summary>
        public static IEnumerable<string> ReadsOf(LayerStack stack) =>
            LayerStacks.MaskKeysOf(stack).Concat(stack.Layers.SelectMany(layer => layer.Source is RulesSource rules
                ? rules.Rules.Rules.Where(rule => rule.Kind == MaskRuleKind.Layer && rule.Source.Length > 0).Select(rule => rule.Source)
                : []));

        /// <summary>
        /// The stack with its grid composited again over the dirty tiles (all when null), in the
        /// document; a stack that came back to the trivial shape leaves the document.
        /// </summary>
        private static MapDocument Composite(MapDocument document, LayerStack stack, bool[]? dirty)
        {
            PaintTarget grid = stack.Target.Grid!;
            MapDocument result = WithStack(document, stack);
            TiledBuffer<byte> current = grid.Read(result);
            TiledBuffer<byte> composite = LayerStacks.CompositeCoverage(result, stack, current, dirty);
            if (!ReferenceEquals(composite, current))
            {
                result = grid.Write(result, composite);
            }
            if (IsTrivialShape(stack) && SameContent(stack.Layers[0].Coverage!, composite))
            {
                result = result with { CoverageStacks = result.CoverageStacks.Remove(stack.Target.Key) };
            }
            return result;
        }

        private static bool MasksChanged(MapDocument previous, MapDocument next, LayerStack stack)
        {
            if (ReferenceEquals(previous.Masks, next.Masks) && ReferenceEquals(previous.Terrain.StratumMasks, next.Terrain.StratumMasks))
            {
                return false;
            }
            return LayerStacks.MaskKeysOf(stack).Any(key => !ReferenceEquals(LayerStacks.MaskOf(previous, key), LayerStacks.MaskOf(next, key)));
        }

        private static bool SameWater(Water a, Water b) =>
            ReferenceEquals(a, b) || (ReferenceEquals(a.FoamMask, b.FoamMask) && ReferenceEquals(a.FlatnessMask, b.FlatnessMask) && ReferenceEquals(a.DepthBiasMask, b.DepthBiasMask));

        /// <summary>
        /// Whether two grids hold the same values (tiles they share are not compared).
        /// </summary>
        internal static bool SameContent(TiledBuffer<byte> a, TiledBuffer<byte> b)
        {
            if (ReferenceEquals(a, b))
            {
                return true;
            }
            if (a.Width != b.Width || a.Height != b.Height)
            {
                return false;
            }
            return a.ChangedTiles(b).All(tile => LayerStacks.SameTile(a, tile.X, tile.Y, a.Tile(tile.X, tile.Y), b.Tile(tile.X, tile.Y)));
        }
    }
}
