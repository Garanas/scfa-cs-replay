
namespace FAForever.FileFormats.Replay
{
    /// <summary>
    /// A map cell of an order (<c>SOCellPos</c> in the engine: int16 x, int16 z). Orders carry a
    /// list of them (<see cref="CommandData.Cells"/>) and <see cref="ReplayInput.SetCommandCells"/>
    /// replaces it; the list was empty in every order of 685 measured replays.
    /// </summary>
    public readonly record struct CommandCell(short X, short Z);
}
