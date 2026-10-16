
namespace FAForever.FileFormats.Replay
{
    public interface CommandFormation
    {
        public record NoFormation() : CommandFormation;

        /// <summary>
        /// An order given in formation.
        /// </summary>
        /// <param name="ScriptIndex">The index of the formation script; the wire stores -1 for an
        /// order without a formation (<see cref="NoFormation"/>).</param>
        /// <param name="Orientation">The orientation of the formation: a quaternion that turns about
        /// the vertical axis, see <see cref="ReplayOrientation.GetHeading"/>.</param>
        /// <param name="Scale">The scale of the formation; 1 in every measured order.</param>
        public record Formation(int ScriptIndex, ReplayOrientation Orientation, float Scale) : CommandFormation;
    }
}
