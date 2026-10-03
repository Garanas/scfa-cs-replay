
namespace FAForever.Replay
{
    /// <summary>
    /// A moderation-relevant event the game logged via the ModeratorEvent sim callback,
    /// e.g. "Created a ping of type 'alert'" or self-destruct announcements.
    /// </summary>
    /// <param name="Timestamp">The in-game time of the event.</param>
    /// <param name="SourceId">The client that emitted the event, see <see cref="ReplayHeader.Clients"/>.</param>
    /// <param name="FromArmy">The 1-based army slot the event refers to, when the payload carries one.</param>
    /// <param name="Message">The logged message.</param>
    public record ReplayModeratorEvent(TimeSpan Timestamp, int SourceId, int? FromArmy, string Message);
}
