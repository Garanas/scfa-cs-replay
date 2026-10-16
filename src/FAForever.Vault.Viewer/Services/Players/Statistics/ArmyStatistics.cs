using FAForever.FileFormats.Blueprints;

namespace FAForever.Vault.Viewer.Services.Players.Statistics;

/// <summary>Games and wins as one faction.</summary>
public sealed record FactionPlays(Faction Faction, long Plays, long Wins)
{
    /// <summary>Games not won.</summary>
    public long Losses => Math.Max(Plays - Wins, 0);

    /// <summary>Wins as a share of the games (0 to 1); null without games.</summary>
    public double? WinShare => Plays > 0 ? (double)Wins / Plays : null;
}

/// <summary>Units built and lost in a group (a category, a tech level).</summary>
public sealed record BuiltAndLost(string Label, long Built, long Lost);

/// <summary>The army statistics of a player, from the lifetime counters.</summary>
/// <param name="Factions">Games and wins per faction, in the game's order.</param>
/// <param name="CustomGames">Custom games played; null when the game no longer records them.</param>
/// <param name="LadderGames">Ladder games played; null when the game no longer records them.</param>
/// <param name="HoursPlayed">Time in game, in hours; null when the game no longer records it.</param>
/// <param name="AcusLost">Commanders lost.</param>
/// <param name="AcusLostPerGame">Commanders lost per game (over the custom and ladder games, or the games with a faction recorded when those are not recorded); null without games.</param>
/// <param name="ByCategory">Units built and lost: air, land and naval.</param>
/// <param name="ByTech">Units built and lost: tech 1, 2, 3 and experimentals.</param>
/// <param name="Engineers">Engineers built and lost.</param>
public sealed record ArmySummary(
    IReadOnlyList<FactionPlays> Factions,
    long? CustomGames,
    long? LadderGames,
    double? HoursPlayed,
    long AcusLost,
    double? AcusLostPerGame,
    IReadOnlyList<BuiltAndLost> ByCategory,
    IReadOnlyList<BuiltAndLost> ByTech,
    BuiltAndLost Engineers);

/// <summary>Statistics over a player's lifetime counters.</summary>
public static class ArmyStatistics
{
    /// <summary>The army statistics of the counters.</summary>
    public static ArmySummary Summarize(PlayerEventCounts counts)
    {
        long? custom = counts.TryGet(PlayerEvents.CustomGamesPlayed);
        long? ladder = counts.TryGet(PlayerEvents.LadderGamesPlayed);
        long? seconds = counts.TryGet(PlayerEvents.SecondsPlayed);
        long acus = counts.Get(PlayerEvents.AcusLost);
        long factionGames = counts.Get(PlayerEvents.UefPlays) + counts.Get(PlayerEvents.AeonPlays)
            + counts.Get(PlayerEvents.CybranPlays) + counts.Get(PlayerEvents.SeraphimPlays);
        long games = custom is null && ladder is null ? factionGames : (custom ?? 0) + (ladder ?? 0);

        return new ArmySummary(
            [
                new FactionPlays(Faction.Uef, counts.Get(PlayerEvents.UefPlays), counts.Get(PlayerEvents.UefWins)),
                new FactionPlays(Faction.Aeon, counts.Get(PlayerEvents.AeonPlays), counts.Get(PlayerEvents.AeonWins)),
                new FactionPlays(Faction.Cybran, counts.Get(PlayerEvents.CybranPlays), counts.Get(PlayerEvents.CybranWins)),
                new FactionPlays(Faction.Seraphim, counts.Get(PlayerEvents.SeraphimPlays), counts.Get(PlayerEvents.SeraphimWins)),
            ],
            custom,
            ladder,
            seconds / 3600.0,
            acus,
            games > 0 ? (double)acus / games : null,
            [
                Pair(counts, "Air", PlayerEvents.BuiltAir, PlayerEvents.LostAir),
                Pair(counts, "Land", PlayerEvents.BuiltLand, PlayerEvents.LostLand),
                Pair(counts, "Naval", PlayerEvents.BuiltNaval, PlayerEvents.LostNaval),
            ],
            [
                Pair(counts, "Tech 1", PlayerEvents.BuiltTech1, PlayerEvents.LostTech1),
                Pair(counts, "Tech 2", PlayerEvents.BuiltTech2, PlayerEvents.LostTech2),
                Pair(counts, "Tech 3", PlayerEvents.BuiltTech3, PlayerEvents.LostTech3),
                Pair(counts, "Experimental", PlayerEvents.BuiltExperimentals, PlayerEvents.LostExperimentals),
            ],
            Pair(counts, "Engineers", PlayerEvents.BuiltEngineers, PlayerEvents.LostEngineers));
    }

    private static BuiltAndLost Pair(PlayerEventCounts counts, string label, string built, string lost)
        => new(label, counts.Get(built), counts.Get(lost));
}
