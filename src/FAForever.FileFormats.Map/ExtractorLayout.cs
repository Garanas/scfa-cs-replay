using System.Numerics;

namespace FAForever.FileFormats.Map
{
    /// <summary>
    /// The role of a mass extractor spot in the layout of a map, after a guideline for map makers
    /// shared in the LOUD Discord: safe extractors in the base, expandable ones in expansions,
    /// raidable ones far from the base and contestable ones in between the players.
    /// </summary>
    public enum ExtractorRole
    {
        /// <summary>In the base: within the base radius of a start position.</summary>
        Safe,

        /// <summary>Part of an expansion: a group of 3 or more on one side, outside the base.</summary>
        Expandable,

        /// <summary>On one side but far from the base, alone or in pairs: easy to raid when unprotected.</summary>
        Raidable,

        /// <summary>About as far from two start positions: rewards map control.</summary>
        Contestable,
    }

    /// <summary>
    /// The distances that decide the roles. The guideline names the roles, not the distances; the
    /// defaults fit 5 to 10 km maps.
    /// </summary>
    /// <param name="BaseRadius">A spot this close to its start position, in ogrids along the route, is safe.</param>
    /// <param name="ContestedWithin">A spot is contestable when its two nearest start positions are this
    /// close in distance, as a fraction of their average distance.</param>
    /// <param name="ExpansionSpacing">Spots this close to each other, in ogrids, belong to one group.</param>
    public sealed record ExtractorThresholds(float BaseRadius = 60, float ContestedWithin = 0.15f, float ExpansionSpacing = 20)
    {
        public static ExtractorThresholds Default { get; } = new ExtractorThresholds();

        /// <summary>The fewest spots of one group that make an expansion.</summary>
        public const int ExpansionSize = 3;
    }

    /// <summary>
    /// What the guideline asks of each role, per player: at least <see cref="Min"/>, at most
    /// <see cref="Max"/> when it has one.
    /// </summary>
    public sealed record ExtractorGuideline(ExtractorRole Role, int Min, int? Max)
    {
        /// <summary>
        /// At least 4 safe, at least 3 expandable (one expansion of 3 or more), 2 to 6 raidable and 1
        /// to 2 contestable extractors per player.
        /// </summary>
        public static IReadOnlyList<ExtractorGuideline> All { get; } =
        [
            new ExtractorGuideline(ExtractorRole.Safe, 4, null),
            new ExtractorGuideline(ExtractorRole.Expandable, 3, null),
            new ExtractorGuideline(ExtractorRole.Raidable, 2, 6),
            new ExtractorGuideline(ExtractorRole.Contestable, 1, 2),
        ];

        public bool IsMetBy(double count) => count >= Min && (Max is not { } max || count <= max);
    }

    /// <summary>
    /// How far each mass spot and each start position is from the start positions, the expensive part
    /// of the layout. Measure once with <see cref="ExtractorLayout.Measure"/>, classify as often as the
    /// thresholds change.
    /// </summary>
    /// <param name="Layer">The layer the distances follow: the first of land, amphibious and hover that
    /// links all start positions; null when none does and the distances are straight lines.</param>
    /// <param name="Nearest">Per extractor, its nearest and second nearest start position, by index into
    /// <paramref name="Starts"/>, with the distances in ogrids.</param>
    /// <param name="Routed">Per extractor, whether the distances follow the layer; a spot that units of the
    /// layer cannot reach is measured in a straight line.</param>
    /// <param name="NearestToStarts">Per start position, itself (first, at distance 0) and the closest
    /// other start position (second).</param>
    public sealed record ExtractorDistances(
        NavLayer? Layer,
        IReadOnlyList<MapMarker> Starts,
        IReadOnlyList<MapMarker> Extractors,
        IReadOnlyList<NearestOrigins> Nearest,
        IReadOnlyList<bool> Routed,
        IReadOnlyList<NearestOrigins> NearestToStarts);

    /// <param name="Owner">The index of the nearest start position.</param>
    /// <param name="Distance">The distance to that start position, in ogrids.</param>
    /// <param name="OtherDistance">The distance to the second nearest start position; infinity with one start.</param>
    public sealed record ExtractorSpot(MapMarker Marker, int Owner, ExtractorRole Role, float Distance, float OtherDistance, bool Routed);

