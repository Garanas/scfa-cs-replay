using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// The kinds of markers one by one, for showing and hiding them per kind (user story R-6).
    /// Path nodes count per layer.
    /// </summary>
    [Flags]
    public enum MarkerKinds
    {
        None = 0,
        Spawns = 1,
        Mass = 2,
        Hydrocarbon = 4,
        /// <summary>
        /// The markers the AI and scripts read (<see cref="AiMarker"/>), of every type.
        /// </summary>
        AiMarkers = 8,
        /// <summary>
        /// Weather definitions and generators.
        /// </summary>
        Weather = 16,
        LandPaths = 32,
        WaterPaths = 64,
        AmphibiousPaths = 128,
        AirPaths = 256,
        PathNodes = LandPaths | WaterPaths | AmphibiousPaths | AirPaths,
        All = Spawns | Mass | Hydrocarbon | AiMarkers | Weather | PathNodes,
    }

    /// <summary>
    /// Where a placed marker lands: where the mouse is, or on a grid.
    /// </summary>
    public enum MarkerSnap
    {
        Off,
        /// <summary>
        /// On multiples of half an ogrid: an ogrid's corner, the middle of an edge or its centre.
        /// </summary>
        Half,
        /// <summary>
        /// On an ogrid's centre (<c>x.5</c>), where the game puts deposits and the map editors put spawns.
        /// </summary>
        Centre,
    }

    /// <summary>
    /// Edits of markers that go beyond adding, changing and removing one (<see cref="DocumentEdits"/>):
    /// the armies of spawns, links between path nodes, chains, and where a marker may be placed
    /// (user stories R-1 to R-5).
    /// </summary>
    /// <remarks>
    /// A spawn is the start position of a playable army named after it, <c>ARMY_1</c>, <c>ARMY_2</c>,
    /// ... in order. Adding one adds its army (<see cref="DocumentEdits.AddMarker(MapDocument, Marker)"/>);
    /// removing one removes its army and numbers the spawns after it down, so the numbers stay
    /// 1 to n (<see cref="RenumberSpawns"/>).
    /// </remarks>
    public static partial class MarkerEdits
    {
        /// <summary>
        /// The most the ground under a deposit may rise or fall before the editor warns, in game
        /// units: the game's default <c>MaxGroundVariation</c> for structures, which the storages
        /// and fabricators around an extractor keep (the extractors themselves allow any slope).
        /// </summary>
        public const float MaxGroundVariation = 1;

        /// <summary>
        /// The text and number of a spawn's name, <c>ARMY_12</c>.
        /// </summary>
        [GeneratedRegex(@"^ARMY_(\d+)$")]
        private static partial Regex ArmyName();

        /// <summary>
        /// The kind a marker counts as when showing markers per kind.
        /// </summary>
        public static MarkerKinds KindOf(Marker marker) => marker switch
        {
            Spawn => MarkerKinds.Spawns,
            ResourceMarker resource => resource.Kind == ResourceKind.Mass ? MarkerKinds.Mass : MarkerKinds.Hydrocarbon,
            PathNode node => KindOf(node.Layer),
            AiMarker => MarkerKinds.AiMarkers,
            _ => MarkerKinds.Weather,
        };

        /// <summary>
        /// The kind of the path nodes of a layer.
        /// </summary>
        public static MarkerKinds KindOf(PathLayer layer) => layer switch
        {
            PathLayer.Land => MarkerKinds.LandPaths,
            PathLayer.Water => MarkerKinds.WaterPaths,
            PathLayer.Amphibious => MarkerKinds.AmphibiousPaths,
            _ => MarkerKinds.AirPaths,
        };

        /// <summary>
        /// A coordinate on the grid of a snap, in ogrids.
        /// </summary>
        public static float Snap(float value, MarkerSnap snap) => snap switch
        {
            MarkerSnap.Half => MathF.Round(value * 2, MidpointRounding.AwayFromZero) / 2,
            MarkerSnap.Centre => MathF.Floor(value) + 0.5f,
            _ => value,
        };

        /// <summary>
        /// How much the ground rises and falls in a square around a place: the highest sample less
        /// the lowest, in game units.
        /// </summary>
        /// <param name="size">The side of the square, in ogrids.</param>
        public static float GroundVariation(Heightmap heights, float x, float z, float size)
        {
            int x0 = Math.Clamp((int)MathF.Floor(x - size / 2), 0, heights.Width);
            int x1 = Math.Clamp((int)MathF.Ceiling(x + size / 2), 0, heights.Width);
            int z0 = Math.Clamp((int)MathF.Floor(z - size / 2), 0, heights.Height);
            int z1 = Math.Clamp((int)MathF.Ceiling(z + size / 2), 0, heights.Height);
            float lowest = float.MaxValue;
            float highest = float.MinValue;
            for (int row = z0; row <= z1; row++)
            {
                for (int column = x0; column <= x1; column++)
                {
                    float height = heights.GetHeight(column, row);
                    lowest = MathF.Min(lowest, height);
                    highest = MathF.Max(highest, height);
                }
            }
            return highest < lowest ? 0 : highest - lowest;
        }

        /// <summary>
        /// The square a deposit needs flat, in ogrids: an extractor with the storages around it
        /// (3), a hydrocarbon plant (4).
        /// </summary>
        public static float DepositSize(ResourceKind kind) => kind == ResourceKind.Mass ? 3 : 4;

        /// <summary>
        /// How steep the ground is for a deposit at a place: its <see cref="GroundVariation"/> when
        /// more than <see cref="MaxGroundVariation"/>, else null.
        /// </summary>
        public static float? Steepness(Heightmap heights, ResourceKind kind, float x, float z)
        {
            float variation = GroundVariation(heights, x, z, DepositSize(kind));
            return variation > MaxGroundVariation ? variation : null;
        }

        #region Spawns and their armies

        /// <summary>
        /// The number of a spawn's name (<c>ARMY_12</c> is 12), or null for another name.
        /// </summary>
        public static int? SpawnNumber(string name)
        {
            Match match = ArmyName().Match(name);
            return match.Success && int.TryParse(match.Groups[1].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out int number) ? number : null;
        }

        /// <summary>
        /// The name of a new spawn: <c>ARMY_</c> and one more than the highest spawn's number,
        /// skipping a name an army already has (an extra army such as <c>ARMY_9</c> of the civilians).
        /// </summary>
        public static string NextSpawnName(this MapDocument document)
        {
            int number = document.Markers.Spawns.Select(spawn => SpawnNumber(spawn.Name) ?? 0).DefaultIfEmpty(0).Max() + 1;
            while (IsArmyNameTaken(document, $"ARMY_{number}"))
            {
                number++;
            }
            return $"ARMY_{number}";
        }

        /// <summary>
        /// Whether a spawn may be named so: an <c>ARMY_n</c> no marker and no army has, other than a
        /// playable army without a spawn.
        /// </summary>
        internal static bool IsFreeSpawnName(MapDocument document, string name) =>
            SpawnNumber(name) is not null
            && !document.Markers.All.Any(marker => marker.Name == name)
            && (document.Scenario.PlayableArmies.Contains(name) || !IsArmyNameTaken(document, name));

        private static bool IsArmyNameTaken(MapDocument document, string name) =>
            document.Markers.All.Any(marker => marker.Name == name)
            || document.Armies.Any(army => army.Name == name)
            || document.Scenario.PlayableArmies.Contains(name)
            || document.Scenario.ExtraArmies.Contains(name);

        /// <summary>
        /// The document with a playable army of that name, as a new spawn needs it: in the list of
        /// playable armies in the order of their numbers, and an army with an empty group
        /// <c>INITIAL</c> unless one exists.
        /// </summary>
        internal static MapDocument WithPlayableArmy(this MapDocument document, string name)
        {
            int number = SpawnNumber(name) ?? int.MaxValue;
            ScenarioInfo scenario = document.Scenario;
            if (!scenario.PlayableArmies.Contains(name))
            {
                int at = scenario.PlayableArmies.Length;
                for (int i = 0; i < scenario.PlayableArmies.Length; i++)
                {
                    if ((SpawnNumber(scenario.PlayableArmies[i]) ?? int.MaxValue) > number)
                    {
                        at = i;
                        break;
                    }
                }
                scenario = scenario with { PlayableArmies = scenario.PlayableArmies.Insert(at, name), ExtraArmies = scenario.ExtraArmies.Remove(name) };
            }
            ImmutableArray<Army> armies = document.Armies;
            if (!armies.Any(army => army.Name == name))
            {
                int at = armies.Length;
                for (int i = 0; i < armies.Length; i++)
                {
                    if (!scenario.PlayableArmies.Contains(armies[i].Name) || (SpawnNumber(armies[i].Name) ?? int.MaxValue) > number)
                    {
                        at = i;
                        break;
                    }
                }
                armies = armies.Insert(at, new Army
                {
                    Name = name,
                    Units = new UnitGroup { Name = "Units", Groups = [new UnitGroup { Name = "INITIAL" }] },
                });
            }
            return document with { Scenario = scenario, Armies = armies };
        }

        /// <summary>
        /// The document without the playable army of a removed spawn: it is no longer playable, its
        /// deposits of an adaptive map no longer wait for it, and the army goes unless it has units
        /// (then it stays as an extra army, with them).
        /// </summary>
        internal static MapDocument WithoutPlayableArmy(this MapDocument document, string name)
        {
            ScenarioInfo scenario = document.Scenario with
            {
                PlayableArmies = document.Scenario.PlayableArmies.Remove(name),
                ArmyFactions = document.Scenario.ArmyFactions.Remove(name),
            };
            ImmutableArray<Army> armies = document.Armies;
            Army? army = armies.FirstOrDefault(a => a.Name == name);
            if (army is not null)
            {
                if (HasUnits(army.Units))
                {
                    scenario = scenario.ExtraArmies.Contains(name) ? scenario : scenario with { ExtraArmies = scenario.ExtraArmies.Add(name) };
                }
                else
                {
                    armies = [.. armies.Where(a => a.Name != name).Select(a => a.Alliances.ContainsKey(name) ? a with { Alliances = a.Alliances.Remove(name) } : a)];
                }
            }
            MarkerSet markers = document.Markers;
            if (markers.Resources.Any(resource => resource.SpawnWithArmy == name))
            {
                markers = markers with { Resources = [.. markers.Resources.Select(resource => resource.SpawnWithArmy == name ? resource with { SpawnWithArmy = null } : resource)] };
            }
            return document with { Scenario = scenario, Armies = armies, Markers = markers };
        }

        private static bool HasUnits(UnitGroup group) => !group.Units.IsEmpty || group.Groups.Any(HasUnits);

        /// <summary>
        /// Gives an army another name everywhere the map names it: its spawn, its army, the
        /// alliances of the others, the lists of playable and extra armies, its factions, and the
        /// deposits of an adaptive map that wait for it.
        /// </summary>
        /// <exception cref="InvalidOperationException">A marker or another army has the new name.</exception>
        public static MapDocument RenameArmy(this MapDocument document, string from, string to)
        {
            if (from == to)
            {
                return document;
            }
            if (IsArmyNameTaken(document, to))
            {
                throw new InvalidOperationException($"The name {to} is taken");
            }
            string Rename(string name) => name == from ? to : name;
            ScenarioInfo scenario = document.Scenario;
            MarkerSet markers = document.Markers;
            return document with
            {
                Scenario = scenario with
                {
                    PlayableArmies = [.. scenario.PlayableArmies.Select(Rename)],
                    ExtraArmies = [.. scenario.ExtraArmies.Select(Rename)],
                    ArmyFactions = scenario.ArmyFactions.ContainsKey(from)
                        ? scenario.ArmyFactions.Remove(from).Add(to, scenario.ArmyFactions[from])
                        : scenario.ArmyFactions,
                },
                Armies = [.. document.Armies.Select(army => army with
                {
                    Name = Rename(army.Name),
                    Alliances = army.Alliances.ContainsKey(from) ? army.Alliances.Remove(from).Add(to, army.Alliances[from]) : army.Alliances,
                })],
                Markers = markers with
                {
                    Spawns = [.. markers.Spawns.Select(spawn => spawn.Name == from ? spawn with { Name = to } : spawn)],
                    Resources = [.. markers.Resources.Select(resource => resource.SpawnWithArmy == from ? resource with { SpawnWithArmy = to } : resource)],
                },
            };
        }

        /// <summary>
        /// Numbers the spawns 1 to n again in the order of their numbers (after one was removed),
        /// skipping the names of armies without a spawn; each army goes with its spawn
        /// (<see cref="RenameArmy"/>). Spawns of another name stay as they are.
        /// </summary>
        public static MapDocument RenumberSpawns(this MapDocument document)
        {
            HashSet<string> spawns = [.. document.Markers.Spawns.Select(spawn => spawn.Name)];
            HashSet<string> reserved = [.. document.Armies.Select(army => army.Name).Concat(document.Scenario.ExtraArmies).Concat(document.Scenario.PlayableArmies).Where(name => !spawns.Contains(name))];
            int next = 1;
            foreach (int number in document.Markers.Spawns.Select(spawn => SpawnNumber(spawn.Name)).OfType<int>().Order().ToList())
            {
                while (reserved.Contains($"ARMY_{next}"))
                {
                    next++;
                }
                if (number > next)
                {
                    document = document.RenameArmy($"ARMY_{number}", $"ARMY_{next}");
                }
                next = Math.Min(number, next) + 1;
            }
            return document;
        }

        #endregion

        #region Path nodes

        /// <summary>
        /// Links two path nodes of one layer both ways, so the AI may go from either to the other.
        /// </summary>
        /// <exception cref="KeyNotFoundException">No path node has one of the ids.</exception>
        /// <exception cref="InvalidOperationException">They are the same node, or lie on different layers.</exception>
        public static MapDocument LinkPathNodes(this MapDocument document, int a, int b)
        {
            (PathNode first, PathNode second) = Nodes(document, a, b);
            if (first.Layer != second.Layer)
            {
                throw new InvalidOperationException($"{first.Name} and {second.Name} lie on different layers ({first.Layer}, {second.Layer}): only nodes of one layer link");
            }
            return document
                .UpdateMarker(a, marker => first.AdjacentTo.Contains(b) ? marker : first with { AdjacentTo = first.AdjacentTo.Add(b) })
                .UpdateMarker(b, marker => second.AdjacentTo.Contains(a) ? marker : second with { AdjacentTo = second.AdjacentTo.Add(a) });
        }

        /// <summary>
        /// Takes the link between two path nodes away, both ways.
        /// </summary>
        /// <exception cref="KeyNotFoundException">No path node has one of the ids.</exception>
        /// <exception cref="InvalidOperationException">They are the same node.</exception>
        public static MapDocument UnlinkPathNodes(this MapDocument document, int a, int b)
        {
            (PathNode first, PathNode second) = Nodes(document, a, b);
            return document
                .UpdateMarker(a, marker => first.AdjacentTo.Contains(b) ? first with { AdjacentTo = first.AdjacentTo.Remove(b) } : marker)
                .UpdateMarker(b, marker => second.AdjacentTo.Contains(a) ? second with { AdjacentTo = second.AdjacentTo.Remove(a) } : marker);
        }

        /// <summary>
        /// Whether two path nodes are linked, either way.
        /// </summary>
        public static bool AreLinked(this MapDocument document, int a, int b) =>
            document.FindMarker(a) is PathNode first && document.FindMarker(b) is PathNode second
            && (first.AdjacentTo.Contains(b) || second.AdjacentTo.Contains(a));

        /// <summary>
        /// Puts a path node on another layer; its links to the nodes of its old layer go.
        /// </summary>
        /// <exception cref="KeyNotFoundException">No path node has the id.</exception>
        public static MapDocument SetPathLayer(this MapDocument document, int id, PathLayer layer)
        {
            if (document.FindMarker(id) is not PathNode node)
            {
                throw new KeyNotFoundException($"The map has no path node {id}");
            }
            if (node.Layer == layer)
            {
                return document;
            }
            foreach (int other in node.AdjacentTo.ToArray())
            {
                if (document.FindMarker(other) is PathNode)
                {
                    document = document.UnlinkPathNodes(id, other);
                }
            }
            foreach (PathNode linking in document.Markers.PathNodes.Where(n => n.AdjacentTo.Contains(id)).ToArray())
            {
                document = document.UnlinkPathNodes(id, linking.Id);
            }
            return document.UpdateMarker(id, marker => ((PathNode)marker) with { Layer = layer, AdjacentTo = [] });
        }

        private static (PathNode, PathNode) Nodes(MapDocument document, int a, int b)
        {
            if (a == b)
            {
                throw new InvalidOperationException("A path node does not link to itself");
            }
            PathNode first = document.FindMarker(a) as PathNode ?? throw new KeyNotFoundException($"The map has no path node {a}");
            PathNode second = document.FindMarker(b) as PathNode ?? throw new KeyNotFoundException($"The map has no path node {b}");
            return (first, second);
        }

        #endregion

        #region Chains

        /// <summary>
        /// Adds a chain; one whose name is taken gets the next free number after the same text
        /// (<see cref="DocumentEdits.FreeName"/>). Ids of markers the map does not have are left out.
        /// </summary>
        public static MapDocument AddChain(this MapDocument document, Chain chain, out Chain added)
        {
            ImmutableArray<Chain> chains = document.Markers.Chains;
            string name = chain.Name.Length == 0 || chains.Any(c => c.Name == chain.Name)
                ? DocumentEdits.FreeName(chains.Select(c => c.Name), chain.Name.Length == 0 ? "Chain_1" : chain.Name)
                : chain.Name;
            added = chain with { Name = name, MarkerIds = [.. chain.MarkerIds.Where(id => document.FindMarker(id) is not null)] };
            return document with { Markers = document.Markers with { Chains = chains.Add(added) } };
        }

        /// <summary>
        /// Changes a chain: its markers, their order, its name.
        /// </summary>
        /// <exception cref="KeyNotFoundException">No chain has the name.</exception>
        /// <exception cref="InvalidOperationException">The new name is empty or another chain's.</exception>
        public static MapDocument UpdateChain(this MapDocument document, string name, Func<Chain, Chain> update)
        {
            ImmutableArray<Chain> chains = document.Markers.Chains;
            int index = -1;
            for (int i = 0; i < chains.Length && index < 0; i++)
            {
                index = chains[i].Name == name ? i : -1;
            }
            if (index < 0)
            {
                throw new KeyNotFoundException($"The map has no chain {name}");
            }
            Chain changed = update(chains[index]);
            if (changed.Name != name && (changed.Name.Length == 0 || chains.Any(chain => chain.Name == changed.Name)))
            {
                throw new InvalidOperationException(changed.Name.Length == 0 ? "A chain needs a name" : $"Another chain is called {changed.Name}");
            }
            return document with { Markers = document.Markers with { Chains = chains.SetItem(index, changed) } };
        }

        /// <exception cref="KeyNotFoundException">No chain has the name.</exception>
        public static MapDocument RemoveChain(this MapDocument document, string name)
        {
            ImmutableArray<Chain> chains = document.Markers.Chains;
            Chain chain = chains.FirstOrDefault(c => c.Name == name) ?? throw new KeyNotFoundException($"The map has no chain {name}");
            return document with { Markers = document.Markers with { Chains = chains.Remove(chain) } };
        }

        #endregion
    }
}
