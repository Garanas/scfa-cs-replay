using FAForever.FileFormats.Blueprints;

namespace FAForever.Vault.Viewer.Services.Players;

/// <summary>A page of a list, with the totals the API reports when asked (<c>page[totals]</c>).</summary>
public sealed record PlayerPage<T>(IReadOnlyList<T> Items, int Page, int? TotalPages, int? TotalRecords)
{
    /// <summary>An empty page.</summary>
    public static PlayerPage<T> Empty { get; } = new([], 1, 0, 0);
}

/// <summary>A name a player had, with the moment it was taken.</summary>
public sealed record PlayerName(string Name, DateTimeOffset? ChangedAt);

/// <summary>The clan a player belongs to.</summary>
public sealed record ClanInfo(int Id, string Name, string Tag);

/// <summary>A player's profile: the current name, the clan and the names the player had before.</summary>
public sealed record PlayerProfile(int Id, string Login, DateTimeOffset? CreatedAt, ClanInfo? Clan, IReadOnlyList<PlayerName> FormerNames)
{
    /// <summary>The avatar's image address, when the player wears one.</summary>
    public string? AvatarUrl { get; init; }

    /// <summary>The avatar's tooltip text.</summary>
    public string? AvatarTooltip { get; init; }
}

/// <summary>A result of the player search: a player whose current or former name matched.</summary>
public sealed record PlayerSearchHit(int Id, string Login, string? ClanTag)
{
    /// <summary>The former name that matched, or null when the current name did.</summary>
    public string? MatchedFormerName { get; init; }
}

/// <summary>A rating list (the global rating, ladder 1v1, a matchmaker queue).</summary>
public sealed record Leaderboard(int Id, string TechnicalName)
{
    /// <summary>The name shown to people (<see cref="Statistics.Leaderboards.DisplayName"/>).</summary>
    public string DisplayName => Statistics.Leaderboards.DisplayName(TechnicalName);
}

/// <summary>A player's rating on one leaderboard.</summary>
public sealed record PlayerRating(Leaderboard Board, double Mean, double Deviation, int TotalGames, int WonGames)
{
    /// <summary>The display rating, mean - 3 x deviation, rounded.</summary>
    public int Rating => FAForever.FileFormats.Replay.ReplayPlayerOptions.DisplayRating(Mean, Deviation) ?? 0;

    /// <summary>The moment of the last rated game on this board.</summary>
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>Wins as a share of the games played (0 to 1), or null before the first game.</summary>
    public double? WinShare => TotalGames > 0 ? (double)WonGames / TotalGames : null;
}

/// <summary>The other side of a game, as shown next to a rating change.</summary>
public sealed record PlayerReference(int Id, string Login);

/// <summary>
/// One rated game of a player on one board: the rating before and after and the game it came from.
/// </summary>
public sealed record RatingJournalEntry(
    int GameId,
    DateTimeOffset? Date,
    double MeanBefore,
    double DeviationBefore,
    double MeanAfter,
    double DeviationAfter)
{
    /// <summary>The game's name.</summary>
    public string? GameName { get; init; }

    /// <summary>The map's display name.</summary>
    public string? MapName { get; init; }

    /// <summary>The map version's folder in the vault.</summary>
    public string? MapFolder { get; init; }

    /// <summary>The players on the other teams (everybody else in a game without teams).</summary>
    public IReadOnlyList<PlayerReference> Opponents { get; init; } = [];

    /// <summary>The display rating before the game, not rounded.</summary>
    public double RatingBefore => MeanBefore - 3 * DeviationBefore;

    /// <summary>The display rating after the game, not rounded.</summary>
    public double RatingAfter => MeanAfter - 3 * DeviationAfter;

    /// <summary>The change of the display rating (positive for a win, negative for a loss).</summary>
    public double Change => RatingAfter - RatingBefore;
}

