namespace FAForever.Vault.Viewer.Features.Players.Compare;

/// <summary>The words and rules of the two name fields of the comparison.</summary>
public static class CompareInput
{
    /// <summary>Shown under the second field when both fields name one player.</summary>
    public const string SamePlayerMessage = "Pick two different players. A player cannot be compared with themselves.";

    /// <summary>Shown under a field whose name no player has.</summary>
    public static string UnknownMessage(string name)
        => $"No player is called \u201C{name.Trim()}\u201D. Check the spelling, or search for the name on the Players page.";

    /// <summary>True when both ids are given and are the same player.</summary>
    public static bool IsSamePlayer(int? a, int? b) => a is not null && a == b;

    /// <summary>
    /// The ids the address keeps after a submit: the players that resolved, the second one dropped when
    /// it is the first again. An unknown name leaves its id out, so the comparison below clears.
    /// </summary>
    public static (int? A, int? B) Resolved(int? a, int? b) => (a, IsSamePlayer(a, b) ? null : b);
}
