using System.Numerics;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Generation
{
    /// <summary>
    /// A region of one layer of the navigation mesh: ogrids units of that layer can path between,
    /// with the starts and deposits in it.
    /// </summary>
    /// <param name="Id">The label's number in its layer (<see cref="NavLabel.Id"/>).</param>
    /// <param name="Cells">How many ogrids it covers.</param>
    /// <param name="Starts">The armies whose start position lies in it, in the order of the spawns.</param>
    /// <param name="Mass">The mass deposits the game assigns to it (none for ships).</param>
    /// <param name="Hydrocarbons">The hydrocarbon deposits the game assigns to it (none for ships).</param>
    public sealed record NavRegion(int Id, NavLayer Layer, int Cells, IReadOnlyList<string> Starts, int Mass, int Hydrocarbons)
    {
        /// <summary>
        /// The area in square kilometres; an ogrid is 20 by 20 metres.
        /// </summary>
        public double Area => Cells * NavGenerator.OGridSquaredToKMSquared;
    }

    /// <summary>
    /// A route between two starts over one layer.
    /// </summary>
    /// <param name="From">The army of the first start.</param>
    /// <param name="Straight">The straight distance between the starts, in ogrids.</param>
    /// <param name="Route">The shortest route, its points in ogrids; null when units of this layer
    /// cannot get from one start to the other.</param>
    public sealed record NavStartRoute(string From, string To, NavLayer Layer, float Straight, NavRoute? Route)
    {
        /// <summary>
        /// How much longer the route is than the straight line (1.3: 30 % longer); null without a route.
        /// </summary>
        public float? Detour => Route is null || Straight <= 0 ? null : Route.Length / Straight;
    }

    /// <summary>
    /// The navigation mesh of a map as the Procedural mode shows it: the game's navigational mesh
    /// (<see cref="MapNavigation"/>), per layer its regions with their starts and deposits, and the
    /// routes between starts that the settings ask for (<see cref="NavigationSettings.Routes"/>).
    /// </summary>
    public sealed record NavigationMesh(MapNavigation Navigation, IReadOnlyDictionary<NavLayer, IReadOnlyList<NavRegion>> Regions, IReadOnlyDictionary<NavLayer, IReadOnlyList<NavStartRoute>> Routes)
    {
        /// <summary>
        /// The layers, in the order the Procedural mode lists them.
        /// </summary>
        public static IReadOnlyList<NavLayer> Layers { get; } = [NavLayer.Land, NavLayer.Hover, NavLayer.Amphibious, NavLayer.Water];

        /// <summary>
        /// The regions of a layer, the largest first.
        /// </summary>
        public IReadOnlyList<NavRegion> RegionsOf(NavLayer layer) => Regions.GetValueOrDefault(layer) ?? [];

        public IReadOnlyList<NavStartRoute> RoutesOf(NavLayer layer) => Routes.GetValueOrDefault(layer) ?? [];

        /// <summary>
        /// The armies whose start lies in no region of a layer, nor within <see cref="StartReach"/>
        /// ogrids of one: their units of that layer cannot move from the start.
        /// </summary>
        public IReadOnlyList<string> StrandedOf(NavLayer layer, IReadOnlyList<Spawn> spawns)
        {
            HashSet<string> placed = [.. RegionsOf(layer).SelectMany(region => region.Starts)];
            return [.. spawns.Select(spawn => spawn.Army).Where(army => !placed.Contains(army))];
        }

        /// <summary>
        /// The region of a layer at a place of the map, or null where units of that layer cannot go.
        /// </summary>
        public NavRegion? RegionAt(NavLayer layer, float x, float z) =>
            Navigation[layer].GetLabel(new Vector3(x, 0, z)) is NavLabel label ? RegionsOf(layer).FirstOrDefault(region => region.Id == label.Id) : null;

        /// <summary>
        /// Makes the regions and routes of a generated mesh.
        /// </summary>
        /// <param name="cancel">Stops it between routes.</param>
        public static NavigationMesh Of(MapDocument document, MapNavigation navigation, CancellationToken cancel = default)
        {
            IReadOnlyList<Spawn> spawns = document.Markers.Spawns;
            Dictionary<NavLayer, IReadOnlyList<NavRegion>> regions = [];
            Dictionary<NavLayer, IReadOnlyList<NavStartRoute>> routes = [];
            Dictionary<NavGrid, IReadOnlyList<NavStartRoute>> routed = [];
            (int A, int B)[] pairs = Pairs(spawns, document.Procedural.Navigation.Routes);
            foreach (NavLayer layer in Layers)
            {
                NavGrid grid = navigation[layer];
                regions[layer] = [.. grid.Labels
                    .Select(label => new NavRegion(label.Id, layer, label.Cells,
                        [.. spawns.Where(spawn => LabelNear(grid, spawn.Position) == label).Select(spawn => spawn.Army)],
                        label.Extractors.Count, label.Hydrocarbons.Count))
                    .OrderByDescending(region => region.Cells)
                    .ThenBy(region => region.Id)];
                // without water the hover and amphibious layers are the land layer: routed once
                if (!routed.TryGetValue(grid, out IReadOnlyList<NavStartRoute>? found))
                {
                    found = Route(grid, navigation.CompressionThreshold, spawns, pairs, cancel);
                    routed[grid] = found;
                }
                routes[layer] = [.. found.Select(route => route with { Layer = layer })];
            }
            return new NavigationMesh(navigation, regions, routes);
        }

        /// <summary>
        /// The pairs of starts to route between: each start with the one nearest to it in a straight
        /// line (each pair once), or every pair.
        /// </summary>
        public static (int A, int B)[] Pairs(IReadOnlyList<Spawn> spawns, RoutePairs choice)
        {
            if (choice == RoutePairs.None || spawns.Count < 2)
            {
                return [];
            }
            HashSet<(int, int)> pairs = [];
            for (int a = 0; a < spawns.Count; a++)
            {
                if (choice == RoutePairs.All)
                {
                    for (int b = a + 1; b < spawns.Count; b++)
                    {
                        pairs.Add((a, b));
                    }
                    continue;
                }
                int nearest = -1;
                float best = float.MaxValue;
                for (int b = 0; b < spawns.Count; b++)
                {
                    float distance = Flat(spawns[a].Position, spawns[b].Position);
                    if (b != a && distance < best)
                    {
                        best = distance;
                        nearest = b;
                    }
                }
                pairs.Add((Math.Min(a, nearest), Math.Max(a, nearest)));
            }
            return [.. pairs.Order()];
        }

        /// <summary>
        /// How far from a start the region it belongs to may lie, in ogrids: a start on ground units
        /// cannot path over (a plateau's top, the middle of a crowded start) belongs to the region its
        /// units walk out into.
        /// </summary>
        public const int StartReach = 8;

        /// <summary>
        /// The region of a layer at a position, else the nearest within <see cref="StartReach"/> ogrids;
        /// null when there is none.
        /// </summary>
        public static NavLabel? LabelNear(NavGrid grid, Vector3 position)
        {
            if (grid.GetLabel(position) is NavLabel here)
            {
                return here;
            }
            int cx = (int)MathF.Floor(position.X);
            int cz = (int)MathF.Floor(position.Z);
            NavLabel? best = null;
            int bestDistance = int.MaxValue;
            for (int z = Math.Max(0, cz - StartReach); z <= Math.Min(grid.Height - 1, cz + StartReach); z++)
            {
                for (int x = Math.Max(0, cx - StartReach); x <= Math.Min(grid.Width - 1, cx + StartReach); x++)
                {
                    int id = grid.Cells[z * grid.Width + x];
                    int distance = (x - cx) * (x - cx) + (z - cz) * (z - cz);
                    if (id > 0 && distance < bestDistance && distance <= StartReach * StartReach)
                    {
                        bestDistance = distance;
                        best = grid.Labels[id - 1];
                    }
                }
            }
            return best;
        }

        private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));

        /// <summary>
        /// The routes over a layer, searched in blocks of the game's compression threshold (as its
        /// quadtrees treat the map: a 4096 map in blocks of 4 ogrids), so a route over the largest
        /// maps takes a moment.
        /// </summary>
        private static IReadOnlyList<NavStartRoute> Route(NavGrid grid, int block, IReadOnlyList<Spawn> spawns, (int A, int B)[] pairs, CancellationToken cancel)
        {
            if (pairs.Length == 0)
            {
                return [];
            }
            NavGrid coarse = grid.Coarsen(block);
            List<NavStartRoute> routes = [];
            foreach ((int a, int b) in pairs)
            {
                cancel.ThrowIfCancellationRequested();
                Vector3 from = spawns[a].Position;
                Vector3 to = spawns[b].Position;
                NavRoute? route = null;
                if (LabelNear(grid, from) is NavLabel label && LabelNear(grid, to) == label
                    && CellOf(coarse, label, from, block) is Vector3 start && CellOf(coarse, label, to, block) is Vector3 end
                    && NavPaths.FindRoute(coarse, start, end) is NavRoute found)
                {
                    route = new NavRoute(found.Length * block, [.. found.Points.Select(point => point * block)]);
                }
                routes.Add(new NavStartRoute(spawns[a].Army, spawns[b].Army, grid.Layer, Flat(from, to), route));
            }
            return routes;
        }

        /// <summary>
        /// The place on the coarse grid of a position, moved to the nearest block of its region when
        /// its own block mixes regions or blocked ogrids.
        /// </summary>
        private static Vector3? CellOf(NavGrid coarse, NavLabel label, Vector3 position, int block)
        {
            int cx = (int)MathF.Floor(position.X / block);
            int cz = (int)MathF.Floor(position.Z / block);
            for (int radius = 0; radius <= 4; radius++)
            {
                for (int dz = -radius; dz <= radius; dz++)
                {
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        int x = cx + dx;
                        int z = cz + dz;
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) == radius && (uint)x < (uint)coarse.Width && (uint)z < (uint)coarse.Height
                            && coarse.Cells[z * coarse.Width + x] == label.Id)
                        {
                            return new Vector3(x + 0.5f, 0, z + 0.5f);
                        }
                    }
                }
            }
            return null;
        }
    }

    /// <summary>
    /// The navigation mesh (the Procedural mode): the game's navigational mesh with its regions and
    /// the routes between starts, made from the heightmap, the terrain types, the water and the
    /// walkability marks as walkability is; it uses walkability's result while that is current.
    /// The result stays in memory; the project keeps only its settings.
    /// </summary>
    public sealed class NavigationMeshGenerator : IGenerator
    {
        public const string GeneratorId = "navigationMesh";

        public string Id => GeneratorId;

        public string Title => "Navigation mesh";

        public string Story => "T-8";

        public IReadOnlyList<GeneratedInput> Inputs { get; } =
            [GeneratedInput.Heightmap, GeneratedInput.TerrainTypes, GeneratedInput.WaterLevel, GeneratedInput.DepositsAndAreas, GeneratedInput.MapType, GeneratedInput.WalkabilityMarks,
             GeneratedInput.Spawns, GeneratedInput.NavigationSettings];

        public IReadOnlyList<string> Reads { get; } = [WalkabilityGenerator.GeneratorId];

        /// <summary>
        /// The last walkability, used while it is current for the version generated from.
        /// </summary>
        public GeneratedResult? Walkability { get; set; }

        public void Read(GeneratedResult result)
        {
            if (result.Generator.Id == WalkabilityGenerator.GeneratorId)
            {
                Walkability = result;
            }
        }

        public object Generate(MapDocument document) => Generate(document, CancellationToken.None);

        public object Generate(MapDocument document, CancellationToken cancel)
        {
            GeneratedResult? walkability = Walkability;
            MapNavigation navigation = walkability is not null && !walkability.IsStale(document) && walkability.Value is MapNavigation current
                ? current
                : Generation.Walkability.Generate(document, cancel);
            return NavigationMesh.Of(document, navigation, cancel);
        }
    }
}
