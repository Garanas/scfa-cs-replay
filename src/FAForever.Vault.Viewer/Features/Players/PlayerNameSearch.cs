using FAForever.Vault.Viewer.Services.Players;

namespace FAForever.Vault.Viewer.Features.Players;

/// <summary>The rules of the suggestions under a player name field: when to search and what to show.</summary>
public static class PlayerNameSuggestions
{
    /// <summary>The fewest characters a search starts at.</summary>
    public const int MinimumLength = 2;

    /// <summary>The most suggestions shown.</summary>
    public const int MaximumShown = 8;

    /// <summary>The wait after the last keystroke before the search goes out.</summary>
    public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(300);

    /// <summary>True when the text (trimmed) is long enough to search for.</summary>
    public static bool ShouldSearch(string? text) => (text?.Trim().Length ?? 0) >= MinimumLength;

    /// <summary>
    /// The hits as shown: a hit whose current name equals the text first of all, then the other current
    /// names, then the hits on a former name; a player once (the first hit wins), at most
    /// <see cref="MaximumShown"/>. The order within each group is the order the API gave.
    /// </summary>
    public static IReadOnlyList<PlayerSearchHit> Arrange(string? text, IEnumerable<PlayerSearchHit> hits)
    {
        string typed = text?.Trim() ?? "";
        return
        [
            .. hits
                .Select((hit, index) => (hit, index))
                .OrderBy(pair => Rank(pair.hit, typed))
                .ThenBy(pair => pair.index)
                .Select(pair => pair.hit)
                .DistinctBy(hit => hit.Id)
                .Take(MaximumShown),
        ];
    }

    private static int Rank(PlayerSearchHit hit, string typed)
    {
        if (hit.MatchedFormerName is { Length: > 0 })
        {
            return 2;
        }

        return string.Equals(hit.Login, typed, StringComparison.OrdinalIgnoreCase) ? 0 : 1;
    }
}
