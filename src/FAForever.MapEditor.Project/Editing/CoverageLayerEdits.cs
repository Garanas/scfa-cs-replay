using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// The edits of the stacks of coverage (<see cref="CoverageLayers"/>): a stratum's mask, a mask
    /// for tools, one of the water's maps. Each takes the target and the layer by name and puts the
    /// changed stack in the document; the session's <see cref="CoverageLayers.Follow"/> then makes the
    /// grid what the stack makes. The first edit of a grid without a stack starts one from its one
    /// painted layer (<see cref="CoverageLayers.Trivial"/>).
    /// </summary>
    /// <remarks>
    /// New layers combine as most people expect of coverage: a painted layer replaces where it was
    /// painted (its alpha starts at 0), an image, a mask reference and rules keep the higher of
    /// theirs and what is below (maximum), so adding one never takes away coverage until its way of
    /// combining is changed.
    /// </remarks>
    public static class CoverageLayerEdits
    {
        /// <summary>
        /// The document with a layer added to a target's stack, above the layer <paramref name="above"/> or on top.
        /// </summary>
        /// <exception cref="ArgumentException">The map has no such grid, the name is taken, the source
        /// does not fit, the grid is the wrong size, or a mask reference reads the target itself.</exception>
        public static MapDocument Add(MapDocument document, LayerTarget target, Layer layer, string? above = null)
        {
            LayerStack stack = Stack(document, target);
            CheckGrid(document, target, layer.Coverage);
            CheckGrid(document, target, layer.Alpha);
            CheckReads(document, target, layer.Source is MaskRefSource reference ? reference.Key : null);
            CheckReads(document, target, layer.Clip);
            return CoverageLayers.WithStack(document, LayerStackEdits.Add(stack, layer, above, CoverageLayers.What(target)));
        }

        /// <summary>
        /// A painted layer for a brush: nothing until painted (its alpha is 0), then it replaces.
        /// </summary>
        public static Layer NewPainted(MapDocument document, LayerTarget target, string name)
        {
            TiledBuffer<byte> grid = Grid(document, target);
            return new Layer
            {
                Name = name,
                Source = LayerSource.Painted,
                Combine = LayerCombine.Replace,
                Coverage = TiledBuffer<byte>.Create(grid.Width, grid.Height),
                Alpha = TiledBuffer<byte>.Create(grid.Width, grid.Height),
            };
        }

        /// <summary>
        /// A layer of an image file's grey, already read at the target grid's size.
        /// </summary>
        /// <param name="values">The grid's width by height bytes, row by row.</param>
        /// <param name="path">The file, relative to the project when it lies in it.</param>
        public static Layer NewImage(MapDocument document, LayerTarget target, string name, byte[] values, string path)
        {
            TiledBuffer<byte> grid = Grid(document, target);
            return new Layer
            {
                Name = name,
                Source = new ImageSource(path),
                Combine = LayerCombine.Maximum,
                Coverage = TiledBuffer<byte>.FromArray(grid.Width, grid.Height, values),
            };
        }

        /// <summary>
        /// A layer that reads another mask (a mask for tools or a stratum's, by key or name), keeping
        /// the higher of it and what is below.
        /// </summary>
        /// <exception cref="ArgumentException">The map has no such mask.</exception>
        public static Layer NewMaskRef(MapDocument document, string name, string mask, float threshold = 0, bool invert = false)
        {
            string key = LayerStacks.KeyOf(document, mask) ?? throw new ArgumentException($"The map has no mask '{mask}'", nameof(mask));
            return new Layer
            {
                Name = name,
                Source = new MaskRefSource(key, Math.Clamp(threshold, 0, 1), invert),
                Combine = LayerCombine.Maximum,
            };
        }

        /// <summary>
        /// A layer made by rules (slope, height, distance to water, other masks, noise), keeping the
        /// higher of what they make and what is below; its grid is made by generating
        /// (<see cref="Generate"/>), and empty until then.
        /// </summary>
        public static Layer NewRules(MapDocument document, LayerTarget target, string name, MaskRuleSet? rules = null)
        {
            TiledBuffer<byte> grid = Grid(document, target);
            return new Layer
            {
                Name = name,
                Source = new RulesSource(rules ?? new MaskRuleSet()),
                Combine = LayerCombine.Maximum,
                Coverage = TiledBuffer<byte>.Create(grid.Width, grid.Height),
            };
        }

        public static MapDocument Remove(MapDocument document, LayerTarget target, string name) =>
            CoverageLayers.WithStack(document, LayerStackEdits.Remove(Stack(document, target), name, CoverageLayers.What(target)));

        /// <summary>
        /// The document with a layer at another place of its stack: 0 is the bottom.
        /// </summary>
        public static MapDocument Move(MapDocument document, LayerTarget target, string name, int index) =>
            CoverageLayers.WithStack(document, LayerStackEdits.Move(Stack(document, target), name, index, CoverageLayers.What(target)));

        public static MapDocument Rename(MapDocument document, LayerTarget target, string name, string newName) =>
            CoverageLayers.WithStack(document, LayerStackEdits.Rename(Stack(document, target), name, newName, CoverageLayers.What(target)));

        public static MapDocument SetCombine(MapDocument document, LayerTarget target, string name, LayerCombine combine) =>
            CoverageLayers.WithStack(document, LayerStackEdits.SetCombine(Stack(document, target), name, combine, CoverageLayers.What(target)));

        public static MapDocument SetStrength(MapDocument document, LayerTarget target, string name, float strength) =>
            CoverageLayers.WithStack(document, LayerStackEdits.SetStrength(Stack(document, target), name, strength, CoverageLayers.What(target)));

        /// <summary>
        /// The document with a layer working only where a mask is (by key or name), or everywhere (null).
        /// </summary>
        /// <exception cref="ArgumentException">The map has no such mask, or it is the target itself.</exception>
        public static MapDocument SetClip(MapDocument document, LayerTarget target, string name, string? clip)
        {
            string? key = clip is null ? null : LayerStacks.KeyOf(document, clip) ?? throw new ArgumentException($"The map has no mask '{clip}'", nameof(clip));
            CheckReads(document, target, key);
            return CoverageLayers.WithStack(document, LayerStackEdits.SetClip(Stack(document, target), name, key, CoverageLayers.What(target)));
        }

        public static MapDocument SetVisible(MapDocument document, LayerTarget target, string name, bool visible) =>
            CoverageLayers.WithStack(document, LayerStackEdits.SetVisible(Stack(document, target), name, visible, CoverageLayers.What(target)));

        public static MapDocument SetLocked(MapDocument document, LayerTarget target, string name, bool locked) =>
            CoverageLayers.WithStack(document, LayerStackEdits.SetLocked(Stack(document, target), name, locked, CoverageLayers.What(target)));

        /// <summary>
        /// The document with a mask reference reading another mask, at another threshold or inverted.
        /// </summary>
        /// <exception cref="ArgumentException">The layer reads no mask, the map has no such mask, or it is the target itself.</exception>
        public static MapDocument SetMaskRef(MapDocument document, LayerTarget target, string name, string? mask = null, float? threshold = null, bool? invert = null)
        {
            LayerStack stack = Stack(document, target);
            int index = LayerStackEdits.IndexOf(stack, name, CoverageLayers.What(target));
            Layer layer = stack.Layers[index];
            MaskRefSource reference = layer.Source as MaskRefSource ?? throw new ArgumentException($"The {CoverageLayers.What(target)} {layer.Name} reads no mask", nameof(name));
            string key = mask is null ? reference.Key : LayerStacks.KeyOf(document, mask) ?? throw new ArgumentException($"The map has no mask '{mask}'", nameof(mask));
            CheckReads(document, target, key);
            MaskRefSource changed = reference with { Key = key, Threshold = threshold is float t ? Math.Clamp(t, 0, 1) : reference.Threshold, Invert = invert ?? reference.Invert };
            return changed == reference ? document : CoverageLayers.WithStack(document, stack.With(index, layer with { Source = changed }));
        }

        /// <summary>
        /// The document with a rules layer's rules; its grid stays what they made last until generated.
        /// </summary>
        /// <exception cref="ArgumentException">The layer is no rules layer.</exception>
        public static MapDocument SetRules(MapDocument document, LayerTarget target, string name, MaskRuleSet rules)
        {
            LayerStack stack = Stack(document, target);
            int index = LayerStackEdits.IndexOf(stack, name, CoverageLayers.What(target));
            Layer layer = stack.Layers[index];
            if (layer.Source is not RulesSource own)
            {
                throw new ArgumentException($"The {CoverageLayers.What(target)} {layer.Name} is not made by rules", nameof(name));
            }
            return own.Rules.Rules.AsSpan().SequenceEqual(rules.Rules.AsSpan()) ? document
                : CoverageLayers.WithStack(document, stack.With(index, layer with { Source = new RulesSource(rules) }));
        }

        /// <summary>
        /// The document with a layer's grid replaced (an image read again, rules generated); the
        /// tiles that hold the same values stay shared.
        /// </summary>
        /// <exception cref="ArgumentException">The grid is the wrong size.</exception>
        public static MapDocument SetGrid(MapDocument document, LayerTarget target, string name, TiledBuffer<byte> grid)
        {
            CheckGrid(document, target, grid);
            LayerStack stack = Stack(document, target);
            int index = LayerStackEdits.IndexOf(stack, name, CoverageLayers.What(target));
            Layer layer = stack.Layers[index];
            TiledBuffer<byte> shared = layer.Coverage is TiledBuffer<byte> held ? Shared(held, grid) : grid;
            return ReferenceEquals(shared, layer.Coverage) ? document : CoverageLayers.WithStack(document, stack.With(index, layer with { Coverage = shared }));
        }

        /// <summary>
        /// Every rules layer of every stack of coverage made again from its rules, in place: a target's
        /// rules after the stacks of the masks they read, each from what those make by then. One
        /// pass, which <see cref="MaskRulesGenerator"/> does on a thread of the pool.
        /// </summary>
        /// <exception cref="ArgumentException">A rule reads a layer the map has not, or masks read each other in a circle.</exception>
        public static IReadOnlyList<(string Target, string Layer, TiledBuffer<byte> Grid)> Generate(MapDocument document)
        {
            List<(string, string, TiledBuffer<byte>)> made = [];
            MapDocument working = document;
            foreach (string key in MaskRules.Order(document))
            {
                LayerStack stack = working.CoverageStacks[key];
                MapDocument before = working;
                foreach (Layer layer in stack.Layers.Where(layer => layer.Source is RulesSource))
                {
                    TiledBuffer<byte> size = stack.Target.Grid!.Read(working);
                    TiledBuffer<byte> grid = MaskRules.Generate(working, size.Width, size.Height, ((RulesSource)layer.Source).Rules, stack.Target.Key);
                    made.Add((key, layer.Name, grid));
                    working = SetGrid(working, stack.Target, layer.Name, grid);
                }
                working = CoverageLayers.Follow(before, working);
            }
            return made;
        }

        /// <summary>
        /// The document with generated grids of rules layers in place, where the layer is still there
        /// with rules and the same size.
        /// </summary>
        public static MapDocument ApplyGenerated(MapDocument document, IEnumerable<(string Target, string Layer, TiledBuffer<byte> Grid)> made)
        {
            MapDocument result = document;
            foreach ((string key, string name, TiledBuffer<byte> grid) in made)
            {
                if (result.CoverageStacks.TryGetValue(key, out LayerStack? stack) && stack.Find(name) is { Source: RulesSource, Coverage: TiledBuffer<byte> held }
                    && held.Width == grid.Width && held.Height == grid.Height)
                {
                    result = SetGrid(result, stack.Target, name, grid);
                }
            }
            return CoverageLayers.Follow(document, result);
        }

        /// <summary>
        /// A grid sharing the tiles of <paramref name="held"/> that hold the same values; held itself
        /// when every tile does.
        /// </summary>
        internal static TiledBuffer<byte> Shared(TiledBuffer<byte> held, TiledBuffer<byte> grid)
        {
            if (held.Width != grid.Width || held.Height != grid.Height)
            {
                return grid;
            }
            TiledBuffer<byte>.Builder? builder = null;
            for (int y = 0; y < grid.TilesY; y++)
            {
                for (int x = 0; x < grid.TilesX; x++)
                {
                    if (!held.SharesTile(grid, x, y) && !LayerStacks.SameTile(grid, x, y, held.Tile(x, y), grid.Tile(x, y)))
                    {
                        builder ??= held.ToBuilder();
                        builder.SetTile(x, y, grid.Tile(x, y));
                    }
                }
            }
            return builder is null ? held : builder.ToBuffer();
        }

        private static LayerStack Stack(MapDocument document, LayerTarget target)
        {
            if (!CoverageLayers.Exists(document, target))
            {
                throw new ArgumentException($"The map has no {target.Grid?.Title ?? target.Key}", nameof(target));
            }
            return CoverageLayers.StackOf(document, target);
        }

        private static TiledBuffer<byte> Grid(MapDocument document, LayerTarget target) =>
            CoverageLayers.Exists(document, target) ? target.Grid!.Read(document) : throw new ArgumentException($"The map has no {target.Grid?.Title ?? target.Key}", nameof(target));

        private static void CheckGrid(MapDocument document, LayerTarget target, TiledBuffer<byte>? grid)
        {
            TiledBuffer<byte> size = Grid(document, target);
            if (grid is not null && (grid.Width != size.Width || grid.Height != size.Height))
            {
                throw new ArgumentException($"A layer of {target.Grid!.Title} has {size.Width} by {size.Height} values, not {grid.Width} by {grid.Height}");
            }
        }

        /// <summary>
        /// Refuses a mask key that is the target itself: a stack cannot read what it makes.
        /// </summary>
        private static void CheckReads(MapDocument document, LayerTarget target, string? key)
        {
            if (key is not null && LayerStacks.StackKeyOf(key) == target.Key)
            {
                throw new ArgumentException($"A layer of {target.Grid!.Title} cannot read {LayerStacks.MaskTitle(key)}: that is what its stack makes");
            }
        }
    }
}
