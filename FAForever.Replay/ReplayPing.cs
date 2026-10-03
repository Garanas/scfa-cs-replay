
namespace FAForever.Replay
{
    /// <summary>
    /// A ping a player placed on the map, extracted from the SpawnPing/SpawnSpecialPing sim
    /// callbacks. Types seen in the wild: Move, Attack, Alert, Marker (which carries the
    /// typed <paramref name="Name"/>) and nuke (special ping).
    /// </summary>
    /// <param name="Timestamp">The in-game time of the ping.</param>
    /// <param name="SourceId">The client that placed the ping, see <see cref="ReplayHeader.Clients"/>.</param>
    /// <param name="Type">The ping type, e.g. Move, Attack, Alert, Marker or nuke.</param>
    /// <param name="X">The map X coordinate (world units).</param>
    /// <param name="Y">The height at the pinged position.</param>
    /// <param name="Z">The map Z coordinate (world units).</param>
    /// <param name="Name">The text of a Marker ping, null otherwise.</param>
    /// <param name="Color">The ping colour as CSS hex, when the payload carries one.</param>
    public record ReplayPing(TimeSpan Timestamp, int SourceId, string Type, float X, float Y, float Z, string? Name, string? Color);
}
