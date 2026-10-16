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
    /// The navigation mesh of a map as the Procedural mode shows it: the game's navigational mesh
    /// (<see cref="MapNavigation"/>) and, per layer, its connected regions with their starts and deposits.
    /// </summary>
    public sealed record NavigationMesh(MapNavigation Navigation, IReadOnlyDictionary<NavLayer, IReadOnlyList<NavRegion>> Regions)
    {
        /// <summary>
        /// The layers, in the order the Procedural mode lists them.
        /// </summary>
        public static IReadOnlyList<NavLayer> Layers { get; } = [NavLayer.Land, NavLayer.Hover, NavLayer.Amphibious, NavLayer.Water];

        /// <summary>
        /// The regions of a layer, the largest first.
        /// </summary>
        public IReadOnlyList<NavRegion> RegionsOf(NavLayer layer) => Regions.GetValueOrDefault(layer) ?? [];

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
        /// Makes the connected regions of a generated mesh.
        /// </summary>
        /// <param name="cancel">Stops it between layers.</param>
        public static NavigationMesh Of(MapDocument document, MapNavigation navigation, CancellationToken cancel = default)
        {
            IReadOnlyList<Spawn> spawns = document.Markers.Spawns;
            Dictionary<NavLayer, IReadOnlyList<NavRegion>> regions = [];
            foreach (NavLayer layer in Layers)
            {
                cancel.ThrowIfCancellationRequested();
                NavGrid grid = navigation[layer];
                regions[layer] = [.. grid.Labels
                    .Select(label => new NavRegion(label.Id, layer, label.Cells,
                        [.. spawns.Where(spawn => LabelNear(grid, spawn.Position) == label).Select(spawn => spawn.Army)],
                        label.Extractors.Count, label.Hydrocarbons.Count))
                    .OrderByDescending(region => region.Cells)
                    .ThenBy(region => region.Id)];
            }
            return new NavigationMesh(navigation, regions);
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
    }

    /// <summary>
    /// The navigation mesh (the Procedural mode): the game's navigational mesh with its connected
    /// regions, made from the heightmap, the terrain types, the water and the
    /// walkability marks as walkability is; it uses walkability's result while that is current.
    /// The result stays in memory.
    /// </summary>
    public sealed class NavigationMeshGenerator : IGenerator
    {
        public const string GeneratorId = "navigationMesh";

        public string Id => GeneratorId;

        public string Title => "Connected regions";

        public string Story => "T-8";

        public IReadOnlyList<GeneratedInput> Inputs { get; } =
            [GeneratedInput.Heightmap, GeneratedInput.TerrainTypes, GeneratedInput.WaterLevel, GeneratedInput.DepositsAndAreas, GeneratedInput.MapType, GeneratedInput.WalkabilityMarks, GeneratedInput.CliffFaces,
             GeneratedInput.Spawns];

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

        // the mesh holds walkability's grids when it was made from a current walkability: they count once
        public IEnumerable<(object Buffer, long Bytes)> MemoryOf(object value) =>
            value is NavigationMesh mesh ? WalkabilityGenerator.Memory(mesh.Navigation) : [];
    }
}
