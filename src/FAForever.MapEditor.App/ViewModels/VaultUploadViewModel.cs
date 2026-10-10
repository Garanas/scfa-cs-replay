using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Checks;
using FAForever.MapEditor.Project.Vault;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The upload of the open map to FAForever's vault (user story M-8): the checks, the export of the
/// next version, the zip, the person's yes, the sign-in and the upload. The tokens of the sign-in
/// are kept in memory only, per API, and gone when the editor closes: nothing secret is written.
/// </summary>
/// <remarks>
/// Without a person (automation, plugins) it uploads only to another server than FAForever's,
/// named as the argument <c>server</c>, and only with <c>confirm: true</c>, so a script can never
/// upload to the real vault, by accident or otherwise.
/// </remarks>
public sealed partial class VaultUploadViewModel(MainViewModel model) : ObservableObject
{
    private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };

    private readonly Dictionary<string, FafTokens> _tokens = new Dictionary<string, FafTokens>(StringComparer.OrdinalIgnoreCase);

    private ILogger Log => model.Logger("Vault");

    /// <summary>
    /// Where the sign-in is; FAForever's unless a test sets another.
    /// </summary>
    public FafOAuthSettings OAuth { get; set; } = FafOAuthSettings.Faf;

    /// <summary>
    /// Where the upload is: <c>Checking</c>, <c>Exporting</c>, <c>Confirming</c>, <c>SigningIn</c>,
    /// <c>Uploading</c>, then <c>Uploaded</c>, <c>Cancelled</c> or <c>Failed</c>; <c>Idle</c> before the first.
    /// </summary>
    [ObservableProperty]
    public partial string Stage { get; private set; } = "Idle";

    /// <summary>
    /// The share of the zip sent, 0 to 1.
    /// </summary>
    [ObservableProperty]
    public partial double Progress { get; private set; }

    /// <summary>
    /// What the last upload came to, in words.
    /// </summary>
    [ObservableProperty]
    public partial string? LastResult { get; private set; }

    /// <summary>
    /// The errors the vault answered the last time, each with its code.
    /// </summary>
    public IReadOnlyList<VaultError> LastErrors { get; private set; } = [];

    /// <summary>
    /// Whether the editor holds a sign-in to FAForever's own API that still works.
    /// </summary>
    public bool IsSignedIn => _tokens.TryGetValue(MapUploadClient.FafApi.ToString(), out FafTokens? tokens) && tokens.IsValid(DateTimeOffset.UtcNow);

    /// <summary>
    /// Forgets every sign-in.
    /// </summary>
    public void SignOut() => _tokens.Clear();

    /// <summary>
    /// Its state for automation's <c>state</c>.
    /// </summary>
    public JsonObject Describe() => new JsonObject
    {
        ["stage"] = Stage,
        ["progress"] = Math.Round(Progress, 3),
        ["lastResult"] = LastResult,
        ["lastErrors"] = new JsonArray([.. LastErrors.Select(e => (JsonNode)new JsonObject { ["code"] = e.Code, ["title"] = e.Title, ["detail"] = e.Detail })]),
        ["signedIn"] = IsSignedIn,
        ["publishable"] = model.Map?.Document.IsPublishable,
        ["nextVersion"] = model.Map?.Document.NextVersion,
    };

    /// <summary>
    /// Uploads the open map as its next version.
    /// </summary>
    /// <param name="dialogs">The questions (refused without a person).</param>
    /// <param name="interactive">Whether a person runs it (the menu, the palette).</param>
    /// <param name="server">The API to upload to; FAForever's when left out (a person only).</param>
    /// <param name="confirm">The yes a person gives in the dialog, given beforehand (automation only).</param>
    /// <param name="token">An access token for <paramref name="server"/> (another server than FAForever's only).</param>
    /// <returns>What was uploaded, or <c>uploaded: false</c> when the person said no.</returns>
    /// <exception cref="InvalidOperationException">The map may not or cannot go to the vault; the message says why.</exception>
    /// <exception cref="VaultUploadException">The vault refused it.</exception>
    public async Task<JsonObject> UploadAsync(IEditorDialogs dialogs, bool interactive, string? server = null, bool confirm = false, string? token = null, bool ranked = false)
    {
        EditedMap map = model.Map ?? throw new InvalidOperationException("No map is open");
        Uri api = server is null ? MapUploadClient.FafApi
            : Uri.TryCreate(server, UriKind.Absolute, out Uri? parsed) && parsed.Scheme is "http" or "https" ? parsed
            : throw new ArgumentException($"'{server}' is no address of a server (http://127.0.0.1:5000/)");
        bool real = MapUploadClient.IsRealVault(api);
        LastErrors = [];
        try
        {
            Stage = "Checking";
            if (!map.Document.IsPublishable)
            {
                throw new InvalidOperationException("This is the sandbox: it is for trying the editor and never goes to the vault. Start a map of your own (File, New map) to upload");
            }
            if (!interactive && real)
            {
                throw new InvalidOperationException("Without a person the editor never uploads to FAForever's vault: give the argument server with a test server");
            }
            if (!interactive && (server is null || !confirm))
            {
                throw new InvalidOperationException("Without a person an upload needs the arguments server (a test server) and confirm: true");
            }
            if (token is not null && real)
            {
                throw new InvalidOperationException("A token is taken only for a test server; FAForever's vault needs a sign-in");
            }

            // the map is checked first, as before an export, and its errors stop the upload (F-14)
            IReadOnlyList<Problem> errors = [.. model.Problems.Run().Where(problem => problem.Severity == ProblemSeverity.Error)];
            if (errors.Count > 0)
            {
                model.Dock = DockTab.Problems;
                throw new InvalidOperationException($"Fix the map's {errors.Count} error{(errors.Count == 1 ? "" : "s")} before uploading (Problems): {string.Join("; ", errors.Take(3).Select(e => e.Message))}");
            }
            int version = map.Document.NextVersion;
            IReadOnlyList<string> refused = VaultRules.Check(map.Document, version);
            if (refused.Count > 0)
            {
                throw new InvalidOperationException($"The vault would refuse the map: {string.Join("; ", refused)}");
            }

            // the next version, exported apart: the map keeps its own version until the vault has it
            Stage = "Exporting";
            string staging = Path.Combine(Path.GetTempPath(), "faf-map-editor-upload", Guid.NewGuid().ToString("N"));
            try
            {
                string folder = await model.ExportAsync(staging, version);
                IReadOnlyList<string> missing = VaultArchive.MissingFiles(folder, map.Document.Scenario.AdaptiveMap);
                if (missing.Count > 0)
                {
                    throw new InvalidOperationException($"The vault wants files the map does not have: {string.Join(", ", missing.Select(ending => map.Document.FileName + ending))}");
                }
                byte[] zip = await Task.Run(() => VaultArchive.Create(folder));
                string fileName = VaultArchive.FileName(folder);
                if (zip.LongLength > VaultRules.MaxUploadBytes)
                {
                    throw new InvalidOperationException($"The map's zip is {Size(zip.LongLength)}; the vault takes at most {Size(VaultRules.MaxUploadBytes)}");
                }
                string name = map.Document.Scenario.Name;
                string what = $"{name}, version {version} ({fileName}, {Size(zip.LongLength)})";

                Stage = "Confirming";
                if (interactive && !confirm)
                {
                    bool yes = await dialogs.ConfirmAsync("Upload to the vault",
                        $"Upload {what} to {(real ? "FAForever's vault" : api.ToString())}? Everyone will be able to download and play it, and a version in the vault cannot be replaced: changes need the next version.",
                        "Upload");
                    if (!yes)
                    {
                        Stage = "Cancelled";
                        LastResult = $"Not uploaded: {what}";
                        model.Status = "The upload was cancelled; the map is unchanged";
                        return new JsonObject { ["uploaded"] = false, ["name"] = name, ["version"] = version, ["bytes"] = zip.LongLength };
                    }
                }

                string access = token ?? await AccessTokenAsync(api, dialogs, interactive);
                Stage = "Uploading";
                Progress = 0;
                int shown = -1;
                Progress<double> progress = new Progress<double>(share =>
                {
                    Progress = share;
                    int percent = (int)(share * 100);
                    if (percent != shown && Stage == "Uploading")
                    {
                        shown = percent;
                        model.Status = $"Uploading {name} version {version}: {percent} %";
                    }
                });
                try
                {
                    await model.BusyAsync("upload", $"Uploading {what}...", () =>
                        MapUploadClient.UploadAsync(Http, api, access, zip, fileName, new MapUploadMetadata(ranked), progress, CancellationToken.None));
                }
                catch (VaultUploadException exception)
                {
                    LastErrors = exception.Errors;
                    if (exception.Status == System.Net.HttpStatusCode.Unauthorized)
                    {
                        _tokens.Remove(api.ToString());
                    }
                    throw;
                }

                // the vault has it: the map goes on from that version, as an export with raise does
                model.EditMap($"Raise the version to {version} (uploaded to the vault)", document => document with { Version = version });
                Stage = "Uploaded";
                LastResult = $"Uploaded {what} to {api}";
                model.Status = $"Uploaded {name} version {version} to {(real ? "the vault" : api.ToString())}; the map is now version {version}";
                Log.LogInformation("Uploaded {Map} version {Version} ({Bytes} bytes) to {Api}", name, version, zip.LongLength, api.ToString());
                return new JsonObject
                {
                    ["uploaded"] = true,
                    ["name"] = name,
                    ["version"] = version,
                    ["file"] = fileName,
                    ["bytes"] = zip.LongLength,
                    ["server"] = api.ToString(),
                };
            }
            finally
            {
                try
                {
                    if (Directory.Exists(staging))
                    {
                        Directory.Delete(staging, recursive: true);
                    }
                }
                catch (IOException exception)
                {
                    Log.LogWarning(exception, "The upload's temporary folder {Folder} could not be removed: {Reason}", staging, exception.Message);
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Stage = "Failed";
            LastResult = exception.Message;
            Log.LogError(exception, "Uploading {Map} failed: {Reason}", map.Document.Scenario.Name, exception.Message);
            throw;
        }
    }

    /// <summary>
    /// A working access token for <paramref name="api"/>: the one held, a refreshed one, or a new
    /// sign-in in the browser (a person only).
    /// </summary>
    private async Task<string> AccessTokenAsync(Uri api, IEditorDialogs dialogs, bool interactive)
    {
        string key = api.ToString();
        if (_tokens.TryGetValue(key, out FafTokens? held))
        {
            if (held.IsValid(DateTimeOffset.UtcNow))
            {
                return held.AccessToken;
            }
            if (held.RefreshToken is string refresh)
            {
                try
                {
                    FafTokens refreshed = await FafSignIn.RefreshAsync(OAuth, Http, refresh, CancellationToken.None);
                    _tokens[key] = refreshed;
                    return refreshed.AccessToken;
                }
                catch (FafSignInException exception)
                {
                    Log.LogWarning("The sign-in could not be renewed, signing in again: {Reason}", exception.Message);
                    _tokens.Remove(key);
                }
            }
        }
        if (!interactive)
        {
            throw new InvalidOperationException("Signing in needs a person: give the argument token for the test server");
        }
        Stage = "SigningIn";
        model.Status = "Sign in to FAForever in the browser that opened, then come back here";
        using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        FafTokens tokens = await FafSignIn.SignInAsync(OAuth, Http, page => _ = dialogs.OpenInBrowserAsync(page), timeout.Token);
        _tokens[key] = tokens;
        Log.LogInformation("Signed in to FAForever for the upload");
        return tokens.AccessToken;
    }

    private static string Size(long bytes) => bytes >= 1024 * 1024 ? $"{bytes / (1024.0 * 1024):0.0} MB" : $"{Math.Max(1, bytes / 1024)} KB";
}
