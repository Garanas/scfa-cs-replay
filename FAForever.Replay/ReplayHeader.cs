
namespace FAForever.Replay
{
    public record ReplayHeader(ReplaySource[] Clients, LuaData[] Mods, LuaData[] ArmyOptions);
}
