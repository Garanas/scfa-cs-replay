using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// The editor's questions, refused while a command runs for automation or a plugin: they give
/// the command's arguments instead, and nobody is there to answer a dialog.
/// </summary>
public sealed class DialogGuard(IEditorDialogs dialogs) : IEditorDialogs
{
    /// <summary>
    /// Whether a question would go unanswered: set by the registry while it runs a command
    /// without a person (<see cref="CommandRegistry.RunAsync(string, System.Text.Json.Nodes.JsonObject?, bool)"/>).
    /// </summary>
    public bool Quiet { get; set; }

    public Task<string?> PickFolderAsync(string title, string? startIn = null) => Quiet
        ? throw new InvalidOperationException($"The command would ask: \"{title}\". Give the folder as an argument")
        : dialogs.PickFolderAsync(title, startIn);

    public Task<SaveChoice> AskToSaveChangesAsync(string mapName) => Quiet
        ? throw new InvalidOperationException($"The command would ask whether to save the changes to {mapName}: save first, or give the folder to open")
        : dialogs.AskToSaveChangesAsync(mapName);

    public Task<string?> PickAssetAsync(AssetKind kind, string? title = null, string? current = null) => Quiet
        ? throw new InvalidOperationException($"The command would ask for {(kind == AssetKind.Unit ? "a unit" : "an asset")} in the picker. Give its key as the argument key: {KeyHint(kind)}")
        : dialogs.PickAssetAsync(kind, title, current);

    public Task<string?> PickFileAsync(string title, IReadOnlyList<string> extensions, string? startIn = null) => Quiet
        ? throw new InvalidOperationException($"The command would ask: \"{title}\". Give the file as the argument path")
        : dialogs.PickFileAsync(title, extensions, startIn);

    public Task<string?> PickSaveFileAsync(string title, string suggestedName, IReadOnlyList<string> extensions, string? startIn = null) => Quiet
        ? throw new InvalidOperationException($"The command would ask: \"{title}\". Give the file as the argument path")
        : dialogs.PickSaveFileAsync(title, suggestedName, extensions, startIn);

    /// <summary>
    /// What a key of the kind looks like, for the message that asks for one.
    /// </summary>
    private static string KeyHint(AssetKind kind) => kind switch
    {
        AssetKind.Unit => "a unit's id, such as uec1101",
        AssetKind.Prop => "a prop's blueprint, such as env/evergreen/props/trees/pine06_prop.bp",
        AssetKind.Brush => "a brush's path, such as brushes/craters/crater01.png",
        _ => "a texture's game path, such as env/tundra/layers/tund_rock_albedo.dds",
    };
}
