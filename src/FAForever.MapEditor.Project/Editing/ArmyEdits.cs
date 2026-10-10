using System.Collections.Immutable;
using System.Numerics;
using System.Text.RegularExpressions;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// Edits of the armies (user story M-4): add, remove and rename an army, make it playable or
    /// extra, limit its factions, set its alliances and its team. They keep everything that names
    /// an army consistent: the scenario's lists, the factions, the teams, the other armies'
    /// alliances and the army's spawn marker.
    /// </summary>
    /// <remarks>
    /// A playable army starts at its spawn, the marker named after it: adding a playable army (or
    /// making an army playable) adds one when it has none, renaming an army renames its spawn, and
    /// removing an army removes its spawn and its units. Extra armies (civilians, <c>ARMY_17</c>)
    /// need no spawn and are given none.
    /// </remarks>
    public static partial class ArmyEdits
    {
        /// <summary>
        /// The stances an army can take towards another, as the save file writes them.
        /// </summary>
        public static readonly ImmutableArray<string> Stances = ["Ally", "Enemy", "Neutral"];

        /// <summary>
        /// The factions a playable army can be limited to, as the scenario writes them.
        /// </summary>
        public static readonly ImmutableArray<string> Factions = ["uef", "aeon", "cybran", "seraphim"];

        /// <summary>
        /// Whether a name can be an army's: letters, digits and underscores, not starting with a
        /// digit (the scenario lists the extra armies separated by spaces, and Lua uses them as keys).
        /// </summary>
        public static bool IsArmyName(string name) => ArmyName().IsMatch(name);

        public static Army? FindArmy(this MapDocument document, string name) => document.Armies.FirstOrDefault(army => army.Name == name);

        /// <summary>
        /// Whether the army is one players take in the lobby.
        /// </summary>
        public static bool IsPlayable(this MapDocument document, string army) => document.Scenario.PlayableArmies.Contains(army);

        /// <summary>
        /// Adds an army without units (a root group <c>Units</c> holding an empty <c>INITIAL</c>),
        /// playable (at the end of the playable armies, with a spawn) or extra.
        /// </summary>
        /// <param name="name">Null or empty for the next free <c>ARMY_n</c>.</param>
        /// <param name="spawn">Where a playable army's spawn goes (x, z in ogrids); the middle of the map when null.</param>
        /// <param name="added">The army's name.</param>
        /// <exception cref="ArgumentException">The name is taken or cannot be an army's.</exception>
        public static MapDocument AddArmy(this MapDocument document, string? name, bool playable, Vector2? spawn, out string added)
        {
            IEnumerable<string> taken = document.Armies.Select(army => army.Name).Concat(document.Scenario.PlayableArmies).Concat(document.Scenario.ExtraArmies);
            added = string.IsNullOrWhiteSpace(name) ? DocumentEdits.FreeName(taken, "ARMY_1") : name.Trim();
            CheckNewName(document, added, null);
            Army army = new Army
            {
                Name = added,
                Units = new UnitGroup { Name = "Units", Groups = [new UnitGroup { Name = UnitGroupEdits.InitialGroup }] },
            };
            ScenarioInfo scenario = document.Scenario;
            document = document with
            {
                Armies = document.Armies.Add(army),
                Scenario = playable
                    ? scenario with { PlayableArmies = scenario.PlayableArmies.Add(added) }
                    : scenario with { ExtraArmies = scenario.ExtraArmies.Add(added) },
            };
            return playable ? document.WithSpawn(added, spawn) : document;
        }

        /// <summary>
        /// Removes an army with its units, its spawn, and every mention of it: the scenario's lists,
        /// its factions, its team and the other armies' alliances with it.
        /// </summary>
        /// <exception cref="KeyNotFoundException">The map has no army of that name.</exception>
        public static MapDocument RemoveArmy(this MapDocument document, string name)
        {
            RequireArmy(document, name);
            ScenarioInfo s = document.Scenario;
            document = document with
            {
                Armies = [.. document.Armies.Where(army => army.Name != name).Select(army => army.Alliances.ContainsKey(name) ? army with { Alliances = army.Alliances.Remove(name) } : army)],
                Scenario = s with
                {
                    PlayableArmies = s.PlayableArmies.Remove(name),
                    ExtraArmies = s.ExtraArmies.Remove(name),
                    ArmyFactions = s.ArmyFactions.Remove(name),
                    Teams = WithoutArmy(s.Teams, name),
                },
            };
            return document.Markers.Spawns.FirstOrDefault(spawn => spawn.Name == name) is Spawn marker ? document.RemoveMarker(marker.Id) : document;
        }

        /// <summary>
        /// Renames an army everywhere it is named: the army, the scenario's lists, its factions, its
        /// team, the other armies' alliances, its spawn marker and the deposits of an adaptive map
        /// that wait for it. Renumbering the spawns (<see cref="MarkerEdits.RenumberSpawns"/>)
        /// renames armies this way too.
        /// </summary>
        /// <exception cref="KeyNotFoundException">The map has neither an army nor a spawn of that name.</exception>
        /// <exception cref="ArgumentException">The new name is taken (by an army or another marker) or cannot be an army's.</exception>
        public static MapDocument RenameArmy(this MapDocument document, string name, string newName)
        {
            if (document.FindArmy(name) is null && !document.Markers.Spawns.Any(spawn => spawn.Name == name))
            {
                throw new KeyNotFoundException($"The map has no army {name}");
            }
            newName = newName.Trim();
            if (newName == name)
            {
                return document;
            }
            CheckNewName(document, newName, name);
            string Renamed(string army) => army == name ? newName : army;
            ScenarioInfo s = document.Scenario;
            document = document with
            {
                Armies = [.. document.Armies.Select(army => army with
                {
                    Name = Renamed(army.Name),
                    Alliances = army.Alliances.ContainsKey(name)
                        ? army.Alliances.Remove(name).SetItem(newName, army.Alliances[name])
                        : army.Alliances,
                })],
                Scenario = s with
                {
                    PlayableArmies = [.. s.PlayableArmies.Select(Renamed)],
                    ExtraArmies = [.. s.ExtraArmies.Select(Renamed)],
                    ArmyFactions = s.ArmyFactions.TryGetValue(name, out ImmutableArray<string> factions)
                        ? s.ArmyFactions.Remove(name).SetItem(newName, factions)
                        : s.ArmyFactions,
                    Teams = [.. s.Teams.Select(team => team.Armies.Contains(name) ? team with { Armies = [.. team.Armies.Select(Renamed)] } : team)],
                },
            };
            document = document with
            {
                Markers = document.Markers with
                {
                    Resources = [.. document.Markers.Resources.Select(resource => resource.SpawnWithArmy == name ? resource with { SpawnWithArmy = newName } : resource)],
                },
            };
            return document.Markers.Spawns.FirstOrDefault(spawn => spawn.Name == name) is Spawn marker
                ? document.UpdateMarker(marker.Id, m => m with { Name = newName })
                : document;
        }

        /// <summary>
        /// Makes an army playable (at the end of the playable armies, with a spawn when it has
        /// none) or extra (at the end of the extra armies, out of its team and without factions).
        /// </summary>
        /// <exception cref="KeyNotFoundException">The map has no army of that name.</exception>
        public static MapDocument SetArmyPlayable(this MapDocument document, string name, bool playable, Vector2? spawn = null)
        {
            RequireArmy(document, name);
            ScenarioInfo s = document.Scenario;
            if (playable == s.PlayableArmies.Contains(name) && (playable || s.ExtraArmies.Contains(name)))
            {
                return document;
            }
            document = document with
            {
                Scenario = playable
                    ? s with { PlayableArmies = s.PlayableArmies.Add(name), ExtraArmies = s.ExtraArmies.Remove(name) }
                    : s with
                    {
                        PlayableArmies = s.PlayableArmies.Remove(name),
                        ExtraArmies = s.ExtraArmies.Contains(name) ? s.ExtraArmies : s.ExtraArmies.Add(name),
                        ArmyFactions = s.ArmyFactions.Remove(name),
                        Teams = WithoutArmy(s.Teams, name),
                    },
            };
            return playable ? document.WithSpawn(name, spawn) : document;
        }

        /// <summary>
        /// Limits the factions a playable army may play; none, or all four, lifts the limit.
        /// </summary>
        /// <param name="factions">Of <see cref="Factions"/>, in any case and order.</param>
        /// <exception cref="KeyNotFoundException">The map has no army of that name.</exception>
        /// <exception cref="ArgumentException">The army is not playable, or a faction is none of the four.</exception>
        public static MapDocument SetArmyFactions(this MapDocument document, string name, IEnumerable<string> factions)
        {
            RequireArmy(document, name);
            if (!document.IsPlayable(name))
            {
                throw new ArgumentException($"Only a playable army can be limited to factions; {name} is an extra army");
            }
            HashSet<string> chosen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string faction in factions)
            {
                string lower = faction.Trim().ToLowerInvariant();
                if (!Factions.Contains(lower))
                {
                    throw new ArgumentException($"There is no faction '{faction}': {string.Join(", ", Factions)}");
                }
                chosen.Add(lower);
            }
            ScenarioInfo s = document.Scenario;
            ImmutableSortedDictionary<string, ImmutableArray<string>> limits = chosen.Count is 0 or 4
                ? s.ArmyFactions.Remove(name)
                : s.ArmyFactions.SetItem(name, [.. Factions.Where(chosen.Contains)]);
            return document with { Scenario = s with { ArmyFactions = limits } };
        }

        /// <summary>
        /// Sets how an army stands towards another, and with <paramref name="mutual"/> the other
        /// towards it too.
        /// </summary>
        /// <param name="stance">Of <see cref="Stances"/>, in any case; null or empty takes the stance away.</param>
        /// <exception cref="KeyNotFoundException">The map has no army of one of the names.</exception>
        /// <exception cref="ArgumentException">The two are the same army, or the stance is none of <see cref="Stances"/>.</exception>
        public static MapDocument SetAlliance(this MapDocument document, string army, string other, string? stance, bool mutual = true)
        {
            RequireArmy(document, army);
            RequireArmy(document, other);
            if (army == other)
            {
                throw new ArgumentException($"An army has no alliance with itself ({army})");
            }
            string? value = string.IsNullOrWhiteSpace(stance)
                ? null
                : Stances.FirstOrDefault(s => s.Equals(stance.Trim(), StringComparison.OrdinalIgnoreCase))
                    ?? throw new ArgumentException($"There is no stance '{stance}': {string.Join(", ", Stances)}");
            Army Set(Army a, string towards) => value is null
                ? a.Alliances.ContainsKey(towards) ? a with { Alliances = a.Alliances.Remove(towards) } : a
                : a with { Alliances = a.Alliances.SetItem(towards, value) };
            return document with
            {
                Armies = [.. document.Armies.Select(a => a.Name == army ? Set(a, other) : mutual && a.Name == other ? Set(a, army) : a)],
            };
        }

        /// <summary>
        /// Puts a playable army in a team (made at the end when there is none of that name), out of
        /// the team it was in; a team left without armies is taken away.
        /// </summary>
        /// <param name="team">Null or empty takes the army out of every team.</param>
        /// <exception cref="KeyNotFoundException">The map has no army of that name.</exception>
        /// <exception cref="ArgumentException">The army is not playable.</exception>
        public static MapDocument SetTeam(this MapDocument document, string army, string? team)
        {
            RequireArmy(document, army);
            if (!document.IsPlayable(army))
            {
                throw new ArgumentException($"Only a playable army is in a team; {army} is an extra army");
            }
            ImmutableArray<Team> teams = WithoutArmy(document.Scenario.Teams, army);
            if (!string.IsNullOrWhiteSpace(team))
            {
                string named = team.Trim();
                int index = -1;
                for (int i = 0; i < teams.Length && index < 0; i++)
                {
                    index = teams[i].Name == named ? i : -1;
                }
                teams = index < 0
                    ? teams.Add(new Team { Name = named, Armies = [army] })
                    : teams.SetItem(index, teams[index] with { Armies = teams[index].Armies.Add(army) });
            }
            return document with { Scenario = document.Scenario with { Teams = teams } };
        }

        /// <summary>
        /// The team a playable army is in, or null.
        /// </summary>
        public static string? TeamOf(this MapDocument document, string army) =>
            document.Scenario.Teams.FirstOrDefault(team => team.Armies.Contains(army))?.Name;

        private static ImmutableArray<Team> WithoutArmy(ImmutableArray<Team> teams, string army) =>
            teams.Any(team => team.Armies.Contains(army))
                ? [.. teams.Select(team => team with { Armies = team.Armies.Remove(army) }).Where(team => !team.Armies.IsEmpty)]
                : teams;

        /// <summary>
        /// The document with a spawn for the army when it has none.
        /// </summary>
        private static MapDocument WithSpawn(this MapDocument document, string army, Vector2? place)
        {
            if (document.Markers.Spawns.Any(spawn => spawn.Name == army))
            {
                return document;
            }
            Vector2 at = place ?? new Vector2(document.Width / 2f, document.Height / 2f);
            Spawn spawn = new Spawn { Id = 0, Name = army, Position = new Vector3(at.X, document.Terrain.Heightmap.HeightAt(at.X, at.Y), at.Y) };
            return document.AddMarker(spawn);
        }

        private static void RequireArmy(MapDocument document, string name)
        {
            if (document.FindArmy(name) is null)
            {
                throw new KeyNotFoundException($"The map has no army {name}");
            }
        }

        /// <param name="renaming">The army whose name is changed, whose own spawn may have the name.</param>
        private static void CheckNewName(MapDocument document, string name, string? renaming)
        {
            if (!IsArmyName(name))
            {
                throw new ArgumentException($"'{name}' cannot be an army's name: letters, digits and underscores, not starting with a digit");
            }
            if (document.FindArmy(name) is not null || document.Scenario.PlayableArmies.Contains(name) || document.Scenario.ExtraArmies.Contains(name))
            {
                throw new ArgumentException($"The map has an army {name} already");
            }
            if (document.Markers.All.Any(marker => marker.Name == name && !(marker is Spawn && marker.Name == renaming)))
            {
                throw new ArgumentException($"A marker is named {name} already: an army's spawn takes its army's name");
            }
        }

        [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
        private static partial Regex ArmyName();
    }
}