/// <summary>How a player's game ended.</summary>
public enum MatchResult
{
    /// <summary>No result was reported (or conflicting ones).</summary>
    Unknown,

    /// <summary>A defeat.</summary>
    Defeat,

    /// <summary>A draw or a mutual draw.</summary>
    Draw,

    /// <summary>A victory.</summary>
    Victory,
}

/// <summary>Somebody who played a game, as listed on a match.</summary>
public sealed record MatchParticipant(int PlayerId, string Login, int? Team, Faction? Faction, int? Rating, MatchResult Result)
{
    /// <summary>True for an AI army.</summary>
    public bool IsAi { get; init; }
}

/// <summary>One game of a player: the game, the player's own statistics and the other players.</summary>
public sealed record PlayerMatch(int GameId, string GameName, DateTimeOffset? StartTime, MatchResult Result)
{
    /// <summary>In-game duration (replay ticks are 10 per second).</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>The map's display name.</summary>
    public string? MapName { get; init; }

    /// <summary>The map version's folder in the vault.</summary>
    public string? MapFolder { get; init; }

    /// <summary>The featured mod's display name (FAF, Phantom-X, ...).</summary>
    public string? FeaturedMod { get; init; }

    /// <summary>The faction the player played; null for random or unknown.</summary>
    public Faction? Faction { get; init; }

    /// <summary>The lobby team (1 is "no team").</summary>
    public int? Team { get; init; }

    /// <summary>The player's score in the game.</summary>
    public int? Score { get; init; }

    /// <summary>The display rating before the game; null for an unrated game.</summary>
    public int? RatingBefore { get; init; }

    /// <summary>The display rating after the game; null for an unrated game.</summary>
    public int? RatingAfter { get; init; }

    /// <summary>The technical name of the board that rated the game; null for an unrated game.</summary>
    public string? Board { get; init; }

    /// <summary>The validity the server gave the game (<c>VALID</c> for a rated one).</summary>
    public string? Validity { get; init; }

    /// <summary>The players of the game, the player included; empty for the lighter lists used by charts.</summary>
    public IReadOnlyList<MatchParticipant> Players { get; init; } = [];

    /// <summary>The change of the display rating, when the game was rated.</summary>
    public int? RatingChange => RatingBefore is { } before && RatingAfter is { } after ? after - before : null;
}

/// <summary>The order of a match list.</summary>
public enum MatchSort
{
    /// <summary>Latest game first.</summary>
    Newest,

    /// <summary>Earliest game first.</summary>
    Oldest,

    /// <summary>Longest game first.</summary>
    Longest,

    /// <summary>Shortest game first.</summary>
    Shortest,
}

/// <summary>What the Matches tab asks for. The defaults are no filter, the latest games first.</summary>
public sealed record MatchQuery
{
    /// <summary>Only games that started at or after this moment (<see cref="Statistics.Periods.Since"/>).</summary>
    public DateTimeOffset? Since { get; init; }

    /// <summary>The mode: a board's technical name, <c>custom</c> (games no matchmaker board rated) or <c>all</c>.</summary>
    public string Mode { get; init; } = "all";

    /// <summary>Only games the player played as this faction.</summary>
    public Faction? Faction { get; init; }

    /// <summary>Only games with this result (victory or defeat).</summary>
    public MatchResult? Result { get; init; }

    /// <summary>Part of the map's name.</summary>
    public string? Map { get; init; }

    /// <summary>The exact name of a player who was in the game too.</summary>
    public string? WithPlayer { get; init; }

    /// <summary>The order of the list.</summary>
    public MatchSort Sort { get; init; } = MatchSort.Newest;

    /// <summary>The page, from 1.</summary>
    public int Page { get; init; } = 1;

    /// <summary>Games per page.</summary>
    public int PageSize { get; init; } = 25;
}

