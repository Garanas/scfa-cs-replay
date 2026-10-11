using System.Collections.Immutable;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// What to do with the units that are wrecks (a unit of a <c>WRECKAGE</c> group, <see cref="UnitWrecks"/>).
    /// </summary>
    public enum WreckChoice
    {
        /// <summary>
        /// Only units that are not wrecks.
        /// </summary>
        Leave,

        /// <summary>
        /// Only wrecks.
        /// </summary>
        Only,

        /// <summary>
        /// Both.
        /// </summary>
        Include,
    }

    /// <summary>
    /// A question about the units of a map (user story U-10): which of them are of an army, a faction,
    /// a kind, a tech level, a blueprint, and wrecks or not. A part left empty asks nothing.
    /// </summary>
    public sealed record UnitQuery
    {
        /// <summary>
        /// The kinds a unit can be of, as the catalogue's <see cref="AssetEntry.Category"/> names them,
        /// and <c>other</c> for the rest (a unit the catalogue lacks, a commander's other layers).
        /// </summary>
        public static readonly ImmutableArray<string> Kinds = ["structure", "land", "air", "naval", "other"];

        /// <summary>
        /// The factions of a unit, as <see cref="ArmyEdits.Factions"/> and <c>other</c> (civilians).
        /// </summary>
        public static readonly ImmutableArray<string> FactionNames = [.. ArmyEdits.Factions, "other"];

        /// <summary>
        /// The army's name; null for every army.
        /// </summary>
        public string? Army { get; init; }

        /// <summary>
        /// The factions wanted (of <see cref="FactionNames"/>); empty for any.
        /// </summary>
        public ImmutableArray<string> Factions { get; init; } = [];

        /// <summary>
        /// The kinds wanted (of <see cref="Kinds"/>); empty for any.
        /// </summary>
        public ImmutableArray<string> Layers { get; init; } = [];

        /// <summary>
        /// The tech levels wanted (1 to 4); empty for any.
        /// </summary>
        public ImmutableArray<int> Tech { get; init; } = [];

        /// <summary>
        /// A blueprint id (<c>ueb1101</c>), or part of an id or of the unit's name
        /// (<c>factory</c>), in any case; null or empty for any.
        /// </summary>
        public string? Blueprint { get; init; }

        public WreckChoice Wrecks { get; init; } = WreckChoice.Leave;

        /// <summary>
        /// The faction of a unit: its blueprint's in the catalogue (<c>uef</c>, <c>aeon</c>, <c>cybran</c>,
        /// <c>seraphim</c>, <c>other</c>), else its army's.
        /// </summary>
        public static string FactionOf(PlacedUnit unit, AssetEntry? entry, MapDocument document)
        {
            if (entry is not null && entry.Group.Length > 0)
            {
                return entry.Group.ToLowerInvariant();
            }
            Army? army = document.FindArmy(unit.Army);
            return army is not null && army.Faction >= 0 && army.Faction < ArmyEdits.Factions.Length ? ArmyEdits.Factions[army.Faction] : "other";
        }

        /// <summary>
        /// The kind of a unit: its blueprint's category, <c>other</c> when it is none of the four or
        /// the catalogue lacks the blueprint.
        /// </summary>
        public static string KindOf(AssetEntry? entry) =>
            entry is not null && Kinds.Contains(entry.Category, StringComparer.OrdinalIgnoreCase) ? entry.Category.ToLowerInvariant() : "other";

        /// <summary>
        /// Whether a unit answers the question.
        /// </summary>
        /// <param name="entry">The unit's blueprint in the catalogue; null when it has none.</param>
        public bool Matches(MapDocument document, PlacedUnit unit, AssetEntry? entry)
        {
            if (Army is not null && !string.Equals(unit.Army, Army, StringComparison.Ordinal))
            {
                return false;
            }
            bool wreck = UnitWrecks.IsWreck(unit);
            if (Wrecks == WreckChoice.Leave && wreck || Wrecks == WreckChoice.Only && !wreck)
            {
                return false;
            }
            if (Factions.Length > 0 && !Factions.Contains(FactionOf(unit, entry, document), StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }
            if (Layers.Length > 0 && !Layers.Contains(KindOf(entry), StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }
            if (Tech.Length > 0 && (entry?.TechLevel is not int tech || !Tech.Contains(tech)))
            {
                return false;
            }
            if (!string.IsNullOrWhiteSpace(Blueprint))
            {
                string wanted = Blueprint.Trim();
                string blueprint = unit.Unit.Blueprint;
                if (!blueprint.Contains(wanted, StringComparison.OrdinalIgnoreCase) && entry?.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase) != true)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// The names of the units that answer the question, in the map's order.
        /// </summary>
        /// <param name="find">A blueprint's entry in the catalogue, or null.</param>
        /// <param name="within">The names of the units to ask among; null for every unit of the map.</param>
        public IEnumerable<string> Select(MapDocument document, Func<string, AssetEntry?> find, IReadOnlySet<string>? within = null)
        {
            foreach (PlacedUnit unit in document.AllUnits())
            {
                if ((within is null || within.Contains(unit.Unit.Name)) && Matches(document, unit, find(unit.Unit.Blueprint)))
                {
                    yield return unit.Unit.Name;
                }
            }
        }
    }
}
