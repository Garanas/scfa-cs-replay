namespace FAForever.Replay.Viewer.Services;

/// <summary>
/// Parses and formats in-game time for filter inputs and query parameters.
/// </summary>
public static class GameTime
{
    /// <summary>
    /// Parses game time as minutes ("12"), m:ss ("12:30") or h:mm:ss ("1:02:30").
    /// Returns null for empty or invalid input.
    /// </summary>
    public static TimeSpan? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string[] parts = text.Trim().Split(':');
        return parts.Length switch
        {
            1 when int.TryParse(parts[0], out int minutes) && minutes >= 0
                => TimeSpan.FromMinutes(minutes),
            2 when int.TryParse(parts[0], out int minutes) && int.TryParse(parts[1], out int seconds)
                && minutes >= 0 && seconds is >= 0 and < 60
                => new TimeSpan(0, minutes, seconds),
            3 when int.TryParse(parts[0], out int hours) && int.TryParse(parts[1], out int minutes) && int.TryParse(parts[2], out int seconds)
                && hours >= 0 && minutes is >= 0 and < 60 && seconds is >= 0 and < 60
                => new TimeSpan(hours, minutes, seconds),
            _ => null,
        };
    }

    public static string Format(TimeSpan time)
        => time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
}
