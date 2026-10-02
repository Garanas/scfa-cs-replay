namespace FAForever.Replay.Viewer.Services.Auth;

/// <summary>
/// OAuth settings, bound from the "OAuth" section of wwwroot/appsettings.json.
///
/// Note: the current client id belongs to the official FAF desktop client and is registered
/// with loopback redirect URIs only (no path). Development therefore runs on the fixed origin
/// http://127.0.0.1:5080 and uses that exact origin as redirect URI. See TODO.md.
/// </summary>
public sealed class OAuthOptions
{
    public string AuthorizationEndpoint { get; set; } = "https://hydra.faforever.com/oauth2/auth";

    /// <summary>
    /// Hydra's token endpoint sends no CORS headers, so the exchange goes through our server
    /// (relative path on the hosting origin).
    /// </summary>
    public string TokenProxyPath { get; set; } = "api/oauth/token";

    public string ClientId { get; set; } = string.Empty;

    public string Scopes { get; set; } = "openid offline public_profile";

    public string RedirectUri { get; set; } = string.Empty;
}
