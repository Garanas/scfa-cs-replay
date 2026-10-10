using System.Collections.Immutable;
using System.Numerics;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Generation
{
    /// <summary>
    /// What to generate path networks for (user story R-3): the layers, how far apart their nodes
    /// lie, and whether the layers' own nodes go.
    /// </summary>
    public sealed record PathNetworkSettings
    {
        /// <summary>
        /// The layers to generate, in order; every layer unless told.
        /// </summary>
        public ImmutableArray<PathLayer> Layers { get; init; } = [PathLayer.Land, PathLayer.Water, PathLayer.Amphibious, PathLayer.Air];

        /// <summary>
        /// About how far apart the nodes of land, water and amphibious networks lie, in ogrids; null
        /// for the map's size (<see cref="PathNetworks.DefaultSpacing"/>).
        /// </summary>
        public float? Spacing { get; init; }

        /// <summary>
        /// How far apart the nodes of the air network lie, in ogrids; null for twice the spacing.
        /// </summary>
        public float? AirSpacing { get; init; }

        /// <summary>
        /// Whether a layer's own nodes are removed first. Otherwise they stay: a part of the
        /// passable ground that holds one of them takes it as its node, and new nodes are only added
        /// where none are.
        /// </summary>
        public bool Replace { get; init; }
    }

    /// <summary>
    /// A node of a generated network: a new one at a place, or a node the map has
    /// (<paramref name="ExistingId"/>) that the network links to.
    /// </summary>
    public sealed record GeneratedPathNode(float X, float Z, int? ExistingId = null);

    /// <summary>
    /// The network generated for one layer: its nodes and the links between them (indices into
    /// <see cref="Nodes"/>, each pair once).
    /// </summary>
    /// <param name="Spacing">How far apart its nodes were placed, in ogrids.</param>
    /// <param name="Replace">Whether the layer's own nodes go when it is applied.</param>
    public sealed record PathLayerNetwork(PathLayer Layer, float Spacing, bool Replace, ImmutableArray<GeneratedPathNode> Nodes, ImmutableArray<(int A, int B)> Links)
    {
        /// <summary>
        /// How many of the nodes are new.
        /// </summary>
        public int NewNodes => Nodes.Count(node => node.ExistingId is null);
    }

    /// <summary>
    /// The path networks generated for some layers (<see cref="PathNetworks.Generate"/>), to put into
    /// the map with <see cref="PathNetworks.Apply"/>.
    /// </summary>
    public sealed record PathNetwork(ImmutableArray<PathLayerNetwork> Layers)
    {
        public int Nodes => Layers.Sum(layer => layer.Nodes.Length);

        public int Links => Layers.Sum(layer => layer.Links.Length);

        public PathLayerNetwork? this[PathLayer layer] => Layers.FirstOrDefault(network => network.Layer == layer);
    }

    /// <summary>
    /// Path networks generated from walkability (user story R-3): the graphs of path nodes the AI of
    /// the game finds its way with (<c>Land Path Node</c>, <c>Water Path Node</c>,
    /// <c>Amphibious Path Node</c>, <c>Air Path Node</c>; read by <c>lua/AI/aiutilities.lua</c> and
    /// <c>lua/sim/MarkerUtilities.lua</c>, which link them by the names in <c>adjacentTo</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Land, water and amphibious networks come from the navigational mesh of their layer
    /// (<see cref="Walkability"/>; amphibious from the game's amphibious layer). The map is cut into
    /// squares of the spacing; in each square every part of the passable ground that hangs together
    /// (4-connected ogrids) gets a node, at its ogrid that lies furthest from blocked ground (up to a
    /// quarter of the spacing), nearest the square's centre. Two parts in squares side by side that
    /// touch are linked; when the straight line between their nodes would cross blocked ground (a
    /// cliff, the shore), nodes are put along the way through the two parts, so no link crosses
    /// ground the layer's units cannot walk. Squares diagonal to each other are linked where the line
    /// between them is walkable and no node of a square beside both links them already. Parts too
    /// small to matter (a sixteenth of a square) lose their node when it ends a path or only passes
    /// on to two nodes that see each other. Last, every region of the mesh is checked to be one
    /// network; two pieces of one region are joined along the shortest route between them.
    /// </para>
    /// <para>
    /// Air nodes lie on a regular grid over the playable area, each linked to the four beside it.
    /// Names follow the map editors (<c>LandPN</c>, <c>WaterPN</c>, <c>AmphPN</c>, <c>AirPN</c> with
    /// a number, as the FAF map editor's <c>GetPrefixByType</c>), going on from the highest number
    /// in use.
    /// </para>
    /// </remarks>
    public static class PathNetworks
    {
        /// <summary>
        /// The spacing for a map unless told: a sixteenth of its larger side, from 16 to 64 ogrids
        /// (32 on a map of 10 km).
        /// </summary>
        public static float DefaultSpacing(MapDocument document) => Math.Clamp(Math.Max(document.Width, document.Height) / 16f, 16, 64);

        /// <summary>
        /// How the map editors begin the names of a layer's nodes.
        /// </summary>
        public static string Prefix(PathLayer layer) => layer switch
        {
            PathLayer.Land => "LandPN",
            PathLayer.Water => "WaterPN",
            PathLayer.Amphibious => "AmphPN",
            _ => "AirPN",
        };

        /// <summary>
        /// The layer of the navigational mesh a layer of path nodes follows; null for air, which goes
        /// everywhere.
        /// </summary>
        public static NavLayer? NavLayerOf(PathLayer layer) => layer switch
        {
            PathLayer.Land => NavLayer.Land,
            PathLayer.Water => NavLayer.Water,
            PathLayer.Amphibious => NavLayer.Amphibious,
            _ => null,
        };

        /// <summary>
        /// Generates the networks of the layers the settings name from a version of the document and
        /// its walkability.
        /// </summary>
        public static PathNetwork Generate(MapDocument document, MapNavigation navigation, PathNetworkSettings settings)
        {
            float spacing = Math.Max(4, settings.Spacing ?? DefaultSpacing(document));
            float airSpacing = Math.Max(4, settings.AirSpacing ?? spacing * 2);
            ImmutableArray<PathLayerNetwork>.Builder layers = ImmutableArray.CreateBuilder<PathLayerNetwork>();
            foreach (PathLayer layer in settings.Layers.Distinct())
            {
                PathNode[] existing = settings.Replace ? [] : [.. document.Markers.PathNodes.Where(node => node.Layer == layer)];
                layers.Add(NavLayerOf(layer) is NavLayer nav
                    ? GenerateLayer(navigation[nav], layer, spacing, existing, settings.Replace)
                    : GenerateAir(document, airSpacing, existing, settings.Replace));
            }
            return new PathNetwork(layers.ToImmutable());
        }

        #region Ground layers

        /// <summary>
        /// The network of one layer of the navigational mesh (<see cref="PathNetworks"/> says how).
        /// </summary>
        /// <param name="existing">The layer's nodes that stay; each part of the ground that holds one takes the first as its node.</param>
        public static PathLayerNetwork GenerateLayer(NavGrid grid, PathLayer layer, float spacing, IReadOnlyList<PathNode> existing, bool replace)
        {
            LayerBuilder builder = new LayerBuilder(grid, (int)MathF.Round(spacing), existing);
            builder.Build();
            ImmutableArray<(int A, int B)> links = [.. builder.LinkPairs()];
            return new PathLayerNetwork(layer, spacing, replace, [.. builder.Nodes], links);
        }

        /// <summary>
        /// Whether units of the grid's layer walk the straight line from one place to another (in
        /// ogrids): every ogrid it passes through, also both beside a corner it passes exactly, is
        /// passable and of the region the line starts in.
        /// </summary>
        public static bool CanWalk(NavGrid grid, Vector2 from, Vector2 to)
        {
            int label = LabelAt(grid, from);
            if (label <= 0)
            {
                return false;
            }
            bool ok = true;
            Traverse(from, to, (x, z) =>
            {
                ok = LabelAt(grid, x, z) == label;
                return ok;
            });
            return ok;
        }

        private static int LabelAt(NavGrid grid, Vector2 place) => LabelAt(grid, (int)MathF.Floor(place.X), (int)MathF.Floor(place.Y));

        private static int LabelAt(NavGrid grid, int x, int z) =>
            (uint)x < (uint)grid.Width && (uint)z < (uint)grid.Height ? grid.Cells[z * grid.Width + x] : -1;

        /// <summary>
        /// Visits the ogrids a line passes through, in order, until <paramref name="visit"/> answers
        /// false (a voxel traversal: Amanatides and Woo). Where the line passes exactly through a
        /// corner, both ogrids beside it are visited.
        /// </summary>
        internal static void Traverse(Vector2 from, Vector2 to, Func<int, int, bool> visit)
        {
            int x = (int)MathF.Floor(from.X);
            int z = (int)MathF.Floor(from.Y);
            int endX = (int)MathF.Floor(to.X);
            int endZ = (int)MathF.Floor(to.Y);
            if (!visit(x, z))
            {
                return;
            }
            float dx = to.X - from.X;
            float dz = to.Y - from.Y;
            int stepX = Math.Sign(dx);
            int stepZ = Math.Sign(dz);
            float deltaX = stepX == 0 ? float.PositiveInfinity : MathF.Abs(1 / dx);
            float deltaZ = stepZ == 0 ? float.PositiveInfinity : MathF.Abs(1 / dz);
            float maxX = stepX == 0 ? float.PositiveInfinity : (stepX > 0 ? x + 1 - from.X : from.X - x) * deltaX;
            float maxZ = stepZ == 0 ? float.PositiveInfinity : (stepZ > 0 ? z + 1 - from.Y : from.Y - z) * deltaZ;
            int steps = Math.Abs(endX - x) + Math.Abs(endZ - z);
            for (int i = 0; i < steps * 2 + 2 && (x != endX || z != endZ); i++)
            {
                if (MathF.Abs(maxX - maxZ) < 1e-6f)
                {
                    // through a corner: both ogrids beside it, then the one across
                    if (!visit(x + stepX, z) || !visit(x, z + stepZ))
                    {
                        return;
                    }
                    x += stepX;
                    z += stepZ;
                    maxX += deltaX;
                    maxZ += deltaZ;
                }
                else if (maxX < maxZ)
                {
                    x += stepX;
                    maxX += deltaX;
                }
                else
                {
                    z += stepZ;
                    maxZ += deltaZ;
                }
                if (!visit(x, z))
                {
                    return;
                }
            }
        }

        /// <summary>
        /// Builds one layer's network: the squares, the parts of ground in them, their nodes and links.
        /// </summary>
        private sealed class LayerBuilder
        {
            private readonly NavGrid _grid;
            private readonly int _width;
            private readonly int _height;
            private readonly int _size;
            private readonly int _squaresX;
            private readonly int _squaresZ;
            private readonly IReadOnlyList<PathNode> _existing;

            /// <summary>The part of ground each ogrid belongs to, -1 where blocked.</summary>
            private readonly int[] _part;

            /// <summary>Per part: its label, ogrids, square, and node (index into <see cref="Nodes"/>, -1 for none).</summary>
            private readonly List<(int Label, int Count, int Square, int Node)> _parts = [];

            /// <summary>The parts of each square.</summary>
            private readonly List<int>[] _squareParts;

            private readonly List<HashSet<int>> _links = [];

            public List<GeneratedPathNode> Nodes { get; } = [];

            private readonly List<int> _nodeLabel = [];

            /// <summary>Whether a node is a part's own (it may be dropped when the part is small) rather than one along a way.</summary>
            private readonly List<int> _nodePart = [];

            private readonly HashSet<int> _removed = [];

            public LayerBuilder(NavGrid grid, int size, IReadOnlyList<PathNode> existing)
            {
                _grid = grid;
                _width = grid.Width;
                _height = grid.Height;
                _size = Math.Max(4, size);
                _squaresX = (_width + _size - 1) / _size;
                _squaresZ = (_height + _size - 1) / _size;
                _existing = existing;
                _part = new int[_width * _height];
                Array.Fill(_part, -1);
                _squareParts = new List<int>[_squaresX * _squaresZ];
                for (int i = 0; i < _squareParts.Length; i++)
                {
                    _squareParts[i] = [];
                }
            }

            private int MinimumCells => Math.Max(4, _size * _size / 16);

            public void Build()
            {
                ushort[] clearance = Clearance();
                FindParts(clearance);
                LinkSquaresBeside();
                LinkDiagonals();
                DropSmallParts();
                JoinRegions();
            }

            public IEnumerable<(int A, int B)> LinkPairs()
            {
                // the kept nodes, numbered again without the removed ones
                int[] index = new int[Nodes.Count];
                int next = 0;
                for (int i = 0; i < Nodes.Count; i++)
                {
                    index[i] = _removed.Contains(i) ? -1 : next++;
                }
                List<GeneratedPathNode> kept = [.. Nodes.Where((_, i) => index[i] >= 0)];
                List<(int, int)> pairs = [];
                for (int a = 0; a < _links.Count; a++)
                {
                    if (index[a] < 0)
                    {
                        continue;
                    }
                    foreach (int b in _links[a].Where(b => b > a && index[b] >= 0).Order())
                    {
                        pairs.Add((index[a], index[b]));
                    }
                }
                Nodes.Clear();
                Nodes.AddRange(kept);
                return pairs;
            }

            /// <summary>
            /// Per ogrid how far it is from blocked ground or the map's edge (a chamfer distance, 3 a
            /// step and 4 a diagonal), 0 where blocked.
            /// </summary>
            private ushort[] Clearance()
            {
                ushort[] distance = new ushort[_width * _height];
                int[] cells = _grid.Cells;
                const int Far = ushort.MaxValue;
                for (int z = 0; z < _height; z++)
                {
                    for (int x = 0; x < _width; x++)
                    {
                        int i = z * _width + x;
                        if (cells[i] <= 0)
                        {
                            distance[i] = 0;
                            continue;
                        }
                        int best = Far;
                        best = Math.Min(best, x == 0 ? 3 : distance[i - 1] + 3);
                        best = Math.Min(best, z == 0 ? 3 : distance[i - _width] + 3);
                        best = Math.Min(best, x == 0 || z == 0 ? 4 : distance[i - _width - 1] + 4);
                        best = Math.Min(best, x == _width - 1 || z == 0 ? 4 : distance[i - _width + 1] + 4);
                        distance[i] = (ushort)Math.Min(best, Far);
                    }
                }
                for (int z = _height - 1; z >= 0; z--)
                {
                    for (int x = _width - 1; x >= 0; x--)
                    {
                        int i = z * _width + x;
                        if (distance[i] == 0)
                        {
                            continue;
                        }
                        int best = distance[i];
                        best = Math.Min(best, x == _width - 1 ? 3 : distance[i + 1] + 3);
                        best = Math.Min(best, z == _height - 1 ? 3 : distance[i + _width] + 3);
                        best = Math.Min(best, x == _width - 1 || z == _height - 1 ? 4 : distance[i + _width + 1] + 4);
                        best = Math.Min(best, x == 0 || z == _height - 1 ? 4 : distance[i + _width - 1] + 4);
                        distance[i] = (ushort)best;
                    }
                }
                return distance;
            }

            /// <summary>
            /// The parts of ground of every square, each with its node: a node of the map that lies in
            /// it, else its ogrid furthest from blocked ground (up to a quarter of the square), nearest
            /// the square's centre.
            /// </summary>
            private void FindParts(ushort[] clearance)
            {
                int[] cells = _grid.Cells;
                Dictionary<int, PathNode> existingAt = [];
                foreach (PathNode node in _existing)
                {
                    int x = (int)MathF.Floor(node.Position.X);
                    int z = (int)MathF.Floor(node.Position.Z);
                    if ((uint)x < (uint)_width && (uint)z < (uint)_height)
                    {
                        existingAt.TryAdd(z * _width + x, node);
                    }
                }
                int cap = Math.Max(3, _size * 3 / 4);
                Stack<int> stack = new Stack<int>();
                List<int> members = [];
                for (int sz = 0; sz < _squaresZ; sz++)
                {
                    for (int sx = 0; sx < _squaresX; sx++)
                    {
                        int square = sz * _squaresX + sx;
                        int x0 = sx * _size;
                        int z0 = sz * _size;
                        int x1 = Math.Min(x0 + _size, _width);
                        int z1 = Math.Min(z0 + _size, _height);
                        float centreX = (x0 + x1) / 2f;
                        float centreZ = (z0 + z1) / 2f;
                        for (int z = z0; z < z1; z++)
                        {
                            for (int x = x0; x < x1; x++)
                            {
                                int start = z * _width + x;
                                if (cells[start] <= 0 || _part[start] >= 0)
                                {
                                    continue;
                                }
                                int part = _parts.Count;
                                int label = cells[start];
                                members.Clear();
                                stack.Push(start);
                                _part[start] = part;
                                while (stack.TryPop(out int cell))
                                {
                                    members.Add(cell);
                                    int cx = cell % _width;
                                    int cz = cell / _width;
                                    Visit(cx - 1, cz);
                                    Visit(cx + 1, cz);
                                    Visit(cx, cz - 1);
                                    Visit(cx, cz + 1);
                                }

                                void Visit(int vx, int vz)
                                {
                                    if (vx < x0 || vx >= x1 || vz < z0 || vz >= z1)
                                    {
                                        return;
                                    }
                                    int v = vz * _width + vx;
                                    if (cells[v] == label && _part[v] < 0)
                                    {
                                        _part[v] = part;
                                        stack.Push(v);
                                    }
                                }

                                PathNode? kept = null;
                                int best = -1;
                                float bestScore = float.NegativeInfinity;
                                foreach (int cell in members)
                                {
                                    if (kept is null && existingAt.TryGetValue(cell, out PathNode? node))
                                    {
                                        kept = node;
                                    }
                                    float dx = cell % _width + 0.5f - centreX;
                                    float dz = cell / _width + 0.5f - centreZ;
                                    float score = Math.Min((int)clearance[cell], cap) - MathF.Sqrt(dx * dx + dz * dz);
                                    if (score > bestScore)
                                    {
                                        bestScore = score;
                                        best = cell;
                                    }
                                }
                                int nodeIndex = kept is not null
                                    ? AddNode(new GeneratedPathNode(kept.Position.X, kept.Position.Z, kept.Id), label, part)
                                    : AddNode(new GeneratedPathNode(best % _width + 0.5f, best / _width + 0.5f), label, part);
                                _parts.Add((label, members.Count, square, nodeIndex));
                                _squareParts[square].Add(part);
                            }
                        }
                    }
                }
            }

            private int AddNode(GeneratedPathNode node, int label, int part)
            {
                Nodes.Add(node);
                _nodeLabel.Add(label);
                _nodePart.Add(part);
                _links.Add([]);
                return Nodes.Count - 1;
            }

            private void Link(int a, int b)
            {
                if (a != b)
                {
                    _links[a].Add(b);
                    _links[b].Add(a);
                }
            }

            private void Unlink(int a, int b)
            {
                _links[a].Remove(b);
                _links[b].Remove(a);
            }

            private Vector2 Place(int node) => new Vector2(Nodes[node].X, Nodes[node].Z);

            /// <summary>
            /// Links the parts that touch across the edge between two squares side by side.
            /// </summary>
            private void LinkSquaresBeside()
            {
                HashSet<(int, int)> touching = [];
                for (int sz = 0; sz < _squaresZ; sz++)
                {
                    for (int sx = 0; sx < _squaresX; sx++)
                    {
                        int x0 = sx * _size;
                        int z0 = sz * _size;
                        int x1 = Math.Min(x0 + _size, _width);
                        int z1 = Math.Min(z0 + _size, _height);
                        if (x1 < _width)
                        {
                            for (int z = z0; z < z1; z++)
                            {
                                Touch(_part[z * _width + x1 - 1], _part[z * _width + x1]);
                            }
                        }
                        if (z1 < _height)
                        {
                            for (int x = x0; x < x1; x++)
                            {
                                Touch(_part[(z1 - 1) * _width + x], _part[z1 * _width + x]);
                            }
                        }
                    }
                }

                void Touch(int a, int b)
                {
                    if (a >= 0 && b >= 0 && _parts[a].Label == _parts[b].Label)
                    {
                        touching.Add((Math.Min(a, b), Math.Max(a, b)));
                    }
                }

                foreach ((int a, int b) in touching.Order())
                {
                    int from = _parts[a].Node;
                    int to = _parts[b].Node;
                    if (CanWalk(_grid, Place(from), Place(to)))
                    {
                        Link(from, to);
                    }
                    else
                    {
                        LinkAlong(from, to, WayThrough(a, b, Place(from), Place(to)));
                    }
                }
            }

            /// <summary>
            /// The ogrids from one place to another through two parts of ground (4-connected), found
            /// breadth first; the parts touch, so there is one.
            /// </summary>
            private List<Vector2> WayThrough(int partA, int partB, Vector2 from, Vector2 to)
            {
                int start = (int)MathF.Floor(from.Y) * _width + (int)MathF.Floor(from.X);
                int goal = (int)MathF.Floor(to.Y) * _width + (int)MathF.Floor(to.X);
                Dictionary<int, int> previous = new Dictionary<int, int> { [start] = -1 };
                Queue<int> queue = new Queue<int>();
                queue.Enqueue(start);
                while (queue.TryDequeue(out int cell) && cell != goal)
                {
                    int cx = cell % _width;
                    int cz = cell / _width;
                    Step(cx - 1, cz);
                    Step(cx + 1, cz);
                    Step(cx, cz - 1);
                    Step(cx, cz + 1);

                    void Step(int x, int z)
                    {
                        if ((uint)x >= (uint)_width || (uint)z >= (uint)_height)
                        {
                            return;
                        }
                        int next = z * _width + x;
                        int part = _part[next];
                        if ((part == partA || part == partB) && previous.TryAdd(next, cell))
                        {
                            queue.Enqueue(next);
                        }
                    }
                }
                List<Vector2> way = [];
                if (!previous.ContainsKey(goal))
                {
                    return way;
                }
                for (int cell = goal; cell >= 0; cell = previous[cell])
                {
                    way.Add(new Vector2(cell % _width + 0.5f, cell / _width + 0.5f));
                }
                way.Reverse();
                return way;
            }

            /// <summary>
            /// Links two nodes through nodes along a way between them: from each node on, the furthest
            /// point of the way it can walk to straight gets the next node.
            /// </summary>
            private void LinkAlong(int from, int to, IReadOnlyList<Vector2> way)
            {
                if (way.Count == 0)
                {
                    return;
                }
                int label = _nodeLabel[from];
                int anchor = from;
                Vector2 anchorPlace = Place(from);
                int at = 0;
                while (!CanWalk(_grid, anchorPlace, Place(to)))
                {
                    int furthest = at;
                    for (int i = at + 1; i < way.Count && CanWalk(_grid, anchorPlace, way[i]); i++)
                    {
                        furthest = i;
                    }
                    if (furthest == at && anchorPlace == way[at])
                    {
                        // the next ogrid of the way lies beside this one
                        furthest = Math.Min(at + 1, way.Count - 1);
                    }
                    int added = AddNode(new GeneratedPathNode(way[furthest].X, way[furthest].Y), label, -1);
                    Link(anchor, added);
                    anchor = added;
                    anchorPlace = way[furthest];
                    if (furthest == way.Count - 1)
                    {
                        break;
                    }
                    at = furthest;
                }
                Link(anchor, to);
            }

            /// <summary>
            /// Links the parts of squares diagonal to each other where the line between their nodes is
            /// walkable and no node of a square beside both links them already.
            /// </summary>
            private void LinkDiagonals()
            {
                for (int sz = 0; sz + 1 < _squaresZ; sz++)
                {
                    for (int sx = 0; sx < _squaresX; sx++)
                    {
                        int square = sz * _squaresX + sx;
                        if (sx + 1 < _squaresX)
                        {
                            LinkDiagonal(square, square + _squaresX + 1);
                        }
                        if (sx > 0)
                        {
                            LinkDiagonal(square, square + _squaresX - 1);
                        }
                    }
                }
            }

            private void LinkDiagonal(int squareA, int squareB)
            {
                foreach (int a in _squareParts[squareA])
                {
                    foreach (int b in _squareParts[squareB])
                    {
                        if (_parts[a].Label != _parts[b].Label)
                        {
                            continue;
                        }
                        int from = _parts[a].Node;
                        int to = _parts[b].Node;
                        if (_links[from].Contains(to) || _links[from].Overlaps(_links[to]))
                        {
                            continue;
                        }
                        if (CanWalk(_grid, Place(from), Place(to)))
                        {
                            Link(from, to);
                        }
                    }
                }
            }

            /// <summary>
            /// Drops the nodes of parts too small to matter where that changes no way: a node that ends
            /// a path, or one between two nodes that can walk to each other straight.
            /// </summary>
            private void DropSmallParts()
            {
                Dictionary<int, int> nodesPerLabel = [];
                foreach (int label in _nodeLabel)
                {
                    nodesPerLabel[label] = nodesPerLabel.GetValueOrDefault(label) + 1;
                }
                for (int part = 0; part < _parts.Count; part++)
                {
                    (int label, int count, _, int node) = _parts[part];
                    if (count >= MinimumCells || Nodes[node].ExistingId is not null || nodesPerLabel[label] <= 1)
                    {
                        continue;
                    }
                    int[] neighbours = [.. _links[node]];
                    if (neighbours.Length == 1
                        || (neighbours.Length == 2 && CanWalk(_grid, Place(neighbours[0]), Place(neighbours[1]))))
                    {
                        if (neighbours.Length == 2)
                        {
                            Link(neighbours[0], neighbours[1]);
                        }
                        foreach (int other in neighbours)
                        {
                            Unlink(node, other);
                        }
                        _removed.Add(node);
                        nodesPerLabel[label]--;
                    }
                }
            }

            /// <summary>
            /// Makes every region of the mesh one network: two pieces of one region are joined along
            /// the shortest route between their nearest nodes.
            /// </summary>
            private void JoinRegions()
            {
                for (int round = 0; round < 10000; round++)
                {
                    int[] piece = Pieces();
                    bool joined = false;
                    foreach (IGrouping<int, int> region in Enumerable.Range(0, Nodes.Count).Where(i => !_removed.Contains(i)).GroupBy(i => _nodeLabel[i]))
                    {
                        int[] nodes = [.. region];
                        int first = piece[nodes[0]];
                        int[] others = [.. nodes.Where(n => piece[n] != first)];
                        if (others.Length == 0)
                        {
                            continue;
                        }
                        // the nearest pair between the first piece and the rest
                        int[] main = [.. nodes.Where(n => piece[n] == first)];
                        (int a, int b) = (main[0], others[0]);
                        float nearest = float.PositiveInfinity;
                        foreach (int m in main)
                        {
                            foreach (int o in others)
                            {
                                float distance = Vector2.DistanceSquared(Place(m), Place(o));
                                if (distance < nearest)
                                {
                                    nearest = distance;
                                    (a, b) = (m, o);
                                }
                            }
                        }
                        Vector2 from = Place(a);
                        Vector2 to = Place(b);
                        if (CanWalk(_grid, from, to))
                        {
                            Link(a, b);
                        }
                        else if (NavPaths.FindRoute(_grid, new Vector3(from.X, 0, from.Y), new Vector3(to.X, 0, to.Y)) is NavRoute route)
                        {
                            LinkAlong(a, b, route.Points);
                        }
                        else
                        {
                            continue;
                        }
                        joined = true;
                    }
                    if (!joined)
                    {
                        return;
                    }
                }
            }

            /// <summary>
            /// The piece of the network each node is in (the smallest node index of its piece).
            /// </summary>
            private int[] Pieces()
            {
                int[] piece = new int[Nodes.Count];
                Array.Fill(piece, -1);
                Stack<int> stack = new Stack<int>();
                for (int start = 0; start < Nodes.Count; start++)
                {
                    if (piece[start] >= 0)
                    {
                        continue;
                    }
                    piece[start] = start;
                    stack.Push(start);
                    while (stack.TryPop(out int node))
                    {
                        foreach (int next in _links[node])
                        {
                            if (piece[next] < 0)
                            {
                                piece[next] = start;
                                stack.Push(next);
                            }
                        }
                    }
                }
                return piece;
            }
        }

        #endregion

        #region Air

        /// <summary>
        /// The air network: nodes on a regular grid over the playable area (the whole map without
        /// one), each linked to the four beside it. Without <paramref name="replace"/>, a place of the
        /// grid within half the spacing of one of the layer's nodes takes that node.
        /// </summary>
        public static PathLayerNetwork GenerateAir(MapDocument document, float spacing, IReadOnlyList<PathNode> existing, bool replace)
        {
            MapArea? area = Walkability.PlayableArea(document);
            float x0 = (float)(area?.X0 ?? 0);
            float z0 = (float)(area?.Z0 ?? 0);
            float x1 = (float)(area?.X1 ?? document.Width);
            float z1 = (float)(area?.Z1 ?? document.Height);
            int columns = Math.Max(1, (int)MathF.Round((x1 - x0) / spacing));
            int rows = Math.Max(1, (int)MathF.Round((z1 - z0) / spacing));
            float stepX = (x1 - x0) / columns;
            float stepZ = (z1 - z0) / rows;
            List<GeneratedPathNode> nodes = [];
            HashSet<int> used = [];
            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    float x = x0 + (column + 0.5f) * stepX;
                    float z = z0 + (row + 0.5f) * stepZ;
                    PathNode? near = existing.FirstOrDefault(node => !used.Contains(node.Id)
                        && MathF.Abs(node.Position.X - x) <= stepX / 2 && MathF.Abs(node.Position.Z - z) <= stepZ / 2);
                    if (near is not null)
                    {
                        used.Add(near.Id);
                        nodes.Add(new GeneratedPathNode(near.Position.X, near.Position.Z, near.Id));
                    }
                    else
                    {
                        nodes.Add(new GeneratedPathNode(x, z));
                    }
                }
            }
            List<(int, int)> links = [];
            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    int i = row * columns + column;
                    if (column + 1 < columns)
                    {
                        links.Add((i, i + 1));
                    }
                    if (row + 1 < rows)
                    {
                        links.Add((i, i + columns));
                    }
                }
            }
            return new PathLayerNetwork(PathLayer.Air, spacing, replace, [.. nodes], [.. links]);
        }

        #endregion

        #region Into the map

        /// <summary>
        /// Puts generated networks into the map: per layer, its own nodes go first when it replaces
        /// them (with their places in chains), the new nodes are added on the terrain with the next
        /// free names of the layer, and the links are added both ways (a node of the map keeps its
        /// links). A node of the map the network names that is gone since becomes a new node.
        /// </summary>
        public static MapDocument Apply(MapDocument document, PathNetwork network)
        {
            foreach (PathLayerNetwork layer in network.Layers)
            {
                document = Apply(document, layer);
            }
            return document;
        }

        private static MapDocument Apply(MapDocument document, PathLayerNetwork network)
        {
            MarkerSet markers = document.Markers;
            if (network.Replace)
            {
                HashSet<int> gone = [.. markers.PathNodes.Where(node => node.Layer == network.Layer).Select(node => node.Id)];
                if (gone.Count > 0)
                {
                    markers = markers with
                    {
                        PathNodes = [.. markers.PathNodes.Where(node => !gone.Contains(node.Id))
                            .Select(node => node.AdjacentTo.Any(gone.Contains) ? node with { AdjacentTo = [.. node.AdjacentTo.Where(id => !gone.Contains(id))] } : node)],
                        Chains = [.. markers.Chains.Select(chain => chain.MarkerIds.Any(gone.Contains) ? chain with { MarkerIds = [.. chain.MarkerIds.Where(id => !gone.Contains(id))] } : chain)],
                    };
                }
            }

            Dictionary<int, int> indexOfNode = [];
            for (int i = 0; i < markers.PathNodes.Length; i++)
            {
                indexOfNode[markers.PathNodes[i].Id] = i;
            }
            string prefix = Prefix(network.Layer);
            HashSet<string> names = [.. markers.All.Select(marker => marker.Name)];
            int number = markers.All.Select(marker => NumberAfter(marker.Name, prefix)).Max(n => (int?)n) is int highest ? highest + 1 : 0;
            int nextId = markers.NextId();
            Heightmap heights = document.Terrain.Heightmap;

            // the id of each node of the network
            int[] ids = new int[network.Nodes.Length];
            List<PathNode> added = [];
            for (int i = 0; i < network.Nodes.Length; i++)
            {
                GeneratedPathNode node = network.Nodes[i];
                if (node.ExistingId is int existing && indexOfNode.TryGetValue(existing, out int at) && markers.PathNodes[at].Layer == network.Layer)
                {
                    ids[i] = existing;
                    continue;
                }
                string name;
                do
                {
                    name = $"{prefix}{number++}";
                }
                while (!names.Add(name));
                ids[i] = nextId++;
                added.Add(new PathNode
                {
                    Id = ids[i],
                    Name = name,
                    Layer = network.Layer,
                    Position = new Vector3(node.X, heights.HeightAt(node.X, node.Z), node.Z),
                });
            }

            Dictionary<int, List<int>> adjacency = [];
            foreach ((int a, int b) in network.Links)
            {
                (adjacency.TryGetValue(ids[a], out List<int>? fromA) ? fromA : adjacency[ids[a]] = []).Add(ids[b]);
                (adjacency.TryGetValue(ids[b], out List<int>? fromB) ? fromB : adjacency[ids[b]] = []).Add(ids[a]);
            }
            PathNode WithLinks(PathNode node) => adjacency.TryGetValue(node.Id, out List<int>? links)
                ? node with { AdjacentTo = [.. node.AdjacentTo.Concat(links.Where(id => !node.AdjacentTo.Contains(id))).Distinct()] }
                : node;

            ImmutableArray<PathNode> pathNodes = [.. markers.PathNodes.Select(node => adjacency.ContainsKey(node.Id) ? WithLinks(node) : node), .. added.Select(WithLinks)];
            return document with { Markers = markers with { PathNodes = pathNodes } };
        }

        /// <summary>
        /// The number after a prefix in a name (<c>LandPN12</c> after <c>LandPN</c> is 12), or null.
        /// </summary>
        private static int? NumberAfter(string name, string prefix) =>
            name.Length > prefix.Length && name.StartsWith(prefix, StringComparison.Ordinal)
                && int.TryParse(name.AsSpan(prefix.Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int number)
                ? number : null;

        #endregion
    }

    /// <summary>
    /// The path networks of the AI (user story R-3, <see cref="PathNetworks"/>), generated from
    /// walkability with the settings of the last time they were asked for, and put into the map's
    /// markers. Out of date when walkability's inputs change.
    /// </summary>
    public sealed class PathNetworkGenerator : IDocumentGenerator
    {
        public const string GeneratorId = "pathNetwork";

        public string Id => GeneratorId;

        public string Title => "Path networks";

        public string Story => "R-3";

        public IReadOnlyList<GeneratedInput> Inputs { get; } =
            [GeneratedInput.Heightmap, GeneratedInput.TerrainTypes, GeneratedInput.WaterLevel, GeneratedInput.DepositsAndAreas, GeneratedInput.MapType];

        /// <summary>
        /// What the next generation makes: the layers, spacing, and whether the layers' nodes go.
        /// </summary>
        public PathNetworkSettings Settings { get; set; } = new PathNetworkSettings();

        /// <summary>
        /// The last walkability generated, when there is one: used while it is current for the
        /// version generated from, so the mesh is not made twice.
        /// </summary>
        public GeneratedResult? Walkability { get; set; }

        public object Generate(MapDocument document)
        {
            GeneratedResult? walkability = Walkability;
            MapNavigation navigation = walkability is not null && !walkability.IsStale(document) && walkability.Value is MapNavigation current
                ? current
                : Generation.Walkability.Generate(document);
            return PathNetworks.Generate(document, navigation, Settings);
        }

        public MapDocument Apply(MapDocument document, object value) => PathNetworks.Apply(document, (PathNetwork)value);
    }
}
