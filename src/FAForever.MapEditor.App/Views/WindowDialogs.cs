using Avalonia.Controls;
using Avalonia.Platform.Storage;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.Views;

/// <summary>
/// The editor's questions: files and folders with the dialogs of the operating system, the rest
/// over the window (<see cref="QuestionViewModel"/>, the picker of an asset).
/// </summary>
public sealed class WindowDialogs(Window window) : IEditorDialogs
{
    public async Task<string?> PickFolderAsync(string title, string? startIn = null)
    {
        IReadOnlyList<IStorageFolder> folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = startIn is { Length: > 0 } && Directory.Exists(startIn)
                ? await window.StorageProvider.TryGetFolderFromPathAsync(startIn)
                : null,
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    /// <summary>
    /// Save, Don't save or Cancel, asked over the window (<see cref="QuestionViewModel"/>); Save has the keyboard first.
    /// </summary>
    public async Task<SaveChoice> AskToSaveChangesAsync(string mapName)
    {
        if (window.DataContext is not MainViewModel model)
        {
            return SaveChoice.Cancel;
        }
        int answer = await model.Question.AskAsync("Unsaved changes", $"Save the changes to {mapName}?", ["Save", "Don't save", "Cancel"], cancel: 2, focus: 0);
        return answer switch
        {
            0 => SaveChoice.Save,
            1 => SaveChoice.Discard,
            _ => SaveChoice.Cancel,
        };
    }

    /// <summary>
    /// The message with the action's button and Cancel, asked over the window; Cancel has the keyboard first.
    /// </summary>
    public async Task<bool> ConfirmAsync(string title, string message, string action) =>
        window.DataContext is MainViewModel model
        && await model.Question.AskAsync(title, message, [action, "Cancel"], cancel: 1, focus: 1) == 0;

    /// <summary>
    /// The message with a text field, the buttons and Cancel, asked over the window; the field has
    /// the keyboard first and Enter in it presses the first button.
    /// </summary>
    public async Task<(int Button, string Text)?> AskTextAsync(string title, string message, string label, string text, IReadOnlyList<string> buttons)
    {
        if (window.DataContext is not MainViewModel model)
        {
            return null;
        }
        (int button, string answer) = await model.Question.AskWithTextAsync(title, message, label, text, [.. buttons, "Cancel"], cancel: buttons.Count);
        return button < buttons.Count ? (button, answer) : null;
    }

    /// <summary>
    /// The system's browser, through the shell.
    /// </summary>
    public Task OpenInBrowserAsync(Uri page)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(page.ToString()) { UseShellExecute = true })?.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Shows the picker over the window (<c>AssetPicker</c>), which the window's view model holds.
    /// </summary>
    public Task<string?> PickAssetAsync(AssetKind kind, string? title = null, string? current = null) =>
        window.DataContext is MainViewModel model
            ? model.Picker.PickAsync(kind, title, current)
            : Task.FromResult<string?>(null);

    public async Task<string?> PickFileAsync(string title, IReadOnlyList<string> extensions, string? startIn = null)
    {
        IReadOnlyList<IStorageFile> files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [Filter(extensions)],
            SuggestedStartLocation = await StartIn(startIn),
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickSaveFileAsync(string title, string suggestedName, IReadOnlyList<string> extensions, string? startIn = null)
    {
        IStorageFile? file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            FileTypeChoices = [.. extensions.Select(extension => Filter([extension]))],
            SuggestedStartLocation = await StartIn(startIn),
        });
        return file?.TryGetLocalPath();
    }

    private static FilePickerFileType Filter(IReadOnlyList<string> extensions) =>
        new FilePickerFileType(string.Join(", ", extensions)) { Patterns = [.. extensions.Select(extension => $"*{extension}")] };

    private async Task<IStorageFolder?> StartIn(string? folder) => folder is { Length: > 0 } && Directory.Exists(folder)
        ? await window.StorageProvider.TryGetFolderFromPathAsync(folder)
        : null;
}
