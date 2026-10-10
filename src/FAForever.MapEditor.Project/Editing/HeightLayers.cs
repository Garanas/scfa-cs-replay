using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// The heightmap's stack of layers (user story T-5, <see cref="MapDocument.HeightStack"/>): the
    /// heightmap is what the stack makes (<see cref="LayerStacks"/>), kept so after every change
    /// (<see cref="Follow"/>).
    /// </summary>
    /// <remarks>
    /// The layers' values share the heightmap's tiles: a change of a layer, or of a mask a layer
    /// reads, composites again only the tiles it touched. A change of the heightmap itself while
    /// there are layers (a brush, an import) goes into the painted layer the editor chose
    /// (<see cref="EditSession.SculptLayer"/>), so that the composite is what the brush made; without
    /// one the composite wins and the change is gone.
    /// </remarks>
    public static class HeightLayers
    {
        /// <summary>
        /// The layer of that name (exactly, else without regard to case), or null.
        /// </summary>
        public static Layer? Find(MapDocument document, string? name) => document.HeightStack.Find(name);

        /// <summary>
        /// Why the sculpt tool cannot write into a layer, or null when it can (or the map has no
        /// layers: then it writes the heightmap).
        /// </summary>
        public static string? SculptProblem(MapDocument document, string? layerName)
        {
            if (document.HeightStack.IsEmpty)
            {
                return null;
            }
            return LayerStacks.PaintProblem(document.HeightStack, layerName, "height layer", "sculpt");
        }

        /// <summary>
        /// The document with its heightmap what its layers make, every tile composited again.
        /// </summary>
        public static MapDocument Recomposite(MapDocument document)
        {
            if (document.HeightStack.IsEmpty)
            {
                return document;
            }
            return WithComposite(document, null);
        }

        /// <summary>
        /// The document after a change, with its heightmap what its layers make again: the tiles
        /// whose layers or masks changed are composited again, and a change of the heightmap itself
        /// goes into the layer <paramref name="sculptLayer"/> first (or is undone without one that
        /// can be sculpted, <see cref="SculptProblem"/>).
        /// </summary>
        /// <param name="previous">The document before the change, whose heightmap was what its
        /// layers made.</param>
        /// <returns><paramref name="next"/> itself when it holds the composite already.</returns>
        public static MapDocument Follow(MapDocument previous, MapDocument next, string? sculptLayer)
        {
            if (next.HeightStack.IsEmpty)
            {
                return next;
            }
            TiledBuffer<ushort> before = previous.Terrain.Heightmap.Samples;
            TiledBuffer<ushort> samples = next.Terrain.Heightmap.Samples;
            bool sameStack = ReferenceEquals(previous.HeightStack, next.HeightStack) || previous.HeightStack.Equals(next.HeightStack);
            bool sameMasks = ReferenceEquals(previous.Masks, next.Masks) && ReferenceEquals(previous.Terrain.StratumMasks, next.Terrain.StratumMasks);
            if (sameStack && sameMasks && ReferenceEquals(before, samples))
            {
                return next;
            }

            LayerStack followed = LayerStacks.FollowRenamedMasks(previous, next, next.HeightStack);
            MapDocument result = ReferenceEquals(followed, next.HeightStack) ? next : next with { HeightStack = followed };
            bool sameSize = before.Width == samples.Width && before.Height == samples.Height;
            bool[] dirty = previous.HeightStack.IsEmpty || !sameSize
                ? AllTiles(samples)
                : LayerStacks.ChangedTiles(previous, previous.HeightStack, result, result.HeightStack);

            if (sameSize && !ReferenceEquals(before, samples))
            {
                LayerStack taken = LayerStacks.TakeChanges(result, result.HeightStack, before, samples, result.Terrain.Heightmap.Scale, sculptLayer, dirty);
                if (!ReferenceEquals(taken, result.HeightStack))
                {
                    result = result with { HeightStack = taken };
                }
            }
            result = WithComposite(result, dirty);

            // a composite that came out as it was is the same heightmap, so a change that changed nothing is no step
            TiledBuffer<ushort> sum = result.Terrain.Heightmap.Samples;
            if (sameSize && !ReferenceEquals(sum, before)
                && sum.ChangedTiles(before).All(tile => sum.Tile(tile.X, tile.Y).SequenceEqual(before.Tile(tile.X, tile.Y))))
            {
                result = result with { Terrain = result.Terrain with { Heightmap = result.Terrain.Heightmap with { Samples = before } } };
            }
            return result;
        }

        /// <summary>
        /// What the layers below <paramref name="upTo"/> (all of them when it is the count) make at
        /// every corner of one tile, in game units.
        /// </summary>
        internal static void Heights(MapDocument document, int tileX, int tileY, int upTo, Span<float> heights) =>
            LayerStacks.TileOf(document, document.HeightStack, tileX, tileY, upTo, heights);

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
    }
}
