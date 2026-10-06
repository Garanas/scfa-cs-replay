using System.Reflection;
using static FAForever.Vault.Viewer.Features.About.ExampleReplay;

namespace FAForever.Vault.Viewer.Features.About;

/// <summary>
/// The facts the blueprint page (Pages/AboutBlueprints.razor) shows, measured once on release 3839
/// of the FA repository (tag 3839, 2026-08-28): the Fatboy's blueprint and script
/// (<c>units/UEL0401/</c>), a count of the constructor calls in every <c>.bp</c> file (the kinds),
/// <c>lua/system/Blueprints.lua</c> and <c>blueprints-units.lua</c> (what the game changes), and
/// <c>git log</c>/<c>git diff</c> between the release tags (the history). The blueprint itself is
/// embedded verbatim (<c>uel0401_unit.bp</c>). Re-measure when the example changes.
/// </summary>
public static class ExampleBlueprint
{
    public const string BlueprintId = "uel0401";

    public const int GameVersion = 3839;

    /// <summary>The release that taught the Fatboy to build while moving (FA pull request 5227).</summary>
    public const int MobileFactoryVersion = 3765;

    /// <summary>The Fatboy's blueprint file as release 3839 has it, with LF line endings.</summary>
    public static string Source { get; } = ReadSource();

    /// <summary>Lines of <c>UEL0401_script.lua</c> in 3839, and in 3764 and 3765 around the change.</summary>
    public const int ScriptLines = 374;

    public const int ScriptLinesBefore = 287;

    public const int ScriptLinesAfter = 360;

    /// <summary>Commits that changed the Fatboy's blueprint file, in the history of the FA repository up to 3839.</summary>
    public const int BlueprintCommits = 63;

    /// <summary>
    /// A part of the blueprint: the top level fields it covers and what they mean, with an optional
    /// link to where the game documents all of its fields.
    /// </summary>
    public sealed record BlueprintPart(string Id, string Title, string Group, Tone Tone, string Text, string[] Keys, PartLink? More = null);

    /// <summary>A link under the explanation of a part.</summary>
    public sealed record PartLink(string Label, string Url);

