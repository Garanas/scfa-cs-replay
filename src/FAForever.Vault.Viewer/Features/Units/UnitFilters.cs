using FAForever.FileFormats.Blueprints;
using FAForever.Vault.Viewer.Services;
using FAForever.Vault.Viewer.Services.Units;
using Microsoft.AspNetCore.Components;

namespace FAForever.Vault.Viewer.Features.Units;

/// <summary>
/// The filters and sort orders of the unit database (<c>/units</c>). Every choice lives in the URL,
/// so a filtered list can be shared; the slugs are part of that contract (see the Viewer guide,
/// "Unit database"). Within a group a unit needs one of the chosen options, across groups all.
/// </summary>
public static class UnitFilters
{
    /// <summary>An option of a filter group: its query value, label and test.</summary>
    public sealed record Option(string Slug, string Label, Func<UnitSummary, bool> Applies);

    /// <summary>A filter group: its query parameter and options.</summary>
    public sealed record Group(string Parameter, string Label, IReadOnlyList<Option> Options);

    public static readonly Group Factions = new("faction", "Faction",
    [
        new("uef", "UEF", unit => unit.Faction == "UEF"),
        new("aeon", "Aeon", unit => unit.Faction == "Aeon"),
        new("cybran", "Cybran", unit => unit.Faction == "Cybran"),
        new("seraphim", "Seraphim", unit => unit.Faction == "Seraphim"),
    ]);

    public static readonly Group Tech = new("tech", "Tech",
    [
        new("1", "Tech 1", unit => unit.TechLevel == 1),
        new("2", "Tech 2", unit => unit.TechLevel == 2),
        new("3", "Tech 3", unit => unit.TechLevel == 3),
        new("4", "Experimental", unit => unit.TechLevel == 4),
    ]);

    public static readonly Group Movement = new("move", "Moves on",
    [
        new("land", "Land", unit => unit.MotionType == "RULEUMT_Land"),
        new("amphibious", "Amphibious", unit => unit.MotionType is "RULEUMT_Amphibious" or "RULEUMT_AmphibiousFloating"),
        new("hover", "Hover", unit => unit.MotionType == "RULEUMT_Hover"),
        new("air", "Air", unit => unit.MotionType == "RULEUMT_Air"),
        new("naval", "Naval", unit => unit.MotionType == "RULEUMT_Water"),
        new("sub", "Submarine", unit => unit.MotionType == "RULEUMT_SurfacingSub"),
        new("structure", "Structure", unit => unit.MotionType == "RULEUMT_None"),
    ]);

    public static readonly Group Weapons = new("weapon", "Weapons",
    [
        new("direct", "Direct fire", unit => HasWeapon(unit, "UWRC_DirectFire")),
        new("indirect", "Artillery and missiles", unit => HasWeapon(unit, "UWRC_IndirectFire")),
        new("antiair", "Anti air", unit => HasWeapon(unit, "UWRC_AntiAir")),
        new("antinavy", "Torpedoes", unit => HasWeapon(unit, "UWRC_AntiNavy")),
        new("defense", "Missile or torpedo defense", unit => HasWeapon(unit, "UWRC_Countermeasure")),
        new("none", "No weapons", unit => !unit.Weapons.Any(UnitRoles.IsRealWeapon)),
    ]);

    public static readonly Group Intel = new("intel", "Intel",
    [
        new("radar", "Radar", unit => unit.RadarRadius is > 0),
        new("sonar", "Sonar", unit => unit.SonarRadius is > 0),
        new("omni", "Omni", unit => unit.OmniRadius is > 0),
    ]);

    public static readonly Group Roles = new("role", "Role",
        UnitRoles.All.Select(role => new Option(role.Slug, role.Label, role.Applies)).ToList());

    /// <summary>The groups in the order of the filter column.</summary>
    public static readonly IReadOnlyList<Group> Groups = [Factions, Tech, Movement, Roles, Weapons, Intel];

    /// <summary>A column to sort by: its query value, its label and the value it sorts on.</summary>
    public sealed record SortOrder(string Slug, string Label, Func<UnitSummary, IComparable?> Key);

    public static readonly IReadOnlyList<SortOrder> SortOrders =
    [
        new("name", "Unit", unit => DisplayName(unit)),
        new("mass", "Mass", unit => unit.BuildCostMass),
        new("energy", "Energy", unit => unit.BuildCostEnergy),
        new("time", "Build time", unit => unit.BuildTime),
        new("health", "Health", unit => unit.MaxHealth),
        new("speed", "Speed", unit => unit.MaxSpeed),
        new("range", "Range", unit => UnitRoles.MaxRange(unit)),
        new("vision", "Vision", unit => unit.VisionRadius),
    ];

    public const string TextParameter = "q";
    public const string AllParameter = "all";
    public const string ChangedParameter = "changed";
    public const string SortParameter = "sort";

