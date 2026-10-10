using System.Numerics;
using FAForever.FileFormats.Lua;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Generation
{
    /// <summary>
    /// Where units can go on a map (user story T-7): the navigational mesh the game generates for its
    /// AI (<see cref="NavGenerator"/> of FileFormats.Map, after <c>lua/sim/NavGenerator.lua</c>), made
    /// from the editor's document. This is the one place that turns the document into what the
    /// generator reads; the rules themselves stay in FileFormats.Map.
    /// </summary>
    public static class Walkability
    {
        private static readonly LuaData.Table NoFields = new LuaData.Table(new Dictionary<string, LuaData>());

        /// <summary>
        /// Generates the mesh of a version of the document: per layer (land, naval, hover,
        /// amphibious) the label of every ogrid, positive where units of that layer can path.
        /// </summary>
        /// <param name="cancel">Stops it between its steps.</param>
        /// <exception cref="OperationCanceledException"><paramref name="cancel"/> was cancelled.</exception>
        public static MapNavigation Generate(MapDocument document, CancellationToken cancel = default) => NavGenerator.Generate(
            HeightmapOf(document.Terrain.Heightmap),
            WaterLevel(document),
            Editing.WalkabilityMarks.TerrainTypes(document),
            ResourceMarkers(document.Markers),
            PlayableArea(document),
            Editing.WalkabilityMarks.ForcedOpen(document),
            cancel);

        /// <summary>
        /// The heightmap as the game's file holds it.
        /// </summary>
        public static ScmapHeightmap HeightmapOf(Heightmap heightmap) =>
            new ScmapHeightmap(heightmap.Width, heightmap.Height, heightmap.Scale, heightmap.Samples.ToArray());

        /// <summary>
        /// The height of the water surface, or null for a map without water.
        /// </summary>
        public static float? WaterLevel(MapDocument document) => document.Water.HasWater ? document.Water.Elevation : null;

        /// <summary>
        /// The mass and hydrocarbon deposits as the save file's markers (<c>Mass</c>, <c>Hydrocarbon</c>),
        /// which the generator assigns to labels; the other markers do not matter to it.
        /// </summary>
        public static IReadOnlyList<MapMarker> ResourceMarkers(MarkerSet markers) =>
            [.. markers.Resources.Select(resource => new MapMarker(
                resource.Name,
                resource.Kind == ResourceKind.Mass ? "Mass" : "Hydrocarbon",
                resource.Position,
                resource.Orientation,
                NoFields))];

        /// <summary>
        /// The playable area the game generates the mesh for, on a skirmish map: the one the scenario
        /// names (<see cref="ScenarioInfo.PlayableArea"/>), else the first area when it is larger than
        /// 32 by 32 ogrids (<see cref="NavGenerator.FindPlayableArea"/>); null for the whole map.
        /// </summary>
        public static MapArea? PlayableArea(MapDocument document)
        {
            if (document.Scenario.Type != ScenarioType.Skirmish || document.PlayableArea is not { } area)
            {
                return null;
            }
            bool named = document.Scenario.PlayableArea == area.Name;
            return named || (area.X1 - area.X0 > 32 && area.Z1 - area.Z0 > 32)
                ? new MapArea(area.Name, area.X0, area.Z0, area.X1, area.Z1)
                : null;
        }

        /// <summary>
        /// Whether units of a layer can path over an ogrid: it has a label. An ogrid of a region the
        /// game culls (small, without deposits) has none.
        /// </summary>
        public static bool IsPassable(NavGrid grid, int x, int z) => grid.Cells[z * grid.Width + x] > 0;
    }

    /// <summary>
    /// What happened to an ogrid between two generations of the walkability.
    /// </summary>
    public enum WalkabilityChange : byte
    {
        Same,

        /// <summary>Blocked before, passable now.</summary>
        Opened,

        /// <summary>Passable before, blocked now.</summary>
        Blocked,
    }

    /// <summary>
    /// A place where two generations of the walkability differ in how the regions connect: a new
    /// passage that joins regions that were apart, or a path that closed and split a region.
    /// </summary>
    /// <param name="Place">An ogrid that changed there, at its centre.</param>
    /// <param name="Regions">How many regions were joined, or how many a region split into.</param>
    public sealed record WalkabilityLink(Vector2 Place, int Regions);

    /// <summary>
    /// The difference between two generations of one layer's walkability (user stories T-7, F-14):
    /// per ogrid whether it opened or was blocked, the counts, and where regions were joined or split.
    /// </summary>
    /// <param name="Cells">Per ogrid, row by row, <see cref="Width"/> by <see cref="Height"/>.</param>
    public sealed record WalkabilityChanges(
        NavLayer Layer,
        int Width,
        int Height,
        WalkabilityChange[] Cells,
        int Opened,
        int Blocked,
        IReadOnlyList<WalkabilityLink> Passages,
        IReadOnlyList<WalkabilityLink> Closures)
    {
        public bool IsEmpty => Opened == 0 && Blocked == 0;

        /// <summary>
        /// Compares a layer of two generations; null when the map's size changed between them.
        /// </summary>
        public static WalkabilityChanges? Compare(MapNavigation before, MapNavigation after, NavLayer layer)
        {
            NavGrid old = before[layer];
            NavGrid now = after[layer];
            if (old.Width != now.Width || old.Height != now.Height)
            {
                return null;
            }

            int width = now.Width;
            int[] oldCells = old.Cells;
            int[] newCells = now.Cells;
            WalkabilityChange[] cells = new WalkabilityChange[newCells.Length];
            int opened = 0;
            int blocked = 0;
            // the regions each new region overlaps, and the other way round
            HashSet<(int New, int Old)> overlaps = [];
            for (int i = 0; i < newCells.Length; i++)
            {
                bool was = oldCells[i] > 0;
                bool isNow = newCells[i] > 0;
                if (was && isNow)
                {
                    overlaps.Add((newCells[i], oldCells[i]));
                }
                else if (isNow)
                {
                    cells[i] = WalkabilityChange.Opened;
                    opened++;
                }
                else if (was)
                {
                    cells[i] = WalkabilityChange.Blocked;
                    blocked++;
                }
            }

            // a new region over two or more old ones joins them: through the ogrids that opened in it
            HashSet<int> joining = [.. overlaps.GroupBy(pair => pair.New).Where(group => group.Count() > 1).Select(group => group.Key)];
            // an old region under two or more new ones split: where ogrids were blocked in it
            HashSet<int> splitting = [.. overlaps.GroupBy(pair => pair.Old).Where(group => group.Count() > 1).Select(group => group.Key)];
            Dictionary<int, int> joined = overlaps.GroupBy(pair => pair.New).ToDictionary(group => group.Key, group => group.Count());
            Dictionary<int, int> split = overlaps.GroupBy(pair => pair.Old).ToDictionary(group => group.Key, group => group.Count());

            List<WalkabilityLink> passages = [.. Places(cells, newCells, width, WalkabilityChange.Opened, joining).Select(place => new WalkabilityLink(place.Value, joined[place.Key]))];
            List<WalkabilityLink> closures = [.. Places(cells, oldCells, width, WalkabilityChange.Blocked, splitting).Select(place => new WalkabilityLink(place.Value, split[place.Key]))];
            return new WalkabilityChanges(layer, width, now.Height, cells, opened, blocked, passages, closures);
        }

        /// <summary>
        /// Per region of <paramref name="labels"/> among <paramref name="regions"/>, the changed ogrid
        /// of the region nearest the centre of its changed ogrids.
        /// </summary>
        private static IEnumerable<KeyValuePair<int, Vector2>> Places(WalkabilityChange[] cells, int[] labels, int width, WalkabilityChange change, HashSet<int> regions)
        {
            if (regions.Count == 0)
            {
                return [];
            }
            Dictionary<int, (double X, double Z, int Count)> sums = [];
            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i] == change && regions.Contains(labels[i]))
                {
                    (double x, double z, int count) = sums.GetValueOrDefault(labels[i]);
                    sums[labels[i]] = (x + i % width, z + i / width, count + 1);
                }
            }
            Dictionary<int, (Vector2 Centre, float Distance, Vector2 Place)> nearest = sums.ToDictionary(
                pair => pair.Key,
                pair => (new Vector2((float)(pair.Value.X / pair.Value.Count), (float)(pair.Value.Z / pair.Value.Count)), float.MaxValue, Vector2.Zero));
            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i] == change && nearest.TryGetValue(labels[i], out (Vector2 Centre, float Distance, Vector2 Place) best))
                {
                    Vector2 cell = new Vector2(i % width, i / width);
                    float distance = Vector2.DistanceSquared(cell, best.Centre);
                    if (distance < best.Distance)
                    {
                        nearest[labels[i]] = (best.Centre, distance, cell);
                    }
                }
            }
            return nearest.OrderBy(pair => pair.Key).Select(pair => new KeyValuePair<int, Vector2>(pair.Key, pair.Value.Place + new Vector2(0.5f, 0.5f)));
        }
    }
}