    public static readonly BlueprintPart[] Parts =
    [
        new("name", "Name and orders", "Everything else", Tone.Unknown,
            "What the unit is called and which faction it belongs to. CommandCaps lists the orders it accepts: attack, move, patrol and more. ToggleCaps adds the button for its shield dome.",
            ["Description", "General"]),
        new("ai", "Hints for the AI", "Everything else", Tone.Unknown,
            "Parameters for behavior that the engine runs on its own. AttackAngle turns the Fatboy 20 degrees while it attacks, so all of its turrets can fire. TargetBones are the parts enemies aim at, and the rest sets how fast an aircraft that lands on it is refuelled and repaired. The Fatboy sets 7 of the 16 fields the engine knows.",
            ["AI"],
            new("All fields of the AI table", $"https://github.com/FAForever/fa/blob/{GameVersion}/engine/Core/Blueprints/UnitBlueprint.lua#L134")),
        new("sound", "Sounds", "Sounds", Tone.Blueprint,
            "Every sound the unit makes: driving on land and in water, opening its factory, building, dying. Each one names a sound bank and a cue in that bank.",
            ["Audio"]),
        new("bumping", "Bumping", "Everything else", Tone.Unknown,
            "How dense the unit is. When units bump into each other, the denser one pushes the other aside. With 12,500, the Fatboy shoves tanks and bots out of its way. Big ships and most other experimentals are denser still: the Megalith tops the list with 110,000.",
            ["AverageDensity"]),
        new("size", "Size", "Everything else", Tone.Unknown,
            "How big the unit is in the world. That decides how easy it is to hit.",
            ["SizeX", "SizeY", "SizeZ"]),
        new("categories", "Categories", "Categories", Tone.Extra,
            "Labels. The rest of the game finds the unit by them: factories, weapons and the interface. Chapter 5 is all about them.",
            ["Categories"]),
        new("defense", "Health and shield", "Health, cost and movement", Tone.Target,
            "12,500 health that slowly comes back, and a shield dome with 20,000 health of its own. The threat levels tell the AI how dangerous the unit is.",
            ["Defense"]),
        new("looks", "Looks", "Looks", Tone.Selection,
            "Everything you see: the model and its textures, dust behind the wheels, the tracks it leaves, the shaking camera, the health bar and the icon when you zoom out. Hidden in here: the wheels crush what they drive over, 400 damage every half second.",
            ["Display", "LifeBarHeight", "LifeBarOffset", "LifeBarSize", "SelectionCenterOffsetZ", "SelectionSizeX", "SelectionSizeZ", "SelectionThickness", "StrategicIconName", "StrategicIconSortPriority", "BuildIconSortPriority"]),
        new("economy", "Cost and building", "Health, cost and movement", Tone.Target,
            "28,000 mass and 350,000 energy to build. BuildRate is its build power: 135. BuildableCategory says what it can build, and the shield costs 600 energy per second.",
            ["Economy"]),
        new("factory", "External factory", "Health, cost and movement", Tone.Target,
            "Where the selection box of its factory sits. That factory is a unit of its own, as chapter 8 shows.",
            ["ExternalFactory"]),
        new("intel", "Vision", "Health, cost and movement", Tone.Target,
            "How far it sees: 32, on land and under water.",
            ["Intel"]),
        new("movement", "Movement", "Health, cost and movement", Tone.Target,
            "A top speed of 1.75, how fast it speeds up and turns, and how it moves: amphibious, so it drives over the seabed.",
            ["Physics"]),
        new("transport", "Docking", "Everything else", Tone.Unknown,
            "Aircraft can land on the Fatboy to be repaired and refuelled. It has four spots for them.",
            ["Transport"]),
        new("veteran", "Veterancy", "Everything else", Tone.Unknown,
            "When the unit gains its next level of veterancy, and with it more health.",
            ["Veteran", "VeteranMassMult"]),
        new("weapons", "Weapons", "Weapons", Tone.Order,
            "Ten weapons in one list: four Gauss cannons, two riot guns, two anti air guns, a torpedo launcher and the blast when it dies. Each one has its damage, range, rate of fire and the projectile it fires.",
            ["Weapon"]),
        new("wreckage", "Wreckage", "Everything else", Tone.Unknown,
            "What stays behind: a wreck with 90 percent of its mass, to reclaim. It points at another blueprint, a prop.",
            ["Wreckage"]),
    ];

    /// <summary>The blueprints of release 3839 by kind: constructor calls in the 4,569 <c>.bp</c> files.</summary>
    public static readonly (string Kind, int Count, string Text)[] Kinds =
    [
        ("Emitters", 2_623, "Particles: sparks, smoke, fire and dust."),
        ("Units", 606, "Every unit, structure and commander."),
        ("Props", 564, "Trees, rocks and wrecks, the things you reclaim."),
        ("Projectiles", 393, "Everything a weapon fires."),
        ("Trails", 185, "The streak behind a shell."),
        ("Beams", 115, "Lasers and other beams."),
        ("Meshes", 79, "The shape of a model."),
    ];

    /// <summary>What the Fatboy builds in 3839 (UnitBuildTree, from its BuildableCategory).</summary>
    public static readonly string[] Builds =
    [
        "del0204", "delk002", "uel0101", "uel0103", "uel0104", "uel0105", "uel0106", "uel0111", "uel0201", "uel0202",
        "uel0203", "uel0205", "uel0208", "uel0303", "uel0304", "uel0307", "uel0309", "xel0209", "xel0305", "xel0306",
    ];

    /// <summary>The start of the Defense table, with its Shield table folded away.</summary>
    public static readonly string[] DefenseSnippet =
    [
        "Defense = {",
        "    ArmorType = \"Experimental\",",
        "    Health = 12500,",
        "    MaxHealth = 12500,",
        "    RegenRate = 20,",
        "    Shield = { ... },",
        "},",
    ];

