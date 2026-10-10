using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace FAForever.MapEditor.Project.Vault
{
    /// <summary>
    /// Where FAForever's sign-in (Ory Hydra, OAuth 2 with PKCE) is, and the client the editor signs
    /// in as.
    /// </summary>
    /// <param name="AuthorizationEndpoint">The page the system's browser opens.</param>
    /// <param name="TokenEndpoint">Where the code is exchanged for tokens.</param>
    /// <param name="ClientId">A public client (no secret) that accepts loopback redirects.</param>
    /// <param name="Scopes">What the editor asks to do; <c>upload_map</c> to upload.</param>
    public sealed record FafOAuthSettings(Uri AuthorizationEndpoint, Uri TokenEndpoint, string ClientId, string Scopes)
    {
        /// <summary>
        /// FAForever's own: the official FAF client's id (the repository's development client,
        /// which takes a redirect to <c>http://127.0.0.1:&lt;any port&gt;</c>), asking to upload maps.
        /// </summary>
        public static FafOAuthSettings Faf { get; } = new FafOAuthSettings(
            new Uri("https://hydra.faforever.com/oauth2/auth"),
            new Uri("https://hydra.faforever.com/oauth2/token"),
            "2e8808cf-5889-469b-b2c3-01f0cc58c4af",
            "openid offline public_profile upload_map");
    }

    /// <summary>
    /// The tokens of a signed-in person. The editor keeps them in memory only.
    /// </summary>
    /// <param name="ExpiresAt">When the access token stops working.</param>
    public sealed record FafTokens(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAt)
    {
        /// <summary>
        /// Whether the access token works for at least another half minute.
        /// </summary>
        public bool IsValid(DateTimeOffset now) => now < ExpiresAt - TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Signing in to FAForever from a desktop application as RFC 8252 describes it: the system's
    /// browser opens Hydra's page, which sends the browser back to a listener of this process on
    /// the loopback address (<c>http://127.0.0.1:&lt;free port&gt;/</c>) with a code, which is
    /// exchanged for tokens with the PKCE verifier (S256). No secret is involved and nothing is
    /// written to disk.
    /// </summary>
    public static class FafSignIn
    {
        /// <summary>
        /// A random string of URL-safe characters from <paramref name="bytes"/> random bytes.
        /// </summary>
        public static string RandomUrlSafe(int bytes) => Base64Url(RandomNumberGenerator.GetBytes(bytes));

        /// <summary>
        /// The PKCE challenge of a verifier: the base64url of its SHA-256.
        /// </summary>
        public static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        /// <summary>
        /// The page to open in the browser.
        /// </summary>
        public static Uri AuthorizeUrl(FafOAuthSettings settings, string redirectUri, string state, string verifier) => new Uri(
            settings.AuthorizationEndpoint
            + "?response_type=code"
            + "&client_id=" + Uri.EscapeDataString(settings.ClientId)
            + "&redirect_uri=" + Uri.EscapeDataString(redirectUri)
            + "&scope=" + Uri.EscapeDataString(settings.Scopes)
            + "&state=" + Uri.EscapeDataString(state)
            + "&code_challenge=" + Challenge(verifier)
            + "&code_challenge_method=S256");

        /// <summary>
        /// A port of the loopback address that is free now.
        /// </summary>
        public static int FreePort()
        {
            using TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        /// <summary>
        /// Signs in: opens the browser on Hydra's page (through <paramref name="openBrowser"/>), waits
        /// for the redirect to the loopback listener and exchanges its code for tokens.
        /// </summary>
        /// <param name="openBrowser">Opens a page in the system's browser.</param>
        /// <exception cref="FafSignInException">The person refused, the answer did not match, or the exchange failed.</exception>
        /// <exception cref="OperationCanceledException">Cancelled, or no answer in time.</exception>
        public static async Task<FafTokens> SignInAsync(FafOAuthSettings settings, HttpClient http, Action<Uri> openBrowser, CancellationToken cancellationToken)
        {
            int port = FreePort();
            string redirectUri = $"http://127.0.0.1:{port}";
            string verifier = RandomUrlSafe(32);
            string state = RandomUrlSafe(16);

            using HttpListener listener = new HttpListener();
            listener.Prefixes.Add($"{redirectUri}/");
            listener.Start();
            openBrowser(AuthorizeUrl(settings, redirectUri, state, verifier));

            string code = await ReceiveCodeAsync(listener, state, cancellationToken);
            return await RequestTokensAsync(settings, http, new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = redirectUri,
                ["client_id"] = settings.ClientId,
                ["code_verifier"] = verifier,
            }, cancellationToken);
        }

        /// <summary>
        /// New tokens for a refresh token.
        /// </summary>
        /// <exception cref="FafSignInException">The refresh token no longer works.</exception>
        public static Task<FafTokens> RefreshAsync(FafOAuthSettings settings, HttpClient http, string refreshToken, CancellationToken cancellationToken) =>
            RequestTokensAsync(settings, http, new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = settings.ClientId,
            }, cancellationToken);

        /// <summary>
        /// Waits for the browser's redirect and answers it with a page that says to go back to the editor.
        /// </summary>
        internal static async Task<string> ReceiveCodeAsync(HttpListener listener, string state, CancellationToken cancellationToken)
        {
            while (true)
            {
                HttpListenerContext context = await listener.GetContextAsync().WaitAsync(cancellationToken);
                System.Collections.Specialized.NameValueCollection query = context.Request.QueryString;
                string? code = query["code"];
                string? error = query["error"];
                if (code is null && error is null)
                {
                    // the browser asking for a favicon and the like
                    context.Response.StatusCode = 404;
                    context.Response.Close();
                    continue;
                }
                bool matches = query["state"] == state;
                string message = error is not null ? $"Signing in failed: {query["error_description"] ?? error}"
                    : !matches ? "Signing in failed: the answer did not belong to this sign-in."
                    : "Signed in. You can close this page and go back to the map editor.";
                byte[] page = Encoding.UTF8.GetBytes($"<!doctype html><html><head><meta charset=\"utf-8\"><title>FAF map editor</title></head><body><p>{WebUtility.HtmlEncode(message)}</p></body></html>");
                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.ContentLength64 = page.Length;
                await context.Response.OutputStream.WriteAsync(page, cancellationToken);
                context.Response.Close();
                if (error is not null)
                {
                    throw new FafSignInException($"Signing in failed: {query["error_description"] ?? error}");
                }
                if (!matches)
                {
                    throw new FafSignInException("The sign-in's answer did not match its request (state)");
                }
                return code!;
            }
        }

        private static async Task<FafTokens> RequestTokensAsync(FafOAuthSettings settings, HttpClient http, Dictionary<string, string> form, CancellationToken cancellationToken)
        {
            using HttpResponseMessage response = await http.PostAsync(settings.TokenEndpoint, new FormUrlEncodedContent(form), cancellationToken);
            TokenResponse? tokens = null;
            try
            {
                tokens = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
            }
            catch (System.Text.Json.JsonException)
            {
            }
            if (!response.IsSuccessStatusCode || tokens?.AccessToken is null)
            {
                throw new FafSignInException($"Signing in failed: {tokens?.ErrorDescription ?? tokens?.Error ?? $"HTTP {(int)response.StatusCode}"}");
            }
            return new FafTokens(tokens.AccessToken, tokens.RefreshToken, DateTimeOffset.UtcNow.AddSeconds(tokens.ExpiresIn ?? 3600));
        }

        private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private sealed record TokenResponse
        {
            [JsonPropertyName("access_token")]
            public string? AccessToken { get; init; }

            [JsonPropertyName("refresh_token")]
            public string? RefreshToken { get; init; }

            [JsonPropertyName("expires_in")]
            public int? ExpiresIn { get; init; }

            [JsonPropertyName("error")]
            public string? Error { get; init; }

            [JsonPropertyName("error_description")]
            public string? ErrorDescription { get; init; }
        }
    }

    /// <summary>
    /// Signing in to FAForever failed.
    /// </summary>
    public sealed class FafSignInException(string message) : Exception(message);
}
