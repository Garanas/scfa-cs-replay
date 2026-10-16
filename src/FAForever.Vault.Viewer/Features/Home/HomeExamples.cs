using System.Globalization;
using FAForever.FileFormats.Blueprints;

namespace FAForever.Vault.Viewer.Features.Home;

/// <summary>
/// A map of the home page's "Look around" card: its vault folder and the vault's facts about it,
/// written down so the page fetches nothing but the preview. The size is in game units
/// (<see cref="Services.Api.MapSizes"/> shows it in kilometres), the players and the name are
/// the scenario file's, the author is the vault's (the login of whoever uploaded it).
/// </summary>
public sealed record ExampleMap(string Folder, string Name, int Size, int Players, string Author);

/// <summary>
/// A unit of the home page's "Look around" card. Its numbers are those of one release, as
/// decoration: the unit's history page, which the card opens, shows the current ones. An
/// experimental's description leaves out the word "Experimental", which its tech says.
/// </summary>
public sealed record ExampleUnit(string BlueprintId, string Name, Faction Faction, int TechLevel, string Description, int Health, int Mass, int Energy)
{
    /// <summary>"Tech 3", or "Experimental" for tech 4, as the unit pages say it.</summary>
    public string Tech => TechLevel == 4 ? "Experimental" : $"Tech {TechLevel}";

    /// <summary>The numbers shown under the icon, with a label each.</summary>
    public IEnumerable<(string Label, string Value)> Stats =>
        [("Health", Number(Health)), ("Mass", Number(Mass)), ("Energy", Number(Energy))];

    private static string Number(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
}

/// <summary>
/// The maps and units the home page can show: one of each on every visit, picked at random. Every
/// map must be in the vault (its preview comes from there) and every unit in the icon atlas.
/// </summary>
public static class HomeExamples
{
    /// <summary>
    /// Checked against the vault on 2026-10-16: the name, size and players from each map's scenario
    /// file, the author from its map page in this app.
    /// </summary>
    public static readonly IReadOnlyList<ExampleMap> Maps =
    [
        new("lily_pads.v0010", "Lily Pads", 1024, 9, "Jip"),
        new("gateway_and_rose.v0002", "Gateway and Rose", 1024, 8, "biass"),
        new("adaptive_moon.v0002", "Adaptive Moon", 1024, 12, "svenni_badbwoi"),
        new("project_tabula.v0006", "Project Tabula", 1024, 8, "IndexLibrorum"),
        new("distortion.v0018", "Distortion", 512, 4, "STlNG"),
        new("adaptive_alpine_valley.v0003", "Adaptive Alpine Valley", 512, 4, "CaptainKlutz"),
        new("optima_ferox.v0002", "Optima Ferox", 256, 2, "BorgCubeJanitor"),
    ];

    /// <summary>Three units per faction; the numbers are those of release 3837 (wwwroot/data/units/3837.json).</summary>
    public static readonly IReadOnlyList<ExampleUnit> Units =
    [
        new("uel0401", "Fatboy", Faction.Uef, 4, "Mobile Factory", 12500, 28000, 350000),
        new("xel0305", "Percival", Faction.Uef, 3, "Armored Assault Bot", 7200, 1280, 14000),
        new("ues0401", "Atlantis", Faction.Uef, 4, "Aircraft Carrier", 40000, 12000, 150000),
        new("ual0401", "Galactic Colossus", Faction.Aeon, 4, "Assault Bot", 99999, 27500, 343750),
        new("ual0303", "Harbinger Mark IV", Faction.Aeon, 3, "Heavy Assault Bot", 3600, 840, 9600),
        new("uaa0310", "CZAR", Faction.Aeon, 4, "Aircraft Carrier", 40000, 42750, 1453500),
        new("url0303", "Loyalist", Faction.Cybran, 3, "Siege Assault Bot", 3000, 480, 5250),
        new("url0402", "Monkeylord", Faction.Cybran, 4, "Spiderbot", 45000, 20000, 260000),
        new("ura0401", "Soul Ripper", Faction.Cybran, 4, "Gunship", 75000, 29000, 812000),
        new("xsl0401", "Ythotha", Faction.Seraphim, 4, "Assault Bot", 67000, 26500, 330000),
        new("xsl0303", "Othuum", Faction.Seraphim, 3, "Siege Tank", 4700, 840, 9600),
        new("xsa0402", "Ahwassa", Faction.Seraphim, 4, "Bomber", 52000, 48000, 1920000),
    ];

    /// <summary>A map for this visit.</summary>
    public static ExampleMap PickMap() => Maps[Random.Shared.Next(Maps.Count)];

    /// <summary>A unit for this visit.</summary>
    public static ExampleUnit PickUnit() => Units[Random.Shared.Next(Units.Count)];
}
