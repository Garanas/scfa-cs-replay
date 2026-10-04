namespace FAForever.Vault.Viewer.Features.About;

/// <summary>
/// The facts the replay format page (Pages/AboutReplayFormat.razor) shows, measured once on vault
/// replay 25717491 (a 1v1 on Osiris, zstd, game version 3828) with the parser: the file sizes,
/// the header, a raw scan of the message frames (kind byte + length) and ReplayAnalysis. They
/// are fixed on purpose, so the page reads the same without downloading anything; re-measure
/// them if the example ever changes.
/// </summary>
public static class ExampleReplay
{
    public const int ReplayId = 25717491;

    /// <summary>A tone groups bytes of the same role in the illustrations; AnnotatedBytes maps it to colours.</summary>
    public enum Tone { Frame, Selection, Order, Target, Blueprint, Formation, Extra, Unknown, Marker, Text, Number }

    /// <summary>A run of bytes (hexadecimal, space separated) with what they mean.</summary>
    public sealed record ByteGroup(string Hex, string Label, string Value, string Explanation, Tone Tone);

    /// <summary>The first bytes of the decompressed replay: the game version as text and its closing zero.</summary>
    public static readonly byte[] VersionBytes = [.. "Supreme Commander v1.50.3828"u8, 0];

    /// <summary>The first 40 bytes of the decompressed replay, for the hex dump in the hero.</summary>
    public static readonly byte[] FirstBytes = [.. "Supreme Commander v1.50.3828"u8, 0, 0x0D, 0x0A, 0, .. "Replay v"u8];

    /// <summary>How the lobby stores <c>Faction = 3</c> (Cybran) as a Lua table.</summary>
    public static readonly ByteGroup[] LuaFaction =
    [
        new("04", "Table starts", "4", "Mark 4 opens a table.", Tone.Marker),
        new("01", "Text follows", "1", "Mark 1: the name of the value is text.", Tone.Marker),
        new("46 61 63 74 69 6F 6E 00", "Faction", "Faction", "The letters of the name, closed by a zero.", Tone.Text),
        new("00", "Number follows", "0", "Mark 0: the value is a number.", Tone.Marker),
        new("00 00 40 40", "3", "3", "The number 3, stored as a decimal number.", Tone.Number),
        new("05", "Table ends", "5", "Mark 5 closes the table.", Tone.Marker),
    ];

    /// <summary>The first order of the game: all 64 bytes of the message, in reading order.</summary>
    public static readonly ByteGroup[] FirstOrder =
    [
        new("0C", "Kind", "12: an order", "The first byte of every message says what kind it is. Kind 12 is an order to units.", Tone.Frame),
        new("40 00", "Length", "64 bytes", "The length of the whole message. The smallest part comes first, so 40 00 is 64. With it, a reader can skip a message it does not need.", Tone.Frame),
        new("01 00 00 00", "Units", "1 unit", "How many units the player had selected.", Tone.Selection),
        new("00 00 00 00", "Unit id", "0: the Commander", "Which unit it is. Every army counts its units from its own starting number, and number 0 of every army is its Commander.", Tone.Selection),
        new("00 00 00 00", "Order number", "0", "Every order gets a number. Later messages use it to change this order, for example to remove it from the queue.", Tone.Order),
        new("FF FF FF FF", "Unknown", "?", "Nobody knows yet what this means. We know its size, so we can step over it.", Tone.Unknown),
        new("08", "Order kind", "8: build", "There are 39 kinds of order. Kind 8 tells an engineer, or a Commander, to build something.", Tone.Order),
        new("FF FF FF FF", "Unknown", "?", "Another mystery of four bytes.", Tone.Unknown),
        new("02", "Target kind", "2: a place", "What the order points at: 0 is nothing, 1 is a unit and 2 is a place on the map.", Tone.Target),
        new("00 C0 E3 43", "X", "455.5", "How far from the left edge of the map, as a decimal number.", Tone.Target),
        new("00 BE 8F 42", "Height", "71.9", "How high the ground is at that spot.", Tone.Target),
        new("00 60 55 44", "Z", "853.5", "How far from the top edge of the map. The map is 1024 by 1024, so this spot lies near the bottom edge.", Tone.Target),
        new("00", "Unknown", "?", "One more unknown byte.", Tone.Unknown),
        new("FF FF FF FF", "Formation", "none", "All bits set means no formation. A move in formation would add its shape here.", Tone.Formation),
        new("75 72 62 30 31 30 31 00", "Blueprint", "urb0101", "The unit to build, as text closed by a zero. URB0101 is the Cybran Land Factory.", Tone.Blueprint),
        new("00 00 00 00", "Unknown", "?", "The first of twelve bytes that nobody has explained yet.", Tone.Unknown),
        new("01 00 00 00", "Unknown", "?", "Still part of the mystery.", Tone.Unknown),
        new("01 00 00 00", "Unknown", "?", "And the last part of it.", Tone.Unknown),
        new("02", "Extra data", "nothing", "Some orders carry a Lua table with extra settings. Mark 2 means there is nothing.", Tone.Extra),
        new("01", "Queue", "replace", "1 means the order replaces what the Commander was doing. 0 means the player held shift to add it to the queue.", Tone.Extra),
    ];

    /// <summary>A share of the body, in bytes, per group of message kinds.</summary>
    public sealed record ByteShare(string Label, long Bytes, int Messages, Tone Tone);

    /// <summary>Where the 2,292,857 bytes of the body go (raw scan of the message frames).</summary>
    public static readonly ByteShare[] BodyShares =
    [
        new("Orders", 1_303_877, 14_154, Tone.Order),
        new("Whose turn", 435_080, 108_770, Tone.Selection),
        new("Time moves on", 380_688, 54_384, Tone.Frame),
        new("Callbacks", 86_090, 569, Tone.Blueprint),
        new("Checksums", 50_048, 2_176, Tone.Target),
        new("Everything else", 37_074, 2_889, Tone.Unknown),
    ];

    /// <summary>A build order entry: game time, blueprint and how often it was ordered at that moment.</summary>
    public sealed record BuildStep(string Time, string BlueprintId, int Count);

    public static readonly BuildStep[] CybranOpening =
    [
        new("0:04", "urb0101", 1),
        new("0:05", "urb1101", 1),
        new("0:06", "urb1101", 1),
        new("0:07", "urb1103", 1),
        new("0:08", "urb1103", 1),
        new("0:10", "urb1103", 1),
    ];

    public static readonly BuildStep[] AeonOpening =
    [
        new("0:06", "uab0101", 1),
        new("0:11", "ual0105", 7),
    ];
}
