namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The core's side of version control (user story F-18).
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// Reads the files of the project that git changed (a pull) again, as the watcher would: at
    /// once, or after the operation or stroke that is going on.
    /// </summary>
    /// <param name="files">Relative to the project folder.</param>
    internal void ReadAgainFromDisk(IEnumerable<string> files)
    {
        foreach (string file in files)
        {
            _pendingReload.Add(file);
        }
        ReloadPending();
    }
}
