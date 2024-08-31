﻿
namespace FAForever.Replay
{
    public interface EventCommandTarget
    {
        /// <summary>
        /// One example is the stop commands.
        /// </summary>
        public record struct None() : EventCommandTarget;

        /// <summary>
        /// Examples are attack, reclaim and repair commands.
        /// </summary>
        /// <param name="EntityId"></param>
        public record struct Entity(int EntityId) : EventCommandTarget;

        /// <summary>
        /// Examples are (attack) move, patrol and ground-assist commands.
        /// </summary>
        /// <param name="X">In world coordinates</param>
        /// <param name="Y">In world coordinates</param>
        /// <param name="Z">In world coordinates</param>
        public record struct Position(float X, float Y, float Z): EventCommandTarget;
    }
}
