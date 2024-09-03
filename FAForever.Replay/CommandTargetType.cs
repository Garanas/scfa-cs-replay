
namespace FAForever.Replay
{
    /// <summary>
    /// An enumeration that maps 1-to-1 with the byte that represents the target type of a command in the binary.
    /// </summary>
    public enum CommandTargetType
    {
        None = 0,
        Entity = 1,
        Position = 2,
    };
}
