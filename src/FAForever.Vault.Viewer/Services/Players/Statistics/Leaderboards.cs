namespace FAForever.Vault.Viewer.Services.Players.Statistics;

/// <summary>Names of the leaderboards, as shown to people.</summary>
public static class Leaderboards
{
    /// <summary>The technical name of the ladder 1v1 board.</summary>
    public const string Ladder1v1 = "ladder_1v1";

    /// <summary>
    /// The name shown for a board: the known boards by name, any other <c>tmm_</c> board from its
    /// technical name (<c>tmm_3v3</c> is "3v3"), anything else with its underscores as spaces.
    /// </summary>
    public static string DisplayName(string technicalName)
    {
        switch (technicalName)
        {
            case "global": return "Global";
            case "ladder_1v1": return "Ladder 1v1";
            case "tmm_2v2": return "2v2";
            case "tmm_4v4_full_share": return "4v4 full share";
            case "tmm_4v4_share_until_death": return "4v4 share until death";
        }

        string name = technicalName.StartsWith("tmm_", StringComparison.Ordinal) ? technicalName[4..] : technicalName;
        name = name.Replace('_', ' ').Trim();
        return name.Length == 0 ? technicalName : char.ToUpperInvariant(name[0]) + name[1..];
    }
}
