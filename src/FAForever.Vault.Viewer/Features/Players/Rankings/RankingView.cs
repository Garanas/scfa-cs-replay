using System.Globalization;
using FAForever.Vault.Viewer.Features.Players.Charts;
using FAForever.Vault.Viewer.Services.Players;

namespace FAForever.Vault.Viewer.Features.Players.Rankings;

/// <summary>
/// The pure parts of the Rankings page: the query from the address, the page that holds a rank and
/// the numbers drawn from the rating distribution.
/// </summary>
public static class RankingView
{
    /// <summary>Rows per page of the table.</summary>
    public const int PageSize = 50;

    /// <summary>The "played in" choice while the address has none.</summary>
    public const string DefaultActivity = "90d";

    /// <summary>The board of the address; anything that is not a board's technical name opens the ladder.</summary>
    public static string Board(string? token)
        => !string.IsNullOrWhiteSpace(token) && token.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') ? token : PlayerFilters.DefaultBoard;

    /// <summary>The "played in" token of the address (30d, 90d, 1y, all); anything else is the default.</summary>
    public static string Activity(string? token)
        => PlayerFilters.Activity.Any(option => option.Token == token) ? token! : DefaultActivity;

    /// <summary>
    /// The start of "played in": a whole day (UTC), so the same choice gives the same query all day
    /// and the distribution requests hit the cache. Null for any time.
    /// </summary>
    public static DateTimeOffset? ActiveSince(string activity, DateTimeOffset now)
    {
        DateTimeOffset today = new(now.UtcDateTime.Date, TimeSpan.Zero);
        return activity switch
        {
            "30d" => today.AddDays(-30),
            "90d" => today.AddDays(-90),
            "1y" => today.AddYears(-1),
            _ => null,
        };
    }

