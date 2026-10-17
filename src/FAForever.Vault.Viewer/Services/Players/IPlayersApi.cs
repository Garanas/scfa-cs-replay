namespace FAForever.Vault.Viewer.Services.Players;

/// <summary>
/// What the Players pages ask of the FAForever API. <see cref="FafPlayersApi"/> calls the API,
/// <see cref="SamplePlayersApi"/> answers from fixtures. Every method throws
/// <see cref="Api.NotAuthenticatedException"/> without a session. Lists of a whole period or season
/// are fetched in pages of 100 (the API's most) and report the number of rows read through <c>progress</c>.
/// </summary>
public interface IPlayersApi
{
    /// <summary>Players whose current or former name contains <paramref name="text"/> (at most 10 of each kind).</summary>
    Task<IReadOnlyList<PlayerSearchHit>> FindPlayersAsync(string text, CancellationToken cancellationToken);

    /// <summary>The player with exactly this current name, or null.</summary>
    Task<PlayerSearchHit?> FindPlayerByNameAsync(string login, CancellationToken cancellationToken);

    /// <summary>A player's profile with the clan and former names; null for an unknown id.</summary>
    Task<PlayerProfile?> GetProfileAsync(int playerId, CancellationToken cancellationToken);

    /// <summary>Every leaderboard; the same list for the whole session.</summary>
    Task<IReadOnlyList<Leaderboard>> GetLeaderboardsAsync(CancellationToken cancellationToken);

    /// <summary>The player's rating on every board they have played.</summary>
    Task<IReadOnlyList<PlayerRating>> GetRatingsAsync(int playerId, CancellationToken cancellationToken);

    /// <summary>
    /// The player's rated games on a board since a moment (null for all), oldest first. Loops over
    /// pages of 100 (the API's most).
    /// </summary>
    Task<IReadOnlyList<RatingJournalEntry>> GetRatingHistoryAsync(int playerId, string boardTechnicalName, DateTimeOffset? since, IProgress<int>? progress, CancellationToken cancellationToken);

    /// <summary>One page of the player's games, with the other players of each game.</summary>
    Task<PlayerPage<PlayerMatch>> GetMatchesAsync(int playerId, MatchQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// Every game of the player that fits the query's filters (page, page size and sort are ignored),
    /// newest first, without the other players. Loops over pages of 100 (the API's most).
    /// </summary>
    Task<IReadOnlyList<PlayerMatch>> GetAllMatchesAsync(int playerId, MatchQuery query, IProgress<int>? progress, CancellationToken cancellationToken);

    /// <summary>The player's lifetime counters.</summary>
    Task<PlayerEventCounts> GetEventCountsAsync(int playerId, CancellationToken cancellationToken);

    /// <summary>Every achievement, in the game's order; the same list for the whole session.</summary>
    Task<IReadOnlyList<AchievementDefinition>> GetAchievementsAsync(CancellationToken cancellationToken);

    /// <summary>The player's progress on the achievements they have touched.</summary>
    Task<IReadOnlyList<PlayerAchievement>> GetPlayerAchievementsAsync(int playerId, CancellationToken cancellationToken);

    /// <summary>A page of a ranking, best rating first; <c>Rank</c> counts from the first row of the first page.</summary>
    Task<PlayerPage<RankingRow>> GetRankingAsync(RankingQuery query, CancellationToken cancellationToken);

    /// <summary>The rank a display rating would have in a ranking (the number of better ratings plus one).</summary>
    Task<int> GetRankOfRatingAsync(RankingQuery query, int rating, CancellationToken cancellationToken);

    /// <summary>
    /// The ratings of a ranking in bins of 100 display rating points from 0 to 2,400; cached per
    /// board and filter for the session.
    /// </summary>
    Task<IReadOnlyList<RatingBin>> GetRatingDistributionAsync(RankingQuery query, CancellationToken cancellationToken);

    /// <summary>The enabled leagues.</summary>
    Task<IReadOnlyList<League>> GetLeaguesAsync(CancellationToken cancellationToken);

    /// <summary>Every season of every league, the latest first.</summary>
    Task<IReadOnlyList<LeagueSeason>> GetSeasonsAsync(CancellationToken cancellationToken);

    /// <summary>The subdivisions of a season with their divisions, the lowest division and subdivision first.</summary>
    Task<IReadOnlyList<LeagueSubdivision>> GetSubdivisionsAsync(int seasonId, CancellationToken cancellationToken);

    /// <summary>The player's place in one season; null when they have none.</summary>
    Task<LeagueEntry?> GetLeagueEntryAsync(int playerId, int seasonId, CancellationToken cancellationToken);

    /// <summary>The player's places in every season that is running now.</summary>
    Task<IReadOnlyList<LeagueEntry>> GetActiveLeagueEntriesAsync(int playerId, CancellationToken cancellationToken);

    /// <summary>The number of players in each subdivision, by subdivision id.</summary>
    Task<IReadOnlyDictionary<int, int>> GetPlayersPerSubdivisionAsync(int seasonId, IReadOnlyList<LeagueSubdivision> subdivisions, CancellationToken cancellationToken);

    /// <summary>A page of 50 rows of a subdivision's standings, with names; best score first.</summary>
    Task<PlayerPage<LeagueStanding>> GetStandingsAsync(int seasonId, int subdivisionId, int page, CancellationToken cancellationToken);

    /// <summary>The changes of a player's league score through a season, in game order.</summary>
    Task<IReadOnlyList<LeagueJournalEntry>> GetLeagueJourneyAsync(int playerId, int seasonId, CancellationToken cancellationToken);

    /// <summary>
    /// A page of 20 of the latest rated games of some players on a board since a moment (a league's
    /// season start), newest first. A game that several of them were in is listed once, with the
    /// statistics of the first of them (<see cref="PlayerMatch.ListedPlayerId"/>), so a page can hold
    /// fewer than 20 rows, and a game whose two rows fall on two pages shows on both. The ids go out in requests of at most 50.
    /// </summary>
    Task<PlayerPage<PlayerMatch>> GetGamesOfPlayersAsync(IReadOnlyList<int> playerIds, string boardTechnicalName, DateTimeOffset since, int page, CancellationToken cancellationToken);

    /// <summary>The games two players were in together since a moment (null for all), newest first.</summary>
    Task<IReadOnlyList<HeadToHeadGame>> GetHeadToHeadAsync(int playerA, int playerB, DateTimeOffset? since, CancellationToken cancellationToken);
}
