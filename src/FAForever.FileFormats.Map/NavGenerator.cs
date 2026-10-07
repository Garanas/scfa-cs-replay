namespace FAForever.FileFormats.Map
{
    /// <summary>
    /// Generates the navigational mesh of a map the way the game does for its AI, after
    /// <c>lua/sim/NavGenerator.lua</c> in the FA repository: per layer, which ogrids units can
    /// path over, the regions (labels) they form and the mass and hydrocarbon markers in each.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The game compresses each layer into quadtrees and labels the leaves; this port works on the
    /// ogrids themselves, which gives the same regions:
    /// </para>
    /// <list type="bullet">
    /// <item>A quadtree leaf is one ogrid, or a block of ogrids that are all pathable or all not.
    /// Where a block of the compression threshold's size is mixed, the game makes all of it
    /// unpathable; so does this port (<see cref="ApplyCompression"/>).</item>
    /// <item>The game links diagonal leaves only when one of the two leaves beside both is
    /// pathable, so its regions are those of ogrids linked by their sides.</item>
    /// </list>
    /// <para>
    /// Known differences: the game numbers its labels in another order, counts the area of a
    /// label's first leaf twice (<c>GenerateLabels</c> assigns <c>self.Label</c>, the tree, for the
    /// first leaf), and adds hydrocarbons to the extractor count; here the area is the number of
    /// ogrids and hydrocarbons have their own list. A label whose first leaf has no pathable
    /// neighbours keeps label 0 in the game; here it is a label like any other.
    /// </para>
    /// </remarks>
    public static class NavGenerator
    {
        /// <summary>
        /// The largest height difference along an ogrid's side that land units can cross.
        /// </summary>
        public const float MaxHeightDifference = 0.75f;

        /// <summary>
        /// The deepest water amphibious units path through.
        /// </summary>
        public const float MaxWaterDepthAmphibious = 25;

        /// <summary>
        /// The shallowest water naval units path through.
        /// </summary>
        public const float MinWaterDepthNaval = 1.5f;

        /// <summary>
        /// The shallowest water hover units cross regardless of the slope below it.
        /// </summary>
        public const float MinWaterDepthHover = 1;

        /// <summary>
        /// The area of an ogrid in square kilometres: 20 by 20 metres.
        /// </summary>
        public const double OGridSquaredToKMSquared = 0.02 * 0.02;

        /// <summary>
        /// Labels smaller than this many ogrids (0.16 km²) without resource markers are culled.
        /// </summary>
        public const int CullBelowCells = 400;

        /// <summary>
        /// The number of quadtrees per axis.
        /// </summary>
        public const int LabelCompressionTreesPerAxis = 16;

        /// <summary>
        /// The playable area the game generates the mesh for: the first area of the save file when
        /// it is larger than 32 by 32 ogrids, which is the area the map editor's script makes
        /// playable (<c>ScenarioFramework.SetPlayableArea('AREA_1')</c>). Only skirmish maps use it;
        /// null means the whole map.
        /// </summary>
        public static MapArea? FindPlayableArea(MapScenario scenario, MapSave save) =>
            scenario.Type == "skirmish" && save.Areas.FirstOrDefault() is { } area && area.X1 - area.X0 > 32 && area.Z1 - area.Z0 > 32
                ? area
                : null;

        /// <summary>
        /// Generates the mesh of a map.
        /// </summary>
        /// <param name="playableArea">The playable area, e.g. from <see cref="FindPlayableArea"/>; null for the whole map.</param>
        public static MapNavigation Generate(Scmap scmap, MapSave save, MapArea? playableArea = null) =>
            Generate(scmap.Heightmap, scmap.Water.HasWater ? scmap.Water.Elevation : null, scmap.TerrainTypes, save.Markers, playableArea);

        /// <summary>
        /// Generates the mesh from the parts of a map it depends on.
        /// </summary>
        /// <param name="waterElevation">The height of the water surface; null for a map without water.</param>
        /// <param name="terrainTypes">The terrain type code of every ogrid, row by row.</param>
        /// <param name="markers">The markers; the mass and hydrocarbon ones are assigned to labels.</param>
        public static MapNavigation Generate(ScmapHeightmap heightmap, float? waterElevation, byte[] terrainTypes, IReadOnlyList<MapMarker> markers, MapArea? playableArea = null)
        {
            int width = heightmap.Width;
            int height = heightmap.Height;
            if (terrainTypes.Length != width * height)
            {
                throw new ArgumentException($"Expected {width * height} terrain types, got {terrainTypes.Length}", nameof(terrainTypes));
            }

            int mapSize = Math.Max(width, height);
            int threshold = GetCompressionThreshold(mapSize);
            int treeSize = mapSize / LabelCompressionTreesPerAxis;

            Caches caches = PopulateCaches(heightmap, waterElevation, terrainTypes, playableArea);
            bool hasWater = Array.Exists(caches.Depth, depth => depth > 0);

            LayerBuilder land = new LayerBuilder(NavLayer.Land, width, height, Pathable(caches, LandRule));
            LayerBuilder water = new LayerBuilder(NavLayer.Water, width, height, Pathable(caches, NavalRule));
            LayerBuilder hover = hasWater ? new LayerBuilder(NavLayer.Hover, width, height, Pathable(caches, HoverRule)) : land;
            LayerBuilder amphibious = hasWater ? new LayerBuilder(NavLayer.Amphibious, width, height, Pathable(caches, AmphibiousRule)) : land;

            LayerBuilder[] layers = hasWater ? [land, water, hover, amphibious] : [land, water];
            foreach (LayerBuilder layer in layers)
            {
                // naval units compress twice as coarse (GenerateCompressionGrids)
                int blockSize = Math.Min(layer.Layer == NavLayer.Water ? 2 * threshold : threshold, treeSize / 2);
                ApplyCompression(layer.Pathable, width, height, blockSize);
                layer.GenerateLabels();
            }

            // GenerateMarkerMetadata: only the land layer, and on maps with water the hover and amphibious layers
            LayerBuilder[] withMarkers = hasWater ? [land, hover, amphibious] : [land];
            foreach (LayerBuilder layer in withMarkers)
            {
                layer.AddMarkers(markers);
            }

            // GenerateCullLabels
            foreach (LayerBuilder layer in layers)
            {
                layer.Cull();
            }

            NavGrid landGrid = land.Build();
            return new MapNavigation(
                hasWater,
                threshold,
                landGrid,
                water.Build(),
                hasWater ? hover.Build() : landGrid,
                hasWater ? amphibious.Build() : landGrid);
        }

        /// <summary>
        /// The compression threshold of <c>Generate</c>: 1, doubled from 1024 ogrids (20 km) and
        /// again from 2048 (40 km).
        /// </summary>
        public static int GetCompressionThreshold(int mapSize) =>
            mapSize >= 2048 ? 4 : mapSize >= 1024 ? 2 : 1;

        #region Caches

        /// <summary>
        /// What <c>PopulateCaches</c> computes per ogrid: whether its sides are flat enough, the
        /// average water depth at its corners, and whether it is in the playable area on a
        /// terrain type that does not block.
        /// </summary>
        private sealed record Caches(bool[] Flat, float[] Depth, bool[] Open);

        private static Caches PopulateCaches(ScmapHeightmap heightmap, float? waterElevation, byte[] terrainTypes, MapArea? playableArea)
        {
            int width = heightmap.Width;
            int height = heightmap.Height;
            int stride = width + 1;
            ushort[] samples = heightmap.Samples;
            float scale = heightmap.Scale;

            // GetTerrainHeight and GetSurfaceHeight at the corners of the ogrids
            float[] terrain = new float[samples.Length];
            float[] depth = new float[samples.Length];
            for (int i = 0; i < samples.Length; i++)
            {
                terrain[i] = samples[i] * scale;
                float surface = waterElevation is { } water ? MathF.Max(terrain[i], water) : terrain[i];
                depth[i] = surface - terrain[i];
            }

            double tlx = playableArea?.X0 ?? 0;
            double tlz = playableArea?.Z0 ?? 0;
            double brx = playableArea?.X1 ?? width;
            double brz = playableArea?.Z1 ?? height;

            bool[] flat = new bool[width * height];
            float[] averageDepth = new float[width * height];
            bool[] open = new bool[width * height];
            for (int z = 0; z < height; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    int topLeft = z * stride + x;
                    int topRight = topLeft + 1;
                    int bottomLeft = topLeft + stride;
                    int bottomRight = bottomLeft + 1;
                    int cell = z * width + x;

                    flat[cell] =
                        MathF.Abs(terrain[topLeft] - terrain[topRight]) < MaxHeightDifference &&
                        MathF.Abs(terrain[bottomLeft] - terrain[bottomRight]) < MaxHeightDifference &&
                        MathF.Abs(terrain[topLeft] - terrain[bottomLeft]) < MaxHeightDifference &&
                        MathF.Abs(terrain[topRight] - terrain[bottomRight]) < MaxHeightDifference;

                    averageDepth[cell] = (depth[topLeft] + depth[bottomLeft] + depth[topRight] + depth[bottomRight]) * 0.25f;

                    // the game samples the playable area and the terrain type at the ogrid's
                    // far corner (absX = bx + x for x = 1 .. size); past the last row or column
                    // that is outside the map, where the terrain type is Default
                    int ax = x + 1;
                    int az = z + 1;
                    bool playable = tlx <= ax && brx >= ax && tlz <= az && brz >= az;
                    bool blocking = ax < width && az < height && TerrainTypes.Get(terrainTypes[az * width + ax]).Blocking;
                    open[cell] = playable && !blocking;
                }
            }

            return new Caches(flat, averageDepth, open);
        }

        private delegate bool PathingRule(bool flat, float depth, bool open);

        // ComputeLandPathingMatrix
        private static bool LandRule(bool flat, float depth, bool open) => depth <= 0 && open && flat;

        // ComputeNavalPathingMatrix
        private static bool NavalRule(bool flat, float depth, bool open) => depth >= MinWaterDepthNaval && open;

        // ComputeHoverPathingMatrix
        private static bool HoverRule(bool flat, float depth, bool open) => open && (depth >= MinWaterDepthHover || flat);

        // ComputeAmphPathingMatrix
        private static bool AmphibiousRule(bool flat, float depth, bool open) => depth <= MaxWaterDepthAmphibious && open && flat;

        private static bool[] Pathable(Caches caches, PathingRule rule)
        {
            bool[] pathable = new bool[caches.Flat.Length];
            for (int i = 0; i < pathable.Length; i++)
            {
                pathable[i] = rule(caches.Flat[i], caches.Depth[i], caches.Open[i]);
            }
            return pathable;
        }

        /// <summary>
        /// The effect of <c>CompressedLabelTree.Compress</c>: a quadrant that is not uniform is
        /// subdivided until its size reaches the threshold, and then made unpathable as a whole.
        /// So every aligned block of that size with both pathable and unpathable ogrids becomes
        /// unpathable.
        /// </summary>
        internal static void ApplyCompression(bool[] pathable, int width, int height, int blockSize)
        {
            if (blockSize <= 1)
            {
                return;
            }

            for (int bz = 0; bz < height; bz += blockSize)
            {
                for (int bx = 0; bx < width; bx += blockSize)
                {
                    int zEnd = Math.Min(bz + blockSize, height);
                    int xEnd = Math.Min(bx + blockSize, width);
                    bool first = pathable[bz * width + bx];
                    bool uniform = true;
                    for (int z = bz; z < zEnd && uniform; z++)
                    {
                        for (int x = bx; x < xEnd; x++)
                        {
                            if (pathable[z * width + x] != first)
                            {
                                uniform = false;
                                break;
                            }
                        }
                    }

                    if (!uniform)
                    {
                        for (int z = bz; z < zEnd; z++)
                        {
                            Array.Fill(pathable, false, z * width + bx, xEnd - bx);
                        }
                    }
                }
            }
        }

        #endregion

        /// <summary>
        /// One layer while it is generated: its pathable ogrids, then its labels and their markers.
        /// </summary>
        private sealed class LayerBuilder(NavLayer layer, int width, int height, bool[] pathable)
        {
            public NavLayer Layer => layer;

            public bool[] Pathable => pathable;

            private int[] _cells = [];
            private readonly List<int> _sizes = [];
            private readonly List<List<MapMarker>> _extractors = [];
            private readonly List<List<MapMarker>> _hydrocarbons = [];

            /// <summary>
            /// Labels the regions of ogrids linked by their sides (<c>GenerateLabels</c>), from 1.
            /// </summary>
            public void GenerateLabels()
            {
                _cells = new int[pathable.Length];
                Array.Fill(_cells, -1);

                Stack<int> stack = new Stack<int>();
                for (int start = 0; start < pathable.Length; start++)
                {
                    if (!pathable[start] || _cells[start] != -1)
                    {
                        continue;
                    }

                    int label = _sizes.Count + 1;
                    int size = 0;
                    _cells[start] = label;
                    stack.Push(start);
                    while (stack.Count > 0)
                    {
                        int cell = stack.Pop();
                        size++;
                        int x = cell % width;
                        int z = cell / width;
                        Visit(x > 0 ? cell - 1 : -1);
                        Visit(x < width - 1 ? cell + 1 : -1);
                        Visit(z > 0 ? cell - width : -1);
                        Visit(z < height - 1 ? cell + width : -1);
                    }

                    _sizes.Add(size);
                    _extractors.Add([]);
                    _hydrocarbons.Add([]);

                    void Visit(int neighbor)
                    {
                        if (neighbor >= 0 && pathable[neighbor] && _cells[neighbor] == -1)
                        {
                            _cells[neighbor] = label;
                            stack.Push(neighbor);
                        }
                    }
                }
            }

            /// <summary>
            /// Adds the mass and hydrocarbon markers to the label of the ogrid they stand on.
            /// </summary>
            public void AddMarkers(IReadOnlyList<MapMarker> markers)
            {
                foreach (MapMarker marker in markers)
                {
                    List<List<MapMarker>>? lists = marker.Type switch
                    {
                        "Mass" => _extractors,
                        "Hydrocarbon" => _hydrocarbons,
                        _ => null,
                    };
                    int x = (int)MathF.Floor(marker.Position.X);
                    int z = (int)MathF.Floor(marker.Position.Z);
                    if (lists is null || (uint)x >= (uint)width || (uint)z >= (uint)height)
                    {
                        continue;
                    }

                    int label = _cells[z * width + x];
                    if (label > 0)
                    {
                        lists[label - 1].Add(marker);
                    }
                }
            }

            /// <summary>
            /// Removes the small labels without resources (<c>GenerateCullLabels</c>) and numbers
            /// the others again from 1.
            /// </summary>
            public void Cull()
            {
                int[] renumbered = new int[_sizes.Count + 1];
                int next = 1;
                for (int i = 0; i < _sizes.Count; i++)
                {
                    bool culled = _sizes[i] < CullBelowCells && _extractors[i].Count == 0 && _hydrocarbons[i].Count == 0;
                    renumbered[i + 1] = culled ? -1 : next++;
                }

                for (int cell = 0; cell < _cells.Length; cell++)
                {
                    if (_cells[cell] > 0)
                    {
                        _cells[cell] = renumbered[_cells[cell]];
                    }
                }

                for (int i = _sizes.Count - 1; i >= 0; i--)
                {
                    if (renumbered[i + 1] == -1)
                    {
                        _sizes.RemoveAt(i);
                        _extractors.RemoveAt(i);
                        _hydrocarbons.RemoveAt(i);
                    }
                }
            }

            public NavGrid Build() => new NavGrid(
                layer,
                width,
                height,
                _cells,
                _sizes.Select((size, i) => new NavLabel(i + 1, layer, size, _extractors[i], _hydrocarbons[i])).ToList());
        }
    }
}
