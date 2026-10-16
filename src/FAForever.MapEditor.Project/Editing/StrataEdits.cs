using System.Collections.Immutable;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// Changes to the terrain's layers (user story S-2): their textures and scales, and the strata
    /// between the lower and the upper layer added, removed and put in another order, each with its
    /// mask.
    /// </summary>
    /// <remarks>
    /// The number of layers is the <c>.scmap</c> version's: six in version 53 (four strata), ten
    /// from version 56 on (eight strata). Layer 0 is the lower layer, under everything; the last is
    /// the upper layer, over everything; neither has a mask. Stratum <c>n</c> (1 to 4 or 8) has the
    /// mask at index <c>n - 1</c> of <see cref="StratumMasks.Layers"/>, and a later stratum covers
    /// an earlier one. A stratum without an albedo texture is unused: adding one fills the first such
    /// slot, removing one empties the last.
    /// </remarks>
    public static class StrataEdits
    {
        /// <summary>
        /// Whether a layer has a texture: an unused stratum has an empty albedo path.
        /// </summary>
        public static bool IsUsed(this TerrainLayer layer) => layer.AlbedoPath.Length > 0;

        /// <summary>
        /// Whether a layer index is a stratum (with a mask), not the lower or upper layer.
        /// </summary>
        public static bool IsStratum(this Terrain terrain, int layer) => layer >= 1 && layer <= terrain.Layers.Length - 2;

        /// <summary>
        /// Changes one layer (0 is the lower layer, the last the upper one).
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">There is no such layer.</exception>
        public static MapDocument UpdateLayer(this MapDocument document, int layer, Func<TerrainLayer, TerrainLayer> update)
        {
            ImmutableArray<TerrainLayer> layers = document.Terrain.Layers;
            CheckLayer(layers, layer);
            return document with { Terrain = document.Terrain with { Layers = layers.SetItem(layer, update(layers[layer])) } };
        }

        /// <summary>
        /// Gives the first unused stratum textures and an empty mask.
        /// </summary>
        /// <param name="added">The stratum's layer index.</param>
        /// <exception cref="InvalidOperationException">Every stratum is used.</exception>
        public static MapDocument AddStratum(this MapDocument document, string albedoPath, string normalPath, float scale, out int added)
        {
            Terrain terrain = document.Terrain;
            int count = terrain.Layers.Length - 2;
            added = Enumerable.Range(1, count).FirstOrDefault(layer => !terrain.Layers[layer].IsUsed());
            if (added == 0)
            {
                throw new InvalidOperationException($"All {count} strata of a version {terrain.ScmapVersion} map are used: remove one first");
            }
            TerrainLayer layer = new TerrainLayer { AlbedoPath = albedoPath, AlbedoScale = scale, NormalPath = normalPath, NormalScale = scale };
            int fresh = added;
            // the stratum starts with an empty mask: a stack it had goes
            document = CoverageLayers.MoveStrata(document, stratum => stratum == fresh ? null : stratum);
            return document with
            {
                Terrain = terrain with
                {
                    Layers = terrain.Layers.SetItem(added, layer),
                    StratumMasks = terrain.StratumMasks with { Layers = terrain.StratumMasks.Layers.SetItem(added - 1, Empty(terrain.StratumMasks.Layers[added - 1])) },
                },
            };
        }

        /// <summary>
        /// Takes a stratum and its mask out; the strata after it move down one place with their
        /// masks (so the map looks the same apart from it), and the last stratum becomes unused.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The layer is no stratum.</exception>
        public static MapDocument RemoveStratum(this MapDocument document, int layer)
        {
            Terrain terrain = document.Terrain;
            CheckStratum(terrain, layer);
            int last = terrain.Layers.Length - 2;
            TerrainLayer unused = new TerrainLayer { AlbedoPath = "", NormalPath = "", AlbedoScale = 4, NormalScale = 4 };
            ImmutableArray<TerrainLayer> layers = terrain.Layers.RemoveAt(layer).Insert(last, unused);
            ImmutableArray<TiledBuffer<byte>> masks = terrain.StratumMasks.Layers;
            TiledBuffer<byte> removed = masks[layer - 1];
            masks = masks.RemoveAt(layer - 1).Insert(last - 1, Empty(removed));
            // the stacks move down with their strata
            document = CoverageLayers.MoveStrata(document, stratum => stratum == layer ? null : stratum > layer ? stratum - 1 : stratum);
            return document with { Terrain = terrain with { Layers = layers, StratumMasks = terrain.StratumMasks with { Layers = masks } } };
        }

        /// <summary>
        /// Puts a stratum in another place among the strata, with its mask; the ones between move
        /// one place to make room. A later stratum covers an earlier one.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Either layer is no stratum.</exception>
        public static MapDocument MoveStratum(this MapDocument document, int layer, int to)
        {
            Terrain terrain = document.Terrain;
            CheckStratum(terrain, layer);
            CheckStratum(terrain, to);
            if (layer == to)
            {
                return document;
            }
            TerrainLayer moved = terrain.Layers[layer];
            ImmutableArray<TiledBuffer<byte>> masks = terrain.StratumMasks.Layers;
            TiledBuffer<byte> mask = masks[layer - 1];
            document = CoverageLayers.MoveStrata(document, stratum =>
                stratum == layer ? to
                : layer < to && stratum > layer && stratum <= to ? stratum - 1
                : to < layer && stratum >= to && stratum < layer ? stratum + 1
                : stratum);
            return document with
            {
                Terrain = terrain with
                {
                    Layers = terrain.Layers.RemoveAt(layer).Insert(to, moved),
                    StratumMasks = terrain.StratumMasks with { Layers = masks.RemoveAt(layer - 1).Insert(to - 1, mask) },
                },
            };
        }

        private static TiledBuffer<byte> Empty(TiledBuffer<byte> like) => TiledBuffer<byte>.Create(like.Width, like.Height);

        private static void CheckLayer(ImmutableArray<TerrainLayer> layers, int layer)
        {
            if (layer < 0 || layer >= layers.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(layer), layer, $"The map has layers 0 to {layers.Length - 1}");
            }
        }

        private static void CheckStratum(Terrain terrain, int layer)
        {
            if (!terrain.IsStratum(layer))
            {
                throw new ArgumentOutOfRangeException(nameof(layer), layer, $"The strata are layers 1 to {terrain.Layers.Length - 2}; 0 is the lower layer and {terrain.Layers.Length - 1} the upper one");
            }
        }
    }
}
