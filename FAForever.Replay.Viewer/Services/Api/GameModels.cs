namespace FAForever.Replay.Viewer.Services.Api;

/// <summary>
/// Search parameters for the vault. Empty fields are not filtered on.
/// </summary>
public sealed record GameSearchQuery
{
    public string PlayerName { get; init; } = string.Empty;

    public string MapName { get; init; } = string.Empty;

    /// <summary>Technical name of the featured mod (e.g. "faf"), or empty for any.</summary>
    public string FeaturedMod { get; init; } = string.Empty;

    /// <summary>Only games that actually finished (have an end time).</summary>
    public bool FinishedOnly { get; init; } = true;

    public DateOnly? PlayedAfter { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 12;
}

public sealed record GameSearchResult(IReadOnlyList<GameSummary> Games, int Page, int? TotalPages, int? TotalRecords);

public sealed record GameSummary(
    int Id,
    string Title,
    DateTimeOffset? StartTime,
    DateTimeOffset? EndTime,
    string? MapName,
    string? MapPreviewUrl,
    string? FeaturedMod,
    IReadOnlyList<GamePlayer> Players)
{
    public TimeSpan? Duration => StartTime is { } start && EndTime is { } end && end > start ? end - start : null;
}

public sealed record GamePlayer(string Login, int? Team, GameFaction? Faction, int? Rating, string? Result);

/// <summary>
/// Faction indices as used by the lobby and the API.
/// </summary>
public enum GameFaction
{
    Uef = 1,
    Aeon = 2,
    Cybran = 3,
    Seraphim = 4,
    Nomads = 5,
}
