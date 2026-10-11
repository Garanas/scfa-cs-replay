using System.Collections.Immutable;
using System.Globalization;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// Edits of a document that the user interface and plugins share, each a function from one
    /// version to the next for <see cref="EditSession.Edit"/> or <see cref="EditTransaction.Apply"/>.
    /// They name what they change (a prop, decal or marker by its id, a unit, area or army by its
    /// name), never a position in a list.
    /// </summary>
    public static class DocumentEdits
    {
        #region Props

        /// <summary>
        /// Adds a prop under a new id, whatever id it had.
        /// </summary>
        public static MapDocument AddProp(this MapDocument document, Prop prop) =>
            document with { Props = document.Props.Add(prop with { Id = document.NextPropId() }) };

        /// <exception cref="KeyNotFoundException">No prop has the id.</exception>
        public static MapDocument UpdateProp(this MapDocument document, int id, Func<Prop, Prop> update) =>
            document with { Props = document.Props.Update(prop => prop.Id == id, update, $"prop {id}") };

        /// <exception cref="KeyNotFoundException">No prop has the id.</exception>
        public static MapDocument RemoveProp(this MapDocument document, int id) =>
            document with { Props = document.Props.RemoveAt(document.Props.Find(prop => prop.Id == id, $"prop {id}")) };

        #endregion

        #region Decals

        /// <summary>
        /// An id for a new decal: one more than the highest in use.
        /// </summary>
        public static int NextDecalId(this MapDocument document) => document.Decals.IsEmpty ? 1 : document.Decals.Max(decal => decal.Id) + 1;

        /// <summary>
        /// Adds a decal on top of the others, under a new id, whatever id it had.
        /// </summary>
        public static MapDocument AddDecal(this MapDocument document, Decal decal) =>
            document with { Decals = document.Decals.Add(decal with { Id = document.NextDecalId() }) };

        /// <exception cref="KeyNotFoundException">No decal has the id.</exception>
        public static MapDocument UpdateDecal(this MapDocument document, int id, Func<Decal, Decal> update) =>
            document with { Decals = document.Decals.Update(decal => decal.Id == id, update, $"decal {id}") };

        /// <summary>
        /// Removes a decal, also from the groups that hold it.
        /// </summary>
        /// <exception cref="KeyNotFoundException">No decal has the id.</exception>
        public static MapDocument RemoveDecal(this MapDocument document, int id) => document with
        {
            Decals = document.Decals.RemoveAt(document.Decals.Find(decal => decal.Id == id, $"decal {id}")),
            DecalGroups = [.. document.DecalGroups.Select(group => group.DecalIds.Contains(id) ? group with { DecalIds = group.DecalIds.Remove(id) } : group)],
        };

        #endregion

        #region Units

        /// <summary>
        /// Every unit of the map with the army it belongs to and the path of its group below the
        /// army's root group (empty for a unit in the root group itself).
        /// </summary>
        public static IEnumerable<PlacedUnit> AllUnits(this MapDocument document)
        {
            foreach (Army army in document.Armies)
            {
                foreach (PlacedUnit unit in UnitsOf(army.Name, [], army.Units))
                {
                    yield return unit;
                }
            }
        }

        /// <summary>
        /// The unit of that name with its army and group, or null.
        /// </summary>
        public static PlacedUnit? FindUnit(this MapDocument document, string name) =>
            document.AllUnits().FirstOrDefault(unit => unit.Unit.Name == name);

        /// <summary>
        /// A name for a new unit: <c>UNIT_</c> and one more than the highest number such names use.
        /// </summary>
        public static string NextUnitName(this MapDocument document) =>
            $"UNIT_{NextNumber(document.AllUnits().Select(unit => unit.Unit.Name), "UNIT_")}";

        /// <summary>
        /// Adds a unit to a group of an army, made when it does not exist; a unit whose name is taken
        /// gets a new one (<see cref="NextUnitName"/>).
        /// </summary>
        /// <param name="group">The names of the groups from the army's root group down, e.g. <c>INITIAL</c>; empty for the root group.</param>
        /// <exception cref="KeyNotFoundException">No army has the name.</exception>
        public static MapDocument AddUnit(this MapDocument document, string army, IReadOnlyList<string> group, Unit unit)
        {
            if (unit.Name.Length == 0 || document.FindUnit(unit.Name) is not null)
            {
                unit = unit with { Name = document.NextUnitName() };
            }
            return document with
            {
                Armies = document.Armies.Update(a => a.Name == army, a => a with { Units = AddTo(a.Units, group, 0, unit) }, $"army {army}"),
            };
        }

        /// <summary>
        /// Adds many units to one group of an army, made when it does not exist, as one new version
        /// (user story U-7): each unit whose name is empty or taken gets the next free name
        /// (<see cref="NextUnitName"/>, counting the units added before it).
        /// </summary>
        /// <param name="group">The names of the groups from the army's root group down, e.g. <c>INITIAL</c>; empty for the root group.</param>
        /// <param name="names">The units' names, in the order given.</param>
        /// <exception cref="KeyNotFoundException">No army has the name.</exception>
        public static MapDocument AddUnits(this MapDocument document, string army, IReadOnlyList<string> group, IEnumerable<Unit> units, out ImmutableArray<string> names)
        {
            HashSet<string> taken = [.. document.AllUnits().Select(unit => unit.Unit.Name)];
            int next = NextNumber(taken, "UNIT_");
            ImmutableArray<Unit>.Builder added = ImmutableArray.CreateBuilder<Unit>();
            ImmutableArray<string>.Builder named = ImmutableArray.CreateBuilder<string>();
            foreach (Unit unit in units)
            {
                Unit placed = unit;
                if (placed.Name.Length == 0 || !taken.Add(placed.Name))
                {
                    string name;
                    do
                    {
                        name = $"UNIT_{next++}";
                    }
                    while (!taken.Add(name));
                    placed = placed with { Name = name };
                }
                added.Add(placed);
                named.Add(placed.Name);
            }
            names = named.ToImmutable();
            if (added.Count == 0)
            {
                if (document.Armies.All(a => a.Name != army))
                {
                    throw new KeyNotFoundException($"The map has no army {army}");
                }
                return document;
            }
            ImmutableArray<Unit> all = added.ToImmutable();
            return document with
            {
                Armies = document.Armies.Update(a => a.Name == army, a => a with { Units = AddAllTo(a.Units, group, 0, all) }, $"army {army}"),
            };
        }

        /// <exception cref="KeyNotFoundException">No unit has the name.</exception>
        public static MapDocument UpdateUnit(this MapDocument document, string name, Func<Unit, Unit> update)
        {
            PlacedUnit placed = document.FindUnit(name) ?? throw new KeyNotFoundException($"The map has no unit {name}");
            return document with
            {
                Armies = document.Armies.Update(a => a.Name == placed.Army, a => a with { Units = Replace(a.Units, name, update) }, $"army {placed.Army}"),
            };
        }

        /// <exception cref="KeyNotFoundException">No unit has the name.</exception>
        public static MapDocument RemoveUnit(this MapDocument document, string name)
        {
            PlacedUnit placed = document.FindUnit(name) ?? throw new KeyNotFoundException($"The map has no unit {name}");
            return document with
            {
                Armies = document.Armies.Update(a => a.Name == placed.Army, a => a with { Units = Replace(a.Units, name, null) }, $"army {placed.Army}"),
            };
        }

        private static IEnumerable<PlacedUnit> UnitsOf(string army, ImmutableArray<string> path, UnitGroup group)
        {
            foreach (Unit unit in group.Units)
            {
                yield return new PlacedUnit(army, path, unit);
            }
            foreach (UnitGroup child in group.Groups)
            {
                foreach (PlacedUnit unit in UnitsOf(army, path.Add(child.Name), child))
                {
                    yield return unit;
                }
            }
        }

        private static UnitGroup AddTo(UnitGroup group, IReadOnlyList<string> path, int depth, Unit unit)
        {
            if (depth == path.Count)
            {
                return group with { Units = group.Units.Add(unit) };
            }
            int index = -1;
            for (int i = 0; i < group.Groups.Length && index < 0; i++)
            {
                index = group.Groups[i].Name == path[depth] ? i : -1;
            }
            return index < 0
                ? group with { Groups = group.Groups.Add(AddTo(new UnitGroup { Name = path[depth] }, path, depth + 1, unit)) }
                : group with { Groups = group.Groups.SetItem(index, AddTo(group.Groups[index], path, depth + 1, unit)) };
        }

        private static UnitGroup AddAllTo(UnitGroup group, IReadOnlyList<string> path, int depth, ImmutableArray<Unit> units)
        {
            if (depth == path.Count)
            {
                return group with { Units = group.Units.AddRange(units) };
            }
            int index = -1;
            for (int i = 0; i < group.Groups.Length && index < 0; i++)
            {
                index = group.Groups[i].Name == path[depth] ? i : -1;
            }
            return index < 0
                ? group with { Groups = group.Groups.Add(AddAllTo(new UnitGroup { Name = path[depth] }, path, depth + 1, units)) }
                : group with { Groups = group.Groups.SetItem(index, AddAllTo(group.Groups[index], path, depth + 1, units)) };
        }

        /// <summary>
        /// The group with the unit of that name changed, or taken out when <paramref name="update"/> is null.
        /// </summary>
        private static UnitGroup Replace(UnitGroup group, string name, Func<Unit, Unit>? update)
        {
            for (int i = 0; i < group.Units.Length; i++)
            {
                if (group.Units[i].Name == name)
                {
                    return group with { Units = update is null ? group.Units.RemoveAt(i) : group.Units.SetItem(i, update(group.Units[i])) };
                }
            }
            for (int i = 0; i < group.Groups.Length; i++)
            {
                UnitGroup changed = Replace(group.Groups[i], name, update);
                if (!ReferenceEquals(changed, group.Groups[i]))
                {
                    return group with { Groups = group.Groups.SetItem(i, changed) };
                }
            }
            return group;
        }

        #endregion

        #region Markers

        /// <summary>
        /// The marker with that id, of any kind, or null.
        /// </summary>
        public static Marker? FindMarker(this MapDocument document, int id) => document.Markers.All.FirstOrDefault(marker => marker.Id == id);

        /// <summary>
        /// Adds a marker of any kind into its kind's list under a new id; a marker whose name is taken
        /// gets the next free number after the same text (<c>Mass 08</c> becomes <c>Mass 12</c>). A
        /// path node's links are kept to the nodes that exist. A spawn is named <c>ARMY_n</c> (its own name
        /// when free, else <see cref="MarkerEdits.NextSpawnName"/>) and its playable army is added with it.
        /// </summary>
        public static MapDocument AddMarker(this MapDocument document, Marker marker) => document.AddMarker(marker, out _);

        /// <inheritdoc cref="AddMarker(MapDocument, Marker)"/>
        /// <param name="added">The marker as it was added, with its id and name.</param>
        public static MapDocument AddMarker(this MapDocument document, Marker marker, out Marker added)
        {
            MarkerSet markers = document.Markers;
            string name = marker.Name;
            if (marker is Spawn)
            {
                // a spawn is a playable army's start: ARMY_n, its army added with it; or the start of an
                // army the map has without one, which becomes playable with it
                bool armyWithoutSpawn = document.Armies.Any(army => army.Name == name) && !markers.All.Any(m => m.Name == name);
                name = armyWithoutSpawn || MarkerEdits.IsFreeSpawnName(document, name) ? name : document.NextSpawnName();
            }
            else if (name.Length == 0 || markers.All.Any(m => m.Name == name))
            {
                name = FreeName(markers.All.Select(m => m.Name), name.Length == 0 ? "Marker" : name);
            }
            added = marker with { Id = markers.NextId(), Name = name };
            if (added is PathNode node)
            {
                HashSet<int> nodes = [.. markers.PathNodes.Select(n => n.Id)];
                added = node with { AdjacentTo = [.. node.AdjacentTo.Where(nodes.Contains)] };
            }
            document = document with { Markers = Change(markers, added.Id, added, add: true) };
            return added is Spawn ? document.WithPlayableArmy(name) : document;
        }

        /// <summary>
        /// Changes a marker of any kind, which stays the kind it is and keeps its id.
        /// </summary>
        /// <exception cref="KeyNotFoundException">No marker has the id.</exception>
        /// <exception cref="InvalidOperationException">The update changed its kind or id.</exception>
        public static MapDocument UpdateMarker(this MapDocument document, int id, Func<Marker, Marker> update)
        {
            Marker marker = document.FindMarker(id) ?? throw new KeyNotFoundException($"The map has no marker {id}");
            Marker changed = update(marker);
            if (changed.GetType() != marker.GetType() || changed.Id != id)
            {
                throw new InvalidOperationException($"An update of the marker {marker.Name} may change neither its kind nor its id");
            }
            return document with { Markers = Change(document.Markers, id, changed, add: false) };
        }

        /// <summary>
        /// Removes a marker of any kind, and every link to it: from the path nodes that link to it
        /// and from the chains that hold it. A spawn takes its playable army with it, and the spawns
        /// after it are numbered down (<see cref="MarkerEdits.RenumberSpawns"/>).
        /// </summary>
        /// <exception cref="KeyNotFoundException">No marker has the id.</exception>
        public static MapDocument RemoveMarker(this MapDocument document, int id)
        {
            Marker removed = document.FindMarker(id) ?? throw new KeyNotFoundException($"The map has no marker {id}");
            MarkerSet markers = Change(document.Markers, id, null, add: false);
            document = document with
            {
                Markers = markers with
                {
                    PathNodes = [.. markers.PathNodes.Select(node => node.AdjacentTo.Contains(id) ? node with { AdjacentTo = [.. node.AdjacentTo.Where(other => other != id)] } : node)],
                    Chains = [.. markers.Chains.Select(chain => chain.MarkerIds.Contains(id) ? chain with { MarkerIds = [.. chain.MarkerIds.Where(other => other != id)] } : chain)],
                },
            };
            // a spawn takes its playable army with it, and the spawns after it are numbered down
            return removed is Spawn spawn && MarkerEdits.SpawnNumber(spawn.Name) is not null
                ? document.WithoutPlayableArmy(spawn.Name).RenumberSpawns()
                : document;
        }

        /// <exception cref="KeyNotFoundException">No deposit has the name.</exception>
        public static MapDocument UpdateResource(this MapDocument document, string name, Func<ResourceMarker, ResourceMarker> update) =>
            document with
            {
                Markers = document.Markers with { Resources = document.Markers.Resources.Update(resource => resource.Name == name, update, $"deposit {name}") },
            };

        /// <summary>
        /// The set with the marker of that id replaced by <paramref name="marker"/>, taken out when it
        /// is null, or with <paramref name="marker"/> added when <paramref name="add"/>.
        /// </summary>
        private static MarkerSet Change(MarkerSet markers, int id, Marker? marker, bool add)
        {
            ImmutableArray<T> In<T>(ImmutableArray<T> list) where T : Marker
            {
                if (add)
                {
                    return marker is T added ? list.Add(added) : list;
                }
                for (int i = 0; i < list.Length; i++)
                {
                    if (list[i].Id == id)
                    {
                        return marker is null ? list.RemoveAt(i) : list.SetItem(i, (T)marker);
                    }
                }
                return list;
            }
            return markers with
            {
                Spawns = In(markers.Spawns),
                Resources = In(markers.Resources),
                PathNodes = In(markers.PathNodes),
                AiMarkers = In(markers.AiMarkers),
                WeatherDefinitions = In(markers.WeatherDefinitions),
                WeatherGenerators = In(markers.WeatherGenerators),
            };
        }

        #endregion

        #region Areas

        /// <summary>
        /// Adds an area; one whose name is taken gets the next free number after the same text.
        /// </summary>
        public static MapDocument AddArea(this MapDocument document, Area area) => document.AddArea(area, out _);

        /// <inheritdoc cref="AddArea(MapDocument, Area)"/>
        /// <param name="added">The area as it was added, with its name.</param>
        public static MapDocument AddArea(this MapDocument document, Area area, out Area added)
        {
            ImmutableArray<Area> areas = document.Markers.Areas;
            added = area.Name.Length == 0 || areas.Any(a => a.Name == area.Name)
                ? area with { Name = FreeName(areas.Select(a => a.Name), area.Name.Length == 0 ? "AREA_1" : area.Name) }
                : area;
            return document with { Markers = document.Markers with { Areas = areas.Add(added) } };
        }

        /// <summary>
        /// Changes an area. Renaming the playable area names it in the scenario
        /// (<see cref="ScenarioInfo.PlayableArea"/>), also when it was the first area by convention,
        /// so the export points the script at the new name.
        /// </summary>
        /// <exception cref="KeyNotFoundException">No area has the name.</exception>
        public static MapDocument UpdateArea(this MapDocument document, string name, Func<Area, Area> update)
        {
            bool playable = document.PlayableArea?.Name == name;
            MapDocument updated = document with { Markers = document.Markers with { Areas = document.Markers.Areas.Update(area => area.Name == name, update, $"area {name}") } };
            string? renamed = updated.Markers.Areas.FirstOrDefault(area => !document.Markers.Areas.Any(before => before.Name == area.Name))?.Name;
            return playable && renamed is not null
                ? updated with { Scenario = updated.Scenario with { PlayableArea = renamed } }
                : updated;
        }

        /// <summary>
        /// Removes an area; removing the playable area named in the scenario makes the first area
        /// the playable one again.
        /// </summary>
        /// <exception cref="KeyNotFoundException">No area has the name.</exception>
        public static MapDocument RemoveArea(this MapDocument document, string name)
        {
            MapDocument removed = document with { Markers = document.Markers with { Areas = document.Markers.Areas.RemoveAt(document.Markers.Areas.Find(area => area.Name == name, $"area {name}")) } };
            return removed.Scenario.PlayableArea == name ? removed with { Scenario = removed.Scenario with { PlayableArea = null } } : removed;
        }

        /// <summary>
        /// Makes an area the playable area at the start (user story M-3), or the first area again
        /// (the convention) with null.
        /// </summary>
        /// <exception cref="KeyNotFoundException">No area has the name.</exception>
        public static MapDocument SetPlayableArea(this MapDocument document, string? name)
        {
            if (name is not null && !document.Markers.Areas.Any(area => area.Name == name))
            {
                throw new KeyNotFoundException($"The map has no area {name}");
            }
            return document.Scenario.PlayableArea == name ? document : document with { Scenario = document.Scenario with { PlayableArea = name } };
        }

        #endregion

        /// <summary>
        /// A name that none of <paramref name="taken"/> is: the text of <paramref name="wanted"/>
        /// without its number at the end, with one more than the highest number it is taken with,
        /// written with as many digits as before (<c>Mass 08</c> becomes <c>Mass 12</c>,
        /// <c>ARMY_9</c> becomes <c>ARMY_10</c>).
        /// </summary>
        public static string FreeName(IEnumerable<string> taken, string wanted)
        {
            int end = wanted.Length;
            while (end > 0 && char.IsAsciiDigit(wanted[end - 1]))
            {
                end--;
            }
            string stem = wanted[..end];
            int digits = Math.Max(1, wanted.Length - end);
            string[] names = [.. taken];
            if (!names.Contains(wanted, StringComparer.Ordinal))
            {
                return wanted;
            }
            string separator = end == wanted.Length && stem.Length > 0 && !stem.EndsWith(' ') && !stem.EndsWith('_') ? " " : "";
            int next = NextNumber(names, stem + separator);
            return stem + separator + next.ToString(CultureInfo.InvariantCulture).PadLeft(digits, '0');
        }

        /// <summary>
        /// A name for a copy of something: <paramref name="wanted"/> when free; else its name (without a
        /// number after it) with the next number from 2 (<c>Cybran farm</c>, <c>Cybran farm 2</c>,
        /// <c>Cybran farm 3</c>). A second placement of a preset reads as the second one.
        /// </summary>
        public static string FreeCopyName(IEnumerable<string> taken, string wanted)
        {
            string[] names = [.. taken];
            if (!names.Contains(wanted, StringComparer.Ordinal))
            {
                return wanted;
            }
            string stem = wanted;
            int space = wanted.LastIndexOf(' ');
            if (space > 0 && int.TryParse(wanted.AsSpan(space + 1), NumberStyles.None, CultureInfo.InvariantCulture, out _))
            {
                stem = wanted[..space];
            }
            int next = Math.Max(2, NextNumber(names, stem + " "));
            return $"{stem} {next.ToString(CultureInfo.InvariantCulture)}";
        }

        /// <summary>
        /// One more than the highest number that follows <paramref name="prefix"/> in a name, 1 when none does.
        /// </summary>
        private static int NextNumber(IEnumerable<string> names, string prefix)
        {
            int highest = 0;
            foreach (string name in names)
            {
                if (name.StartsWith(prefix, StringComparison.Ordinal)
                    && int.TryParse(name.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int number))
                {
                    highest = Math.Max(highest, number);
                }
            }
            return highest + 1;
        }

        /// <summary>
        /// The array with its one item that <paramref name="match"/> finds replaced by <paramref name="update"/>'s result.
        /// </summary>
        /// <exception cref="KeyNotFoundException">No item matches.</exception>
        public static ImmutableArray<T> Update<T>(this ImmutableArray<T> items, Func<T, bool> match, Func<T, T> update, string what)
        {
            int index = items.Find(match, what);
            return items.SetItem(index, update(items[index]));
        }

        private static int Find<T>(this ImmutableArray<T> items, Func<T, bool> match, string what)
        {
            for (int i = 0; i < items.Length; i++)
            {
                if (match(items[i]))
                {
                    return i;
                }
            }
            throw new KeyNotFoundException($"The map has no {what}");
        }
    }

    /// <summary>
    /// A unit with where it is in the armies' trees of groups.
    /// </summary>
    /// <param name="Group">The names of its group and the groups above it, from below the army's root group down; empty for the root group.</param>
    public sealed record PlacedUnit(string Army, ImmutableArray<string> Group, Unit Unit);
}
