using Avalonia.Controls;
using Avalonia.Platform.Storage;
using FAForever.MapEditor.App.ViewModels;

namespace FAForever.MapEditor.App.Views;

/// <summary>
/// The editor's questions, answered with the dialogs of the operating system.
/// </summary>
public sealed class WindowDialogs(Window window) : IEditorDialogs
{
    public async Task<string?> PickFolderAsync(string title)
    {
        IReadOnlyList<IStorageFolder> folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
}
