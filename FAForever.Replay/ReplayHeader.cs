
namespace FAForever.Replay
{
    /// <summary>
    /// The header of a replay: everything the game wrote before the input stream starts.
    /// </summary>
    /// <param name="GameVersion">The game version, e.g. "Supreme Commander v1.50.3701".</param>
    /// <param name="ReplayVersion">The replay format version, e.g. "Replay v1.9".</param>
    /// <param name="PathToScenario">The path to the scenario file of the map that was played.</param>
    /// <param name="Scenario">The scenario: map information and lobby options.</param>
    /// <param name="Clients">The clients (players and observers) connected to the game. The index in this array is the source id of replay inputs.</param>
    /// <param name="Mods">The mods that were active, as raw Lua tables (name, uid, version, ...).</param>
    /// <param name="Armies">The armies of the game with their lobby options (faction, team, rating, ...). Linked to <paramref name="Clients"/> via <see cref="ReplayPlayerOptions.SourceId"/>.</param>
    /// <param name="CheatsEnabled">Whether cheats were enabled.</param>
    /// <param name="Seed">The random seed of the simulation.</param>
    public record ReplayHeader(
        string GameVersion,
        string ReplayVersion,
        string PathToScenario,
        ReplayScenario Scenario,
        ReplaySource[] Clients,
        LuaData[] Mods,
        ReplayPlayerOptions[] Armies,
        bool CheatsEnabled,
        int Seed);
}
