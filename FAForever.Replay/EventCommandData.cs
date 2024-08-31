
namespace FAForever.Replay
{
    public record struct EventCommandData(int Identifier, EventCommandType Type, EventCommandTarget Target, EventCommandFormation Formation, String BlueprintId, LuaData LuaParameters, Boolean AddToQueue, byte[] Unknown1, byte[] Unknown2, byte[] Unknown3, byte[] Unknown4);
}
