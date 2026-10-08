using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using FAForever.MapEditor.App.ViewModels;

namespace FAForever.MapEditor.App.Views;

/// <summary>
/// The editor's questions, answered with the dialogs of the operating system and the editor's own.
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
}
