using System.Text.Json.Nodes;
using FAForever.MapEditor.App.ViewModels;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Uploads the open map to FAForever's vault as its next version (user story M-8): refused for a
/// map that is not publishable (the sandbox) or that has errors (Problems) or that the vault's own
/// rules would refuse; then the next version is exported apart and zipped, the person says yes to
/// what goes (name, version, size), signs in in the browser when needed, and the zip goes with
/// progress in the status line. The map's own version becomes the uploaded one only when the vault
/// has it. The status line shows the result or the vault's errors.
/// </summary>
/// <param name="Server">The API to upload to, for tests (<c>http://127.0.0.1:5000/</c>); left out,
/// FAForever's, which only a person can upload to.</param>
/// <param name="Confirm">Yes given beforehand; without a person the upload needs it and a server.</param>
/// <param name="Token">An access token for a test server (never FAForever's).</param>
/// <param name="Ranked">Whether the map may be played in rated games.</param>
/// <returns>What was uploaded (name, version, file, bytes, server), or <c>uploaded: false</c>.</returns>
[EditorCommand("file.upload", "Upload to the vault...", Story = "M-8", Needs = Needs.Map | Needs.Idle | Needs.NoStroke)]
public sealed record Upload(string? Server = null, bool Confirm = false, string? Token = null, bool Ranked = false) : IRequest<JsonObject>;

public sealed class UploadHandler(VaultUploadViewModel upload, IEditorDialogs dialogs) : IRequestHandler<Upload, JsonObject>
{
    public Task<JsonObject> Handle(Upload request, CancellationToken cancellationToken) =>
        upload.UploadAsync(dialogs, interactive: dialogs is not DialogGuard { Quiet: true }, request.Server, request.Confirm, request.Token, request.Ranked);
}
