using System.Numerics;

namespace FAForever.FileFormats.Map
{
    /// <summary>
    /// The layers of the navigational mesh, named as in <c>lua/shared/NavGenerator.lua</c>. Air is
    /// left out: every ogrid is pathable for it.
    /// </summary>
    public enum NavLayer
    {
        Land,
        Water,
        Hover,
        Amphibious,
    }

    /// <summary>
    /// The navigational mesh of a map: per layer, which ogrids units can path over and which
    /// regions (labels) they form. Generate it with <see cref="NavGenerator"/>.
    /// </summary>
    /// <param name="HasWater">Whether any part of the map is under water. Without water the hover and
    /// amphibious layers are the land layer, as in the game.</param>
    /// <param name="CompressionThreshold">The size in ogrids below which the game stops subdividing
    /// its quadtrees: 1, 2 from 20 km and 4 from 40 km.</param>
    public sealed record MapNavigation(bool HasWater, int CompressionThreshold, NavGrid Land, NavGrid Water, NavGrid Hover, NavGrid Amphibious)
    {
        public NavGrid this[NavLayer layer] => layer switch
        {
            NavLayer.Land => Land,
            NavLayer.Water => Water,
            NavLayer.Hover => Hover,
            NavLayer.Amphibious => Amphibious,
            _ => throw new ArgumentOutOfRangeException(nameof(layer)),
        };
    }

    /// <summary>
    /// One layer of the navigational mesh: the label of every ogrid, row by row
    /// (<see cref="Width"/> by <see cref="Height"/>), and the labels themselves.
    /// </summary>
    /// <param name="Cells">The label of each ogrid; -1 where units of this layer cannot go.</param>
    /// <param name="Labels">The regions, by id: <c>Labels[id - 1]</c>.</param>
    public sealed record NavGrid(NavLayer Layer, int Width, int Height, int[] Cells, IReadOnlyList<NavLabel> Labels)
    {
        /// <summary>
        /// The label of the ogrid at a position, or null where units of this layer cannot go or
        /// the position is outside the map.
        /// </summary>
        public NavLabel? GetLabel(Vector3 position)
        {
            int x = (int)MathF.Floor(position.X);
            int z = (int)MathF.Floor(position.Z);
            if ((uint)x >= (uint)Width || (uint)z >= (uint)Height)
            {
                return null;
            }
            int id = Cells[z * Width + x];
            return id > 0 ? Labels[id - 1] : null;
        }

        /// <summary>
        /// Whether units of this layer can path from one position to another.
        /// </summary>
        public bool CanPathTo(Vector3 origin, Vector3 destination) =>
            GetLabel(origin) is { } label && label == GetLabel(destination);
    }

    /// <summary>
    /// A region of a layer that units can path through, with the resource markers in it
    /// (<c>NavLabelMetadata</c> in <c>lua/sim/NavGenerator.lua</c>).
    /// </summary>
    /// <param name="Id">The label's number in its layer, from 1 in the order of the ogrids; the game
    /// numbers its labels in another order.</param>
    /// <param name="Cells">The number of ogrids.</param>
    public sealed record NavLabel(int Id, NavLayer Layer, int Cells, IReadOnlyList<MapMarker> Extractors, IReadOnlyList<MapMarker> Hydrocarbons)
    {
        /// <summary>
        /// The area in square kilometres; an ogrid is 20 by 20 metres.
        /// </summary>
        public double Area => Cells * NavGenerator.OGridSquaredToKMSquared;
    }
}
