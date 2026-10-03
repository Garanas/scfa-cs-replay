namespace FAForever.Replay
{
    /// <summary>
    /// A brush stroke a player painted on the map, extracted from the SharePaintingBrushStroke
    /// sim callback (lua/ui/game/painting). Every stroke is sent once, by its author; strokes of
    /// observers travel through chat and are therefore not in the replay. In game a stroke is
    /// drawn in the author's army colour, is visible to allies and spectators only and fades
    /// after 25 seconds (by default).
    /// </summary>
    /// <param name="Timestamp">The in-game time the stroke was shared, i.e. finished.</param>
    /// <param name="SourceId">The client that painted the stroke, see <see cref="ReplayHeader.Clients"/>.</param>
    /// <param name="PeerName">The author's name as the painting UI sent it.</param>
    /// <param name="ShareId">The author's sequence number of the stroke.</param>
    /// <param name="Points">The samples of the stroke on the map plane, in world units.</param>
    public record ReplayDrawing(TimeSpan Timestamp, int SourceId, string? PeerName, int? ShareId, IReadOnlyList<ReplayAnalysis.MapPosition> Points);
}
