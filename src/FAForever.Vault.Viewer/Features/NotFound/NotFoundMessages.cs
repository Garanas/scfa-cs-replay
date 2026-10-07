using FAForever.Vault.Viewer.Services.Theming;

namespace FAForever.Vault.Viewer.Features.NotFound;

/// <summary>
/// One message of the 404 page: a joke on the lore and units of a faction, taken from the game
/// manuals (Supreme Commander) and the Forged Alliance campaign: the commanders' lines and taunts
/// in the FA repository's <c>loc/US/strings_db.lua</c> and <c>lua/ui/game/taunt.lua</c>, and the
/// Seraphim language that their unit names are built from.
/// </summary>
/// <param name="Unit">The blueprint id of the unit the joke is about, shown as its icon.</param>
/// <param name="DataLink">An optional tip in the style of the manual's "Data-Link" boxes.</param>
public sealed record NotFoundMessage(string Title, string Body, string Unit, string UnitName, string? DataLink = null);

/// <summary>The messages of the 404 page per faction; the page shows one at random.</summary>
public static class NotFoundMessages
{
    /// <summary>Who sends the messages of a faction, shown above the title.</summary>
    public static string Sender(FactionTheme faction) => faction.Id switch
    {
        "uef" => "Transmission from EarthCom",
        "aeon" => "Message from the Illuminate",
        "seraphim" => "Signal from quantum space",
        _ => "Broadcast from a Cybran Node",
    };

    public static IReadOnlyList<NotFoundMessage> For(FactionTheme faction) => faction.Id switch
    {
        "uef" => Uef,
        "aeon" => Aeon,
        "seraphim" => Seraphim,
        _ => Cybran,
    };

    private static readonly NotFoundMessage[] Uef =
    [
        new("Radar contact lost",
            "Our sensors show a contact at this address, but nothing is there. Some units flood an area with false-positive hits, so that nobody can tell which ones are real.",
            "ueb3104", "SA3 - Omni",
            "The Omni Sensor prevents false positives and reveals everything that is cloaked or stealthed. It finds no page here either."),
        new("No Mass deposit here",
            "Mass Extractors must be built on areas of the map designated as Mass deposits. Our Engineers surveyed this address: no deposit, so nothing was built.",
            "ueb1103", "Mass Pump 1",
            "Rocks give only Mass, while trees give Mass and Energy. Reclaiming this page gives neither."),
        new("Intercepted",
            "The Buzzkill has six rotating barrels that fire up to 12,000 rounds a minute. Whatever was heading for this address did not arrive.",
            "ueb4201", "Buzzkill"),
        new("Keep your CD Key safe",
            "This address leads nowhere. Should a page here ask for your CD Key, close it: neither FAForever nor this vault will ever ask you for it.",
            "uel0001", "Armored Command Unit",
            "Place the game case and manual in a secure place; you will need them if you ever need to reinstall the game."),
    ];

    private static readonly NotFoundMessage[] Cybran =
    [
        new("Stealthed and cloaked",
            "Like a Cybran ACU with both stealth and cloaking, this page is invisible to everything except the Omni Sensor. We built one. Still nothing.",
            "urb3104", "Olympus",
            "Cloaking protects you from visual confirmation but not from radar. Stealth is the other way around."),
        new("This is just a shell",
            "QAI says it of every ACU you destroy, because another rises in its place. This address is just a shell as well, but nothing rises in it.",
            "url0001", "Armored Command Unit"),
        new("Perhaps some remedial training is in order?",
            "There is no page at this address, my child. Oh yes. No page at all. Are you sure you want to do that again?",
            "xrl0403", "Megalith",
            "Dr. Brackman's Megalith has absorbed an awful lot of damage. Oh yes. So has this link."),
        new("Dostya out.",
            "Nothing at these coordinates, Commander. Return to base and pick another target. Dostya out.",
            "ura0401", "Soul Ripper",
            "I have a Soul Ripper ready to attack. Give me an attack marker on a page that exists, and I will send it."),
    ];

    private static readonly NotFoundMessage[] Aeon =
    [
        new("The portents are blurry",
            "Without the guidance of the Seraphim, the Aeon cannot fully master The Way, and the future is wrought with confusion. One thing the seers do know: there is no page at this address.",
            "uab3104", "Oculus"),
        new("Rhiza out.",
            "There is nothing at this address, Champion. Lay your sights upon the home page and bring glory to the Princess. Rhiza out.",
            "ual0401", "Galactic Colossus",
            "May the Galactic Colossus eliminate your enemies. It found nothing here to eliminate."),
        new("The truth was always in front of you",
            "So the Princess told Kael, who chose to ignore it. The truth here is plain: this address leads to no page.",
            "xab3301", "Eye of Rhianne",
            "The Eye of Rhianne reveals any spot on the map for 5,000 Energy a second. Here it reveals nothing."),
        new("A faded memory",
            "Gari called the Princess nothing more than a faded memory. She was wrong about the Princess. About this page she is right.",
            "ual0001", "Armored Command Unit"),
    ];

    private static readonly NotFoundMessage[] Seraphim =
    [
        new("[Language Not Recognized]",
            "Until Dr. Brackman finished his translator, every Seraphim transmission came through like this. The translator works now. This address still means nothing.",
            "xsl0001", "Armored Command Unit",
            "Brackman needed a captured Seraphim ACU to complete the translator. You only need a link that works."),
        new("Aezesel reports nothing",
            "Seraphim names say what a unit does: aez is command, esel is radar, so the Aezesel is the command radar. It swept this address and nothing answers to it.",
            "xsb3104", "Aezesel",
            "Hyal is Mass and atoh is extractor: a Hyalatoh mines Mass. The Seraphim have no word for this page."),
        new("The Rift is closed",
            "\"As long as the Rift remains open, we have a door to your galaxy,\" said Thel-Uuthow. This address was a door as well. It is closed.",
            "xsb0304", "Aezthu-uhthe"),
        new("We see what you see",
            "\"We know what you know, we see what you see,\" said Oum-Eoshi. So we see it too: there is nothing at this address.",
            "xsl0101", "Selen",
            "The Selen cloaks once it stands still. You have stood still here long enough: this page is empty, not cloaked."),
        new("Only one may ascend",
            "Only one species can attain perfection, the Seraphim believe. Only one address leads to each page of the vault, and this is not one of them.",
            "xsl0401", "Ythotha"),
    ];
}
