using System.Text.Json.Serialization;

namespace FAForever.Replay.Viewer.Services.Auth;

/// <summary>
/// The token response of Hydra's /oauth2/token endpoint.
/// </summary>
public sealed record TokenResponse
{
    [JsonPropertyName("access_token")] public string? AccessToken { get; init; }

    [JsonPropertyName("refresh_token")] public string? RefreshToken { get; init; }

    [JsonPropertyName("id_token")] public string? IdToken { get; init; }

    [JsonPropertyName("expires_in")] public int ExpiresIn { get; init; }

    [JsonPropertyName("error")] public string? Error { get; init; }

    [JsonPropertyName("error_description")] public string? ErrorDescription { get; init; }
}

/// <summary>
/// The authenticated session as persisted in session storage: it survives a refresh of the
/// tab, but not a new tab or a restart of the browser.
/// </summary>
public sealed record AuthSession
{
    public required string AccessToken { get; init; }

    public string? RefreshToken { get; init; }

    public string? IdToken { get; init; }

    /// <summary>Unix seconds at which <see cref="AccessToken"/> expires.</summary>
    public long ExpiresAt { get; init; }

    public string? UserId { get; init; }

    public string? UserName { get; init; }
}
