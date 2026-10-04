namespace FAForever.FileFormats.Replay
{
    /// <summary>
    /// The end-of-game statistics (lua/sim/score.lua) that every client reports to the server
    /// with <c>GpgNetSend('JsonStats', json)</c> when the game ends. FAF hooks GpgNetSend
    /// (lua/ui/globals/GpgNetSend.lua) to also log the call through the ModeratorEvent sim
    /// callback, which is how the payload ends up in the replay. The server uses the report to
    /// award achievements; it is not what the in-game score screen (lua/ui/dialogs/hotstats.lua)
    /// shows, which is not in the replay.
    /// </summary>
    /// <remarks>
    /// Only replays of games that ended normally carry it, and only those recorded since the
    /// hook exists. The sim is deterministic, so every client reports the same numbers; the
    /// first report that parses is used. All numbers are floats as the game reported them, so
    /// counts can show noise (a defeated army with 0.4 units left); see
    /// <see cref="ReplayGameStatsReader"/> for the encoder bug that is corrected on reading.
    /// </remarks>
    /// <param name="Timestamp">The in-game time the stats were reported, i.e. the end of the game.</param>
    /// <param name="SourceId">The client whose report this is, see <see cref="ReplayHeader.Clients"/>.</param>
    /// <param name="Armies">One entry per non-civilian army, in army order.</param>
    public record ReplayGameStats(TimeSpan Timestamp, int SourceId, IReadOnlyList<ReplayArmyStats> Armies);

    /// <summary>The statistics of one army (<c>ArmyScore[index]</c> in lua/sim/score.lua).</summary>
    /// <param name="Name">The army's nickname, matching <see cref="ReplayPlayerOptions.PlayerName"/>.</param>
    /// <param name="Faction">1 = UEF, 2 = Aeon, 3 = Cybran, 4 = Seraphim.</param>
    /// <param name="Type">The brain type: <c>Human</c> or <c>AI</c>.</param>
    /// <param name="Defeated">
    /// Absent while the army is alive. When it is defeated the game stores the game time (in
    /// seconds) + 15, and once that moment has passed, -1, after which its stats stop updating,
    /// so <see cref="ReplayArmyGeneralStats.LastUpdateTick"/> tells when it was knocked out. At the
    /// end of the game every army counts as defeated, so the survivors carry the end time + 15.
    /// </param>
    /// <param name="Units">Built / kills / lost per unit category: <c>land</c>, <c>air</c>, <c>naval</c>,
    /// <c>cdr</c>, <c>experimental</c>, <c>structures</c>, <c>transportation</c>, <c>sacu</c>,
    /// <c>engineer</c>, <c>tech1</c>, <c>tech2</c>, <c>tech3</c>.</param>
    /// <param name="Blueprints">Built / kills / lost of the units the server tracks for achievements
    /// (ACUs, air superiority fighters, experimentals), keyed by lowercase blueprint id. Only units
    /// the army had any stats for are present.</param>
    public record ReplayArmyStats(
        string? Name,
        int? Faction,
        string? Type,
        double? Defeated,
        ReplayArmyGeneralStats General,
        IReadOnlyDictionary<string, ReplayUnitTally> Units,
        IReadOnlyDictionary<string, ReplayUnitTally> Blueprints,
        ReplayArmyResourceStats Resources)
    {
        /// <summary>Whether the army was knocked out before the game ended.</summary>
        public bool EliminatedEarly => Defeated is < 0;
    }

    /// <param name="Score">The score as shown in game (CalculateBrainScore).</param>
    /// <param name="LastUpdateTick">The tick of the last resource update; it stops when the army is defeated.</param>
    /// <param name="CurrentUnits">Units alive (UnitCap_Current).</param>
    /// <param name="CurrentCap">The unit cap (UnitCap_MaxCap).</param>
    /// <param name="Kills">Enemy units destroyed, with their mass and energy value.</param>
    /// <param name="Built">Units built (Units_History), with their mass and energy value.</param>
    /// <param name="Lost">Own units lost, with their mass and energy value.</param>
    public record ReplayArmyGeneralStats(
        double Score,
        double LastUpdateTick,
        double CurrentUnits,
        double CurrentCap,
        ReplayValueTally Kills,
        ReplayValueTally Built,
        ReplayValueTally Lost);

    /// <summary>A number of units and their combined mass and energy value.</summary>
    public record ReplayValueTally(double Count, double Mass, double Energy);

    /// <summary>Units built, enemy units killed and own units lost.</summary>
    /// <param name="LowestHealth">The lowest health the unit had; only reported for ACUs.</param>
    public record ReplayUnitTally(double Built, double Kills, double Lost, double? LowestHealth = null);

    /// <param name="MassIn">Mass produced (MassIn.Total includes reclaim).</param>
    /// <param name="MassOut">Mass consumed; Excess is what was wasted at full storage.</param>
    public record ReplayArmyResourceStats(
        ReplayResourceIncome MassIn,
        ReplayResourceExpense MassOut,
        ReplayResourceIncome EnergyIn,
        ReplayResourceExpense EnergyOut,
        ReplayResourceStorage Storage);

    /// <param name="Total">Everything produced, reclaim included.</param>
    /// <param name="Rate">Income per tick at the end of the game, reclaim excluded.</param>
    /// <param name="Reclaimed">The part of <paramref name="Total"/> that came from reclaim.</param>
    /// <param name="ReclaimRate">Reclaim per tick at the end of the game.</param>
    public record ReplayResourceIncome(double Total, double Rate, double Reclaimed, double ReclaimRate);

    /// <param name="Total">Everything consumed.</param>
    /// <param name="Rate">Consumption per tick at the end of the game.</param>
    /// <param name="Excess">Accumulated excess: production that was wasted because storage was full.</param>
    public record ReplayResourceExpense(double Total, double Rate, double Excess);

    /// <summary>Resources in storage at the end of the game.</summary>
    public record ReplayResourceStorage(double StoredMass, double MaxMass, double StoredEnergy, double MaxEnergy);
}
