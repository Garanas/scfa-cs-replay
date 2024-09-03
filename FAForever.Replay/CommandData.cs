
namespace FAForever.Replay
{
    public record struct CommandData(int Identifier, CommandType Type, CommandTarget Target, CommandFormation Formation, String BlueprintId, LuaData LuaParameters, Boolean AddToQueue, byte[] Unknown1, byte[] Unknown2, byte[] Unknown3, byte[] Unknown4);
}
