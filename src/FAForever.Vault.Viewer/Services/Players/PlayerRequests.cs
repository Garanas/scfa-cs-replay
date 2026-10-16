using FAForever.FileFormats.Blueprints;
using System.Globalization;
using FAForever.Vault.Viewer.Services.Api;

namespace FAForever.Vault.Viewer.Services.Players;

/// <summary>
/// The requests of <see cref="FafPlayersApi"/>, as addresses relative to the API's base address
/// (<c>/data/...</c>). Filters are RSQL joined with <c>;</c> and percent-encoded as a whole. Every
/// request here was checked against the live API on 2026-10-16 (the list is in
/// <c>docs/vault/players-plan.md</c>).
/// </summary>
public static class PlayerRequests
{
    /// <summary>
    /// The size of the pages of the loops over a period or a season. The API caps every page at 100
    /// rows (a larger <c>page[size]</c> still answers 100), so the loops ask for 100 and stop by the
    /// page totals.
    /// </summary>
    public const int LoopPageSize = 100;

    /// <summary>
    /// The most pages a loop reads (a safety stop): 30,000 rows. A loop that reaches it returns what
    /// it has read, which only a player with more games than that sees.
    /// </summary>
    public const int MaxLoopPages = 300;

    /// <summary>The most rows the API answers in a page.</summary>
    public const int MaxPageSize = 100;

    /// <summary>Players whose login contains the text.</summary>
    public static string FindPlayers(string text)
        => $"/data/player?include=clanMembership.clan&page[size]=10&filter={Filter($"login=={FafApiClient.Quote("*" + text.Trim() + "*")}")}";

    /// <summary>Former names that contain the text, with their players.</summary>
    public static string FindNames(string text)
        => $"/data/nameRecord?include=player,player.clanMembership.clan&page[size]=10&filter={Filter($"name=={FafApiClient.Quote("*" + text.Trim() + "*")}")}";

    /// <summary>The player with exactly this login.</summary>
    public static string PlayerByLogin(string login)
        => $"/data/player?include=clanMembership.clan&page[size]=1&filter={Filter($"login=={FafApiClient.Quote(login.Trim())}")}";

    /// <summary>A player with names, clan and avatar.</summary>
    public static string Profile(int playerId)
        => $"/data/player/{Number(playerId)}?include=names,clanMembership.clan,avatarAssignments.avatar"; // the selected assignment is the current avatar

    /// <summary>Every leaderboard.</summary>
    public static string Leaderboards() => "/data/leaderboard?page[size]=100";

    /// <summary>The ratings of a player.</summary>
    public static string Ratings(int playerId)
        => $"/data/leaderboardRating?include=leaderboard&page[size]=100&filter={Filter($"player.id=={Number(playerId)}")}";

    /// <summary>One page of a player's rating journal on a board, newest first.</summary>
    public static string RatingJournal(int playerId, string board, DateTimeOffset? since, int page)
    {
        List<string> filters =
        [
            $"gamePlayerStats.player.id=={Number(playerId)}",
            $"leaderboard.technicalName=={FafApiClient.Quote(board)}",
        ];
        if (since is { } start)
        {
            filters.Add($"gamePlayerStats.scoreTime=ge={Time(start)}");
        }

        return "/data/leaderboardRatingJournal"
            + "?include=gamePlayerStats,gamePlayerStats.game,gamePlayerStats.game.mapVersion.map,gamePlayerStats.game.playerStats,gamePlayerStats.game.playerStats.player"
            + "&sort=-gamePlayerStats.scoreTime"
            + $"&page[size]={LoopPageSize}&page[number]={page}&page[totals]"
            + $"&filter={Filter(string.Join(";", filters))}";
    }

    /// <summary>One page of a player's games with the other players (<paramref name="page"/> and the size come from the query).</summary>
    public static string Matches(int playerId, MatchQuery query)
        => MatchesRequest(playerId, query, query.Page, query.PageSize, withPlayers: true);

    /// <summary>One page of a player's games for the charts: no other players, pages of 100 (<see cref="LoopPageSize"/>).</summary>
    public static string MatchesForCharts(int playerId, MatchQuery query, int page)
        => MatchesRequest(playerId, query, page, LoopPageSize, withPlayers: false);

    /// <summary>The lifetime counters of a player.</summary>
    public static string EventCounts(int playerId, int page)
        => $"/data/playerEvent?include=event&page[size]={LoopPageSize}&page[number]={page}&page[totals]&filter={Filter($"player.id=={Number(playerId)}")}";

    /// <summary>Every achievement.</summary>
    public static string Achievements() => "/data/achievement?sort=order&page[size]=100";

