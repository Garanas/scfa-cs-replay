using System.Numerics;

namespace FAForever.FileFormats.Map
{
    /// <summary>
    /// A route over one layer of the navigational mesh.
    /// </summary>
    /// <param name="Length">The length in ogrids.</param>
    /// <param name="Points">The centres of the ogrids it passes, from start to end.</param>
    public sealed record NavRoute(float Length, IReadOnlyList<Vector2> Points);

    /// <summary>
    /// The two origins closest to an ogrid along a layer, by index; -1 and infinity where fewer reach it.
    /// </summary>
    public readonly record struct NearestOrigins(int First, float FirstDistance, int Second, float SecondDistance)
    {
        public static NearestOrigins None { get; } = new NearestOrigins(-1, float.PositiveInfinity, -1, float.PositiveInfinity);
    }

    /// <summary>
    /// Distances and routes over a layer of the navigational mesh. They move from ogrid to ogrid in
    /// eight directions, a diagonal step only past two pathable ogrids, the rule by which the game links
    /// diagonal leaves (<c>GenerateDirectNeighbors</c> in <c>lua/sim/NavGenerator.lua</c>). The game's
    /// own pathfinding (<c>NavUtils.PathTo</c>) moves between the centres of its quadtree leaves, so
    /// its routes can be a little longer.
    /// </summary>
    public static class NavPaths
    {
        private const float Diagonal = 1.41421356f;

        /// <summary>
        /// The distance in ogrids from a position to every ogrid of the grid, row by row; infinity
        /// where units of this layer cannot get to from there.
        /// </summary>
        public static float[] DistancesFrom(NavGrid grid, Vector3 origin)
        {
            float[] distances = new float[grid.Cells.Length];
            Array.Fill(distances, float.PositiveInfinity);
            int start = CellOf(grid, origin);
            if (start < 0 || grid.Cells[start] <= 0)
            {
                return distances;
            }

            PriorityQueue<int, float> queue = new PriorityQueue<int, float>();
            distances[start] = 0;
            queue.Enqueue(start, 0);
            while (queue.TryDequeue(out int cell, out float distance))
            {
                if (distance > distances[cell])
                {
                    continue;
                }
                for (int direction = 0; direction < 8; direction++)
                {
                    int neighbor = Step(grid, cell, direction, out float step);
                    if (neighbor >= 0 && distance + step < distances[neighbor])
                    {
                        distances[neighbor] = distance + step;
                        queue.Enqueue(neighbor, distance + step);
                    }
                }
            }
            return distances;
        }

        /// <summary>
        /// For every ogrid, the two origins closest to it along the layer, in one search from all
        /// origins together: it costs about twice one search, where measuring from each origin
        /// apart would cost one search per origin. At an origin's own ogrid, the first is that origin
        /// and the second the closest other one. At most 32 origins.
        /// </summary>
        /// <remarks>
        /// Steps cost 1000 and 1414 (the diagonal), so the search takes the shortest distance first
        /// from a ring of buckets instead of a heap (Dial's algorithm): far quicker in the browser's
        /// WebAssembly interpreter. The rounding of the diagonal is 0.003%.
        /// </remarks>
        public static NearestOrigins[] NearestTwo(NavGrid grid, IReadOnlyList<Vector3> origins)
        {
            const int Straight = 1000;
            const int Slanted = 1414;
            const int Buckets = Slanted + 1;
            if (origins.Count > 32)
            {
                throw new ArgumentException("At most 32 origins", nameof(origins));
            }

            int[] cells = grid.Cells;
            int width = grid.Width;
            int height = grid.Height;
            int[] firstSource = new int[cells.Length];
            int[] secondSource = new int[cells.Length];
            int[] firstDistance = new int[cells.Length];
            int[] secondDistance = new int[cells.Length];
            Array.Fill(firstSource, -1);
            Array.Fill(secondSource, -1);

            // an entry is cell * 32 + origin, in the bucket of its distance modulo the ring's size
            List<int>[] ring = new List<int>[Buckets];
            for (int i = 0; i < ring.Length; i++)
            {
                ring[i] = [];
            }
            int pending = 0;
            for (int i = 0; i < origins.Count; i++)
            {
                int start = CellOf(grid, origins[i]);
                if (start >= 0 && cells[start] > 0)
                {
                    ring[0].Add(start * 32 + i);
                    pending++;
                }
            }

            for (int distance = 0; pending > 0; distance++)
            {
                List<int> bucket = ring[distance % Buckets];
                // steps cost at least 1000, so nothing is added to this bucket while it is emptied
                for (int e = 0; e < bucket.Count; e++)
                {
                    int cell = bucket[e] >> 5;
                    int origin = bucket[e] & 31;
                    if (firstSource[cell] < 0)
                    {
                        firstSource[cell] = origin;
                        firstDistance[cell] = distance;
                    }
                    else if (secondSource[cell] < 0 && firstSource[cell] != origin)
                    {
                        secondSource[cell] = origin;
                        secondDistance[cell] = distance;
                    }
                    else
                    {
                        continue;
                    }

                    int x = cell % width;
                    int z = cell / width;
                    for (int direction = 0; direction < 8; direction++)
                    {
                        int nx = x + StepX[direction];
                        int nz = z + StepZ[direction];
                        if ((uint)nx >= (uint)width || (uint)nz >= (uint)height)
                        {
                            continue;
                        }
                        int neighbor = nz * width + nx;
                        if (cells[neighbor] <= 0 || secondSource[neighbor] >= 0 || firstSource[neighbor] == origin
                            || (direction >= 4 && (cells[z * width + nx] <= 0 || cells[nz * width + x] <= 0)))
                        {
                            continue;
                        }
                        ring[(distance + (direction < 4 ? Straight : Slanted)) % Buckets].Add(neighbor * 32 + origin);
                        pending++;
                    }
                }
                pending -= bucket.Count;
                bucket.Clear();
            }

            NearestOrigins[] nearest = new NearestOrigins[cells.Length];
            for (int i = 0; i < nearest.Length; i++)
            {
                nearest[i] = new NearestOrigins(
                    firstSource[i], firstSource[i] < 0 ? float.PositiveInfinity : firstDistance[i] / (float)Straight,
                    secondSource[i], secondSource[i] < 0 ? float.PositiveInfinity : secondDistance[i] / (float)Straight);
            }
            return nearest;
        }

