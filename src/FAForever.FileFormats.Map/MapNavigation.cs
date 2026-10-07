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

        /// <summary>
        /// The first layer of land, amphibious and hover over which units can path between all of
        /// these positions, e.g. the start positions; null when only air units can.
        /// </summary>
        public NavLayer? FindConnectingLayer(IReadOnlyList<Vector3> positions)
        {
            foreach (NavLayer layer in new[] { NavLayer.Land, NavLayer.Amphibious, NavLayer.Hover })
            {
                NavGrid grid = this[layer];
                NavLabel? first = positions.Count > 0 ? grid.GetLabel(positions[0]) : null;
                if (first is not null && positions.All(position => grid.GetLabel(position) == first))
                {
                    return layer;
                }
            }
            return null;
        }
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
        /// The grid in blocks of <paramref name="block"/> by <paramref name="block"/> ogrids: a block has
        /// the label its ogrids share, and none when they are mixed, as the game's quadtrees treat
        /// blocks of their compression threshold. Positions on it are positions on the map divided by
        /// the block size; <see cref="Labels"/> stay those of this grid.
        /// </summary>
        public NavGrid Coarsen(int block)
        {
            if (block <= 1)
            {
                return this;
            }

            int width = (Width + block - 1) / block;
            int height = (Height + block - 1) / block;
            int[] cells = new int[width * height];
            for (int bz = 0; bz < height; bz++)
            {
                for (int bx = 0; bx < width; bx++)
                {
                    int label = Cells[Math.Min(bz * block, Height - 1) * Width + Math.Min(bx * block, Width - 1)];
                    for (int z = bz * block; z < Math.Min((bz + 1) * block, Height) && label > 0; z++)
                    {
                        for (int x = bx * block; x < Math.Min((bx + 1) * block, Width); x++)
                        {
                            if (Cells[z * Width + x] != label)
                            {
                                label = -1;
                                break;
                            }
                        }
                    }
                    cells[bz * width + bx] = label;
                }
            }
            return new NavGrid(Layer, width, height, cells, Labels);
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