    /// <summary>A player's achievements.</summary>
    public static string PlayerAchievements(int playerId)
        => $"/data/playerAchievement?include=achievement&page[size]=100&filter={Filter($"player.id=={Number(playerId)}")}";

    /// <summary>One page of a ranking.</summary>
    public static string Ranking(RankingQuery query)
        => "/data/leaderboardRating?include=player,player.clanMembership.clan&sort=-rating"
            + $"&page[size]={Math.Clamp(query.PageSize, 1, MaxPageSize)}&page[number]={Math.Max(query.Page, 1)}&page[totals]"
            + $"&filter={Filter(RankingFilter(query))}";

    /// <summary>The number of ratings better than <paramref name="rating"/> in a ranking.</summary>
    public static string BetterRatings(RankingQuery query, int rating)
        => $"/data/leaderboardRating?page[size]=1&page[totals]&filter={Filter($"{RankingFilter(query with { Name = null })};rating=gt={Number(rating)}")}";

    /// <summary>The number of ratings from <paramref name="from"/> (inclusive) to <paramref name="to"/> (exclusive).</summary>
    public static string RatingsBetween(RankingQuery query, int from, int to)
        => $"/data/leaderboardRating?page[size]=1&page[totals]&filter={Filter($"{RankingFilter(query with { Name = null })};rating=ge={Number(from)};rating=lt={Number(to)}")}";

    /// <summary>The enabled leagues.</summary>
    public static string Leagues() => $"/data/league?page[size]=100&filter={Filter("enabled==true")}";

    /// <summary>Every season, the latest first.</summary>
    public static string Seasons() => "/data/leagueSeason?include=league,leaderboard&sort=-startDate&page[size]=100";

    /// <summary>The subdivisions of a season with their divisions.</summary>
    public static string Subdivisions(int seasonId)
        => "/data/leagueSeasonDivisionSubdivision?include=leagueSeasonDivision&sort=leagueSeasonDivision.divisionIndex,subdivisionIndex&page[size]=100"
            + $"&filter={Filter($"leagueSeasonDivision.leagueSeason.id=={Number(seasonId)}")}";

    /// <summary>The place of a player in one season.</summary>
    public static string LeagueEntry(int playerId, int seasonId)
        => "/data/leagueSeasonScore?include=leagueSeason,leagueSeasonDivisionSubdivision.leagueSeasonDivision&page[size]=10"
            + $"&filter={Filter($"loginId=={Number(playerId)};leagueSeason.id=={Number(seasonId)}")}";

    /// <summary>The places of a player in the seasons that run at <paramref name="now"/>.</summary>
    public static string ActiveLeagueEntries(int playerId, DateTimeOffset now)
        => "/data/leagueSeasonScore?include=leagueSeason,leagueSeasonDivisionSubdivision.leagueSeasonDivision&page[size]=20"
            + $"&filter={Filter($"loginId=={Number(playerId)};leagueSeason.endDate=ge={Time(now)}")}";

    /// <summary>The number of players in a subdivision.</summary>
    public static string PlayersInSubdivision(int seasonId, int subdivisionId)
        => "/data/leagueSeasonScore?page[size]=1&page[totals]"
            + $"&filter={Filter($"leagueSeason.id=={Number(seasonId)};leagueSeasonDivisionSubdivision.id=={Number(subdivisionId)}")}";

    /// <summary>A page of the standings of a subdivision.</summary>
    public static string Standings(int seasonId, int subdivisionId, int page, int pageSize)
        => $"/data/leagueSeasonScore?sort=-score,-gameCount&page[size]={pageSize}&page[number]={Math.Max(page, 1)}&page[totals]"
            + $"&filter={Filter($"leagueSeason.id=={Number(seasonId)};leagueSeasonDivisionSubdivision.id=={Number(subdivisionId)}")}";

    /// <summary>The players with these ids.</summary>
    public static string PlayersById(IEnumerable<int> ids)
        => $"/data/player?page[size]=100&filter={Filter($"id=in=({string.Join(",", ids.Select(Number))})")}";

    /// <summary>The league journal of a player in a season.</summary>
    public static string LeagueJourney(int playerId, int seasonId, int page)
        => "/data/leagueScoreJournal?include=leagueSeasonDivisionSubdivisionBefore.leagueSeasonDivision,leagueSeasonDivisionSubdivisionAfter.leagueSeasonDivision&sort=gameId"
            + $"&page[size]={LoopPageSize}&page[number]={page}&page[totals]"
            + $"&filter={Filter($"loginId=={Number(playerId)};leagueSeason.id=={Number(seasonId)}")}";

