namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Makes a request a command of the editor (user story F-1): the menus, the command palette,
/// shortcuts, automation and later Lua find it by its id. The request's properties are the
/// command's arguments; its handler, in the same file, does the work.
/// </summary>
/// <param name="id">The command's name, <c>area.what</c>: <c>file.save</c>, <c>terrain.curve</c>.</param>
/// <param name="title">What the menu and the palette say, e.g. <c>Save</c>.</param>
[AttributeUsage(AttributeTargets.Class)]
public sealed class EditorCommandAttribute(string id, string title) : Attribute
{
    public string Id => id;

    public string Title => title;

    /// <summary>
    /// The key gesture, e.g. <c>Ctrl+S</c>.
    /// </summary>
    public string? Gesture { get; init; }

    /// <summary>
    /// The user story it serves, e.g. <c>T-4</c> (<c>docs/map-editor/user-stories.md</c>).
    /// </summary>
    public string? Story { get; init; }

    /// <summary>
    /// What the editor must have before the command can run.
    /// </summary>
    public Needs Needs { get; init; }
}

/// <summary>
/// Marks a command that is not built yet, with the user story that will build it: a request
/// without a handler. Running it reports what is missing instead of doing anything;
/// <c>CommandRegistryTest</c> lists them all and checks that every story exists.
/// </summary>
/// <param name="story">The user story, e.g. <c>T-4</c>.</param>
/// <param name="missing">What it is to do, for the person who runs into it.</param>
[AttributeUsage(AttributeTargets.Class)]
public sealed class IncompleteAttribute(string story, string missing) : Attribute
{
    public string Story => story;

    public string Missing => missing;
}

/// <summary>
/// Makes a command one step of undo: it runs inside a transaction of the open map, which is kept
/// when the handler returns and thrown away when it fails (<see cref="UndoStepBehavior{TRequest, TResponse}"/>).
/// The handler edits through <see cref="CurrentEdit"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class UndoStepAttribute : Attribute;
