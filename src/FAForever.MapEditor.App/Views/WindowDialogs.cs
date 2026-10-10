using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.Views;

/// <summary>
/// The editor's questions, answered with the dialogs of the operating system and the editor's own,
/// and the picker of an asset.
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

    public async Task<SaveChoice> AskToSaveChangesAsync(string mapName)
    {
        Window dialog = new Window
        {
            Title = "Unsaved changes",
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        Button Choice(string text, SaveChoice choice)
        {
            Button button = new Button { Content = text, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
            button.Click += (_, _) => dialog.Close(choice);
            return button;
        }
        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = $"Save the changes to {mapName}?" },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { Choice("Save", SaveChoice.Save), Choice("Don't save", SaveChoice.Discard), Choice("Cancel", SaveChoice.Cancel) },
                },
            },
        };
        return await dialog.ShowDialog<SaveChoice?>(window) ?? SaveChoice.Cancel;
    }

    /// <summary>
    /// A small dialog with the message, the action's button and Cancel, which has the keyboard first.
    /// </summary>
    public async Task<bool> ConfirmAsync(string title, string message, string action)
    {
        Window dialog = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        Button Choice(string text, bool choice)
        {
            Button button = new Button { Content = text, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
            button.Click += (_, _) => dialog.Close(choice);
            return button;
        }
        Button cancel = Choice("Cancel", false);
        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 16,
            MaxWidth = 520,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { Choice(action, true), cancel },
                },
            },
        };
        dialog.Opened += (_, _) => cancel.Focus();
        return await dialog.ShowDialog<bool?>(window) ?? false;
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
