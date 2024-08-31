
namespace FAForever.Replay
{
    public interface EventCommandFormation
    {
        public record NoFormation(): EventCommandFormation;

        public record Formation(int FormationIdentifier, float Heading, float X, float Y, float Z, float Scale): EventCommandFormation;
    }
}
