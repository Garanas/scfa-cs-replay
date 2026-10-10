namespace FAForever.MapEditor.Project.Generation
{
    /// <summary>
    /// A skeleton traced into a graph (<see cref="PathSkeleton.Trace"/>): its nodes (junctions and
    /// dead ends, and one cell of every ring without a junction) and the chains of cells between them.
    /// </summary>
    /// <param name="Nodes">Per node its cell (an index into the grid) and whether it is a dead end.</param>
    /// <param name="Chains">Per chain the nodes at its ends (the same node for a ring) and its cells in
    /// order from the first end to the second, both ends' cells included.</param>
    public sealed record SkeletonGraph(IReadOnlyList<(int Cell, bool End)> Nodes, IReadOnlyList<(int A, int B, int[] Cells)> Chains);

    /// <summary>
    /// The skeletons of the regions of a grid of labels: the middle line of every corridor, one cell
    /// wide, with the shape of each region kept (a region with a hole keeps a ring around it).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Thin"/> is a distance-ordered homotopic thinning: cells are peeled off in the order
    /// of their distance from the region's edge (a chamfer distance, 3 a step and 4 a diagonal), and a
    /// cell goes only when it is simple (taking it away splits nothing and opens no hole: Yokoi's
    /// connectivity number for 8-connected regions is 1) and is no end of a line (it has two or more
    /// neighbours left). What stays runs along the middle. <see cref="Prune"/> takes away the short
    /// spurs a ragged edge leaves, and <see cref="Trace"/> turns the cells into nodes and chains.
    /// </para>
    /// <para>
    /// A cell's neighbours are those of the same label: two regions that touch stay apart.
    /// </para>
    /// </remarks>
    public static class PathSkeleton
    {
        // the eight neighbours, counter-clockwise from east; rows grow downward
        private static readonly int[] DX = [1, 1, 0, -1, -1, -1, 0, 1];
        private static readonly int[] DZ = [0, -1, -1, -1, 0, 1, 1, 1];

        /// <summary>
        /// Per pattern of neighbours (bit k for neighbour k) whether the cell between them is simple.
        /// </summary>
        private static readonly bool[] Simple = MakeSimple();

        private static bool[] MakeSimple()
        {
            bool[] simple = new bool[256];
            for (int mask = 0; mask < 256; mask++)
            {
                // Yokoi's connectivity number for 8-connected regions, over the 4-neighbours (even k)
                int number = 0;
                for (int k = 0; k < 8; k += 2)
                {
                    int a = 1 - ((mask >> k) & 1);
                    int b = 1 - ((mask >> ((k + 1) & 7)) & 1);
                    int c = 1 - ((mask >> ((k + 2) & 7)) & 1);
                    number += a - a * b * c;
                }
                simple[mask] = number == 1;
            }
            return simple;
        }

        /// <summary>
        /// Per cell how far it lies from a cell of another label, a blocked cell or the grid's edge: a
        /// chamfer distance, 3 a step and 4 a diagonal; 0 where blocked.
        /// </summary>
        public static int[] Distance(int[] labels, int width, int height)
        {
            int[] distance = new int[width * height];
            const int Far = int.MaxValue / 2;
            for (int z = 0; z < height; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = z * width + x;
                    int label = labels[i];
                    if (label <= 0)
                    {
                        continue;
                    }
                    int best = Far;
                    best = Math.Min(best, x == 0 || labels[i - 1] != label ? 3 : distance[i - 1] + 3);
                    best = Math.Min(best, z == 0 || labels[i - width] != label ? 3 : distance[i - width] + 3);
                    best = Math.Min(best, x == 0 || z == 0 || labels[i - width - 1] != label ? 4 : distance[i - width - 1] + 4);
                    best = Math.Min(best, x == width - 1 || z == 0 || labels[i - width + 1] != label ? 4 : distance[i - width + 1] + 4);
                    distance[i] = best;
                }
            }
            for (int z = height - 1; z >= 0; z--)
            {
                for (int x = width - 1; x >= 0; x--)
                {
                    int i = z * width + x;
                    int label = labels[i];
                    if (label <= 0)
                    {
                        continue;
                    }
                    int best = distance[i];
                    best = Math.Min(best, x == width - 1 || labels[i + 1] != label ? 3 : distance[i + 1] + 3);
                    best = Math.Min(best, z == height - 1 || labels[i + width] != label ? 3 : distance[i + width] + 3);
                    best = Math.Min(best, x == width - 1 || z == height - 1 || labels[i + width + 1] != label ? 4 : distance[i + width + 1] + 4);
                    best = Math.Min(best, x == 0 || z == height - 1 || labels[i + width - 1] != label ? 4 : distance[i + width - 1] + 4);
                    distance[i] = best;
                }
            }
            return distance;
        }

        /// <summary>
        /// Thins every region of the grid to its skeleton (<see cref="PathSkeleton"/> says how).
        /// </summary>
        /// <param name="labels">The label of each cell, row by row; 0 or less where blocked.</param>
        /// <returns>Per cell whether it is of the skeleton.</returns>
        public static bool[] Thin(int[] labels, int width, int height)
        {
            int count = width * height;
            int[] distance = Distance(labels, width, height);
            bool[] kept = new bool[count];
            bool[] queued = new bool[count];
            List<List<int>?> buckets = [];
            void Enqueue(int cell, int key)
            {
                while (buckets.Count <= key)
                {
                    buckets.Add(null);
                }
                (buckets[key] ??= []).Add(cell);
                queued[cell] = true;
            }

            for (int z = 0; z < height; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = z * width + x;
                    int label = labels[i];
                    if (label <= 0)
                    {
                        continue;
                    }
                    kept[i] = true;
                    // the cells at the edge first
                    if (x == 0 || z == 0 || x == width - 1 || z == height - 1
                        || labels[i - 1] != label || labels[i + 1] != label || labels[i - width] != label || labels[i + width] != label)
                    {
                        Enqueue(i, distance[i]);
                    }
                }
            }

            for (int key = 0; key < buckets.Count; key++)
            {
                List<int>? bucket = buckets[key];
                if (bucket is null)
                {
                    continue;
                }
                // the bucket may grow while it is worked through: a neighbour nearer the edge comes back at this key
                for (int at = 0; at < bucket.Count; at++)
                {
                    int cell = bucket[at];
                    queued[cell] = false;
                    if (!kept[cell])
                    {
                        continue;
                    }
                    int mask = Neighbours(kept, labels, width, height, cell);
                    if (!Simple[mask] || System.Numerics.BitOperations.PopCount((uint)mask) < 2)
                    {
                        continue;
                    }
                    kept[cell] = false;
                    int x = cell % width;
                    int z = cell / width;
                    for (int k = 0; k < 8; k++)
                    {
                        if ((mask & (1 << k)) == 0)
                        {
                            continue;
                        }
                        int next = (z + DZ[k]) * width + x + DX[k];
                        if (!queued[next])
                        {
                            Enqueue(next, Math.Max(distance[next], key));
                        }
                    }
                }
                buckets[key] = null;
            }

            // last, the corners of staircases the order left: simple cells that end no line
            for (bool changed = true; changed;)
            {
                changed = false;
                for (int cell = 0; cell < count; cell++)
                {
                    if (!kept[cell])
                    {
                        continue;
                    }
                    int mask = Neighbours(kept, labels, width, height, cell);
                    if (Simple[mask] && System.Numerics.BitOperations.PopCount((uint)mask) >= 2)
                    {
                        kept[cell] = false;
                        changed = true;
                    }
                }
            }
            return kept;
        }

        /// <summary>
        /// The pattern of a cell's neighbours that are of the skeleton and of its label: bit k for
        /// neighbour k (counter-clockwise from east).
        /// </summary>
        private static int Neighbours(bool[] kept, int[] labels, int width, int height, int cell)
        {
            int x = cell % width;
            int z = cell / width;
            int label = labels[cell];
            int mask = 0;
            for (int k = 0; k < 8; k++)
            {
                int nx = x + DX[k];
                int nz = z + DZ[k];
                if ((uint)nx < (uint)width && (uint)nz < (uint)height)
                {
                    int next = nz * width + nx;
                    if (kept[next] && labels[next] == label)
                    {
                        mask |= 1 << k;
                    }
                }
            }
            return mask;
        }

        private static int Degree(bool[] kept, int[] labels, int width, int height, int cell) =>
            System.Numerics.BitOperations.PopCount((uint)Neighbours(kept, labels, width, height, cell));

        /// <summary>
        /// Takes away the spurs of a skeleton: a branch from a dead end to a junction that is shorter
        /// than <paramref name="shortest"/> cells goes (the junction stays). A line with two dead ends
        /// and no junction stays whole, so every region keeps its skeleton.
        /// </summary>
        public static void Prune(bool[] kept, int[] labels, int width, int height, int shortest)
        {
            if (shortest <= 1)
            {
                return;
            }
            List<int> branch = [];
            for (int round = 0; round < 3; round++)
            {
                bool pruned = false;
                for (int start = 0; start < kept.Length; start++)
                {
                    if (!kept[start] || Degree(kept, labels, width, height, start) != 1)
                    {
                        continue;
                    }
                    branch.Clear();
                    branch.Add(start);
                    int previous = -1;
                    int current = start;
                    bool atJunction = false;
                    while (branch.Count < shortest)
                    {
                        int mask = Neighbours(kept, labels, width, height, current);
                        int next = -1;
                        int choices = 0;
                        for (int k = 0; k < 8; k++)
                        {
                            if ((mask & (1 << k)) == 0)
                            {
                                continue;
                            }
                            int cell = (current / width + DZ[k]) * width + current % width + DX[k];
                            if (cell != previous && !(branch.Count >= 2 && cell == branch[^2]))
                            {
                                next = cell;
                                choices++;
                            }
                        }
                        if (choices != 1)
                        {
                            // a dead end (the whole line) or a junction at this cell
                            atJunction = choices > 1 && current != start;
                            break;
                        }
                        if (Degree(kept, labels, width, height, next) >= 3)
                        {
                            atJunction = true;
                            break;
                        }
                        previous = current;
                        current = next;
                        branch.Add(current);
                    }
                    if (atJunction && branch.Count < shortest)
                    {
                        if (Degree(kept, labels, width, height, current) >= 3 && current != start)
                        {
                            // the walk stopped on the junction itself: it stays
                            branch.RemoveAt(branch.Count - 1);
                        }
                        foreach (int cell in branch)
                        {
                            kept[cell] = false;
                        }
                        pruned |= branch.Count > 0;
                    }
                }
                if (!pruned)
                {
                    return;
                }
            }
        }

        /// <summary>
        /// Turns a skeleton into nodes and chains: a node at each junction (touching junction cells
        /// are one, at the cell nearest their middle) and each dead end, one on every ring without a
        /// junction, and a chain of cells between nodes that follow each other.
        /// </summary>
        public static SkeletonGraph Trace(bool[] kept, int[] labels, int width, int height)
        {
            int count = width * height;
            int[] node = new int[count];
            Array.Fill(node, -1);
            bool[] visited = new bool[count];
            List<(int Cell, bool End)> nodes = [];
            List<(int, int, int[])> chains = [];
            List<int> cluster = [];
            Stack<int> stack = new Stack<int>();

            // junctions, each group of touching ones a node
            for (int cell = 0; cell < count; cell++)
            {
                if (!kept[cell] || node[cell] >= 0 || Degree(kept, labels, width, height, cell) < 3)
                {
                    continue;
                }
                int index = nodes.Count;
                cluster.Clear();
                stack.Push(cell);
                node[cell] = index;
                while (stack.TryPop(out int at))
                {
                    cluster.Add(at);
                    int mask = Neighbours(kept, labels, width, height, at);
                    for (int k = 0; k < 8; k++)
                    {
                        if ((mask & (1 << k)) != 0)
                        {
                            int next = (at / width + DZ[k]) * width + at % width + DX[k];
                            if (node[next] < 0 && Degree(kept, labels, width, height, next) >= 3)
                            {
                                node[next] = index;
                                stack.Push(next);
                            }
                        }
                    }
                }
                double mx = cluster.Average(c => c % width);
                double mz = cluster.Average(c => c / width);
                int middle = cluster.MinBy(c => (c % width - mx) * (c % width - mx) + (c / width - mz) * (c / width - mz));
                nodes.Add((middle, false));
            }
            // dead ends and single cells
            for (int cell = 0; cell < count; cell++)
            {
                if (kept[cell] && node[cell] < 0 && Degree(kept, labels, width, height, cell) <= 1)
                {
                    node[cell] = nodes.Count;
                    nodes.Add((cell, true));
                }
            }

            HashSet<(int, int)> direct = [];
            void TraceFrom(int from)
            {
                int start = node[from];
                int mask = Neighbours(kept, labels, width, height, from);
                for (int k = 0; k < 8; k++)
                {
                    if ((mask & (1 << k)) == 0)
                    {
                        continue;
                    }
                    int first = (from / width + DZ[k]) * width + from % width + DX[k];
                    if (node[first] >= 0)
                    {
                        // two nodes side by side
                        int other = node[first];
                        if (other != start && direct.Add((Math.Min(start, other), Math.Max(start, other))))
                        {
                            chains.Add((start, other, [nodes[start].Cell, nodes[other].Cell]));
                        }
                        continue;
                    }
                    if (visited[first])
                    {
                        continue;
                    }
                    List<int> cells = [nodes[start].Cell, first];
                    visited[first] = true;
                    int previous = from;
                    int current = first;
                    int end = -1;
                    bool loopsBack = false;
                    while (end < 0)
                    {
                        int around = Neighbours(kept, labels, width, height, current);
                        int next = -1;
                        for (int j = 0; j < 8 && end < 0; j++)
                        {
                            if ((around & (1 << j)) == 0)
                            {
                                continue;
                            }
                            int cell = (current / width + DZ[j]) * width + current % width + DX[j];
                            if (cell == previous)
                            {
                                continue;
                            }
                            if (node[cell] >= 0)
                            {
                                // back to where it began only after going round a ring
                                if (node[cell] != start || cells.Count > 3)
                                {
                                    end = node[cell];
                                }
                                else
                                {
                                    loopsBack = true;
                                }
                            }
                            else if (!visited[cell] && next < 0)
                            {
                                next = cell;
                            }
                        }
                        if (end >= 0)
                        {
                            break;
                        }
                        if (next < 0 && loopsBack)
                        {
                            // a few cells beside the node it began at: no chain
                            break;
                        }
                        if (next < 0)
                        {
                            // a chain that runs into one already traced: it ends at a node of its own
                            end = nodes.Count;
                            node[current] = end;
                            nodes.Add((current, true));
                            cells.RemoveAt(cells.Count - 1);
                            break;
                        }
                        visited[next] = true;
                        cells.Add(next);
                        previous = current;
                        current = next;
                    }
                    if (end < 0)
                    {
                        continue;
                    }
                    cells.Add(nodes[end].Cell);
                    chains.Add((start, end, [.. cells]));
                }
            }

            for (int i = 0; i < nodes.Count; i++)
            {
                // every cell of a junction's group starts chains
                int at = nodes[i].Cell;
                TraceFrom(at);
            }
            for (int cell = 0; cell < count; cell++)
            {
                if (kept[cell] && node[cell] >= 0 && nodes[node[cell]].Cell != cell)
                {
                    TraceFrom(cell);
                }
            }
            // rings without a junction: a node on each
            for (int cell = 0; cell < count; cell++)
            {
                if (kept[cell] && node[cell] < 0 && !visited[cell])
                {
                    node[cell] = nodes.Count;
                    nodes.Add((cell, false));
                    TraceFrom(cell);
                }
            }
            return new SkeletonGraph(nodes, chains);
        }
    }
}
