using FAForever.Vault.Viewer.Services.Players;
using FAForever.Vault.Viewer.Services.Players.Statistics;

namespace FAForever.Vault.Viewer.Features.Players.Profile;

/// <summary>The achievements of a player in one line: how many, the points and the rarest one.</summary>
public sealed record AchievementSummary(int Unlocked, int Total, int ExperiencePoints, AchievementDefinition? Rarest);

/// <summary>A name the player has had, with the span it was theirs ("Mar 2014 to Jun 2017", "since Mar 2021").</summary>
public sealed record NameSpan(string Name, string Span, bool IsCurrent);

/// <summary>What a rating card says about the league: the division and score, or how far the placement is.</summary>
public sealed record LeagueLine(string Title, string Detail);

/// <summary>Pure shaping of the profile's data for the overview.</summary>
public static class ProfileSummaries
{
    /// <summary>The ratings with the most games first (ties keep the API's order).</summary>
    public static IReadOnlyList<PlayerRating> ByGames(IReadOnlyList<PlayerRating> ratings)
        => [.. ratings.OrderByDescending(rating => rating.TotalGames)];

    /// <summary>The technical name of the board with the most games; null without a rating.</summary>
    public static string? DefaultBoard(IReadOnlyList<PlayerRating> ratings)
        => ByGames(ratings).FirstOrDefault()?.Board.TechnicalName;

    /// <summary>The boards a player can pick, as options of a select (the most played first).</summary>
    public static IReadOnlyList<ChoiceOption> BoardOptions(IReadOnlyList<PlayerRating> ratings)
        => [.. ByGames(ratings).Select(rating => new ChoiceOption(rating.Board.TechnicalName, rating.Board.DisplayName))];

    /// <summary>The board of the address when the player has a rating on it, otherwise the default.</summary>
    public static string? ActiveBoard(IReadOnlyList<PlayerRating> ratings, string? requested)
        => requested is not null && ratings.Any(rating => rating.Board.TechnicalName == requested) ? requested : DefaultBoard(ratings);

    /// <summary>
    /// The change over the entries (in any order): the rating after the last one minus the rating
    /// before the first one; null without entries.
    /// </summary>
    public static int? ChangeOver(IReadOnlyList<RatingJournalEntry> entries)
    {
        if (entries.Count == 0)
        {
            return null;
        }

        RatingJournalEntry first = entries.OrderBy(entry => entry.Date ?? DateTimeOffset.MinValue).ThenBy(entry => entry.GameId).First();
        RatingJournalEntry last = entries.OrderBy(entry => entry.Date ?? DateTimeOffset.MinValue).ThenBy(entry => entry.GameId).Last();
        return Display(last.MeanAfter, last.DeviationAfter) - Display(first.MeanBefore, first.DeviationBefore);
    }

    /// <summary>Unlocked of total, the experience points of the unlocked ones and the unlocked one the fewest players have.</summary>
    public static AchievementSummary Achievements(IReadOnlyList<AchievementDefinition> definitions, IReadOnlyList<PlayerAchievement> mine)
    {
        HashSet<string> unlockedIds = [.. mine.Where(achievement => achievement.State == AchievementState.Unlocked).Select(achievement => achievement.AchievementId)];
        AchievementDefinition[] unlocked = [.. definitions.Where(definition => unlockedIds.Contains(definition.Id))];
        return new AchievementSummary(
            unlocked.Length,
            definitions.Count,
            unlocked.Sum(definition => definition.ExperiencePoints),
            unlocked.OrderBy(definition => definition.UnlockersPercent).FirstOrDefault());
    }

    /// <summary>
    /// The league line of a board's card: the player's entry in the season that is running on that
    /// board. Null when no season runs on it or the player has no entry. In placement it says how
    /// many of the placement games are played.
    /// </summary>
    public static LeagueLine? LeagueOf(string board, IReadOnlyList<LeagueSeason> seasons, IReadOnlyList<LeagueEntry> entries, DateTimeOffset now)
    {
        foreach (LeagueSeason season in seasons.Where(season => season.BoardTechnicalName == board && season.IsActive(now)))
        {
            LeagueEntry? entry = entries.FirstOrDefault(candidate => candidate.SeasonId == season.Id);
            if (entry is null)
            {
                continue;
            }

            // A player in placement has no score yet (the score may be absent), so no points are shown.
            int? score = entry.Score;
            if (entry.Subdivision is { } subdivision && score is { } points)
            {
                return new LeagueLine(subdivision.FullName, $"{ProfileFormat.Number(points)} points");
            }

            int placement = entry.ReturningPlayer ? season.PlacementGamesReturningPlayer : season.PlacementGames;
            return new LeagueLine("Placement", placement > 0
                ? $"{Math.Min(entry.GameCount, placement)} of {placement} games"
                : $"{entry.GameCount} games");
        }

        return null;
    }

    /// <summary>
    /// The names of a player, the current one first, each with its span. A former name's
    /// <c>ChangedAt</c> is when it stopped being the name.
    /// </summary>
    public static IReadOnlyList<NameSpan> NameHistory(PlayerProfile profile, TimeZoneInfo? zone = null)
    {
        List<NameSpan> names = [];
        IReadOnlyList<PlayerName> former = profile.FormerNames;
        string since = former.Count > 0 && former[0].ChangedAt is { } changed ? $"since {ProfileFormat.Month(changed, zone)}" : "";
        names.Add(new NameSpan(profile.Login, since, true));
        for (int i = 0; i < former.Count; i++)
        {
            DateTimeOffset? from = i + 1 < former.Count ? former[i + 1].ChangedAt : profile.CreatedAt;
            DateTimeOffset? until = former[i].ChangedAt;
            string span = (from, until) switch
            {
                (not null, not null) => $"{ProfileFormat.Month(from, zone)} to {ProfileFormat.Month(until, zone)}",
                (null, not null) => $"until {ProfileFormat.Month(until, zone)}",
                _ => "",
            };
            names.Add(new NameSpan(former[i].Name, span, false));
        }

        return names;
    }

    private static int Display(double mean, double deviation)
        => FAForever.FileFormats.Replay.ReplayPlayerOptions.DisplayRating(mean, deviation) ?? 0;
}