    /// <summary>The filters and sort order of the page, as read from the URL.</summary>
    public sealed record State(
        string? Text,
        IReadOnlyDictionary<Group, HashSet<string>> Chosen,
        bool All,
        bool ChangedOnly,
        SortOrder? Sort,
        bool Descending)
    {
        public bool IsChosen(Group group, Option option) => Chosen[group].Contains(option.Slug);

        /// <summary>
        /// The units that pass, in the chosen order. <paramref name="changed"/> are the ids of the units
        /// changed or added in this version, for <see cref="ChangedOnly"/>.
        /// </summary>
        public IReadOnlyList<UnitSummary> Apply(IEnumerable<UnitSummary> units, IReadOnlySet<string> changed)
        {
            IEnumerable<UnitSummary> passing = units.Where(Passes).Where(unit => !ChangedOnly || changed.Contains(unit.BlueprintId));
            if (Sort is null)
            {
                // default: faction, then tech, then name, so related units sit together
                return passing
                    .OrderBy(unit => UnitRoles.FactionOf(unit) ?? Faction.Random)
                    .ThenBy(unit => unit.TechLevel ?? 0)
                    .ThenBy(DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            // units without the value (a structure has no speed) always go last
            List<UnitSummary> withValue = passing.Where(unit => Sort.Key(unit) is not null).ToList();
            List<UnitSummary> without = passing.Where(unit => Sort.Key(unit) is null).ToList();
            IOrderedEnumerable<UnitSummary> sorted = Descending
                ? withValue.OrderByDescending(Sort.Key)
                : withValue.OrderBy(Sort.Key);
            return sorted.ThenBy(DisplayName, StringComparer.OrdinalIgnoreCase).Concat(without).ToList();
        }

        private bool Passes(UnitSummary unit)
        {
            if (!All && !unit.Buildable)
            {
                return false;
            }

            if (Text is { Length: > 0 } text
                && !(unit.BlueprintId.Contains(text, StringComparison.OrdinalIgnoreCase)
                     || unit.Name?.Contains(text, StringComparison.OrdinalIgnoreCase) == true
                     || unit.Description?.Contains(text, StringComparison.OrdinalIgnoreCase) == true))
            {
                return false;
            }

            foreach ((Group group, HashSet<string> chosen) in Chosen)
            {
                if (chosen.Count > 0 && !group.Options.Any(option => chosen.Contains(option.Slug) && option.Applies(unit)))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>What is filtered, in a few words, for the folded filter column on narrow screens.</summary>
        public string Summary()
        {
            List<string> parts = [];
            if (Text is { Length: > 0 })
            {
                parts.Add($"\"{Text}\"");
            }

            foreach ((Group group, HashSet<string> chosen) in Chosen)
            {
                if (chosen.Count > 0)
                {
                    parts.Add(string.Join(", ", group.Options.Where(option => chosen.Contains(option.Slug)).Select(option => option.Label)));
                }
            }

            if (ChangedOnly)
            {
                parts.Add("changed in this version");
            }

            if (All)
            {
                parts.Add("campaign units too");
            }

            return parts.Count > 0 ? string.Join(" · ", parts) : "all buildable units";
        }
    }

    /// <summary>
    /// Reads the filters from the URL. Unknown values are ignored, so an old or edited link still
    /// opens a sensible list.
    /// </summary>
    public static State Read(NavigationManager navigation)
    {
        Dictionary<Group, HashSet<string>> chosen = Groups.ToDictionary(
            group => group,
            group =>
            {
                string[] values = (UrlQuery.Get(navigation, group.Parameter) ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
                return group.Options.Select(option => option.Slug).Intersect(values, StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
            });

        string? sort = UrlQuery.Get(navigation, SortParameter);
        bool descending = sort?.StartsWith('-') == true;
        SortOrder? order = SortOrders.FirstOrDefault(candidate => candidate.Slug == sort?.TrimStart('-'));

        return new State(
            UrlQuery.Get(navigation, TextParameter)?.Trim(),
            chosen,
            UrlQuery.Get(navigation, AllParameter) == "1",
            UrlQuery.Get(navigation, ChangedParameter) == "1",
            order,
            descending && order is not null);
    }

    /// <summary>The value of a group's parameter with an option toggled, or null when none is left.</summary>
    public static string? Toggled(State state, Group group, Option option)
    {
        HashSet<string> chosen = new(state.Chosen[group], StringComparer.OrdinalIgnoreCase);
        if (!chosen.Remove(option.Slug))
        {
            chosen.Add(option.Slug);
        }

        // keep the option order, so the parameter is stable and shareable
        string[] ordered = group.Options.Select(candidate => candidate.Slug).Where(chosen.Contains).ToArray();
        return ordered.Length > 0 ? string.Join(',', ordered) : null;
    }

    /// <summary>The name shown for a unit: its name, else its description, else its id.</summary>
    public static string DisplayName(UnitSummary unit) => unit.Name ?? unit.Description ?? unit.BlueprintId;

    private static bool HasWeapon(UnitSummary unit, string rangeCategory) =>
        unit.Weapons.Any(weapon => UnitRoles.IsRealWeapon(weapon) && weapon.RangeCategory == rangeCategory);
}
