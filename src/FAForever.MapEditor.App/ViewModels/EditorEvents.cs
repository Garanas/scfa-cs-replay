using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Workspaces;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// A save or export about to happen, which a handler may stop with a reason (a check of the map).
/// </summary>
/// <param name="Folder">Where it writes: the project folder, or the folder an export writes into;
/// null for a save into the project folder the map has.</param>
public sealed class VetoableEventArgs(EditedMap map, string? folder) : EventArgs
{
    private readonly List<string> _reasons = [];

    public EditedMap Map => map;

    public string? Folder => folder;

    /// <summary>
    /// Why the handlers stopped it; empty when it goes on.
    /// </summary>
    public IReadOnlyList<string> Reasons => _reasons;

    public bool IsVetoed => _reasons.Count > 0;

    /// <summary>
    /// Stops the save or export; the person reads <paramref name="reason"/> in the status line.
    /// </summary>
    public void Veto(string reason) => _reasons.Add(reason);
}

/// <summary>
/// A save or export that a handler of <see cref="EditorEvents.BeforeSave"/> or
/// <see cref="EditorEvents.BeforeExport"/> stopped.
/// </summary>
public sealed class OperationVetoedException(string operation, IReadOnlyList<string> reasons)
    : InvalidOperationException($"{operation} was stopped: {string.Join("; ", reasons)}")
{
    public IReadOnlyList<string> Reasons => reasons;
}

/// <summary>
/// What happens in the editor, for C# (and later Lua plugins, user story F-15) to react to (user
/// story F-13). <see cref="MainViewModel"/> raises them from its operations, on the user
/// interface's thread. A handler that throws is logged and stops nothing but its own work.
/// </summary>
public sealed class EditorEvents(ILogger log)
{
    /// <summary>
    /// The workspace is ready: set up, converted or opened at the start.
    /// </summary>
    public event Action<Workspace>? WorkspaceOpened;

    /// <summary>
    /// A map was opened, imported or loaded as a project.
    /// </summary>
    public event Action<EditedMap>? MapOpened;

    /// <summary>
    /// The map was saved into its project folder (<see cref="EditedMap.ProjectFolder"/>).
    /// </summary>
    public event Action<EditedMap>? MapSaved;

    /// <summary>
    /// The map was exported into a map folder of the game (the folder).
    /// </summary>
    public event Action<EditedMap, string>? MapExported;

    /// <summary>
    /// The open map's document changed: an edit (with its description), an undo or redo, or what
    /// was read again from disk. A brush stroke is one change, when it ends.
    /// </summary>
    public event Action<DocumentChange>? DocumentChanged;

    /// <summary>
    /// A save is about to start; a handler may stop it (<see cref="VetoableEventArgs.Veto"/>).
    /// </summary>
    public event EventHandler<VetoableEventArgs>? BeforeSave;

    /// <summary>
    /// An export is about to start; a handler may stop it (<see cref="VetoableEventArgs.Veto"/>).
    /// </summary>
    public event EventHandler<VetoableEventArgs>? BeforeExport;

    internal void OnWorkspaceOpened(Workspace workspace) => Raise(nameof(WorkspaceOpened), WorkspaceOpened, handler => ((Action<Workspace>)handler)(workspace));

    internal void OnMapOpened(EditedMap map) => Raise(nameof(MapOpened), MapOpened, handler => ((Action<EditedMap>)handler)(map));

    internal void OnMapSaved(EditedMap map) => Raise(nameof(MapSaved), MapSaved, handler => ((Action<EditedMap>)handler)(map));

    internal void OnMapExported(EditedMap map, string folder) => Raise(nameof(MapExported), MapExported, handler => ((Action<EditedMap, string>)handler)(map, folder));

    internal void OnDocumentChanged(DocumentChange change) => Raise(nameof(DocumentChanged), DocumentChanged, handler => ((Action<DocumentChange>)handler)(change));

    /// <summary>
    /// Asks the handlers of <see cref="BeforeSave"/> whether the save may go on.
    /// </summary>
    /// <exception cref="OperationVetoedException">A handler stopped it.</exception>
    internal void CheckSave(EditedMap map, string? folder) => Check("Saving", nameof(BeforeSave), BeforeSave, map, folder);

    /// <summary>
    /// Asks the handlers of <see cref="BeforeExport"/> whether the export may go on.
    /// </summary>
    /// <exception cref="OperationVetoedException">A handler stopped it.</exception>
    internal void CheckExport(EditedMap map, string folder) => Check("Exporting", nameof(BeforeExport), BeforeExport, map, folder);

    private void Check(string operation, string name, EventHandler<VetoableEventArgs>? handlers, EditedMap map, string? folder)
    {
        VetoableEventArgs arguments = new VetoableEventArgs(map, folder);
        Raise(name, handlers, handler => ((EventHandler<VetoableEventArgs>)handler)(this, arguments));
        if (arguments.IsVetoed)
        {
            log.LogWarning("{Operation} was stopped: {Reasons}", operation, string.Join("; ", arguments.Reasons));
            throw new OperationVetoedException(operation, arguments.Reasons);
        }
    }

    /// <summary>
    /// Calls each handler; one that throws is logged, and the others are called still.
    /// </summary>
    private void Raise(string name, Delegate? handlers, Action<Delegate> call)
    {
        foreach (Delegate handler in handlers?.GetInvocationList() ?? [])
        {
            try
            {
                call(handler);
            }
            catch (Exception exception)
            {
                log.LogError(exception, "A handler of {Event} failed: {Reason}", name, exception.Message);
            }
        }
    }
}