    /// <summary>
    /// One page of the games two players were in together, newest first: the rows of
    /// <paramref name="playerA"/> with the game and everybody in it (player B is in
    /// <c>game.playerStats</c>). Two <c>playerStats.player.id</c> predicates on <c>/data/game</c> are
    /// applied to one row and match nothing, so the request goes through the statistics of A.
    /// </summary>
    public static string HeadToHead(int playerA, int playerB, DateTimeOffset? since, int page)
    {
        List<string> filters = [$"player.id=={Number(playerA)}", $"game.playerStats.player.id=={Number(playerB)}"];
        if (since is { } start)
        {
            filters.Add($"scoreTime=ge={Time(start)}");
        }

        return "/data/gamePlayerStats?include=game,game.playerStats.player,game.mapVersion.map,game.featuredMod&sort=-scoreTime"
            + $"&page[size]={LoopPageSize}&page[number]={Math.Max(page, 1)}&page[totals]"
            + $"&filter={Filter(string.Join(";", filters))}";
    }

    private static string MatchesRequest(int playerId, MatchQuery query, int page, int pageSize, bool withPlayers)
    {
        List<string> filters = [$"player.id=={Number(playerId)}"];
        if (query.Since is { } since)
        {
            filters.Add($"scoreTime=ge={Time(since)}");
        }
        if (BoardOfMode(query.Mode) is { } board)
        {
            filters.Add($"ratingChanges.leaderboard.technicalName=={FafApiClient.Quote(board)}");
        }
        if (query.Faction is { } faction)
        {
            filters.Add($"faction=={FactionName(faction)}"); // the filter takes the enum's name; the attribute comes back as a number
        }
        if (query.Result is MatchResult.Victory)
        {
            filters.Add("result==VICTORY");
        }
        else if (query.Result is MatchResult.Defeat)
        {
            filters.Add("result==DEFEAT");
        }
        if (!string.IsNullOrWhiteSpace(query.Map))
        {
            filters.Add($"game.mapVersion.map.displayName=={FafApiClient.Quote("*" + query.Map.Trim() + "*")}");
        }
        if (!string.IsNullOrWhiteSpace(query.WithPlayer))
        {
            filters.Add($"game.playerStats.player.login=={FafApiClient.Quote(query.WithPlayer.Trim())}");
        }

        string sort = query.Sort switch
        {
            MatchSort.Oldest => "scoreTime",
            MatchSort.Longest => "-game.replayTicks",
            MatchSort.Shortest => "game.replayTicks",
            _ => "-scoreTime",
        };

        string include = withPlayers
            ? "game,game.mapVersion.map,game.featuredMod,game.playerStats,game.playerStats.player,ratingChanges.leaderboard"
            : "game,game.mapVersion.map,game.featuredMod,ratingChanges.leaderboard";
        return $"/data/gamePlayerStats?include={include}&sort={sort}"
            + $"&page[size]={Math.Clamp(pageSize, 1, MaxPageSize)}&page[number]={Math.Max(page, 1)}&page[totals]"
            + $"&filter={Filter(string.Join(";", filters))}";
    }

    /// <summary>The name the API's faction filter takes (<c>faction==UEF</c>); a number is refused.</summary>
    public static string FactionName(Faction faction) => faction switch
    {
        Faction.Uef => "UEF",
        Faction.Aeon => "AEON",
        Faction.Cybran => "CYBRAN",
        _ => "SERAPHIM",
    };

    /// <summary>
    /// The board a mode filters on; null for <c>all</c>. Custom games are rated on the global board
    /// (<c>ratingChanges.leaderboard.technicalName=="global"</c> matches them), the other modes are
    /// board names.
    /// </summary>
    public static string? BoardOfMode(string? mode) => mode switch
    {
        null or "" or "all" => null,
        "custom" => "global",
        _ => mode,
    };

    private static string RankingFilter(RankingQuery query)
    {
        List<string> filters = [$"leaderboard.technicalName=={FafApiClient.Quote(query.Board)}", "totalGames=gt=0"];
        if (query.SettledOnly)
        {
            filters.Add("deviation=lt=100");
        }
        if (query.ActiveSince is { } since)
        {
            filters.Add($"updateTime=ge={Time(since)}");
        }
        if (!string.IsNullOrWhiteSpace(query.Name))
        {
            filters.Add($"player.login=={FafApiClient.Quote("*" + query.Name.Trim() + "*")}");
        }
        return string.Join(";", filters);
    }

    private static string Filter(string rsql) => Uri.EscapeDataString(rsql);

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Time(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
