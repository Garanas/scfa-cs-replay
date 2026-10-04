
using FAForever.FileFormats.Lua;

namespace FAForever.FileFormats.Replay
{
    /// <param name="ClearQueue">Whether the order replaces the queue of the selected units. False
    /// means the order was queued after their current orders (shift-click). Verified on real
    /// replays: orders that follow another order to the same selection within the same tick
    /// (drag-building, several shift-clicks) carry false in 94-98% of the cases.</param>
    public record CommandData(int Identifier, CommandType Type, CommandTarget Target, CommandFormation Formation, String BlueprintId, LuaData LuaParameters, Boolean ClearQueue, int Unknown1, int Unknown2, byte Unknown3, int Unknown4, int Unknown5, int Unknown6);
}
