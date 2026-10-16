namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// What the editor must have before a command can run. The registry checks it before it sends the
/// command, and the menus show a command whose needs are not met as disabled.
/// </summary>
[Flags]
public enum Needs
{
    None = 0,

    /// <summary>
    /// A map is open.
    /// </summary>
    Map = 1,

    /// <summary>
    /// The editor is not opening, saving, exporting or committing.
    /// </summary>
    Idle = 2,

    /// <summary>
    /// There is a step to undo, and no brush stroke is going on.
    /// </summary>
    Undo = 4,

    /// <summary>
    /// There is a step to redo, and no brush stroke is going on.
    /// </summary>
    Redo = 8,

    /// <summary>
    /// A workspace is set up.
    /// </summary>
    Workspace = 16,

    /// <summary>
    /// The open map is a project on disk.
    /// </summary>
    Project = 32,

    /// <summary>
    /// No brush stroke is going on: a stroke holds the map's transaction while the mouse is down.
    /// </summary>
    NoStroke = 64,

    /// <summary>
    /// Something is selected on the map (<see cref="ViewModels.MainViewModel.Selection"/>).
    /// </summary>
    Selection = 128,
}
