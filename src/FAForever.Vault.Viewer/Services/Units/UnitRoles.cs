using FAForever.FileFormats.Blueprints;

namespace FAForever.Vault.Viewer.Services.Units;

/// <summary>
/// Readable labels for what a unit is, from its blueprint's categories and motion type, to pick
/// out special units on the unit card (a bomber, a gunship, an engineer, ...) and to filter the
/// unit database. The slugs are query values of the Units page: never rename one.
/// </summary>
public static class UnitRoles
{
    public sealed record Role(string Slug, string Label, Func<UnitSummary, bool> Applies);

    /// <summary>
    /// Every role, in order of display. Gunships have no category of their own: FAF marks them
    /// AIR + GROUNDATTACK.
    /// </summary>
    public static readonly IReadOnlyList<Role> All =
    [
        new("commander", "Commander", unit => unit.HasCategory("COMMAND")),
        new("subcommander", "Support commander", unit => unit.HasCategory("SUBCOMMANDER")),
        new("engineer", "Engineer", unit => unit.HasCategory("ENGINEER") && !unit.HasCategory("COMMAND") && !unit.HasCategory("SUBCOMMANDER")),
        new("factory", "Factory", unit => unit.HasCategory("FACTORY")),
        new("strategicbomber", "Strategic bomber", unit => unit.HasCategory("STRATEGICBOMBER")),
        new("bomber", "Bomber", unit => unit.HasCategory("BOMBER") && !unit.HasCategory("STRATEGICBOMBER")),
        new("gunship", "Gunship", unit => unit.HasCategory("AIR") && unit.HasCategory("GROUNDATTACK")),
        new("airsuperiority", "Air superiority", unit => unit.HasCategory("ASF")),
        new("antiair", "Anti air", unit => unit.HasCategory("ANTIAIR") && !unit.HasCategory("ASF")),
        new("transport", "Transport", unit => unit.HasCategory("TRANSPORTATION")),
        new("scout", "Scout", unit => unit.HasCategory("SCOUT")),
        new("sniper", "Sniper", unit => unit.HasCategory("SNIPER")),
        new("artillery", "Artillery", unit => unit.HasCategory("ARTILLERY")),
        new("tactical", "Tactical missiles", unit => unit.HasCategory("TACTICALMISSILEPLATFORM")),
        new("nuke", "Nuke", unit => unit.HasCategory("NUKE")),
        new("missiledefense", "Missile defense", unit => unit.HasCategory("ANTIMISSILE")),
        new("antinaval", "Anti naval", unit => unit.HasCategory("ANTINAVY") || unit.HasCategory("ANTISUB")),
        new("torpedodefense", "Torpedo defense", unit => unit.HasCategory("ANTITORPEDO")),
        new("carrier", "Carrier", unit => unit.HasCategory("CARRIER")),
        new("shield", "Shield", unit => unit.HasCategory("SHIELD")),
        new("stealth", "Stealth", unit => unit.HasCategory("STEALTH") || unit.HasCategory("STEALTHFIELD")),
        new("massextractor", "Mass extractor", unit => unit.HasCategory("MASSEXTRACTION")),
        new("massfabricator", "Mass fabricator", unit => unit.HasCategory("MASSFABRICATION")),
        new("power", "Power generator", unit => unit.HasCategory("ENERGYPRODUCTION")),
        new("experimental", "Experimental", unit => unit.HasCategory("EXPERIMENTAL")),
    ];

    /// <summary>The roles of a unit, in display order.</summary>
    public static IEnumerable<string> For(UnitSummary unit) =>
        All.Where(role => role.Applies(unit)).Select(role => role.Label);

    /// <summary>Where the unit moves, from <see cref="UnitSummary.MotionType"/>.</summary>
    public static string? Layer(UnitSummary unit) => unit.MotionType switch
    {
        "RULEUMT_Land" => "Land",
        "RULEUMT_Air" => "Air",
        "RULEUMT_Amphibious" => "Amphibious",
        "RULEUMT_AmphibiousFloating" => "Amphibious (floats)",
        "RULEUMT_Hover" => "Hover",
        "RULEUMT_Water" => "Naval",
        "RULEUMT_SurfacingSub" => "Submarine",
        "RULEUMT_None" => "Structure",
        _ => null,
    };

    /// <summary>
    /// A real weapon: not the explosion when the unit dies, a teleport effect, or an aim-only
    /// dummy without damage. The unit card and the unit database show only these.
    /// </summary>
    public static bool IsRealWeapon(UnitSummaryWeapon weapon) =>
        weapon.WeaponCategory is not ("Death" or "Teleport") && weapon.Damage is > 0;

    /// <summary>The longest range of the unit's real weapons, or null without one.</summary>
    public static double? MaxRange(UnitSummary unit) =>
        unit.Weapons.Where(IsRealWeapon).Select(weapon => weapon.MaxRadius).Max();

    /// <summary>The faction index of the unit (1 = UEF … 4 = Seraphim), as the faction helpers take it.</summary>
    public static Faction? FactionOf(UnitSummary unit) => unit.Faction switch
    {
        "UEF" => Faction.Uef,
        "Aeon" => Faction.Aeon,
        "Cybran" => Faction.Cybran,
        "Seraphim" => Faction.Seraphim,
        _ => null,
    };
}
