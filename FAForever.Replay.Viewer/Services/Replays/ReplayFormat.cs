namespace FAForever.Replay.Viewer.Services.Replays;

public enum ReplayFormat
{
    FAForever,
    SCFA,
}

public static class ReplayFormats
{
    /// <summary>
    /// Detects the replay format from a file name, case-insensitively.
    /// Returns null when the extension is not a known replay format.
    /// </summary>
    public static ReplayFormat? FromFileName(string fileName)
    {
        if (fileName.EndsWith(".fafreplay", StringComparison.OrdinalIgnoreCase))
        {
            return ReplayFormat.FAForever;
        }

        if (fileName.EndsWith(".scfareplay", StringComparison.OrdinalIgnoreCase))
        {
            return ReplayFormat.SCFA;
        }

        return null;
    }
}