    /// <summary>The page of the address, from 1.</summary>
    public static int PageNumber(string? token)
        => int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out int page) && page > 1 ? page : 1;

    /// <summary>The query of the address parameters: <c>board</c>, <c>settled</c> (0 for every deviation), <c>active</c>, <c>page</c>.</summary>
    public static RankingQuery ToQuery(string? board, string? settled, string? active, string? page, DateTimeOffset now) => new()
    {
        Board = Board(board),
        SettledOnly = settled != "0",
        ActiveSince = ActiveSince(Activity(active), now),
        Page = PageNumber(page),
        PageSize = PageSize,
    };

    /// <summary>The player of the address (<c>player</c>), a positive id; 0 for anything else.</summary>
    public static int PlayerId(string? token)
        => int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out int id) && id > 0 ? id : 0;

    /// <summary>Where a player stands relative to a ranking.</summary>
    public enum PlayerPlace
    {
        /// <summary>The player has a rating on the board and the filters let it through; the rank can be asked.</summary>
        Listed,

        /// <summary>The player has no rating on the board.</summary>
        NoRating,

        /// <summary>The player has a rating the filters leave out (an unsettled one, or one not played in the chosen period).</summary>
        FilteredOut,
    }

    /// <summary>
    /// Whether a player's rating on the ranking's board is in the ranking with the query's filters
    /// (the same conditions the ranking request applies: a deviation below 100 when settled only, a
    /// rating changed since the "played in" start).
    /// </summary>
    public static PlayerPlace Place(PlayerRating? rating, RankingQuery query)
    {
        if (rating is null || rating.TotalGames <= 0)
        {
            return PlayerPlace.NoRating;
        }

        if (query.SettledOnly && rating.Deviation >= 100)
        {
            return PlayerPlace.FilteredOut;
        }

        if (query.ActiveSince is { } since && (rating.UpdatedAt is null || rating.UpdatedAt < since))
        {
            return PlayerPlace.FilteredOut;
        }

        return PlayerPlace.Listed;
    }

    /// <summary>The sentence under the field for a player that is not in the ranking; null for a listed one.</summary>
    public static string? PlaceMessage(PlayerPlace place, string login, RankingQuery query, string boardName) => place switch
    {
        PlayerPlace.NoRating => query.SettledOnly ? $"{login} has no settled rating on {boardName}" : $"{login} has no rating on {boardName}",
        PlayerPlace.FilteredOut => $"{login} is not in this ranking with these filters",
        _ => null,
    };

    /// <summary>
    /// The address parameters that hold a value the page does not read as given: an unknown board
    /// (not in <paramref name="knownBoards"/>), a "settled" other than 0, an unknown "played in", a
    /// page that is not a number above 1, a <c>player</c> that is not an id, and the retired name
    /// filter <c>q</c> whatever it holds. The page drops them from the address.
    /// </summary>
    public static IReadOnlyList<string> StaleParameters(string? board, string? settled, string? active, string? page, string? player, string? retiredName, IEnumerable<string> knownBoards)
    {
        List<string> stale = [];
        if (retiredName is not null)
        {
            stale.Add("q");
        }

        if (player is not null && PlayerId(player) == 0)
        {
            stale.Add("player");
        }

        if (board is not null && !knownBoards.Contains(board))
        {
            stale.Add("board");
        }

        if (settled is not null && settled != "0")
        {
            stale.Add("settled");
        }

        if (active is not null && (!PlayerFilters.Activity.Any(option => option.Token == active) || active == DefaultActivity))
        {
            stale.Add("active");
        }

        if (page is not null && PageNumber(page) <= 1)
        {
            stale.Add("page");
        }

        return stale;
    }

    /// <summary>The last page of a result: from the page count when the API gave one, else from the total.</summary>
    public static int LastPage(int? totalPages, int? totalRecords, int pageSize)
        => Math.Max(1, totalPages ?? (totalRecords is { } total ? (total + Math.Max(pageSize, 1) - 1) / Math.Max(pageSize, 1) : 1));

    /// <summary>The line under the table: the ranks shown out of the board's players.</summary>
    public static string RangeText(int itemCount, int firstRank, int lastRank, int? total)
    {
        string Count(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
        if (itemCount == 0)
        {
            return "No ranks to show";
        }

        string range = $"Ranks {Count(firstRank)} to {Count(lastRank)}";
        return total is { } all ? $"{range} of {Count(all)}" : range;
    }

    /// <summary>The page of the table that holds a rank (rank 1 to 50 is page 1).</summary>
    public static int PageOfRank(int rank, int pageSize = PageSize) => Math.Max(rank - 1, 0) / Math.Max(pageSize, 1) + 1;

    /// <summary>The index of the bin a display rating falls in; below the first bin is the first, above the last the last.</summary>
    public static int BinIndex(IReadOnlyList<RatingBin> bins, int rating)
    {
        for (int i = 0; i < bins.Count; i++)
        {
            if (rating < bins[i].To)
            {
                return i;
            }
        }

        return Math.Max(bins.Count - 1, 0);
    }

    /// <summary>The median display rating, interpolated inside its bin; null without ratings.</summary>
    public static int? Median(IReadOnlyList<RatingBin> bins)
    {
        long total = bins.Sum(bin => (long)bin.Count);
        if (total == 0)
        {
            return null;
        }

        double half = total / 2.0;
        long below = 0;
        foreach (RatingBin bin in bins)
        {
            if (bin.Count > 0 && below + bin.Count >= half)
            {
                return (int)Math.Round(bin.From + (half - below) / bin.Count * (bin.To - bin.From));
            }

            below += bin.Count;
        }

        return null;
    }

    /// <summary>The ratings at or above a display rating; the bin that holds it counts in proportion.</summary>
    public static int CountAtLeast(IReadOnlyList<RatingBin> bins, int rating)
    {
        double count = 0;
        foreach (RatingBin bin in bins)
        {
            if (bin.From >= rating)
            {
                count += bin.Count;
            }
            else if (bin.To > rating && bin.To > bin.From)
            {
                count += bin.Count * (double)(bin.To - rating) / (bin.To - bin.From);
            }
        }

        return (int)Math.Round(count);
    }

    /// <summary>The columns of the distribution: the player's bin in the accent colour, the others faint.</summary>
    public static IReadOnlyList<ColumnItem> Columns(IReadOnlyList<RatingBin> bins, int? playerRating)
    {
        int mine = playerRating is { } rating ? BinIndex(bins, rating) : -1;
        return [.. bins.Select((bin, index) => new ColumnItem(
            "",
            bin.Count,
            Color: index == mine ? ChartColor.Primary : ChartColor.Faint,
            Title: $"{bin.Count.ToString("N0", CultureInfo.InvariantCulture)} players from {bin.From} to {bin.To - 1}{(index == mine ? ", your bin" : "")}"))];
    }

    /// <summary>How far a rating is below the rating that opens the top: null when it is there already.</summary>
    public static int? PointsToReach(int? ratingNeeded, int playerRating)
        => ratingNeeded is { } needed && needed > playerRating ? needed - playerRating : null;
}
