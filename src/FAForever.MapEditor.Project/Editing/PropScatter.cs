using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// A prop of a palette and how often it is chosen against the others (user story P-2).
    /// </summary>
    /// <param name="Blueprint">The blueprint's game path, e.g. <c>/env/evergreen/props/trees/pine06_prop.bp</c>.</param>
    /// <param name="Weight">Chosen in proportion to it; 0 or less never.</param>
    public sealed record PaletteEntry(string Blueprint, double Weight);

    /// <summary>
    /// Where props may be painted (user story P-3): a place the brush reaches is asked first.
    /// </summary>
    public interface IPlacementCondition
    {
        bool Allows(float x, float z);
    }

    /// <summary>
    /// Allows a place where a mask (a stratum's, later a mask layer's of user story K-1) is at least
    /// a threshold, or, <see cref="Inverted"/>, where it is below it. A mask need not have the map's
    /// size; a place reads the pixel under it.
    /// </summary>
    /// <param name="Threshold">From 0 (anything) to 1 (fully covered).</param>
    public sealed record MaskCondition(TiledBuffer<byte> Mask, int MapWidth, int MapHeight, float Threshold, bool Inverted = false) : IPlacementCondition
    {
        public bool Allows(float x, float z)
        {
            int px = Math.Clamp((int)(x * Mask.Width / MapWidth), 0, Mask.Width - 1);
            int pz = Math.Clamp((int)(z * Mask.Height / MapHeight), 0, Mask.Height - 1);
            bool covered = Mask[px, pz] >= Threshold * 255 && Mask[px, pz] > 0;
            return covered != Inverted;
        }
    }

    /// <summary>
    /// The props of a map by place, to find those near a place fast: a grid of cells of a few
    /// ogrids. A brush stroke makes one when it starts and keeps it up to date with what it adds and
    /// removes.
    /// </summary>
    public sealed class PropGrid
    {
        private const float CellSize = 4;

        private readonly Dictionary<(int X, int Z), List<(int Id, float X, float Z, string Blueprint)>> _cells = [];

        public PropGrid(IEnumerable<Prop> props)
        {
            foreach (Prop prop in props)
            {
                Add(prop);
            }
        }

        public int Count { get; private set; }

        public void Add(Prop prop)
        {
            (int, int) cell = Cell(prop.Position.X, prop.Position.Z);
            if (!_cells.TryGetValue(cell, out List<(int, float, float, string)>? list))
            {
                list = [];
                _cells[cell] = list;
            }
            list.Add((prop.Id, prop.Position.X, prop.Position.Z, prop.Blueprint));
            Count++;
        }

        public void Remove(int id, float x, float z)
        {
            if (_cells.TryGetValue(Cell(x, z), out List<(int Id, float X, float Z, string Blueprint)>? list))
            {
                Count -= list.RemoveAll(entry => entry.Id == id);
            }
        }

        /// <summary>
        /// The props within a distance of a place: id, place and blueprint. Take them into a list
        /// before removing any.
        /// </summary>
        public IEnumerable<(int Id, float X, float Z, string Blueprint)> Near(float x, float z, float distance)
        {
            (int x0, int z0) = Cell(x - distance, z - distance);
            (int x1, int z1) = Cell(x + distance, z + distance);
            float reach = distance * distance;
            for (int cz = z0; cz <= z1; cz++)
            {
                for (int cx = x0; cx <= x1; cx++)
                {
                    if (!_cells.TryGetValue((cx, cz), out List<(int Id, float X, float Z, string Blueprint)>? list))
                    {
                        continue;
                    }
                    foreach ((int Id, float X, float Z, string Blueprint) entry in list)
                    {
                        float dx = entry.X - x;
                        float dz = entry.Z - z;
                        if (dx * dx + dz * dz <= reach)
                        {
                            yield return entry;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Whether a prop lies closer to a place than a distance.
        /// </summary>
        public bool AnyWithin(float x, float z, float distance) => distance > 0 && Near(x, z, distance).Any();

        private static (int, int) Cell(float x, float z) => ((int)MathF.Floor(x / CellSize), (int)MathF.Floor(z / CellSize));
    }

    /// <summary>
    /// Paints props with a brush (user story P-2): each dab tries places at random in its circle and
    /// puts a prop of the palette (by weight) on each one that is far enough from every other prop,
    /// inside the map, and allowed by the conditions (user story P-3). Repeated strokes over one
    /// place fill it up to the spacing and no further.
    /// </summary>
    public sealed class PropScatter
    {
        private readonly PropGrid _grid;
        private readonly Random _random;
        private readonly PaletteEntry[] _palette;
        private readonly double _totalWeight;

        /// <param name="props">The props the map has, which new ones keep their distance from.</param>
        /// <exception cref="ArgumentException">The palette has no prop with a weight above 0.</exception>
        public PropScatter(IEnumerable<Prop> props, IEnumerable<PaletteEntry> palette, PropPlacement placement, Random random)
        {
            _palette = [.. palette.Where(entry => entry.Weight > 0 && !string.IsNullOrWhiteSpace(entry.Blueprint))];
            if (_palette.Length == 0)
            {
                throw new ArgumentException("The palette has no props to paint: add props with a weight above 0", nameof(palette));
            }
            _totalWeight = _palette.Sum(entry => entry.Weight);
            _grid = new PropGrid(props);
            _random = random;
            Placement = placement;
        }

        public PropPlacement Placement { get; }

        /// <summary>
        /// Props per 100 square ogrids (a square of 10 by 10) that a stroke aims at.
        /// </summary>
        public float Density { get; init; } = 2;

        /// <summary>
        /// The least distance between two props' centres, in ogrids.
        /// </summary>
        public float Spacing { get; init; } = 2;

        /// <summary>
        /// Where props may go; empty for anywhere on the map.
        /// </summary>
        public IReadOnlyList<IPlacementCondition> Conditions { get; init; } = [];

        /// <summary>
        /// How many places a dab over <paramref name="area"/> square ogrids tries for the density:
        /// the whole part, and the rest of a part at random.
        /// </summary>
        public int Tries(float area)
        {
            double wanted = Density * area / 100;
            int whole = (int)wanted;
            return whole + (_random.NextDouble() < wanted - whole ? 1 : 0);
        }

        /// <summary>
        /// The props of one dab in a circle at a place, in ogrids: up to <paramref name="tries"/>.
        /// They count for the spacing of the next dabs.
        /// </summary>
        public List<Prop> Dab(Heightmap heights, float x, float z, float radius, int tries)
        {
            List<Prop> placed = [];
            for (int i = 0; i < tries; i++)
            {
                // a place spread evenly over the circle
                double angle = _random.NextDouble() * 2 * Math.PI;
                double distance = radius * Math.Sqrt(_random.NextDouble());
                float px = x + (float)(Math.Cos(angle) * distance);
                float pz = z + (float)(Math.Sin(angle) * distance);
                if (px < 0 || pz < 0 || px > heights.Width || pz > heights.Height
                    || _grid.AnyWithin(px, pz, Spacing)
                    || !Conditions.All(condition => condition.Allows(px, pz)))
                {
                    continue;
                }
                Prop prop = Placement.Make(Choose(), px, pz, heights, _random);
                placed.Add(prop);
                _grid.Add(prop);
            }
            return placed;
        }

        private string Choose()
        {
            double pick = _random.NextDouble() * _totalWeight;
            foreach (PaletteEntry entry in _palette)
            {
                pick -= entry.Weight;
                if (pick < 0)
                {
                    return entry.Blueprint;
                }
            }
            return _palette[^1].Blueprint;
        }
    }
}
