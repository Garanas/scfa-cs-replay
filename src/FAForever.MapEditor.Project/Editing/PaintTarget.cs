using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// One of the water's masks (<see cref="Water"/>), half the map's size each.
    /// </summary>
    public enum WaterMask
    {
        Foam,
        Flatness,
        DepthBias,
    }

    /// <summary>
    /// A grid of bytes of the document that a brush paints: a terrain layer's mask, the terrain's
    /// types, one of the water's masks or a mask for tools. Targets are equal when they name the
    /// same grid, so an <see cref="EditTransaction"/> keeps one builder per target.
    /// </summary>
    public abstract record PaintTarget
    {
        /// <summary>
        /// What it is, for the history: <c>layer 3</c>, <c>terrain types</c>, <c>water foam</c>.
        /// </summary>
        public abstract string Title { get; }

        /// <summary>
        /// The grid in a document.
        /// </summary>
        /// <exception cref="ArgumentException">The document has no such grid.</exception>
        public abstract TiledBuffer<byte> Read(MapDocument document);

        /// <summary>
        /// The document with the grid replaced.
        /// </summary>
        public abstract MapDocument Write(MapDocument document, TiledBuffer<byte> grid);

        /// <summary>
        /// The mask of terrain layer <paramref name="layer"/>: 1 to 8, or 1 to 4 in version 53.
        /// </summary>
        public static PaintTarget Stratum(int layer) => new StratumTarget(layer);

        public static PaintTarget TerrainTypes { get; } = new TerrainTypesTarget();

        public static PaintTarget Water(WaterMask mask) => new WaterTarget(mask);

        /// <summary>
        /// The mask for tools of that name (<see cref="MapDocument.Masks"/>).
        /// </summary>
        public static PaintTarget Mask(string name) => new MaskTarget(name);

        private sealed record StratumTarget(int Layer) : PaintTarget
        {
            public override string Title => $"layer {Layer}";

            public override TiledBuffer<byte> Read(MapDocument document)
            {
                StratumMasks masks = document.Terrain.StratumMasks;
                return Layer >= 1 && Layer <= masks.Layers.Length
                    ? masks.Layers[Layer - 1]
                    : throw new ArgumentOutOfRangeException(nameof(Layer), $"Layer {Layer} has no mask; layers 1 to {masks.Layers.Length} do");
            }

            public override MapDocument Write(MapDocument document, TiledBuffer<byte> grid)
            {
                StratumMasks masks = document.Terrain.StratumMasks;
                return document with { Terrain = document.Terrain with { StratumMasks = masks with { Layers = masks.Layers.SetItem(Layer - 1, grid) } } };
            }
        }

        private sealed record TerrainTypesTarget : PaintTarget
        {
            public override string Title => "terrain types";

            public override TiledBuffer<byte> Read(MapDocument document) => document.Terrain.Types;

            public override MapDocument Write(MapDocument document, TiledBuffer<byte> grid) =>
                document with { Terrain = document.Terrain with { Types = grid } };
        }

        private sealed record WaterTarget(WaterMask Kind) : PaintTarget
        {
            public override string Title => Kind switch
            {
                WaterMask.Foam => "water foam",
                WaterMask.Flatness => "water flatness",
                _ => "water depth bias",
            };

            public override TiledBuffer<byte> Read(MapDocument document) => Kind switch
            {
                WaterMask.Foam => document.Water.FoamMask,
                WaterMask.Flatness => document.Water.FlatnessMask,
                _ => document.Water.DepthBiasMask,
            };

            public override MapDocument Write(MapDocument document, TiledBuffer<byte> grid) => document with
            {
                Water = Kind switch
                {
                    WaterMask.Foam => document.Water with { FoamMask = grid },
                    WaterMask.Flatness => document.Water with { FlatnessMask = grid },
                    _ => document.Water with { DepthBiasMask = grid },
                },
            };
        }

        private sealed record MaskTarget(string Name) : PaintTarget
        {
            public override string Title => $"mask {Name}";

            public override TiledBuffer<byte> Read(MapDocument document) =>
                document.Masks.TryGetValue(Name, out TiledBuffer<byte>? mask) ? mask : throw new ArgumentException($"The map has no mask '{Name}'");

            public override MapDocument Write(MapDocument document, TiledBuffer<byte> grid) => document with { Masks = document.Masks.SetItem(Name, grid) };
        }
    }
}
