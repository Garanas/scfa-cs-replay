using System.Collections.Immutable;
using System.Numerics;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Generation
{
    /// <summary>
    /// Why a node of a generated network is where it is.
    /// </summary>
    public enum PathNodeRole
    {
        /// <summary>Where corridors of the skeleton meet.</summary>
        Junction,

        /// <summary>Where a corridor of the skeleton ends.</summary>
        End,

        /// <summary>Along a corridor, about a spacing from the next.</summary>
        Along,

        /// <summary>On open ground away from the skeleton.</summary>
        Fill,

        /// <summary>Placed by hand (an edit), or a moved node whose generated node is gone.</summary>
        Added,
    }

    /// <summary>
    /// A node of a generated network.
    /// </summary>
    public sealed record GeneratedPathNode(float X, float Z, PathNodeRole Role = PathNodeRole.Along)
    {
        /// <summary>
        /// How edits refer to it: a generated node by the place it was generated at, a node added by
        /// hand by its number. A moved node keeps the reference of where it was generated.
        /// </summary>
        public PathNodeRef Ref { get; init; }

        public Vector2 Place => new Vector2(X, Z);
    }

    /// <summary>
    /// The network generated for one layer: its nodes and the links between them (indices into
    /// <see cref="Nodes"/>, each pair once, the smaller first).
    /// </summary>
    /// <param name="Spacing">How far apart its nodes were placed, in ogrids.</param>
    public sealed record PathLayerNetwork(PathLayer Layer, float Spacing, ImmutableArray<GeneratedPathNode> Nodes, ImmutableArray<(int A, int B)> Links)
    {
        /// <summary>
        /// The nodes linked to each node.
        /// </summary>
        public IReadOnlyList<int>[] Neighbours()
        {
            List<int>[] neighbours = new List<int>[Nodes.Length];
            for (int i = 0; i < neighbours.Length; i++)
            {
                neighbours[i] = [];
            }
            foreach ((int a, int b) in Links)
            {
                neighbours[a].Add(b);
                neighbours[b].Add(a);
            }
            return neighbours;
        }

        /// <summary>
        /// The node nearest to a place within a distance, or -1.
        /// </summary>
        public int Nearest(float x, float z, float within)
        {
            int best = -1;
            float nearest = within * within;
            for (int i = 0; i < Nodes.Length; i++)
            {
                float dx = Nodes[i].X - x;
                float dz = Nodes[i].Z - z;
                float distance = dx * dx + dz * dz;
                if (distance <= nearest)
                {
                    nearest = distance;
                    best = i;
                }
            }
            return best;
        }
    }

    /// <summary>
    /// The path networks generated for some layers (<see cref="PathNetworks.Generate"/>): previewed in
    /// the views, and written as markers into the files an export writes (<see cref="PathNetworks.Apply"/>).
    /// </summary>
    public sealed record PathNetwork(ImmutableArray<PathLayerNetwork> Layers)
    {
        public int Nodes => Layers.Sum(layer => layer.Nodes.Length);

        public int Links => Layers.Sum(layer => layer.Links.Length);

        public PathLayerNetwork? this[PathLayer layer] => Layers.FirstOrDefault(network => network.Layer == layer);

        /// <summary>
        /// The navigational mesh it was generated from: links of nodes added by hand are checked on it.
        /// </summary>
        public MapNavigation? Navigation { get; init; }
    }

    /// <summary>
    /// Path networks generated from the navigation mesh (user story R-3): the graphs of path nodes the
    /// AI of the game finds its way with (<c>Land Path Node</c>, <c>Water Path Node</c>,
    /// <c>Amphibious Path Node</c>, <c>Air Path Node</c>; read by <c>lua/AI/aiutilities.lua</c> and
    /// <c>lua/sim/MarkerUtilities.lua</c>, which link them by the names in <c>adjacentTo</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Land, water and amphibious networks come from their layer of the navigation mesh, region by
    /// region (<see cref="GenerateLayer"/>):
    /// </para>
    /// <list type="number">
    /// <item>The layer's labels in blocks of about a twelfth of the spacing (a block takes the label
    /// most of its passable ogrids have), so a large map thins quickly.</item>
    /// <item>Every region is thinned to its skeleton (<see cref="PathSkeleton"/>), spurs shorter than
    /// half the spacing taken away.</item>
    /// <item>Nodes go at the skeleton's junctions and dead ends, and along each chain about a spacing
    /// apart, linked one to the next. Open ground further than three quarters of the spacing from any
    /// node gets nodes on a grid of the spacing (<see cref="PathNodeSettings.Fill"/>).</item>
    /// <item>Nodes that see each other on the grid (<see cref="CanWalk"/>) within one and a half
    /// spacings are linked when no other node lies in the circle over the two (a Gabriel graph).</item>
    /// <item>Smoothing: each round every node but the dead ends moves halfway toward the middle of
    /// its neighbours, unless that takes it out of its region or makes a link cross ground the layer
    /// cannot walk.</item>
    /// <item>Nodes closer than the minimum distance are merged (dead ends and junctions stay before
    /// the others), the links are made again, and each region is made one network: pieces are joined
    /// along a route through the region, with nodes where the straight line would leave it.</item>
    /// </list>
    /// <para>
    /// A region smaller than a sixteenth of a spacing squared gets no nodes. Air nodes lie on a grid
    /// over the playable area, 16 by 16 unless told, each linked to the eight around it, as the
    /// game's maps have them. Everything is deterministic: the same map and settings give the same
    /// nodes, which is what lets edits by hand find their nodes again (<see cref="PathNetworkEdits"/>).
    /// Names at export follow the map editors (<c>LandPN</c>, <c>WaterPN</c>, <c>AmphPN</c>,
    /// <c>AirPN</c> with a number, as the FAF map editor's <c>GetPrefixByType</c>), going on from the
    /// highest number in use.
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
        /// The nodes per side of the air network unless told.
        /// </summary>
        public const int AirNodesPerSide = 16;

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
        /// The spacing the settings give for a map, from 4 to 512 ogrids.
        /// </summary>
        public static float SpacingOf(MapDocument document, PathNodeSettings settings) => Math.Clamp(settings.Spacing ?? DefaultSpacing(document), 4, 512);

        /// <summary>
        /// Generates the networks of the layers the settings name from a version of the document and
        /// its navigational mesh.
        /// </summary>
        /// <param name="cancel">Stops it between layers.</param>
        public static PathNetwork Generate(MapDocument document, MapNavigation navigation, PathNodeSettings settings, CancellationToken cancel = default)
        {
            float spacing = SpacingOf(document, settings);
            float minimum = Math.Clamp(settings.MinimumDistance ?? spacing * 0.4f, 0, spacing);
            PathLayer[] chosen = [.. settings.Layers.Distinct()];
            PathLayerNetwork[] layers = new PathLayerNetwork[chosen.Length];
            MapArea? area = Walkability.PlayableArea(document);
            // the layers are apart from each other: side by side
            Parallel.For(0, chosen.Length, new ParallelOptions { CancellationToken = cancel }, i =>
            {
                layers[i] = NavLayerOf(chosen[i]) is NavLayer nav
                    ? GenerateLayer(navigation[nav], chosen[i], spacing, Math.Clamp(settings.Smoothing, 0, 10), minimum, settings.Fill, area)
                    : GenerateAir(document, settings.AirSpacing);
            });
            return new PathNetwork([.. layers]) { Navigation = navigation };
        }

        #region Ground layers

        /// <summary>
        /// The network of one layer of the navigational mesh (<see cref="PathNetworks"/> says how).
        /// </summary>
        /// <param name="smoothing">The rounds of smoothing.</param>
        /// <param name="minimum">How close two nodes may lie, in ogrids.</param>
        /// <param name="fill">Whether open ground away from the skeleton gets nodes.</param>
        /// <param name="area">The playable area, where fill nodes go; null for the whole map.</param>
        public static PathLayerNetwork GenerateLayer(NavGrid grid, PathLayer layer, float spacing, int smoothing = PathNodeSettings.DefaultSmoothing, float? minimum = null, bool fill = true, MapArea? area = null)
        {
            LayerBuilder builder = new LayerBuilder(grid, spacing, minimum ?? spacing * 0.4f, area);
            builder.Build(smoothing, fill);
            return builder.Result(layer);
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

        internal static int LabelAt(NavGrid grid, Vector2 place) => LabelAt(grid, (int)MathF.Floor(place.X), (int)MathF.Floor(place.Y));

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
        /// The shortest route over a grid's region between two places, searched in a box around them
        /// that grows until it holds a route or the whole map; null when there is none.
        /// </summary>
        internal static List<Vector2>? Route(NavGrid grid, Vector2 from, Vector2 to)
        {
            int label = LabelAt(grid, from);
            if (label <= 0 || LabelAt(grid, to) != label)
            {
                return null;
            }
            int sx = (int)MathF.Floor(from.X);
            int sz = (int)MathF.Floor(from.Y);
            int gx = (int)MathF.Floor(to.X);
            int gz = (int)MathF.Floor(to.Y);
            int margin = Math.Max(16, Math.Max(Math.Abs(gx - sx), Math.Abs(gz - sz)) / 2);
            while (true)
            {
                int x0 = Math.Max(0, Math.Min(sx, gx) - margin);
                int z0 = Math.Max(0, Math.Min(sz, gz) - margin);
                int x1 = Math.Min(grid.Width - 1, Math.Max(sx, gx) + margin);
                int z1 = Math.Min(grid.Height - 1, Math.Max(sz, gz) + margin);
                if (RouteIn(grid, label, sx, sz, gx, gz, x0, z0, x1, z1) is List<Vector2> route)
                {
                    return route;
                }
                if (x0 == 0 && z0 == 0 && x1 == grid.Width - 1 && z1 == grid.Height - 1)
                {
                    return null;
                }
                margin *= 2;
            }
        }

        private static List<Vector2>? RouteIn(NavGrid grid, int label, int sx, int sz, int gx, int gz, int x0, int z0, int x1, int z1)
        {
            int width = x1 - x0 + 1;
            int height = z1 - z0 + 1;
            float[] distance = new float[width * height];
            Array.Fill(distance, float.PositiveInfinity);
            int[] previous = new int[width * height];
            int start = (sz - z0) * width + sx - x0;
            int goal = (gz - z0) * width + gx - x0;
            float Heuristic(int cell)
            {
                int dx = Math.Abs(cell % width - (gx - x0));
                int dz = Math.Abs(cell / width - (gz - z0));
                return Math.Max(dx, dz) + 0.41421357f * Math.Min(dx, dz);
            }
            PriorityQueue<int, float> queue = new PriorityQueue<int, float>();
            distance[start] = 0;
            previous[start] = -1;
            queue.Enqueue(start, Heuristic(start));
            while (queue.TryDequeue(out int cell, out float estimate))
            {
                if (cell == goal)
                {
                    break;
                }
                float at = distance[cell];
                if (estimate > at + Heuristic(cell) + 1e-3f)
                {
                    continue;
                }
                int cx = cell % width;
                int cz = cell / width;
                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = cx + dx;
                        int nz = cz + dz;
                        if ((dx == 0 && dz == 0) || (uint)nx >= (uint)width || (uint)nz >= (uint)height
                            || grid.Cells[(nz + z0) * grid.Width + nx + x0] != label)
                        {
                            continue;
                        }
                        if (dx != 0 && dz != 0
                            && (grid.Cells[(cz + z0) * grid.Width + nx + x0] != label || grid.Cells[(nz + z0) * grid.Width + cx + x0] != label))
                        {
                            // no cutting a corner
                            continue;
                        }
                        int next = nz * width + nx;
                        float step = at + (dx != 0 && dz != 0 ? 1.41421357f : 1);
                        if (step < distance[next])
                        {
                            distance[next] = step;
                            previous[next] = cell;
                            queue.Enqueue(next, step + Heuristic(next));
                        }
                    }
                }
            }
            if (float.IsPositiveInfinity(distance[goal]))
            {
                return null;
            }
            List<Vector2> points = [];
            for (int cell = goal; cell >= 0; cell = previous[cell])
            {
                points.Add(new Vector2(cell % width + x0 + 0.5f, cell / width + z0 + 0.5f));
            }
            points.Reverse();
            return points;
        }

        /// <summary>
        /// Builds one layer's network from the skeletons of its regions.
        /// </summary>
        private sealed class LayerBuilder
        {
            private readonly NavGrid _grid;
            private readonly float _spacing;
            private readonly float _minimum;
            private readonly MapArea? _area;
            private readonly int _block;

            private readonly List<Vector2> _places = [];
            private readonly List<PathNodeRole> _roles = [];
            private readonly List<int> _labels = [];
            private readonly List<HashSet<int>> _links = [];

            /// <summary>The links along the skeleton's chains: they stay when the others are made again.</summary>
            private readonly HashSet<(int, int)> _chainLinks = [];
            private readonly HashSet<int> _removed = [];

            public LayerBuilder(NavGrid grid, float spacing, float minimum, MapArea? area)
            {
                _grid = grid;
                _spacing = Math.Max(4, spacing);
                _minimum = minimum;
                _area = area;
                _block = Math.Max(1, (int)(_spacing / 12));
            }

            private int MinimumCells => (int)Math.Max(4, _spacing * _spacing / 16);

            public void Build(int smoothing, bool fill)
            {
                (int[] coarse, int width, int height) = Coarse();
                bool[] skeleton = PathSkeleton.Thin(coarse, width, height);
                PathSkeleton.Prune(skeleton, coarse, width, height, Math.Max(2, (int)MathF.Round(_spacing / 2 / _block)));
                SkeletonGraph graph = PathSkeleton.Trace(skeleton, coarse, width, height);
                PlaceAlongSkeleton(graph, coarse, width);
                DropSmallRegions();
                AddMissingRegions();
                if (fill)
                {
                    Fill();
                }
                LinkNeighbours();
                Smooth(smoothing);
                Merge();
                LinkNeighbours();
                JoinRegions();
            }

            /// <summary>
            /// The labels in blocks: each block the label most of its passable ogrids have, 0 when none is.
            /// </summary>
            private (int[] Labels, int Width, int Height) Coarse()
            {
                if (_block == 1)
                {
                    int[] copy = new int[_grid.Cells.Length];
                    for (int i = 0; i < copy.Length; i++)
                    {
                        copy[i] = Math.Max(0, _grid.Cells[i]);
                    }
                    return (copy, _grid.Width, _grid.Height);
                }
                int width = (_grid.Width + _block - 1) / _block;
                int height = (_grid.Height + _block - 1) / _block;
                int[] labels = new int[width * height];
                // the labels of a block and how often each comes; a block rarely holds more than a few
                int[] seen = new int[_block * _block];
                int[] counts = new int[_block * _block];
                int[] cells = _grid.Cells;
                for (int bz = 0; bz < height; bz++)
                {
                    for (int bx = 0; bx < width; bx++)
                    {
                        int distinct = 0;
                        int z1 = Math.Min(_grid.Height, (bz + 1) * _block);
                        int x1 = Math.Min(_grid.Width, (bx + 1) * _block);
                        for (int z = bz * _block; z < z1; z++)
                        {
                            int row = z * _grid.Width;
                            for (int x = bx * _block; x < x1; x++)
                            {
                                int label = cells[row + x];
                                if (label <= 0)
                                {
                                    continue;
                                }
                                int k = 0;
                                while (k < distinct && seen[k] != label)
                                {
                                    k++;
                                }
                                if (k == distinct)
                                {
                                    seen[distinct] = label;
                                    counts[distinct++] = 0;
                                }
                                counts[k]++;
                            }
                        }
                        int best = 0;
                        int most = 0;
                        for (int k = 0; k < distinct; k++)
                        {
                            if (counts[k] > most || (counts[k] == most && seen[k] < best))
                            {
                                (best, most) = (seen[k], counts[k]);
                            }
                        }
                        labels[bz * width + bx] = best;
                    }
                }
                return (labels, width, height);
            }

            /// <summary>
            /// The place of a block's middle, moved onto the nearest ogrid of its label when the middle
            /// is not of it.
            /// </summary>
            private Vector2 PlaceOf(int cell, int width, int label)
            {
                Vector2 place = new Vector2((cell % width + 0.5f) * _block, (cell / width + 0.5f) * _block);
                return Snap(place, label) ?? place;
            }

            private Vector2? Snap(Vector2 place, int label)
            {
                if (LabelAt(_grid, place) == label)
                {
                    return place;
                }
                int cx = (int)MathF.Floor(place.X);
                int cz = (int)MathF.Floor(place.Y);
                for (int radius = 1; radius <= _block * 2 + 2; radius++)
                {
                    Vector2? best = null;
                    float nearest = float.MaxValue;
                    for (int dz = -radius; dz <= radius; dz++)
                    {
                        for (int dx = -radius; dx <= radius; dx++)
                        {
                            if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != radius || LabelAt(_grid, cx + dx, cz + dz) != label)
                            {
                                continue;
                            }
                            Vector2 at = new Vector2(cx + dx + 0.5f, cz + dz + 0.5f);
                            float distance = Vector2.DistanceSquared(at, place);
                            if (distance < nearest)
                            {
                                (best, nearest) = (at, distance);
                            }
                        }
                    }
                    if (best is not null)
                    {
                        return best;
                    }
                }
                return null;
            }

            private int AddNode(Vector2 place, PathNodeRole role, int label)
            {
                _places.Add(place);
                _roles.Add(role);
                _labels.Add(label);
                _links.Add([]);
                return _places.Count - 1;
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

            /// <summary>
            /// Links two nodes: straight when units walk the line, else through nodes along a route
            /// between them, each where the one before stops seeing the route.
            /// </summary>
            private void Connect(int a, int b, bool chain)
            {
                Vector2 from = _places[a];
                Vector2 to = _places[b];
                if (Sees(from, to))
                {
                    Link(a, b);
                    if (chain)
                    {
                        _chainLinks.Add((Math.Min(a, b), Math.Max(a, b)));
                    }
                    return;
                }
                if (Route(_grid, from, to) is not List<Vector2> way || way.Count == 0)
                {
                    return;
                }
                int anchor = a;
                Vector2 anchorPlace = from;
                int at = 0;
                while (!Sees(anchorPlace, to))
                {
                    int furthest = at;
                    for (int i = at + 1; i < way.Count && Sees(anchorPlace, way[i]); i++)
                    {
                        furthest = i;
                    }
                    if (furthest == at)
                    {
                        furthest = Math.Min(at + 1, way.Count - 1);
                    }
                    int added = AddNode(way[furthest], PathNodeRole.Along, _labels[a]);
                    Link(anchor, added);
                    _chainLinks.Add((Math.Min(anchor, added), Math.Max(anchor, added)));
                    anchor = added;
                    anchorPlace = way[furthest];
                    if (furthest >= way.Count - 1)
                    {
                        break;
                    }
                    at = furthest;
                }
                Link(anchor, b);
                _chainLinks.Add((Math.Min(anchor, b), Math.Max(anchor, b)));
            }

            /// <summary>
            /// Whether units walk the line between two places both ways (a line along a corner may be
            /// walkable one way only).
            /// </summary>
            private bool Sees(Vector2 a, Vector2 b) => CanWalk(_grid, a, b) && CanWalk(_grid, b, a);

            /// <summary>
            /// Nodes at the skeleton's junctions and dead ends, and along each chain about a spacing
            /// apart, linked one to the next.
            /// </summary>
            private void PlaceAlongSkeleton(SkeletonGraph graph, int[] coarse, int width)
            {
                int[] index = new int[graph.Nodes.Count];
                for (int i = 0; i < graph.Nodes.Count; i++)
                {
                    (int cell, bool end) = graph.Nodes[i];
                    int label = coarse[cell];
                    index[i] = AddNode(PlaceOf(cell, width, label), end ? PathNodeRole.End : PathNodeRole.Junction, label);
                }
                foreach ((int a, int b, int[] cells) in graph.Chains)
                {
                    int label = coarse[cells[0]];
                    float[] along = new float[cells.Length];
                    for (int i = 1; i < cells.Length; i++)
                    {
                        int dx = Math.Abs(cells[i] % width - cells[i - 1] % width);
                        int dz = Math.Abs(cells[i] / width - cells[i - 1] / width);
                        along[i] = along[i - 1] + (dx + dz > 1 ? 1.41421357f : dx + dz) * _block;
                    }
                    float length = along[^1];
                    int inner = Math.Max(0, (int)MathF.Round(length / _spacing) - 1);
                    if (a == b)
                    {
                        // a ring: at least a triangle
                        inner = Math.Max(inner, 2);
                    }
                    int previous = index[a];
                    int previousAt = 0;
                    int at = 0;
                    for (int n = 1; n <= inner; n++)
                    {
                        float target = length * n / (inner + 1);
                        while (at < cells.Length - 1 && along[at] < target)
                        {
                            at++;
                        }
                        int node = AddNode(PlaceOf(cells[at], width, label), PathNodeRole.Along, label);
                        ConnectAlong(previous, node, cells, previousAt, at, width, label);
                        previous = node;
                        previousAt = at;
                    }
                    ConnectAlong(previous, index[b], cells, previousAt, cells.Length - 1, width, label);
                }
            }

            /// <summary>
            /// Links two nodes of a chain: straight when they see each other, else through a node at
            /// the chain's cell halfway between them, and so on; two cells side by side that still do
            /// not see each other are joined along a route (<see cref="Connect"/>).
            /// </summary>
            private void ConnectAlong(int a, int b, int[] cells, int from, int to, int width, int label)
            {
                if (to - from <= 1 || Sees(_places[a], _places[b]))
                {
                    Connect(a, b, chain: true);
                    return;
                }
                int middle = (from + to) / 2;
                int node = AddNode(PlaceOf(cells[middle], width, label), PathNodeRole.Along, label);
                ConnectAlong(a, node, cells, from, middle, width, label);
                ConnectAlong(node, b, cells, middle, to, width, label);
            }

            /// <summary>
            /// Takes out the nodes of regions too small to matter.
            /// </summary>
            private void DropSmallRegions()
            {
                for (int i = 0; i < _places.Count; i++)
                {
                    int label = _labels[i];
                    if (label <= 0 || _grid.Labels[label - 1].Cells < MinimumCells)
                    {
                        Remove(i);
                    }
                }
            }

            private void Remove(int node)
            {
                if (_removed.Add(node))
                {
                    foreach (int other in _links[node].ToArray())
                    {
                        Unlink(node, other);
                    }
                }
            }

            /// <summary>
            /// A node for each region large enough that the blocks lost (a region narrower than a
            /// block): at its ogrid nearest to its middle.
            /// </summary>
            private void AddMissingRegions()
            {
                HashSet<int> placed = [.. Alive().Select(i => _labels[i])];
                int count = _grid.Labels.Count;
                double[] sumX = new double[count + 1];
                double[] sumZ = new double[count + 1];
                bool missing = false;
                for (int id = 1; id <= count; id++)
                {
                    missing |= !placed.Contains(id) && _grid.Labels[id - 1].Cells >= MinimumCells;
                }
                if (!missing)
                {
                    return;
                }
                int[] cells = _grid.Cells;
                for (int i = 0; i < cells.Length; i++)
                {
                    int label = cells[i];
                    if (label > 0)
                    {
                        sumX[label] += i % _grid.Width + 0.5;
                        sumZ[label] += i / _grid.Width + 0.5;
                    }
                }
                float[] nearest = new float[count + 1];
                int[] best = new int[count + 1];
                Array.Fill(nearest, float.MaxValue);
                Array.Fill(best, -1);
                for (int i = 0; i < cells.Length; i++)
                {
                    int label = cells[i];
                    if (label <= 0 || placed.Contains(label) || _grid.Labels[label - 1].Cells < MinimumCells)
                    {
                        continue;
                    }
                    int cellCount = _grid.Labels[label - 1].Cells;
                    float dx = (float)(i % _grid.Width + 0.5 - sumX[label] / cellCount);
                    float dz = (float)(i / _grid.Width + 0.5 - sumZ[label] / cellCount);
                    float distance = dx * dx + dz * dz;
                    if (distance < nearest[label])
                    {
                        nearest[label] = distance;
                        best[label] = i;
                    }
                }
                for (int label = 1; label <= count; label++)
                {
                    if (best[label] >= 0)
                    {
                        AddNode(new Vector2(best[label] % _grid.Width + 0.5f, best[label] / _grid.Width + 0.5f), PathNodeRole.End, label);
                    }
                }
            }

            private IEnumerable<int> Alive() => Enumerable.Range(0, _places.Count).Where(i => !_removed.Contains(i));

            /// <summary>
            /// The nodes in cells of a spacing, for finding those near a place.
            /// </summary>
            private Dictionary<(int, int), List<int>> Buckets()
            {
                Dictionary<(int, int), List<int>> buckets = [];
                foreach (int i in Alive())
                {
                    (int, int) key = Key(_places[i]);
                    (buckets.TryGetValue(key, out List<int>? list) ? list : buckets[key] = []).Add(i);
                }
                return buckets;
            }

            private (int, int) Key(Vector2 place) => ((int)MathF.Floor(place.X / _spacing), (int)MathF.Floor(place.Y / _spacing));

            private IEnumerable<int> Near(Dictionary<(int, int), List<int>> buckets, Vector2 place, float radius)
            {
                int reach = (int)MathF.Ceiling(radius / _spacing);
                (int kx, int kz) = Key(place);
                float squared = radius * radius;
                for (int dz = -reach; dz <= reach; dz++)
                {
                    for (int dx = -reach; dx <= reach; dx++)
                    {
                        if (buckets.TryGetValue((kx + dx, kz + dz), out List<int>? list))
                        {
                            foreach (int i in list)
                            {
                                if (Vector2.DistanceSquared(_places[i], place) <= squared)
                                {
                                    yield return i;
                                }
                            }
                        }
                    }
                }
            }

            /// <summary>
            /// Nodes on a grid of the spacing over open ground further than three quarters of a spacing
            /// from every node of its region.
            /// </summary>
            private void Fill()
            {
                Dictionary<(int, int), List<int>> buckets = Buckets();
                float x0 = (float)(_area?.X0 ?? 0);
                float z0 = (float)(_area?.Z0 ?? 0);
                float x1 = (float)(_area?.X1 ?? _grid.Width);
                float z1 = (float)(_area?.Z1 ?? _grid.Height);
                float clear = _spacing * 0.75f;
                for (float z = z0 + _spacing / 2; z < z1; z += _spacing)
                {
                    for (float x = x0 + _spacing / 2; x < x1; x += _spacing)
                    {
                        Vector2 place = new Vector2(MathF.Floor(x) + 0.5f, MathF.Floor(z) + 0.5f);
                        int label = LabelAt(_grid, place);
                        if (label <= 0 || _grid.Labels[label - 1].Cells < MinimumCells || Near(buckets, place, clear).Any(i => _labels[i] == label))
                        {
                            continue;
                        }
                        int node = AddNode(place, PathNodeRole.Fill, label);
                        (int, int) key = Key(place);
                        (buckets.TryGetValue(key, out List<int>? list) ? list : buckets[key] = []).Add(node);
                    }
                }
            }

            /// <summary>
            /// Links nodes of one region within one and a half spacings that see each other, when no
            /// other node lies in the circle over the two; the chains' links stay.
            /// </summary>
            private void LinkNeighbours()
            {
                foreach (int i in Alive())
                {
                    foreach (int j in _links[i].ToArray())
                    {
                        if (!_chainLinks.Contains((Math.Min(i, j), Math.Max(i, j))))
                        {
                            Unlink(i, j);
                        }
                    }
                }
                Dictionary<(int, int), List<int>> buckets = Buckets();
                float reach = _spacing * 1.5f;
                foreach (int a in Alive())
                {
                    Vector2 pa = _places[a];
                    // a node in the circle over a and b lies nearer to a than b does: among these
                    int[] near = [.. Near(buckets, pa, reach).Where(c => c != a).Order()];
                    foreach (int b in near)
                    {
                        if (b < a || _labels[b] != _labels[a] || _links[a].Contains(b))
                        {
                            continue;
                        }
                        Vector2 pb = _places[b];
                        float ab = Vector2.DistanceSquared(pa, pb);
                        bool blocked = false;
                        foreach (int c in near)
                        {
                            if (c != b && Vector2.DistanceSquared(pa, _places[c]) + Vector2.DistanceSquared(_places[c], pb) <= ab * 1.0001f)
                            {
                                blocked = true;
                                break;
                            }
                        }
                        if (!blocked && Sees(pa, pb))
                        {
                            Link(a, b);
                        }
                    }
                }
            }

            /// <summary>
            /// Rounds of moving every node but the dead ends halfway toward the middle of its
            /// neighbours, kept inside its region with every link walkable.
            /// </summary>
            private void Smooth(int rounds)
            {
                for (int round = 0; round < rounds; round++)
                {
                    foreach (int i in Alive())
                    {
                        if (_roles[i] == PathNodeRole.End || _links[i].Count < 2)
                        {
                            continue;
                        }
                        Vector2 middle = Vector2.Zero;
                        foreach (int j in _links[i])
                        {
                            middle += _places[j];
                        }
                        middle /= _links[i].Count;
                        Vector2 place = _places[i] + (middle - _places[i]) / 2;
                        if (LabelAt(_grid, place) != _labels[i] || !_links[i].All(j => Sees(place, _places[j])))
                        {
                            continue;
                        }
                        _places[i] = place;
                    }
                }
            }

            private static int Keep(PathNodeRole role) => role switch
            {
                PathNodeRole.End => 0,
                PathNodeRole.Junction => 1,
                PathNodeRole.Along => 2,
                _ => 3,
            };

            /// <summary>
            /// Merges nodes closer than the minimum distance, nearest pairs first: the dead end or
            /// junction stays, and takes the other's links it can walk.
            /// </summary>
            private void Merge()
            {
                if (_minimum <= 0)
                {
                    return;
                }
                Dictionary<(int, int), List<int>> buckets = Buckets();
                List<(float Distance, int A, int B)> pairs = [];
                foreach (int a in Alive())
                {
                    foreach (int b in Near(buckets, _places[a], _minimum))
                    {
                        if (b > a && _labels[b] == _labels[a])
                        {
                            pairs.Add((Vector2.Distance(_places[a], _places[b]), a, b));
                        }
                    }
                }
                foreach ((_, int a, int b) in pairs.OrderBy(pair => pair.Distance).ThenBy(pair => pair.A).ThenBy(pair => pair.B))
                {
                    if (_removed.Contains(a) || _removed.Contains(b))
                    {
                        continue;
                    }
                    (int kept, int gone) = Keep(_roles[b]) < Keep(_roles[a]) ? (b, a) : (a, b);
                    foreach (int other in _links[gone].ToArray())
                    {
                        bool chain = _chainLinks.Contains((Math.Min(gone, other), Math.Max(gone, other)));
                        if (other != kept && Sees(_places[kept], _places[other]))
                        {
                            Link(kept, other);
                            if (chain)
                            {
                                _chainLinks.Add((Math.Min(kept, other), Math.Max(kept, other)));
                            }
                        }
                    }
                    Remove(gone);
                }
            }

            /// <summary>
            /// The piece of the network each node is in (the smallest node index of its piece).
            /// </summary>
            private int[] Pieces()
            {
                int[] piece = new int[_places.Count];
                Array.Fill(piece, -1);
                Stack<int> stack = new Stack<int>();
                foreach (int start in Alive())
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

            /// <summary>
            /// Makes every region one network: the nearest pair of nodes between a region's first piece
            /// and the rest is joined (<see cref="Connect"/>), until it is whole.
            /// </summary>
            private void JoinRegions()
            {
                HashSet<(int, int)> tried = [];
                for (int round = 0; round < 10000; round++)
                {
                    int[] piece = Pieces();
                    bool joined = false;
                    foreach (IGrouping<int, int> region in Alive().GroupBy(i => _labels[i]))
                    {
                        int[] nodes = [.. region];
                        int first = piece[nodes[0]];
                        int[] others = [.. nodes.Where(n => piece[n] != first)];
                        if (others.Length == 0)
                        {
                            continue;
                        }
                        int[] main = [.. nodes.Where(n => piece[n] == first)];
                        (int a, int b) = (-1, -1);
                        float nearest = float.PositiveInfinity;
                        foreach (int m in main)
                        {
                            foreach (int o in others)
                            {
                                float distance = Vector2.DistanceSquared(_places[m], _places[o]);
                                if (distance < nearest && !tried.Contains((m, o)))
                                {
                                    nearest = distance;
                                    (a, b) = (m, o);
                                }
                            }
                        }
                        if (a < 0)
                        {
                            continue;
                        }
                        tried.Add((a, b));
                        Connect(a, b, chain: true);
                        joined = true;
                    }
                    if (!joined)
                    {
                        return;
                    }
                }
            }

            public PathLayerNetwork Result(PathLayer layer)
            {
                int[] index = new int[_places.Count];
                int next = 0;
                ImmutableArray<GeneratedPathNode>.Builder nodes = ImmutableArray.CreateBuilder<GeneratedPathNode>();
                for (int i = 0; i < _places.Count; i++)
                {
                    if (_removed.Contains(i))
                    {
                        index[i] = -1;
                        continue;
                    }
                    index[i] = next++;
                    Vector2 place = _places[i];
                    nodes.Add(new GeneratedPathNode(place.X, place.Y, _roles[i]) { Ref = PathNodeRef.Generated(place.X, place.Y) });
                }
                List<(int, int)> links = [];
                for (int a = 0; a < _places.Count; a++)
                {
                    if (index[a] < 0)
                    {
                        continue;
                    }
                    foreach (int b in _links[a].Where(b => b > a && index[b] >= 0).Order())
                    {
                        links.Add((index[a], index[b]));
                    }
                }
                return new PathLayerNetwork(layer, _spacing, nodes.ToImmutable(), [.. links]);
            }
        }

        #endregion

        #region Air

        /// <summary>
        /// The air network: nodes on a regular grid over the playable area (the whole map without
        /// one), 16 by 16 unless a spacing is given, each linked to the eight around it.
        /// </summary>
        public static PathLayerNetwork GenerateAir(MapDocument document, float? spacing = null)
        {
            MapArea? area = Walkability.PlayableArea(document);
            float x0 = (float)(area?.X0 ?? 0);
            float z0 = (float)(area?.Z0 ?? 0);
            float x1 = (float)(area?.X1 ?? document.Width);
            float z1 = (float)(area?.Z1 ?? document.Height);
            int columns = spacing is float sx and > 0 ? Math.Max(1, (int)MathF.Round((x1 - x0) / sx)) : AirNodesPerSide;
            int rows = spacing is float sz and > 0 ? Math.Max(1, (int)MathF.Round((z1 - z0) / sz)) : AirNodesPerSide;
            float stepX = (x1 - x0) / columns;
            float stepZ = (z1 - z0) / rows;
            List<GeneratedPathNode> nodes = [];
            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    float x = x0 + (column + 0.5f) * stepX;
                    float z = z0 + (row + 0.5f) * stepZ;
                    nodes.Add(new GeneratedPathNode(x, z, PathNodeRole.Fill) { Ref = PathNodeRef.Generated(x, z) });
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
                        if (column > 0)
                        {
                            links.Add((i, i + columns - 1));
                        }
                        links.Add((i, i + columns));
                        if (column + 1 < columns)
                        {
                            links.Add((i, i + columns + 1));
                        }
                    }
                }
            }
            return new PathLayerNetwork(PathLayer.Air, Math.Max(stepX, stepZ), [.. nodes], [.. links]);
        }

        #endregion

        #region Into the map

        /// <summary>
        /// Puts generated networks into the map's markers (the export, into the version it writes):
        /// per layer the new nodes go on the terrain with the next free names of the layer and are
        /// linked both ways. With <paramref name="replace"/> the map's own nodes of the layer go first
        /// (with their places in chains); without it they stay, and each piece of them (nodes linked to
        /// each other) is linked to the nearest generated node one of them sees.
        /// </summary>
        public static MapDocument Apply(MapDocument document, PathNetwork network, bool replace = false)
        {
            foreach (PathLayerNetwork layer in network.Layers)
            {
                NavGrid? grid = network.Navigation is MapNavigation navigation && NavLayerOf(layer.Layer) is NavLayer nav ? navigation[nav] : null;
                document = Apply(document, layer, replace, grid);
            }
            return document;
        }

        private static MapDocument Apply(MapDocument document, PathLayerNetwork network, bool replace, NavGrid? grid)
        {
            MarkerSet markers = document.Markers;
            if (replace)
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

            string prefix = Prefix(network.Layer);
            HashSet<string> names = [.. markers.All.Select(marker => marker.Name)];
            int number = markers.All.Select(marker => NumberAfter(marker.Name, prefix)).Max(n => (int?)n) is int highest ? highest + 1 : 0;
            int nextId = markers.NextId();
            Heightmap heights = document.Terrain.Heightmap;

            int[] ids = new int[network.Nodes.Length];
            List<PathNode> added = [];
            for (int i = 0; i < network.Nodes.Length; i++)
            {
                GeneratedPathNode node = network.Nodes[i];
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
            void Add(int a, int b)
            {
                (adjacency.TryGetValue(a, out List<int>? fromA) ? fromA : adjacency[a] = []).Add(b);
                (adjacency.TryGetValue(b, out List<int>? fromB) ? fromB : adjacency[b] = []).Add(a);
            }
            foreach ((int a, int b) in network.Links)
            {
                Add(ids[a], ids[b]);
            }
            foreach ((int own, int generated) in JoinOwn(markers, network, grid))
            {
                Add(own, ids[generated]);
            }
            PathNode WithLinks(PathNode node) => adjacency.TryGetValue(node.Id, out List<int>? links)
                ? node with { AdjacentTo = [.. node.AdjacentTo.Concat(links.Where(id => !node.AdjacentTo.Contains(id))).Distinct()] }
                : node;

            ImmutableArray<PathNode> pathNodes = [.. markers.PathNodes.Select(node => adjacency.ContainsKey(node.Id) ? WithLinks(node) : node), .. added.Select(WithLinks)];
            return document with { Markers = markers with { PathNodes = pathNodes } };
        }

        /// <summary>
        /// Per piece of the map's own nodes of the layer, the link that joins it to the generated
        /// network: the nearest pair of one of its nodes and a generated node that see each other.
        /// </summary>
        private static IEnumerable<(int Own, int Generated)> JoinOwn(MarkerSet markers, PathLayerNetwork network, NavGrid? grid)
        {
            PathNode[] own = [.. markers.PathNodes.Where(node => node.Layer == network.Layer)];
            if (own.Length == 0 || network.Nodes.Length == 0)
            {
                yield break;
            }
            Dictionary<int, PathNode> byId = own.ToDictionary(node => node.Id);
            HashSet<int> seen = [];
            foreach (PathNode start in own)
            {
                if (!seen.Add(start.Id))
                {
                    continue;
                }
                List<PathNode> piece = [start];
                for (int i = 0; i < piece.Count; i++)
                {
                    foreach (int id in piece[i].AdjacentTo)
                    {
                        if (byId.TryGetValue(id, out PathNode? next) && seen.Add(id))
                        {
                            piece.Add(next);
                        }
                    }
                }
                (int Own, int Generated)? best = null;
                float nearest = float.MaxValue;
                foreach (PathNode node in piece)
                {
                    Vector2 from = new Vector2(node.Position.X, node.Position.Z);
                    for (int g = 0; g < network.Nodes.Length; g++)
                    {
                        float distance = Vector2.DistanceSquared(from, network.Nodes[g].Place);
                        if (distance < nearest && (grid is null || CanWalk(grid, from, network.Nodes[g].Place)))
                        {
                            nearest = distance;
                            best = (node.Id, g);
                        }
                    }
                }
                if (best is (int, int) link)
                {
                    yield return link;
                }
            }
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
    /// The path networks of the AI (user story R-3, <see cref="PathNetworks"/>), generated from the
    /// navigation mesh with the map's settings (<see cref="ProceduralSettings.PathNodes"/>). The result
    /// stays in memory, the base the edits by hand go on (<see cref="PathNetworkEdits"/>); the
    /// export writes it into the markers of the files. Out of date when walkability's inputs or the
    /// settings change; an edit by hand leaves it current.
    /// </summary>
    public sealed class PathNetworkGenerator : IGenerator
    {
        public const string GeneratorId = "pathNetwork";

        public string Id => GeneratorId;

        public string Title => "Path networks";

        public string Story => "R-3";

        public IReadOnlyList<GeneratedInput> Inputs { get; } =
            [GeneratedInput.Heightmap, GeneratedInput.TerrainTypes, GeneratedInput.WaterLevel, GeneratedInput.DepositsAndAreas, GeneratedInput.MapType, GeneratedInput.WalkabilityMarks,
             GeneratedInput.PathNodeSettings];

        /// <summary>
        /// The last walkability or navigation mesh generated: used while it is current for the
        /// version generated from, so the mesh is not made twice.
        /// </summary>
        public GeneratedResult? Walkability { get; set; }

        public GeneratedResult? NavigationMesh { get; set; }

        /// <summary>
        /// Walkability, whose current result it uses rather than making the mesh again.
        /// </summary>
        public IReadOnlyList<string> Reads { get; } = [WalkabilityGenerator.GeneratorId];

        public void Read(GeneratedResult result)
        {
            if (result.Generator.Id == WalkabilityGenerator.GeneratorId)
            {
                Walkability = result;
            }
            else if (result.Generator.Id == NavigationMeshGenerator.GeneratorId)
            {
                NavigationMesh = result;
            }
        }

        public object Generate(MapDocument document) => Generate(document, CancellationToken.None);

        public object Generate(MapDocument document, CancellationToken cancel)
        {
            MapNavigation navigation = Walkability is { } walkability && !walkability.IsStale(document) && walkability.Value is MapNavigation current ? current
                : NavigationMesh is { } mesh && !mesh.IsStale(document) && mesh.Value is Generation.NavigationMesh made ? made.Navigation
                : Generation.Walkability.Generate(document, cancel);
            return PathNetworks.Generate(document, navigation, document.Procedural.PathNodes, cancel);
        }
    }
}
