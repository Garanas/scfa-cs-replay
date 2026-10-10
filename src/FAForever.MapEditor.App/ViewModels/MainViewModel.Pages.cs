namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The pages over the editor (diagnostics, plugins, version control, report a problem, new map):
/// while one shows, the window's shortcuts and the tools' keys wait, and Escape closes it.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// Whether a page shows over the editor.
    /// </summary>
    public bool IsPageOpen => OpenPageName is not null;

    /// <summary>
    /// The page that shows over the editor, by name (the last one opened lies on top), or null.
    /// </summary>
    public string? OpenPageName =>
        NewMapPage?.IsOpen == true ? "newMap"
        : Report?.IsOpen == true ? "report"
        : VersionControl?.IsOpen == true ? "versionControl"
        : Plugins?.IsOpen == true ? "plugins"
        : Diagnostics?.IsOpen == true ? "diagnostics"
        : null;

    /// <summary>
    /// Closes the page on top, as its Close or Cancel button does; false when none shows.
    /// </summary>
    public bool ClosePage()
    {
        switch (OpenPageName)
        {
            case "newMap":
                NewMapPage.IsOpen = false;
                break;
            case "report":
                Report.IsOpen = false;
                break;
            case "versionControl":
                VersionControl.IsOpen = false;
                break;
            case "plugins":
                Plugins.IsOpen = false;
                break;
            case "diagnostics":
                Diagnostics.IsOpen = false;
                break;
            default:
                return false;
        }
        return true;
    }
}