/// <summary>A player's lifetime counters, by event id (<see cref="Statistics.PlayerEvents"/>).</summary>
public sealed record PlayerEventCounts(IReadOnlyDictionary<string, long> Counts)
{
    /// <summary>The counter of an event; 0 when the player never triggered it.</summary>
    public long Get(string eventId) => Counts.TryGetValue(eventId.ToLowerInvariant(), out long count) ? count : 0;

    /// <summary>Whether the player has a row for the event (the game server no longer records some of them).</summary>
    public bool Has(string eventId) => Counts.ContainsKey(eventId.ToLowerInvariant());

    /// <summary>The counter of an event; null when the player has no row for it.</summary>
    public long? TryGet(string eventId) => Counts.TryGetValue(eventId.ToLowerInvariant(), out long count) ? count : null;
}

/// <summary>The kind of an achievement.</summary>
public enum AchievementKind
{
    /// <summary>Unlocked in one go.</summary>
    Standard,

    /// <summary>Unlocked after a number of steps.</summary>
    Incremental,
}

/// <summary>An achievement's state for a player.</summary>
public enum AchievementState
{
    /// <summary>Not shown yet.</summary>
    Hidden,

    /// <summary>Shown, not unlocked.</summary>
    Revealed,

    /// <summary>Unlocked.</summary>
    Unlocked,
}

/// <summary>An achievement of the game; the name and description come localised from the API.</summary>
public sealed record AchievementDefinition(string Id, string Name, string Description, AchievementKind Kind, int TotalSteps, int Order)
{
    /// <summary>The icon of a locked achievement.</summary>
    public string? RevealedIconUrl { get; init; }

    /// <summary>The icon of an unlocked achievement.</summary>
    public string? UnlockedIconUrl { get; init; }

    /// <summary>The experience points an unlock gives.</summary>
    public int ExperiencePoints { get; init; }

    /// <summary>How many players unlocked it.</summary>
    public int UnlockersCount { get; init; }

    /// <summary>The share of players that unlocked it, in percent (0 to 100).</summary>
    public double UnlockersPercent { get; init; }
}

/// <summary>A player's progress on one achievement.</summary>
public sealed record PlayerAchievement(string AchievementId, AchievementState State, int CurrentSteps, DateTimeOffset? UpdatedAt);

/// <summary>A row of a leaderboard.</summary>
public sealed record RankingRow(int Rank, int PlayerId, string Login, double Mean, double Deviation, int TotalGames, int WonGames)
{
    /// <summary>The player's clan tag.</summary>
    public string? ClanTag { get; init; }

    /// <summary>The moment of the last rated game.</summary>
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>The display rating, mean - 3 x deviation, rounded.</summary>
    public int Rating => FAForever.FileFormats.Replay.ReplayPlayerOptions.DisplayRating(Mean, Deviation) ?? 0;

    /// <summary>Wins as a share of the games (0 to 1), or null before the first game.</summary>
    public double? WinShare => TotalGames > 0 ? (double)WonGames / TotalGames : null;
}

/// <summary>Which ratings a ranking lists.</summary>
public sealed record RankingQuery
{
    /// <summary>The board's technical name.</summary>
    public string Board { get; init; } = "ladder_1v1";

    /// <summary>Only ratings whose deviation is below 100 (settled).</summary>
    public bool SettledOnly { get; init; } = true;

    /// <summary>Only players whose rating changed at or after this moment.</summary>
    public DateTimeOffset? ActiveSince { get; init; }

    /// <summary>Part of the player's name.</summary>
    public string? Name { get; init; }

    /// <summary>The page, from 1.</summary>
    public int Page { get; init; } = 1;

    /// <summary>Rows per page.</summary>
    public int PageSize { get; init; } = 50;
}

/// <summary>The number of ratings in a range of display ratings, <c>From</c> inclusive and <c>To</c> exclusive.</summary>
public sealed record RatingBin(int From, int To, int Count);

/// <summary>A league (the ladder 1v1 league).</summary>
public sealed record League(int Id, string TechnicalName, string NameKey)
{
    /// <summary>The league's image.</summary>
    public string? ImageUrl { get; init; }

