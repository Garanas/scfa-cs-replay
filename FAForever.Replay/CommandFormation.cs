
namespace FAForever.Replay
{
    public interface CommandFormation
    {
        public record NoFormation(): CommandFormation;

        public record Formation(int FormationIdentifier, float Heading, float X, float Y, float Z, float Scale): CommandFormation;
    }
}
