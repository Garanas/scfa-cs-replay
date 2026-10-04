
using FAForever.FileFormats.Lua;

namespace FAForever.FileFormats.Replay
{
    /// <summary>
    /// The game options of the lobby, read from the Options sub-table of the scenario.
    ///
    /// These options reflect the following file: https://github.com/FAForever/fa/blob/develop/lua/ui/lobby/lobbyOptions.lua
    /// In practice all mod options are sent along too, so <see cref="Raw"/> can contain
    /// quite literally anything; only the common lobby options are typed.
    /// </summary>
    /// <param name="Victory">The victory condition key: demoralization (assassination), domination (supremacy), eradication (annihilation) or sandbox.</param>
    /// <param name="Share">The share condition key, e.g. FullShare or ShareUntilDeath.</param>
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
        string? Share,
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
            : this(null, null, null, null, null, null, null, null, null, null, null, null, null, null)
        {
        }

        /// <summary>
        /// The victory condition as the lobby displays it (lua/ui/lobby/lobbyOptions.lua).
        /// </summary>
        public string? VictoryName => Victory switch
        {
            "demoralization" => "Assassination",
            "decapitation" => "Decapitation",
            "domination" => "Supremacy",
            "eradication" => "Annihilation",
            "sandbox" => "Sandbox",
            null => null,
            _ => Victory,
        };

        /// <summary>
        /// The share condition as the lobby displays it (lua/ui/lobby/lobbyOptions.lua).
        /// </summary>
        public string? ShareName => Share switch
        {
            "FullShare" => "Full share",
            "ShareUntilDeath" => "Share until death",
            "PartialShare" => "Partial share",
            "TransferToKiller" => "Traitors",
            "Defectors" => "Defectors",
            "CivilianDeserter" => "Desert to civilians",
            null => null,
            _ => Share,
        };

        /// <summary>
        /// The automatic team assignment as the lobby displays it.
        /// </summary>
        public string? AutoTeamsName => AutoTeams switch
        {
            "none" => "None",
            "manual" => "Manual",
            "tvsb" => "Top vs bottom",
            "lvsr" => "Left vs right",
            "pvsi" => "Even vs uneven",
            null => null,
            _ => AutoTeams,
        };
    }
}
