
namespace FAForever.Replay
{
    /// <summary>
    /// The game options of the lobby, read from the Options sub-table of the scenario.
    ///
    /// These options reflect the following file: https://github.com/FAForever/fa/blob/develop/lua/ui/lobby/lobbyOptions.lua
    /// In practice all mod options are sent along too, so <see cref="Raw"/> can contain
    /// quite literally anything; only the common lobby options are typed.
    /// </summary>
    /// <param name="Victory">The victory condition key: demoralization (assassination), domination (supremacy), eradication (annihilation) or sandbox.</param>
    /// <param name="UnitCap">The unit cap per army.</param>
    /// <param name="CheatsEnabled">Whether cheats are enabled.</param>
    /// <param name="PrebuiltUnits">Whether armies start with prebuilt units.</param>
    /// <param name="AllowObservers">Whether observers are allowed.</param>
    /// <param name="RevealCivilians">Whether civilian armies are revealed on the map.</param>
    /// <param name="Score">Whether the score is enabled.</param>
    /// <param name="AutoTeams">The automatic team assignment: none, manual, tvsb, lvsr or pvsi.</param>
    /// <param name="TeamLock">Whether teams are locked or unlocked.</param>
    /// <param name="TeamSpawn">The spawn assignment, e.g. fixed, random or one of the balanced variants.</param>
    /// <param name="Unranked">Whether the lobby was explicitly unranked.</param>
    /// <param name="ScenarioFile">The path to the scenario file of the map.</param>
    /// <param name="Raw">The complete Options table, including all mod options.</param>
    public record ReplayScenarioOptions(
        string? Victory,
        int? UnitCap,
        bool? CheatsEnabled,
        bool? PrebuiltUnits,
        bool? AllowObservers,
        bool? RevealCivilians,
        bool? Score,
        string? AutoTeams,
        string? TeamLock,
        string? TeamSpawn,
        string? Unranked,
        string? ScenarioFile,
        LuaData.Table? Raw)
    {
        /// <summary>
        /// An empty set of options, used when the scenario carries no Options table.
        /// </summary>
        public ReplayScenarioOptions()
            : this(null, null, null, null, null, null, null, null, null, null, null, null, null)
        {
        }
    }
}
