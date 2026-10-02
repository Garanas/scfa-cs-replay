using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.JSInterop;

namespace FAForever.Replay.Viewer.Services.Auth;

/// <summary>
/// Implements the OAuth2 authorization code flow with PKCE against FAForever's Hydra, by hand.
///
/// Why not Microsoft.AspNetCore.Components.WebAssembly.Authentication? Two reasons:
/// Hydra's token endpoint has no CORS headers (the exchange must go through our server proxy),
/// and the temporary client only accepts a redirect URI without a path, which does not fit the
/// library's fixed authentication/login-callback route. The flow is small enough to own.
/// </summary>
public sealed class AuthService(HttpClient http, IJSRuntime js, NavigationManager navigation, OAuthOptions options, IConfiguration configuration)
{
    private const string VerifierKey = "faf-oauth-verifier";
    private const string StateKey = "faf-oauth-state";
    private const string ReturnUrlKey = "faf-oauth-return";
    private const string SessionKey = "faf-oauth-session";

    /// <summary>Refresh the access token this long before it actually expires.</summary>
    private static readonly TimeSpan ExpiryMargin = TimeSpan.FromSeconds(30);

    public AuthSession? Session { get; private set; }

    public bool IsAuthenticated => Session is not null;

    /// <summary>The error of the last failed login attempt, for display on the page.</summary>
    public string? LastError { get; private set; }

    public event Action? Changed;

    /// <summary>
    /// Restores a persisted session and, when the current URL is an OAuth callback
    /// (?code=&amp;state=), completes the login. Called once at startup.
    /// </summary>
    public async Task InitializeAsync()
    {
        string? persisted = await js.InvokeAsync<string?>("fafReplay.sessionGet", SessionKey);
        if (persisted is not null)
        {
            try
            {
                Session = JsonSerializer.Deserialize<AuthSession>(persisted);
            }
            catch (JsonException)
            {
                await js.InvokeVoidAsync("fafReplay.sessionRemove", SessionKey);
            }
        }

        await CompleteLoginFromUrlAsync();
        Changed?.Invoke();
    }

    /// <summary>
    /// Starts the login: stores the PKCE verifier and state, then navigates to Hydra.
    /// </summary>
    public async Task BeginLoginAsync(string? returnUrl = null)
    {
        if (string.IsNullOrEmpty(options.ClientId) || string.IsNullOrEmpty(options.RedirectUri))
        {
            throw new InvalidOperationException("OAuth is not configured: set OAuth:ClientId and OAuth:RedirectUri in appsettings.json.");
        }

        LastError = null;
        string verifier = RandomUrlSafeString(32);
        string challenge = Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        string state = RandomUrlSafeString(16);

        await js.InvokeVoidAsync("fafReplay.sessionSet", VerifierKey, verifier);
        await js.InvokeVoidAsync("fafReplay.sessionSet", StateKey, state);
        await js.InvokeVoidAsync("fafReplay.sessionSet", ReturnUrlKey, returnUrl ?? navigation.ToBaseRelativePath(navigation.Uri));

        string url = options.AuthorizationEndpoint
            + "?response_type=code"
            + "&client_id=" + Uri.EscapeDataString(options.ClientId)
            + "&redirect_uri=" + Uri.EscapeDataString(options.RedirectUri)
            + "&scope=" + Uri.EscapeDataString(options.Scopes)
            + "&state=" + Uri.EscapeDataString(state)
            + "&code_challenge=" + challenge
            + "&code_challenge_method=S256";

        navigation.NavigateTo(url, forceLoad: true);
    }

    /// <summary>
    /// Forgets the local session. Hydra's own SSO cookie is not touched, so the next login
    /// may complete without a password prompt.
    /// </summary>
    public async Task LogoutAsync()
    {
        Session = null;
        await js.InvokeVoidAsync("fafReplay.sessionRemove", SessionKey);
        Changed?.Invoke();
    }

    /// <summary>
    /// Returns an access token that is valid for at least another 30 seconds, refreshing it
    /// when needed. Returns null when there is no (longer a) valid session.
    /// </summary>
    public async Task<string?> GetValidAccessTokenAsync()
    {
        if (Session is null)
        {
            return null;
        }

        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() < Session.ExpiresAt - (long)ExpiryMargin.TotalSeconds)
        {
            return Session.AccessToken;
        }

