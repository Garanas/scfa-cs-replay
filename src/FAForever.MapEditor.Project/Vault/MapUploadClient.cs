using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FAForever.MapEditor.Project.Vault
{
    /// <summary>
    /// What the upload says about the map besides its zip (<c>MapUploadMetadata</c> of the API).
    /// </summary>
    /// <param name="IsRanked">Whether the map may be played in rated games.</param>
    /// <param name="LicenseId">The vault's licence id; null for the vault's default.</param>
    public sealed record MapUploadMetadata(
        [property: JsonPropertyName("isRanked")] bool IsRanked,
        [property: JsonPropertyName("licenseId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? LicenseId = null);

    /// <summary>
    /// One error the API answered (JSON:API's error object).
    /// </summary>
    /// <param name="Code">The API's number as text, such as <c>107</c> (<c>MAP_VERSION_EXISTS</c>).</param>
    public sealed record VaultError(string? Code, string? Title, string? Detail)
    {
        public override string ToString() => Detail is { Length: > 0 } detail ? (Title is { Length: > 0 } title ? $"{title}: {detail}" : detail) : Title ?? $"error {Code}";
    }

    /// <summary>
    /// The vault refused the upload or could not be reached.
    /// </summary>
    /// <param name="Status">The HTTP status; null when no answer came.</param>
    public sealed class VaultUploadException(string message, HttpStatusCode? status, IReadOnlyList<VaultError> errors) : Exception(message)
    {
        public HttpStatusCode? Status { get; } = status;

        public IReadOnlyList<VaultError> Errors { get; } = errors;
    }

    /// <summary>
    /// Uploads a map's zip to FAForever's API (user story M-8), as the FAF client does
    /// (<c>FafApiAccessor.uploadMap</c>): <c>POST {api}/maps/upload</c>, multipart with the zip as
    /// the part <c>file</c> (its file name ending in <c>.zip</c>) and <c>metadata</c>, JSON
    /// (<c>{"isRanked": false}</c>), with the bearer token of a person signed in with the scope
    /// <c>upload_map</c>. The API answers 200 with no body, or 422 with JSON:API errors.
    /// </summary>
    public static class MapUploadClient
    {
        /// <summary>FAForever's API: the real vault.</summary>
        public static Uri FafApi { get; } = new Uri("https://api.faforever.com/");

        /// <summary>
        /// Whether <paramref name="api"/> is FAForever's own (any host under <c>faforever.com</c>).
        /// </summary>
        public static bool IsRealVault(Uri api) =>
            api.Host.Equals("faforever.com", StringComparison.OrdinalIgnoreCase) || api.Host.EndsWith(".faforever.com", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The upload endpoint of an API.
        /// </summary>
        public static Uri UploadUrl(Uri api) => new Uri(new Uri(api.ToString().TrimEnd('/') + "/"), "maps/upload");

        /// <summary>
        /// Uploads the zip.
        /// </summary>
        /// <param name="progress">The share of the zip sent, 0 to 1.</param>
        /// <exception cref="VaultUploadException">The API refused it, or could not be reached.</exception>
        public static async Task UploadAsync(HttpClient http, Uri api, string accessToken, byte[] zip, string fileName, MapUploadMetadata metadata,
            IProgress<double>? progress, CancellationToken cancellationToken)
        {
            using MultipartFormDataContent content = new MultipartFormDataContent();
            ProgressContent file = new ProgressContent(zip, progress);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
            content.Add(file, "file", fileName);
            StringContent json = new StringContent(JsonSerializer.Serialize(metadata), Encoding.UTF8, "application/json");
            content.Add(json, "metadata");

            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, UploadUrl(api)) { Content = content };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException exception)
            {
                throw new VaultUploadException($"The vault could not be reached: {exception.Message}", null, []);
            }
            using (response)
            {
                if (response.IsSuccessStatusCode)
                {
                    progress?.Report(1);
                    return;
                }
                string body = await response.Content.ReadAsStringAsync(cancellationToken);
                IReadOnlyList<VaultError> errors = ParseErrors(body);
                string reason = errors.Count > 0 ? string.Join("; ", errors)
                    : response.StatusCode switch
                    {
                        HttpStatusCode.Unauthorized => "the sign-in is not accepted (sign in again)",
                        HttpStatusCode.Forbidden => "not allowed: the account may lack the right to upload maps, or be banned from the vault",
                        HttpStatusCode.RequestEntityTooLarge => "the map is too large",
                        _ => $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}",
                    };
                throw new VaultUploadException($"The vault refused the map: {reason}", response.StatusCode, errors);
            }
        }

        /// <summary>
        /// The errors of a JSON:API answer (<c>{"errors": [{"code", "title", "detail"}]}</c>); none
        /// when it is not one.
        /// </summary>
        public static IReadOnlyList<VaultError> ParseErrors(string body)
        {
            try
            {
                using JsonDocument json = JsonDocument.Parse(body);
                if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("errors", out JsonElement errors) || errors.ValueKind != JsonValueKind.Array)
                {
                    return [];
                }
                return [.. errors.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object).Select(e => new VaultError(Text(e, "code"), Text(e, "title"), Text(e, "detail")))];
            }
            catch (JsonException)
            {
                return [];
            }

            static string? Text(JsonElement element, string name) => element.TryGetProperty(name, out JsonElement value)
                ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString()
                : null;
        }

        /// <summary>
        /// Bytes sent in pieces, saying how far it is.
        /// </summary>
        private sealed class ProgressContent(byte[] bytes, IProgress<double>? progress) : HttpContent
        {
            private const int Piece = 64 * 1024;

            protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            {
                for (int offset = 0; offset < bytes.Length; offset += Piece)
                {
                    int length = Math.Min(Piece, bytes.Length - offset);
                    await stream.WriteAsync(bytes.AsMemory(offset, length));
                    progress?.Report((double)(offset + length) / Math.Max(1, bytes.Length));
                }
            }

            protected override bool TryComputeLength(out long length)
            {
                length = bytes.Length;
                return true;
            }
        }
    }
}