    /// <summary>The label that links a weapon of the blueprint to its class in the script.</summary>
    public const string WeaponLabel = "RightTurret01";

    /// <summary>The first weapon in the blueprint, shortened.</summary>
    public static readonly string[] WeaponInBlueprint =
    [
        "Weapon = {",
        "    {",
        "        Damage = 250,",
        "        DisplayName = \"Gauss Cannon\",",
        "        Label = \"RightTurret01\",",
        "        MaxRadius = 100,",
        "        RateOfFire = 10/10,",
        "        ...",
    ];

    /// <summary>The weapons of <c>UEL0401_script.lua</c>, shortened.</summary>
    public static readonly string[] WeaponInScript =
    [
        "Weapons = {",
        "    RightTurret01 = ClassWeapon(TDFGaussCannonWeapon) {},",
        "    RightRiotgun = ClassWeapon(TDFRiotWeapon) { ... },",
        "    RightAAGun = ClassWeapon(TAALinkedRailgun) {},",
        "    Torpedo = ClassWeapon(TANTorpedoAngler) {},",
        "    ...",
    ];

    /// <summary>
    /// What release 3765 changed in the Fatboy's blueprint (<c>git diff 3764 3765</c>).
    /// </summary>
    public static readonly (string Line, string Text)[] MobileFactoryChanges =
    [
        ("\"EXTERNALFACTORY\",", "A new category: build with a factory of its own."),
        ("BuildRate = 135,", "Build power went down from 180."),
        ("Damage = { Amount = 400, ... }", "The wheels crush what they drive over."),
    ];

    /// <summary>A line of the blueprint and the index of its part in <see cref="Parts"/>; -1 for the opening and closing line.</summary>
    public sealed record BlueprintLine(string Text, int Part);

    /// <summary>The lines of <see cref="Source"/>, each with its part: the top level field it belongs to (indented four spaces).</summary>
    public static IReadOnlyList<BlueprintLine> Lines { get; } = Split();

    /// <summary>A share of the file, in bytes (a line ending counts as one), per group of parts.</summary>
    public sealed record PartShare(string Label, int Bytes, int Lines, Tone Tone);

    /// <summary>Where the bytes of the file go, by group, largest first and the rest last.</summary>
    public static IReadOnlyList<PartShare> Shares { get; } = Lines
        .Where(line => line.Part >= 0)
        .GroupBy(line => Parts[line.Part].Group)
        .Select(group => new PartShare(group.Key, group.Sum(line => line.Text.Length + 1), group.Count(), Parts[group.First().Part].Tone))
        .OrderByDescending(share => share.Label == "Everything else" ? -1 : share.Bytes)
        .ToArray();

    private static BlueprintLine[] Split()
    {
        Dictionary<string, int> partOfKey = Parts
            .SelectMany((part, index) => part.Keys.Select(key => (key, index)))
            .ToDictionary(entry => entry.key, entry => entry.index);

        int current = -1;
        return Source.TrimEnd('\n').Split('\n').Select(text =>
        {
            if (text.Length > 4 && text.StartsWith("    ", StringComparison.Ordinal) && char.IsLetter(text[4]))
            {
                string key = new(text.AsSpan(4).ToArray().TakeWhile(char.IsLetterOrDigit).ToArray());
                current = partOfKey.TryGetValue(key, out int part) ? part
                    : throw new InvalidOperationException($"The blueprint field {key} has no part.");
            }
            return new BlueprintLine(text, text.StartsWith(' ') ? current : -1);
        }).ToArray();
    }

    private static string ReadSource()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("uel0401_unit.bp")
            ?? throw new InvalidOperationException("The embedded blueprint uel0401_unit.bp is missing.");
        using StreamReader reader = new(stream);
        return reader.ReadToEnd().Replace("\r\n", "\n");
    }
}
