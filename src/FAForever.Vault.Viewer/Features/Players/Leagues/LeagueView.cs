using System.Globalization;
using FAForever.Vault.Viewer.Services.Players;

namespace FAForever.Vault.Viewer.Features.Players.Leagues;

/// <summary>One point of the season journey: the subdivision a player stood in after one game.</summary>
public sealed record JourneyPoint(int Game, int Position, LeagueSubdivision Subdivision, int Score, bool Promoted);

/// <summary>A subdivision with the number of players in it.</summary>
public sealed record SubdivisionCount(LeagueSubdivision Subdivision, int Players);

/// <summary>A division with its subdivisions, the lowest first.</summary>
public sealed record DivisionGroup(LeagueDivision Division, int Total, IReadOnlyList<SubdivisionCount> Subdivisions);

/// <summary>The pure parts of the Leagues page: what the address chooses and the shapes the charts draw.</summary>
public static class LeagueView
{
    /// <summary>The league of the address (its technical name); the ladder 1v1 league, else the first, when the address names none.</summary>
    public static League? ChooseLeague(IReadOnlyList<League> leagues, string? token)
        => leagues.FirstOrDefault(league => league.TechnicalName == token) ?? DefaultLeague(leagues);

    /// <summary>The league that opens without a choice.</summary>
    public static League? DefaultLeague(IReadOnlyList<League> leagues)
        => leagues.FirstOrDefault(league => league.TechnicalName == PlayerFilters.DefaultBoard) ?? leagues.FirstOrDefault();

    /// <summary>The seasons of one league, the latest first.</summary>
    public static IReadOnlyList<LeagueSeason> SeasonsOf(IReadOnlyList<LeagueSeason> seasons, int leagueId)
        => [.. seasons.Where(season => season.LeagueId == leagueId).OrderByDescending(season => season.StartDate ?? DateTimeOffset.MinValue)];

    /// <summary>The running season of a league, else its latest.</summary>
    public static LeagueSeason? DefaultSeason(IReadOnlyList<LeagueSeason> seasons, int leagueId, DateTimeOffset now)
    {
        IReadOnlyList<LeagueSeason> own = SeasonsOf(seasons, leagueId);
        return own.FirstOrDefault(season => season.IsActive(now)) ?? own.FirstOrDefault();
    }

    /// <summary>The season whose id the address holds, when it belongs to the league; otherwise the default.</summary>
    public static LeagueSeason? ChooseSeason(IReadOnlyList<LeagueSeason> seasons, int leagueId, string? token, DateTimeOffset now)
        => int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out int id) && SeasonsOf(seasons, leagueId).FirstOrDefault(season => season.Id == id) is { } chosen
            ? chosen
            : DefaultSeason(seasons, leagueId, now);

    /// <summary>The subdivision shown without a choice: the player's own, else the top one (the list has the lowest first).</summary>
    public static LeagueSubdivision? DefaultSubdivision(IReadOnlyList<LeagueSubdivision> subdivisions, LeagueSubdivision? own)
        => own is not null && subdivisions.FirstOrDefault(subdivision => subdivision.Id == own.Id) is { } found ? found : subdivisions.LastOrDefault();

    /// <summary>The subdivision whose id the address holds, else the default.</summary>
    public static LeagueSubdivision? ChooseSubdivision(IReadOnlyList<LeagueSubdivision> subdivisions, string? token, LeagueSubdivision? own)
        => int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out int id) && subdivisions.FirstOrDefault(subdivision => subdivision.Id == id) is { } chosen
            ? chosen
            : DefaultSubdivision(subdivisions, own);

    /// <summary>The place of a subdivision in the list, the lowest being 0; -1 when it is not in the list.</summary>
    public static int Position(IReadOnlyList<LeagueSubdivision> subdivisions, LeagueSubdivision subdivision)
    {
        for (int i = 0; i < subdivisions.Count; i++)
        {
            if (subdivisions[i].Id == subdivision.Id)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// The staircase of a season: a point after every game that left the player in a subdivision
    /// (placement games have none), flagged when the game moved them up.
    /// </summary>
    public static IReadOnlyList<JourneyPoint> Journey(IReadOnlyList<LeagueJournalEntry> journal, IReadOnlyList<LeagueSubdivision> subdivisions)
    {
        List<JourneyPoint> points = [];
        foreach (LeagueJournalEntry entry in journal.OrderBy(entry => entry.GameCount))
        {
            if (entry.After is not { } after)
            {
                continue;
            }

            int position = Position(subdivisions, after);
            if (position < 0)
            {
                continue;
            }

            int before = entry.Before is { } b ? Position(subdivisions, b) : -1;
            points.Add(new JourneyPoint(entry.GameCount, position, subdivisions[position], entry.ScoreAfter, before >= 0 && position > before));
        }

        return points;
    }

    /// <summary>The subdivisions grouped by division, the lowest first, with the players in each.</summary>
    public static IReadOnlyList<DivisionGroup> Groups(IReadOnlyList<LeagueSubdivision> subdivisions, IReadOnlyDictionary<int, int> counts)
        => [.. subdivisions
            .GroupBy(subdivision => subdivision.Division.Id)
            .Select(group =>
            {
                SubdivisionCount[] items = [.. group.Select(subdivision => new SubdivisionCount(subdivision, counts.TryGetValue(subdivision.Id, out int players) ? players : 0))];
                return new DivisionGroup(group.First().Division, items.Sum(item => item.Players), items);
            })];

    /// <summary>The address parameters that name a league, season or subdivision that does not exist (or that equal the default), to drop.</summary>
    public static IReadOnlyList<string> StaleParameters(
        IReadOnlyList<League> leagues,
        IReadOnlyList<LeagueSeason> seasons,
        League league,
        IReadOnlyList<LeagueSubdivision> subdivisions,
        string? leagueToken,
        string? seasonToken,
        string? subdivisionToken,
        string? pageToken)
    {
        List<string> stale = [];
        if (leagueToken is not null && leagues.All(item => item.TechnicalName != leagueToken))
        {
            stale.Add("league");
        }

        if (seasonToken is not null && !(int.TryParse(seasonToken, NumberStyles.None, CultureInfo.InvariantCulture, out int seasonId) && SeasonsOf(seasons, league.Id).Any(season => season.Id == seasonId)))
        {
            stale.Add("season");
        }

        if (subdivisionToken is not null && !(int.TryParse(subdivisionToken, NumberStyles.None, CultureInfo.InvariantCulture, out int subdivisionId) && subdivisions.Any(subdivision => subdivision.Id == subdivisionId)))
        {
            stale.Add("subdivision");
        }

        if (pageToken is not null && Rankings.RankingView.PageNumber(pageToken) <= 1)
        {
            stale.Add("page");
        }

        return stale;
    }

    /// <summary>
    /// The players per subdivision with one source for the listed subdivision: its standings total,
    /// which the table below shows too, replaces the separate count.
    /// </summary>
    public static IReadOnlyDictionary<int, int> CountsWithStandings(IReadOnlyDictionary<int, int> counts, int? subdivisionId, int? standingsTotal)
    {
        if (subdivisionId is not { } id || standingsTotal is not { } total)
        {
            return counts;
        }

        Dictionary<int, int> merged = new(counts) { [id] = total };
        return merged;
    }

    /// <summary>The games a player must play before the league places them.</summary>
    public static int PlacementGames(LeagueSeason season, bool returningPlayer)
        => returningPlayer ? season.PlacementGamesReturningPlayer : season.PlacementGames;
}
