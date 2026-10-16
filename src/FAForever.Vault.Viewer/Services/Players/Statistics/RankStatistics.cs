namespace FAForever.Vault.Viewer.Services.Players.Statistics;

/// <summary>Ranks in a leaderboard.</summary>
public static class RankStatistics
{
    /// <summary>The rank of a rating when <paramref name="betterRatings"/> ratings are higher: one more than that.</summary>
    public static int RankFromCount(int betterRatings) => Math.Max(betterRatings, 0) + 1;

    /// <summary>
    /// The share of the ratings (0 to 1) that rank at or above this rank, for "top 12 percent" text;
    /// null without ratings.
    /// </summary>
    public static double? TopShare(int rank, int totalRatings)
        => totalRatings > 0 ? Math.Clamp((double)rank / totalRatings, 0, 1) : null;
}
