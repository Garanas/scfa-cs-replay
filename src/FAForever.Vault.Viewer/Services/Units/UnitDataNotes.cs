using System.Text.RegularExpressions;
using FAForever.FileFormats.Lua;
using FAForever.Vault.Viewer.Services.Replays;

namespace FAForever.Vault.Viewer.Services.Units;

/// <summary>
/// Why the unit data may not match a replay. The data holds one game version of the FAF branch
/// (featured mod <c>faf</c>) without mods; anything else gets a note on the unit card.
/// </summary>
public static partial class UnitDataNotes
{
    /// <summary>
    /// The notes for a replay, empty when it was played on the FAF branch, without mods, on the
    /// game version of the data.
    /// </summary>
    public static IReadOnlyList<string> For(LoadedReplay replay, int dataVersion)
    {
        List<string> notes = [];

        string? featuredMod = replay.Metadata?.FeaturedMod;
        int? gameVersion = GameVersion(replay.Replay.Header.GameVersion);
        if (featuredMod is { Length: > 0 } && !featuredMod.Equals("faf", StringComparison.OrdinalIgnoreCase))
        {
            notes.Add($"Played on the {featuredMod} branch; these values are from FAF and may be incorrect.");
        }
        else if (gameVersion is null)
        {
            notes.Add($"Not played on FAF ({replay.Replay.Header.GameVersion}); these values may be incorrect.");
        }

        if (gameVersion is { } version && version != dataVersion)
        {
            notes.Add($"Played on game version {version}; these values are from {dataVersion} and may differ.");
        }

        string[] mods = replay.Replay.Header.Mods.Select(ModName).ToArray();
        if (mods.Length > 0)
        {
            notes.Add($"Mods were active ({string.Join(", ", mods)}); these values may be incorrect.");
        }

        return notes;
    }

    /// <summary>
    /// The FAF game version from a replay header: "Supreme Commander v1.50.3809" → 3809. Null for
    /// other versions, such as the Steam release (v1.60).
    /// </summary>
    public static int? GameVersion(string? headerVersion) =>
        headerVersion is not null && FafVersion().Match(headerVersion) is { Success: true } match && int.TryParse(match.Groups[1].ValueSpan, out int version)
            ? version
            : null;

    private static string ModName(LuaData mod) =>
        mod is LuaData.Table table && table.TryGetStringValue("name", out string? name) && name is { Length: > 0 } ? name : "unnamed mod";

    [GeneratedRegex(@"v1\.50\.(\d+)\s*$")]
    private static partial Regex FafVersion();
}
