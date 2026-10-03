namespace FAForever.Replay.Viewer.Services.Api;

/// <summary>
/// Search parameters for the vault. Empty fields are not filtered on.
/// </summary>
public sealed record GameSearchQuery
{
    public string PlayerName { get; init; } = string.Empty;

    public string MapName { get; init; } = string.Empty;

    /// <summary>Technical name of the featured mod (e.g. "faf"), or empty for any.</summary>
    public string FeaturedMod { get; init; } = string.Empty;

    /// <summary>Only games that actually finished (have an end time).</summary>
    public bool FinishedOnly { get; init; } = true;

    /// <summary>Centre of the period to search in, or null for any date.</summary>
    public DateOnly? PlayedAround { get; init; }

    /// <summary>How far before and after <see cref="PlayedAround"/> to search.</summary>
    public SearchPeriod Period { get; init; } = SearchPeriod.Year;

    /// <summary>The start dates to search in: [From, To] inclusive, or null for any date.</summary>
    public (DateOnly From, DateOnly To)? PlayedWindow => PlayedAround is { } around
        ? Period switch
        {
            SearchPeriod.Week => (around.AddDays(-7), around.AddDays(7)),
            SearchPeriod.Month => (around.AddMonths(-1), around.AddMonths(1)),
            SearchPeriod.Quarter => (around.AddMonths(-3), around.AddMonths(3)),
            _ => (around.AddYears(-1), around.AddYears(1)),
        }
        : null;

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 12;
}

/// <summary>
/// The half-width of the "played around" window; a year at most, to keep the
/// date filter meaningful.
/// </summary>
public enum SearchPeriod
{
    Week,
    Month,
    Quarter,
    Year,
}

public sealed record GameSearchResult(IReadOnlyList<GameSummary> Games, int Page, int? TotalPages, int? TotalRecords);

public sealed record GameSummary(
    int Id,
    string Title,
    DateTimeOffset? StartTime,
    DateTimeOffset? EndTime,
    string? MapName,
    string? MapPreviewUrl,
    string? FeaturedMod,
    IReadOnlyList<GamePlayer> Players)
{
    /// <summary>Simulation ticks in the replay (10 per second), as recorded by the server.</summary>
    public int? ReplayTicks { get; init; }

    /// <summary>"VALID" for a rated game, otherwise the reason it was not rated (e.g. "TOO_SHORT").</summary>
    public string? Validity { get; init; }

    /// <summary>"DEMORALIZATION" (assassination), "DOMINATION", "ERADICATION" or "SANDBOX".</summary>
    public string? VictoryCondition { get; init; }

    /// <summary>False when the vault holds no replay file for this game.</summary>
    public bool? ReplayAvailable { get; init; }

    /// <summary>The in-game duration when the tick count is known, otherwise the wall-clock duration.</summary>
    public TimeSpan? Duration => ReplayTicks is > 0 and { } ticks
        ? TimeSpan.FromSeconds(ticks / 10.0)
        : StartTime is { } start && EndTime is { } end && end > start ? end - start : null;

    public bool IsRated => Validity == "VALID";

    /// <summary>
    /// The players grouped into the sides of the match. Lobby team 1 means "no team", so in
    /// free-for-all games every such player forms a side of their own.
    /// </summary>
    public IReadOnlyList<GameSide> Sides
    {
        get
        {
            List<GameSide> sides = [];
            foreach (IGrouping<int?, GamePlayer> team in Players.Where(player => player.Team is > 1).GroupBy(player => player.Team).OrderBy(team => team.Key))
            {
                sides.Add(new GameSide(team.Key, [.. team.OrderByDescending(player => player.Rating ?? int.MinValue)]));
            }
            foreach (GamePlayer player in Players.Where(player => player.Team is not > 1).OrderByDescending(player => player.Rating ?? int.MinValue))
            {
                sides.Add(new GameSide(null, [player]));
            }
            return sides;
        }
    }
}

/// <summary>
/// One side of a match: a lobby team, or a single player without a team.
/// </summary>
public sealed record GameSide(int? Team, IReadOnlyList<GamePlayer> Players)
{
    public GameOutcome Outcome => Players.Select(player => player.Outcome).DefaultIfEmpty(GameOutcome.Unknown).Max();

    /// <summary>Average display rating of the rated players on this side.</summary>
    public int? AverageRating
    {
        get
        {
            int[] ratings = [.. Players.Select(player => player.Rating).OfType<int>()];
            return ratings.Length > 0 ? (int)Math.Round(ratings.Average()) : null;
        }
    }
}

public sealed record GamePlayer(string Login, int? Team, FAForever.Replay.Faction? Faction, int? Rating, string? Result)
{
    /// <summary>Display rating after the game; null when the game was not rated.</summary>
    public int? RatingAfter { get; init; }

    /// <summary>CSS colour of the in-game army colour, when known.</summary>
    public string? Color { get; init; }

    public bool IsAi { get; init; }

    public int? RatingChange => Rating is { } before && RatingAfter is { } after ? after - before : null;

    public GameOutcome Outcome => Result switch
    {
        "VICTORY" => GameOutcome.Victory,
        "DRAW" or "MUTUAL_DRAW" => GameOutcome.Draw,
        "DEFEAT" => GameOutcome.Defeat,
        _ => GameOutcome.Unknown,
    };
}

/// <summary>
/// The result of a player or side as reported by the FAF API ("result" on gamePlayerStats).
/// Ordered so that the best outcome of a side is its maximum.
/// </summary>
public enum GameOutcome
{
    Unknown,
    Defeat,
    Draw,
    Victory,
}
