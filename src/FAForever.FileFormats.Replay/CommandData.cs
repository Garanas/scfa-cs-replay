
using FAForever.FileFormats.Lua;

namespace FAForever.FileFormats.Replay
{
    /// <summary>
    /// An order as the engine writes it (<c>SSTICommandIssueData</c>, read by
    /// <c>CDecoder::DecodeCommandData</c>; see AGENTS.md, "What the engine does").
    /// </summary>
    /// <param name="Identifier">The command id, <c>(source &lt;&lt; 24) | serial</c>: later inputs refer to
    /// the order by it. Ids are reused: a serial returns about 100 beats after its command ended.</param>
    /// <param name="ClearQueue">Whether the order replaces the queue of the selected units. False
    /// means the order was queued after their current orders (shift-click). Verified on real
    /// replays: orders that follow another order to the same selection within the same tick
    /// (drag-building, several shift-clicks) carry false in 94-98% of the cases. The engine
    /// rejects any value other than 0 and 1.</param>
    /// <param name="CoordinatingCommandId">The id of an earlier command of the same source that this
    /// attack coordinates with ("Coordinated Attack!": several drags onto the same target), or -1.
    /// Seen only on attack orders.</param>
    /// <param name="InsertBeforeCommandId">The id of the queued command this order goes in front of,
    /// or -1 to append: inserting into a factory queue.</param>
    /// <param name="SecondTarget">The engine's second target of an order;
    /// <see cref="CommandTarget.None"/> in every measured order.</param>
    /// <param name="Cells">The map cells of the order; empty in every measured order.</param>
    /// <param name="MaxCount">The most the order builds; 1 at issue in every measured order.</param>
    /// <param name="Count">The count of the order, which <see cref="ReplayInput.IncreaseCommandCount"/>
    /// and <see cref="ReplayInput.DecreaseCommandCount"/> change; 1 at issue in every measured order.</param>
    public record CommandData(
        int Identifier,
        CommandType Type,
        CommandTarget Target,
        CommandFormation Formation,
        String BlueprintId,
        LuaData LuaParameters,
        Boolean ClearQueue,
        int CoordinatingCommandId,
        int InsertBeforeCommandId,
        CommandTarget SecondTarget,
        IReadOnlyList<CommandCell> Cells,
        int MaxCount,
        int Count);
}
