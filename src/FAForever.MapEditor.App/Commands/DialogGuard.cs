using FAForever.MapEditor.App.ViewModels;

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
}
