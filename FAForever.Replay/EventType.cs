
namespace FAForever.Replay
{
    /// <summary>
    /// Order of the enumeration is relevant - it maps directly to the byte value that represents the command type in the replay binary file.
    /// </summary>
    public enum EventType
    {
        Advance,
        SetCommandSource,
        CommandSourceTerminated,
        VerifyChecksum,
        RequestPause,
        RequestResume,
        SingleStep,
        CreateUnit,
        CreateProp,
        DestroyEntity,
        WarpEntity,
        ProcessInfoPair,
        IssueCommand,
        IssueFactoryCommand,
        IncreaseCommandCount,
        DecreaseCommandCount,
        UpdateCommandTarget,
        UpdateCommandType,
        UpdateCommandParameters,
        RemoveFromCommandQueue,
        DebugCommand,
        ExecuteLuaInSim,
        Simcallback,
        EndGame
    }

}
