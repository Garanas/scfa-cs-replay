namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The pages of the Help and File menus about the editor itself: the keyboard shortcuts and the
/// workspace's settings.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// The keyboard shortcuts page (Help, Keyboard shortcuts).
    /// </summary>
    public ShortcutsViewModel Shortcuts { get; private set; } = null!;

    /// <summary>
    /// The workspace's settings page (File, Workspace settings).
    /// </summary>
    public WorkspaceSettingsViewModel WorkspaceSettings { get; private set; } = null!;

    private void InitializeHelp()
    {
        Shortcuts = new ShortcutsViewModel(Commands);
        WorkspaceSettings = new WorkspaceSettingsViewModel(Setup, _dialogs, Logger("Workspace"), RunWithAsync);
        Shortcuts.PropertyChanged += (_, e) => OnPageChanged(e.PropertyName);
        WorkspaceSettings.PropertyChanged += (_, e) => OnPageChanged(e.PropertyName);
    }

    private void OnPageChanged(string? property)
    {
        if (property == "IsOpen")
        {
            OnPropertyChanged(nameof(IsPageOpen));
            OnPropertyChanged(nameof(OpenPageName));
        }
    }
}
