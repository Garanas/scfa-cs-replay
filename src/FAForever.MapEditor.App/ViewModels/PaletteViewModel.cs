using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.App.Commands;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The command palette (user story F-2): the commands in menus, found by typing, the ones that
/// work first.
/// </summary>
/// <param name="run">Runs a command by its id, as a menu item does.</param>
public sealed partial class PaletteViewModel(CommandRegistry commands, Func<string, Task> run) : ObservableObject
{
    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    public partial string Query { get; set; } = "";

    [ObservableProperty]
    public partial IReadOnlyList<EditorCommand> Results { get; private set; } = [];

    [ObservableProperty]
    public partial EditorCommand? Selection { get; set; }

    partial void OnQueryChanged(string value) => Search();

    /// <summary>
    /// Opens the palette, empty.
    /// </summary>
    public void Open()
    {
        Query = "";
        Search();
        IsOpen = true;
    }

    /// <summary>
    /// Runs the command chosen, and closes the palette.
    /// </summary>
    public async Task RunSelectionAsync()
    {
        EditorCommand? command = Selection;
        IsOpen = false;
        if (command is not null)
        {
            await run(command.Id);
        }
    }

    private void Search()
    {
        Results = [.. commands.Search(Query).Take(60)];
        Selection = Results.FirstOrDefault();
    }
}