    /// <summary>
    /// The extractor spots of a map by role, and how many each player has.
    /// </summary>
    /// <param name="PerPlayer">Per start position, the number of spots per role. Contestable spots belong
    /// to no one: each player's count is their total divided by the number of players.</param>
    public sealed record ExtractorLayout(
        NavLayer? Layer,
        ExtractorThresholds Thresholds,
        IReadOnlyList<ExtractorSpot> Spots,
        IReadOnlyList<IReadOnlyDictionary<ExtractorRole, double>> PerPlayer)
    {
        /// <summary>Whether every player meets the guideline of this role.</summary>
        public bool Meets(ExtractorGuideline guideline) => PerPlayer.All(counts => guideline.IsMetBy(counts[guideline.Role]));

        /// <summary>How many of the four guidelines every player meets.</summary>
        public int GuidelinesMet => ExtractorGuideline.All.Count(Meets);

        /// <summary>
        /// Measures how far every extractor and every start position is from the start positions,
        /// over the layer that links the start positions, in one search from all of them
        /// (<see cref="NavPaths.NearestTwo"/>).
        /// </summary>
        public static ExtractorDistances Measure(MapNavigation navigation, IReadOnlyList<MapMarker> starts, IReadOnlyList<MapMarker> extractors)
        {
            List<Vector3> origins = [.. starts.Select(start => start.Position)];
            NavLayer? layer = navigation.FindConnectingLayer(origins);

            // the search runs in blocks of the game's compression threshold (2 by 2 ogrids from 20 km,
            // 4 by 4 from 40 km): the game itself sees no finer, and it is 4 or 16 times less work
            int block = NavGenerator.GetCompressionThreshold(Math.Max(navigation.Land.Width, navigation.Land.Height));
            NavGrid? grid = layer is { } connecting ? navigation[connecting].Coarsen(block) : null;
            NearestOrigins[] field = [];
            List<Vector3> placed = [];
            if (grid is not null)
            {
                placed = [.. origins.Select(origin => Place(grid, origin, block) ?? new Vector3(origin.X / block, 0, origin.Z / block))];
                field = NavPaths.NearestTwo(grid, placed);
            }

            NearestOrigins Lookup(Vector3 position, out bool routed)
            {
                if (grid is not null && Place(grid, position, block) is { } cell)
                {
                    NearestOrigins found = field[(int)cell.Z * grid.Width + (int)cell.X];
                    if (found.First >= 0)
                    {
                        routed = true;
                        return new NearestOrigins(found.First, found.FirstDistance * block, found.Second, found.SecondDistance * block);
                    }
                }
                routed = false;
                return Straight(position, origins);
            }

            List<NearestOrigins> nearest = [];
            List<bool> routedSpots = [];
            foreach (MapMarker extractor in extractors)
            {
                nearest.Add(Lookup(extractor.Position, out bool routed));
                routedSpots.Add(routed);
            }

            List<NearestOrigins> nearestToStarts = [.. starts.Select(start => Lookup(start.Position, out _))];
            return new ExtractorDistances(layer, starts, extractors, nearest, routedSpots, nearestToStarts);
        }

        /// <summary>
        /// The block of a coarse grid for a position on the map: its own block when pathable, else the
        /// nearest pathable block within two blocks (a mass spot at the foot of a cliff can share a
        /// block with it); null when there is none.
        /// </summary>
        private static Vector3? Place(NavGrid grid, Vector3 position, int block)
        {
            int x = (int)MathF.Floor(position.X / block);
            int z = (int)MathF.Floor(position.Z / block);
            Vector3? best = null;
            int bestDistance = int.MaxValue;
            int reach = block > 1 ? 2 : 0;
            for (int dz = -reach; dz <= reach; dz++)
            {
                for (int dx = -reach; dx <= reach; dx++)
                {
                    int nx = x + dx;
                    int nz = z + dz;
                    if ((uint)nx < (uint)grid.Width && (uint)nz < (uint)grid.Height && grid.Cells[nz * grid.Width + nx] > 0 && dx * dx + dz * dz < bestDistance)
                    {
                        bestDistance = dx * dx + dz * dz;
                        best = new Vector3(nx + 0.5f, 0, nz + 0.5f);
                    }
                }
            }
            return best;
        }

        /// <summary>The two origins nearest in a straight line.</summary>
        private static NearestOrigins Straight(Vector3 position, IReadOnlyList<Vector3> origins)
        {
            NearestOrigins best = NearestOrigins.None;
            for (int i = 0; i < origins.Count; i++)
            {
                float d = Vector2.Distance(new Vector2(position.X, position.Z), new Vector2(origins[i].X, origins[i].Z));
                if (d < best.FirstDistance)
                {
                    best = new NearestOrigins(i, d, best.First, best.FirstDistance);
                }
                else if (d < best.SecondDistance)
                {
                    best = best with { Second = i, SecondDistance = d };
                }
            }
            return best;
        }

        /// <summary>
        /// Gives every extractor a role: safe within the base radius, contestable when its two nearest
        /// start positions are about as far, expandable in a group of 3 or more on one side, raidable
        /// otherwise.
        /// </summary>
        public static ExtractorLayout Classify(ExtractorDistances distances, ExtractorThresholds? thresholds = null)
        {
            ExtractorThresholds t = thresholds ?? ExtractorThresholds.Default;
            int players = distances.Starts.Count;
            ExtractorRole?[] roles = new ExtractorRole?[distances.Extractors.Count];
            int[] owners = new int[roles.Length];
            float[] own = new float[roles.Length];
            float[] other = new float[roles.Length];

            for (int e = 0; e < roles.Length; e++)
            {
                NearestOrigins nearest = distances.Nearest[e];
                owners[e] = Math.Max(0, nearest.First);
                own[e] = nearest.FirstDistance;
                other[e] = nearest.SecondDistance;
                float second = nearest.SecondDistance;

                bool contested = !float.IsPositiveInfinity(second) && second - own[e] <= t.ContestedWithin * (own[e] + second) / 2;
                roles[e] = own[e] <= t.BaseRadius ? ExtractorRole.Safe : contested ? ExtractorRole.Contestable : null;
            }

            // the rest form groups on each side: spots of the same owner within the spacing of each other
            bool[] grouped = new bool[roles.Length];
            for (int e = 0; e < roles.Length; e++)
            {
                if (roles[e] is not null || grouped[e])
                {
                    continue;
                }

                List<int> group = [e];
                grouped[e] = true;
                for (int g = 0; g < group.Count; g++)
                {
                    Vector3 position = distances.Extractors[group[g]].Position;
                    for (int o = 0; o < roles.Length; o++)
                    {
                        if (!grouped[o] && roles[o] is null && owners[o] == owners[e]
                            && Vector2.Distance(new Vector2(position.X, position.Z), new Vector2(distances.Extractors[o].Position.X, distances.Extractors[o].Position.Z)) <= t.ExpansionSpacing)
                        {
                            grouped[o] = true;
                            group.Add(o);
                        }
                    }
                }

                ExtractorRole role = group.Count >= ExtractorThresholds.ExpansionSize ? ExtractorRole.Expandable : ExtractorRole.Raidable;
                foreach (int member in group)
                {
                    roles[member] = role;
                }
            }

            List<ExtractorSpot> spots = [];
            for (int e = 0; e < roles.Length; e++)
            {
                spots.Add(new ExtractorSpot(distances.Extractors[e], owners[e], roles[e]!.Value, own[e], other[e], distances.Routed[e]));
            }

            int contestable = spots.Count(spot => spot.Role == ExtractorRole.Contestable);
            List<IReadOnlyDictionary<ExtractorRole, double>> perPlayer = [];
            for (int i = 0; i < players; i++)
            {
                Dictionary<ExtractorRole, double> counts = Enum.GetValues<ExtractorRole>().ToDictionary(role => role, _ => 0.0);
                foreach (ExtractorSpot spot in spots)
                {
                    if (spot.Owner == i && spot.Role != ExtractorRole.Contestable)
                    {
                        counts[spot.Role]++;
                    }
                }
                counts[ExtractorRole.Contestable] = (double)contestable / players;
                perPlayer.Add(counts);
            }

            return new ExtractorLayout(distances.Layer, t, spots, perPlayer);
        }
    }
}