        /// <summary>
        /// The shortest route between two positions, or null when units of this layer cannot path
        /// from one to the other. An A* search: the octile distance to the end, which no route can
        /// beat, keeps it to a corridor around the route.
        /// </summary>
        public static NavRoute? FindRoute(NavGrid grid, Vector3 from, Vector3 to)
        {
            int start = CellOf(grid, from);
            int goal = CellOf(grid, to);
            if (start < 0 || goal < 0 || !grid.CanPathTo(from, to))
            {
                return null;
            }

            int width = grid.Width;
            int goalX = goal % width;
            int goalZ = goal / width;
            float Heuristic(int cell)
            {
                int dx = Math.Abs(cell % width - goalX);
                int dz = Math.Abs(cell / width - goalZ);
                return Math.Max(dx, dz) + (Diagonal - 1) * Math.Min(dx, dz);
            }

            float[] distances = new float[grid.Cells.Length];
            Array.Fill(distances, float.PositiveInfinity);
            int[] previous = new int[grid.Cells.Length];
            Array.Fill(previous, -1);
            PriorityQueue<int, float> queue = new PriorityQueue<int, float>();
            distances[start] = 0;
            queue.Enqueue(start, Heuristic(start));
            while (queue.TryDequeue(out int cell, out float estimate))
            {
                if (cell == goal)
                {
                    break;
                }
                float distance = distances[cell];
                if (estimate > distance + Heuristic(cell) + 1e-3f)
                {
                    continue;
                }
                for (int direction = 0; direction < 8; direction++)
                {
                    int neighbor = Step(grid, cell, direction, out float step);
                    if (neighbor >= 0 && distance + step < distances[neighbor])
                    {
                        distances[neighbor] = distance + step;
                        previous[neighbor] = cell;
                        queue.Enqueue(neighbor, distance + step + Heuristic(neighbor));
                    }
                }
            }

            if (float.IsPositiveInfinity(distances[goal]))
            {
                return null;
            }

            List<Vector2> points = [];
            for (int cell = goal; cell >= 0; cell = previous[cell])
            {
                points.Add(new Vector2(cell % width + 0.5f, cell / width + 0.5f));
            }
            points.Reverse();
            return new NavRoute(distances[goal], points);
        }

        private static int CellOf(NavGrid grid, Vector3 position)
        {
            int x = (int)MathF.Floor(position.X);
            int z = (int)MathF.Floor(position.Z);
            return (uint)x < (uint)grid.Width && (uint)z < (uint)grid.Height ? z * grid.Width + x : -1;
        }

        private static readonly int[] StepX = [1, -1, 0, 0, 1, 1, -1, -1];
        private static readonly int[] StepZ = [0, 0, 1, -1, 1, -1, 1, -1];

        /// <summary>
        /// The ogrid one step from <paramref name="cell"/> in one of eight directions (four straight,
        /// then four diagonal), or -1 when units of this layer cannot step onto it.
        /// </summary>
        private static int Step(NavGrid grid, int cell, int direction, out float length)
        {
            int width = grid.Width;
            int[] cells = grid.Cells;
            int x = cell % width;
            int z = cell / width;
            int nx = x + StepX[direction];
            int nz = z + StepZ[direction];
            length = direction < 4 ? 1 : Diagonal;
            if ((uint)nx >= (uint)width || (uint)nz >= (uint)grid.Height)
            {
                return -1;
            }

            int neighbor = nz * width + nx;
            if (cells[neighbor] <= 0 || (direction >= 4 && (cells[z * width + nx] <= 0 || cells[nz * width + x] <= 0)))
            {
                return -1;
            }
            return neighbor;
        }
    }
}
