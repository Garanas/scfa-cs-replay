
namespace FAForever.FileFormats.Replay
{
    /// <summary>
    /// A rotation as the engine stores it: a unit quaternion in the order w, x, y, z
    /// (<c>Wm3::Quatf</c>; the orientation of a <c>VTransform</c> and of a formation). See AGENTS.md,
    /// "What the engine does", for the source in faf-re.
    /// </summary>
    public readonly record struct ReplayOrientation(float W, float X, float Y, float Z)
    {
        /// <summary>
        /// The rotation about the vertical (y) axis in radians, <c>2 · atan2(y, w)</c>: the heading of
        /// an orientation that only turns about that axis, which is what orders and warps carry.
        /// </summary>
        public double GetHeading() => 2 * Math.Atan2(Y, W);
    }
}
