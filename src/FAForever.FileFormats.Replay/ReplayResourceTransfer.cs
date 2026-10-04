
namespace FAForever.FileFormats.Replay
{
    /// <summary>
    /// A resource transfer between players, sent via the same sim callback as chat messages.
    /// </summary>
    /// <param name="Timestamp">The in-game time of the transfer.</param>
    /// <param name="SourceId">The source (client) that sent the resources, see <see cref="ReplayHeader.Clients"/>.</param>
    /// <param name="FromArmy">The 1-based army slot that sent the resources.</param>
    /// <param name="ToArmy">The 1-based army slot that received the resources.</param>
    /// <param name="MassRatio">The fraction (0..1) of the sender's current mass storage that was sent.</param>
    /// <param name="EnergyRatio">The fraction (0..1) of the sender's current energy storage that was sent.</param>
    public record ReplayResourceTransfer(TimeSpan Timestamp, int SourceId, int FromArmy, int ToArmy, double MassRatio, double EnergyRatio);
}
