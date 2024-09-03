
namespace FAForever.Replay
{
    /// <summary>
    /// Order of the enumeration is relevant - it maps directly to the byte value that represents the command type in the replay binary file.
    /// </summary>
    public enum ReplayInputType
    {
        Advance = 0,
        SetCommandSource = 1,
        CommandSourceTerminated = 2,
        VerifyChecksum = 3,
        RequestPause = 4,
        RequestResume = 5,
        SingleStep = 6,
        CreateUnit = 7,
        CreateProp = 8,
        DestroyEntity = 9,
        WarpEntity = 10,
        ProcessInfoPair = 11,
        IssueCommand = 12,
        IssueFactoryCommand = 13,
        IncreaseCommandCount = 14,
        DecreaseCommandCount = 15,
        UpdateCommandTarget = 16,
        UpdateCommandType = 17,
        UpdateCommandParameters = 18,
        RemoveFromCommandQueue = 19,
        DebugCommand = 20,
        ExecuteLuaInSim = 21,
        Simcallback = 22,
        EndGame = 23
    }

}
