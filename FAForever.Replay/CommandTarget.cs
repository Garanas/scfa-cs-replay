
namespace FAForever.Replay
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
        /// <param name="EntityId"></param>
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
