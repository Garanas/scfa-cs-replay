
namespace FAForever.Replay
{
    public record ReplayHeader(ReplayScenario Scenario, ReplaySource[] Clients, LuaData[] Mods, LuaData[] ArmyOptions);
}