    /// <summary>The league's small image.</summary>
    public string? SmallImageUrl { get; init; }
}

/// <summary>A season of a league.</summary>
public sealed record LeagueSeason(int Id, int LeagueId, string LeagueTechnicalName, int SeasonNumber, string NameKey, DateTimeOffset? StartDate, DateTimeOffset? EndDate)
{
    /// <summary>The technical name of the board whose games count.</summary>
    public string? BoardTechnicalName { get; init; }

    /// <summary>Placement games of a new player.</summary>
    public int PlacementGames { get; init; }

    /// <summary>Placement games of a player who played in an earlier season.</summary>
    public int PlacementGamesReturningPlayer { get; init; }

    /// <summary>True when the season started and has not ended at <paramref name="now"/>.</summary>
    public bool IsActive(DateTimeOffset now) => (StartDate is null || StartDate <= now) && (EndDate is null || EndDate >= now);
}

/// <summary>A division of a season (Bronze, Silver, ...); a higher index is a better division.</summary>
public sealed record LeagueDivision(int Id, int Index, string Name);

/// <summary>A subdivision of a division (Bronze I, ...), the group whose standings are listed.</summary>
public sealed record LeagueSubdivision(int Id, int Index, string Name, LeagueDivision Division)
{
    /// <summary>The lowest rating that starts in this subdivision.</summary>
    public double? MinRating { get; init; }

    /// <summary>The highest rating that starts in this subdivision.</summary>
    public double? MaxRating { get; init; }

    /// <summary>The highest score reached in the subdivision.</summary>
    public int? HighestScore { get; init; }

    /// <summary>The subdivision's image.</summary>
    public string? ImageUrl { get; init; }

    /// <summary>The subdivision's small image.</summary>
    public string? SmallImageUrl { get; init; }

    /// <summary>The division and the subdivision, as in "Silver II".</summary>
    public string FullName => $"{Division.Name} {Name}";
}

/// <summary>A player's place in a season. <c>Subdivision</c> is null while the player is still in placement.</summary>
public sealed record LeagueEntry(int PlayerId, int SeasonId, int Score, int GameCount, bool ReturningPlayer, LeagueSubdivision? Subdivision)
{
    /// <summary>
    /// True while the player is in placement: the API's score is null and there is no subdivision
    /// yet (<see cref="Score"/> is then 0, <see cref="GameCount"/> counts the placement games played).
    /// </summary>
    public bool InPlacement { get; init; }
}

/// <summary>A row of a subdivision's standings.</summary>
public sealed record LeagueStanding(int Rank, int PlayerId, string Login, int Score, int GameCount);

/// <summary>A change of a player's league score after one game.</summary>
public sealed record LeagueJournalEntry(int GameId, int ScoreBefore, int ScoreAfter, int GameCount, LeagueSubdivision? Before, LeagueSubdivision? After);

/// <summary>A game two players played against or with each other.</summary>
public sealed record HeadToHeadGame(int GameId, string GameName, DateTimeOffset? StartTime, MatchParticipant A, MatchParticipant B)
{
    /// <summary>In-game duration.</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>The map's display name.</summary>
    public string? MapName { get; init; }

    /// <summary>The map version's folder in the vault.</summary>
    public string? MapFolder { get; init; }

    /// <summary>The featured mod's display name.</summary>
    public string? FeaturedMod { get; init; }

    /// <summary>True when both were on the same team (not an opposing game).</summary>
    public bool SameTeam => A.Team is > 1 && A.Team == B.Team;
}

/// <summary>The bins of the rating distribution: 24 bins of 100 display rating points from 0 to 2,400.</summary>
public static class RatingBinning
{
    /// <summary>The width of a bin.</summary>
    public const int Width = 100;

    /// <summary>The number of bins.</summary>
    public const int Count = 24;
}
