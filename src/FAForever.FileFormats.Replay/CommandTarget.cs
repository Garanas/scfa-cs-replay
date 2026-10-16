
namespace FAForever.FileFormats.Replay
{
    public interface CommandTarget
    {
        /// <summary>
        /// One example is the stop commands.
        /// </summary>
        public record None() : CommandTarget;

        /// <summary>
        /// Examples are attack, reclaim and repair commands.
        /// </summary>
        /// <param name="EntityId">The targeted entity. Its family (<see cref="ReplayEntityId.GetFamily"/>)
        /// says what it is: an own or allied unit, a prop (reclaim), or a recon blip (an enemy as the
        /// player saw it on radar or in vision; attack, capture, overcharge).</param>
        public record Entity(int EntityId) : CommandTarget;

        /// <summary>
        /// Examples are (attack) move, patrol and ground-assist commands.
        /// </summary>
        /// <param name="X">In world coordinates</param>
        /// <param name="Y">In world coordinates</param>
        /// <param name="Z">In world coordinates</param>
        public record Position(float X, float Y, float Z) : CommandTarget;
    }
}
