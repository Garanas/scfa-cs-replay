using System.Collections.Immutable;

namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// How much of each terrain layer covers the terrain, one value per pixel. All masks have the
    /// same size, which need not be the map's size. The mask of layer 1 is at index 0.
    /// </summary>
    /// <remarks>
    /// Layer 0 covers the whole terrain and the last layer is drawn over everything, so neither has
    /// a mask: the layers in between have one each, eight from version 56 on, four before.
    /// </remarks>
    public sealed record StratumMasks
    {
        public required ImmutableArray<TiledBuffer<byte>> Layers { get; init; }

        public int Width => Layers[0].Width;

        public int Height => Layers[0].Height;
    }
}
