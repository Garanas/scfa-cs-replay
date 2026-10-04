using FAForever.FileFormats.Blueprints;

namespace FAForever.Vault.Viewer.Services.Units;

/// <summary>
/// Readable labels for what a unit is, from its blueprint's categories and motion type, to pick
/// out special units on the unit card (a bomber, a gunship, an engineer, ...).
/// </summary>
public static class UnitRoles
{
    // In order of display. Gunships have no category of their own: FAF marks them AIR + GROUNDATTACK.
    private static readonly (Func<UnitSummary, bool> Applies, string Label)[] Roles =
    [
        (unit => unit.HasCategory("COMMAND"), "Commander"),
        (unit => unit.HasCategory("SUBCOMMANDER"), "Support commander"),
        (unit => unit.HasCategory("ENGINEER") && !unit.HasCategory("COMMAND") && !unit.HasCategory("SUBCOMMANDER"), "Engineer"),
        (unit => unit.HasCategory("FACTORY"), "Factory"),
        (unit => unit.HasCategory("STRATEGICBOMBER"), "Strategic bomber"),
        (unit => unit.HasCategory("BOMBER") && !unit.HasCategory("STRATEGICBOMBER"), "Bomber"),
        (unit => unit.HasCategory("AIR") && unit.HasCategory("GROUNDATTACK"), "Gunship"),
        (unit => unit.HasCategory("ASF"), "Air superiority"),
        (unit => unit.HasCategory("ANTIAIR") && !unit.HasCategory("ASF"), "Anti air"),
        (unit => unit.HasCategory("TRANSPORTATION"), "Transport"),
        (unit => unit.HasCategory("SCOUT"), "Scout"),
        (unit => unit.HasCategory("SNIPER"), "Sniper"),
        (unit => unit.HasCategory("ARTILLERY"), "Artillery"),
        (unit => unit.HasCategory("TACTICALMISSILEPLATFORM"), "Tactical missiles"),
        (unit => unit.HasCategory("NUKE"), "Nuke"),
        (unit => unit.HasCategory("ANTIMISSILE"), "Missile defense"),
        (unit => unit.HasCategory("ANTINAVY") || unit.HasCategory("ANTISUB"), "Anti naval"),
        (unit => unit.HasCategory("ANTITORPEDO"), "Torpedo defense"),
        (unit => unit.HasCategory("CARRIER"), "Carrier"),
        (unit => unit.HasCategory("SHIELD"), "Shield"),
        (unit => unit.HasCategory("STEALTH") || unit.HasCategory("STEALTHFIELD"), "Stealth"),
        (unit => unit.HasCategory("MASSEXTRACTION"), "Mass extractor"),
        (unit => unit.HasCategory("MASSFABRICATION"), "Mass fabricator"),
        (unit => unit.HasCategory("ENERGYPRODUCTION"), "Power generator"),
        (unit => unit.HasCategory("EXPERIMENTAL"), "Experimental"),
    ];

    /// <summary>The roles of a unit, in display order.</summary>
    public static IEnumerable<string> For(UnitSummary unit) =>
        Roles.Where(role => role.Applies(unit)).Select(role => role.Label);

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
}