        return await RefreshAsync();
    }

    private async Task CompleteLoginFromUrlAsync()
    {
        Dictionary<string, string> query = ParseQuery(navigation.ToAbsoluteUri(navigation.Uri).Query);
        if (!query.ContainsKey("code") && !query.ContainsKey("error"))
        {
            return;
        }

        string? verifier = await js.InvokeAsync<string?>("fafReplay.sessionGet", VerifierKey);
        string? expectedState = await js.InvokeAsync<string?>("fafReplay.sessionGet", StateKey);
        string? returnUrl = await js.InvokeAsync<string?>("fafReplay.sessionGet", ReturnUrlKey);
        await js.InvokeVoidAsync("fafReplay.sessionRemove", VerifierKey);
        await js.InvokeVoidAsync("fafReplay.sessionRemove", StateKey);
        await js.InvokeVoidAsync("fafReplay.sessionRemove", ReturnUrlKey);

        if (verifier is null)
        {
            // Not a callback we initiated (e.g. a pasted URL); ignore it.
            return;
        }

        if (query.TryGetValue("error", out string? error))
        {
            LastError = query.GetValueOrDefault("error_description", error);
        }
        else if (!query.TryGetValue("state", out string? state) || state != expectedState)
        {
            LastError = "The login response did not match the login request (state mismatch).";
        }
        else
        {
            TokenResponse? tokens = await RequestTokensAsync(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = query["code"],
                ["redirect_uri"] = options.RedirectUri,
                ["client_id"] = options.ClientId,
                ["code_verifier"] = verifier,
            });

            if (tokens?.AccessToken is null)
            {
                LastError = tokens?.ErrorDescription ?? tokens?.Error ?? "The token exchange failed.";
            }
            else
            {
                await StoreSessionAsync(tokens);
            }
        }

        navigation.NavigateTo(returnUrl ?? string.Empty, replace: true);
    }

    private async Task<string?> RefreshAsync()
    {
        if (Session?.RefreshToken is not { } refreshToken)
        {
            await LogoutAsync();
            return null;
        }

        TokenResponse? tokens = await RequestTokensAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = options.ClientId,
        });

        if (tokens?.AccessToken is null)
        {
            // The refresh token was revoked or expired: the session is over.
            await LogoutAsync();
            return null;
        }

        await StoreSessionAsync(tokens);
        return Session?.AccessToken;
    }

    private async Task<TokenResponse?> RequestTokensAsync(Dictionary<string, string> form)
    {
        try
        {
            using HttpResponseMessage response = await http.PostAsync(options.TokenProxyPath, new FormUrlEncodedContent(form));
            return await response.Content.ReadFromJsonAsync<TokenResponse>();
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            return new TokenResponse { Error = "proxy_unreachable", ErrorDescription = "The token endpoint could not be reached." };
        }
    }

    private async Task StoreSessionAsync(TokenResponse tokens)
    {
        string? userId = tokens.IdToken is { } idToken ? ReadJwtClaim(idToken, "sub") : null;

        Session = new AuthSession
        {
            AccessToken = tokens.AccessToken!,
            RefreshToken = tokens.RefreshToken ?? Session?.RefreshToken,
            IdToken = tokens.IdToken ?? Session?.IdToken,
            ExpiresAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + tokens.ExpiresIn,
            UserId = userId ?? Session?.UserId,
            UserName = Session?.UserName,
        };

        if (Session.UserName is null)
        {
            Session = Session with { UserName = await FetchUserNameAsync(Session.AccessToken) };
        }

        await js.InvokeVoidAsync("fafReplay.sessionSet", SessionKey, JsonSerializer.Serialize(Session));
        Changed?.Invoke();
    }

    /// <summary>
    /// Best-effort lookup of the player name via the FAF API's /me endpoint.
    /// </summary>
    private async Task<string?> FetchUserNameAsync(string accessToken)
    {
        try
        {
            string apiBase = configuration["FafApi:BaseUrl"] ?? "https://api.faforever.com";
            using HttpRequestMessage request = new(HttpMethod.Get, $"{apiBase}/me");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            using HttpResponseMessage response = await http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (document.RootElement.TryGetProperty("data", out JsonElement data)
                && data.TryGetProperty("attributes", out JsonElement attributes)
                && attributes.TryGetProperty("userName", out JsonElement userName))
            {
                return userName.GetString();
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            // The name is a nicety; the session works without it.
        }

        return null;
    }

    private static string? ReadJwtClaim(string jwt, string claim)
    {
        try
        {
            string[] segments = jwt.Split('.');
            if (segments.Length < 2)
            {
                return null;
            }

            string payload = segments[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + ((4 - payload.Length % 4) % 4), '=');
            using JsonDocument document = JsonDocument.Parse(Convert.FromBase64String(payload));
            return document.RootElement.TryGetProperty(claim, out JsonElement value) ? value.ToString() : null;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        Dictionary<string, string> values = [];
        foreach (string pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = pair.IndexOf('=');
            string key = separator < 0 ? pair : pair[..separator];
            string value = separator < 0 ? string.Empty : pair[(separator + 1)..];
            values[Uri.UnescapeDataString(key)] = Uri.UnescapeDataString(value.Replace('+', ' '));
        }

        return values;
    }

    private static string RandomUrlSafeString(int bytes)
    {
        byte[] buffer = new byte[bytes];
        RandomNumberGenerator.Fill(buffer);
        return Base64UrlEncode(buffer);
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
