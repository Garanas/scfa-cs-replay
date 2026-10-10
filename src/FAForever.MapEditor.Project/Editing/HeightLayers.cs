using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// The heightmap's stacks of layers (user story T-5, <see cref="MapDocument.HeightStack"/>, the
    /// final, and <see cref="MapDocument.DraftStack"/>, the draft; <see cref="HeightStacks"/>): the
    /// heightmap is what the final makes and the draft's samples what the draft makes
    /// (<see cref="LayerStacks"/>), kept so after every change (<see cref="Follow"/>).
    /// </summary>
    /// <remarks>
    /// The layers' values share the heightmap's tiles: a change of a layer, or of a mask a layer
    /// reads, composites again only the tiles it touched. A change of the heightmap itself while
    /// there are layers (a brush, an import) goes into the painted layer the editor chose
    /// (<see cref="EditSession.SculptLayer"/>), so that the composite is what the brush made; without
    /// one the composite wins and the change is gone. A brush on the draft writes the draft's
    /// samples (<see cref="EditSession.Heights"/>), whose change goes into the chosen layer of the
    /// draft the same way.
    /// </remarks>
    public static class HeightLayers
    {
        /// <summary>
        /// The layer of that name in either stack (exactly, else without regard to case), or null.
        /// </summary>
        public static Layer? Find(MapDocument document, string? name) => HeightStacks.Find(document, name);

        /// <summary>
        /// Why the sculpt tool cannot write into a layer of a stack (the final unless named), or null
        /// when it can (or the map has no layers: then it writes the heightmap).
        /// </summary>
        public static string? SculptProblem(MapDocument document, string? layerName, HeightStackKind kind = HeightStackKind.Final)
        {
            if (document.HeightStack.IsEmpty)
            {
                return null;
            }
            LayerStack stack = HeightStacks.Of(document, kind);
            if (stack.IsEmpty)
            {
                stack = document.HeightStack;
            }
            if (stack.Find(layerName) is null && HeightStacks.Find(document, layerName) is Layer elsewhere)
            {
                return kind == HeightStackKind.Draft
                    ? $"The height layer {elsewhere.Name} is not in the draft: choose a layer of the draft, or share it"
                    : $"The height layer {elsewhere.Name} is not in the final: choose a layer of the final, or share it";
            }
            return LayerStacks.PaintProblem(stack, layerName, "height layer", "sculpt");
        }

        /// <summary>
        /// The document with its heightmap what its final's layers make and its draft's samples what
        /// the draft's make, every tile composited again.
        /// </summary>
        public static MapDocument Recomposite(MapDocument document)
        {
            document = HeightStacks.Normalized(document);
            if (document.HeightStack.IsEmpty)
            {
                return document;
            }
            document = WithComposite(document, null);
            return WithDraftComposite(document, null, null);
        }

        /// <summary>
        /// The document after a change, with its heightmap what its final's layers make again and the
        /// draft's samples what the draft's make: the tiles whose layers or masks changed are
        /// composited again, and a change of a heightmap itself (the terrain's, or the draft's samples
        /// a brush wrote while the draft was shown) goes into the layer <paramref name="sculptLayer"/>
        /// of that stack first (or is undone without one that can be sculpted, <see cref="SculptProblem"/>).
        /// A shared layer's change in one stack is made in the other (<see cref="HeightStacks.Sync"/>).
        /// </summary>
        /// <param name="previous">The document before the change, whose heightmap and draft were what
        /// their layers made.</param>
        /// <returns><paramref name="next"/> itself when it holds the composites already.</returns>
        public static MapDocument Follow(MapDocument previous, MapDocument next, string? sculptLayer)
        {
            if (next.HeightStack.IsEmpty)
            {
                return HeightStacks.Normalized(next);
            }
            TiledBuffer<ushort> before = previous.Terrain.Heightmap.Samples;
            TiledBuffer<ushort> samples = next.Terrain.Heightmap.Samples;
            TiledBuffer<ushort> draftBefore = previous.DraftSamples ?? before;
            TiledBuffer<ushort>? draftNow = next.DraftSamples;
            bool sameStack = ReferenceEquals(previous.HeightStack, next.HeightStack) || previous.HeightStack.Equals(next.HeightStack);
            bool sameDraft = ReferenceEquals(previous.DraftStack, next.DraftStack) || previous.DraftStack.Equals(next.DraftStack);
            bool sameMasks = ReferenceEquals(previous.Masks, next.Masks) && ReferenceEquals(previous.Terrain.StratumMasks, next.Terrain.StratumMasks);
            bool draftByHand = draftNow is not null && !ReferenceEquals(draftNow, previous.DraftSamples);
            if (sameStack && sameDraft && sameMasks && ReferenceEquals(before, samples) && !draftByHand && !next.DraftStack.IsEmpty)
            {
                return next;
            }

            MapDocument result = HeightStacks.Normalized(next);
            LayerStack followed = LayerStacks.FollowRenamedMasks(previous, result, result.HeightStack);
            LayerStack followedDraft = LayerStacks.FollowRenamedMasks(previous, result, result.DraftStack);
            if (!ReferenceEquals(followed, result.HeightStack) || !ReferenceEquals(followedDraft, result.DraftStack))
            {
                result = result with { HeightStack = followed, DraftStack = followedDraft };
            }
            bool sameSize = before.Width == samples.Width && before.Height == samples.Height;
            bool known = sameSize && !previous.HeightStack.IsEmpty;
            LayerStack previousDraft = previous.DraftStack.IsEmpty ? previous.HeightStack : previous.DraftStack;

            // a brush's work on the draft goes into the draft's sculpting layer
            bool[]? draftTaken = null;
            if (draftByHand && known && draftNow!.Width == draftBefore.Width && draftNow.Height == draftBefore.Height)
            {
                draftTaken = new bool[samples.TilesX * samples.TilesY];
                LayerStack taken = LayerStacks.TakeChanges(result, result.DraftStack, draftBefore, draftNow, result.Terrain.Heightmap.Scale, sculptLayer, draftTaken);
                if (!ReferenceEquals(taken, result.DraftStack))
                {
                    result = result with { DraftStack = taken };
                }
            }

            bool[] dirty = known
                ? LayerStacks.ChangedTiles(previous, previous.HeightStack, result, result.HeightStack)
                : AllTiles(samples);

            if (sameSize && !ReferenceEquals(before, samples))
            {
                LayerStack taken = LayerStacks.TakeChanges(result, result.HeightStack, before, samples, result.Terrain.Heightmap.Scale, sculptLayer, dirty);
                if (!ReferenceEquals(taken, result.HeightStack))
                {
                    result = result with { HeightStack = taken };
                }
            }

            // a shared layer is one: what changed in one stack is in the other
            MapDocument synced = HeightStacks.Sync(previous with { DraftStack = previousDraft }, result);
            if (known && !ReferenceEquals(synced.HeightStack, result.HeightStack))
            {
                bool[] carried = LayerStacks.ChangedTiles(result, result.HeightStack, synced, synced.HeightStack);
                for (int i = 0; i < dirty.Length; i++)
                {
                    dirty[i] |= carried[i];
                }
            }
            result = WithComposite(synced, dirty);

            // a composite that came out as it was is the same heightmap, so a change that changed nothing is no step
            TiledBuffer<ushort> sum = result.Terrain.Heightmap.Samples;
            if (sameSize && !ReferenceEquals(sum, before)
                && sum.ChangedTiles(before).All(tile => sum.Tile(tile.X, tile.Y).SequenceEqual(before.Tile(tile.X, tile.Y))))
            {
                result = result with { Terrain = result.Terrain with { Heightmap = result.Terrain.Heightmap with { Samples = before } } };
            }

            if (!known || draftBefore.Width != samples.Width || draftBefore.Height != samples.Height)
            {
                return WithDraftComposite(result, null, null);
            }
            // the draft as it was (the final's samples while the two made the same) with the tiles
            // whose draft layers or masks changed composited again
            bool[] draftDirty = LayerStacks.ChangedTiles(previous, previousDraft, result, result.DraftStack);
            if (draftTaken is not null)
            {
                for (int i = 0; i < draftDirty.Length; i++)
                {
                    draftDirty[i] |= draftTaken[i];
                }
            }
            if (previous.DraftSamples is not null && !draftDirty.Contains(true)
                && !HeightStacks.MakeTheSame(result.HeightStack, result.DraftStack))
            {
                // a change of the final alone (a stroke in a layer of its own): the draft stays as it was
                return ReferenceEquals(result.DraftSamples, previous.DraftSamples) ? result : result with { DraftSamples = previous.DraftSamples };
            }
            return WithDraftComposite(result, draftBefore, draftDirty);
        }

        /// <summary>
        /// What the layers below <paramref name="upTo"/> (all of them when it is the count) of a stack
        /// make at every corner of one tile, in game units.
        /// </summary>
        internal static void Heights(MapDocument document, int tileX, int tileY, int upTo, Span<float> heights, HeightStackKind kind = HeightStackKind.Final) =>
            LayerStacks.TileOf(document, HeightStacks.Of(document, kind), tileX, tileY, upTo, heights);

        private static bool[] AllTiles(TiledBuffer<ushort> samples)
        {
            bool[] dirty = new bool[samples.TilesX * samples.TilesY];
            Array.Fill(dirty, true);
            return dirty;
        }

        /// <summary>
        /// The document with the dirty tiles of its heightmap (all when null) composited again; tiles
        /// that come out as they were stay shared.
        /// </summary>
        private static MapDocument WithComposite(MapDocument document, bool[]? dirty)
        {
            Heightmap heightmap = document.Terrain.Heightmap;
            TiledBuffer<ushort> samples = LayerStacks.CompositeHeights(document, document.HeightStack, heightmap, dirty);
            return ReferenceEquals(samples, heightmap.Samples)
                ? document
                : document with { Terrain = document.Terrain with { Heightmap = heightmap with { Samples = samples } } };
        }

        /// <summary>
        /// The document with its draft's samples what the draft's layers make: none while they make
        /// what the final does (the same layers in the same order), else the dirty tiles (all when
        /// <paramref name="current"/> is null) of <paramref name="current"/>, the draft as it was,
        /// composited again, tiles that come out as they were shared.
        /// </summary>
        private static MapDocument WithDraftComposite(MapDocument document, TiledBuffer<ushort>? current, bool[]? dirty)
        {
            if (document.DraftStack.IsEmpty || HeightStacks.MakeTheSame(document.HeightStack, document.DraftStack))
            {
                return document.DraftSamples is null ? document : document with { DraftSamples = null };
            }
            Heightmap heightmap = document.Terrain.Heightmap;
            TiledBuffer<ushort> start = current ?? heightmap.Samples;
            TiledBuffer<ushort> draft = LayerStacks.CompositeHeights(document, document.DraftStack, heightmap with { Samples = start }, current is null ? null : dirty);
            return ReferenceEquals(draft, document.DraftSamples) ? document : document with { DraftSamples = draft };
        }
    }
}
