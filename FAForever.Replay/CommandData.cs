
namespace FAForever.Replay
{
    public record CommandData(int Identifier, CommandType Type, CommandTarget Target, CommandFormation Formation, String BlueprintId, LuaData LuaParameters, Boolean AddToQueue, int Unknown1, int Unknown2, byte Unknown3, int Unknown4, int Unknown5, int Unknown6);
}
