namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Makes a method of a controller a command of the editor (user story F-1): the menus, the
/// command palette, shortcuts, automation and later Lua find it by its id. The method takes no
/// arguments and returns nothing or a <see cref="Task"/>.
/// </summary>
/// <param name="id">The command's name, <c>area.what</c>: <c>file.save</c>, <c>terrain.curve</c>.</param>
/// <param name="title">What the menu and the palette say, e.g. <c>Save</c>.</param>
[AttributeUsage(AttributeTargets.Method)]
public sealed class EditorCommandAttribute(string id, string title) : Attribute
{
    public string Id => id;

    public string Title => title;

    /// <summary>
    /// The menu it is in, e.g. <c>File</c>; empty for none (the palette still finds it).
    /// </summary>
    public string Menu { get; init; } = "";

    /// <summary>
    /// A separator goes before it in its menu.
    /// </summary>
    public bool SeparatorBefore { get; init; }

    /// <summary>
    /// The key gesture, e.g. <c>Ctrl+S</c>.
    /// </summary>
    public string? Gesture { get; init; }

    /// <summary>
    /// The user story it serves, e.g. <c>T-4</c> (<c>USER_STORIES.md</c>).
    /// </summary>
    public string? Story { get; init; }

    /// <summary>
    /// The name of a <see cref="bool"/> property of the controller that says whether it can run now.
    /// </summary>
    public string? CanRun { get; init; }
}

/// <summary>
/// Marks a function that is not built yet, with the user story that will build it. Calling a
/// command that carries it reports what is missing instead of doing anything;
/// <c>CommandRegistryTest</c> lists them all and checks that every story exists.
/// </summary>
/// <param name="story">The user story, e.g. <c>T-4</c>.</param>
/// <param name="missing">What it is to do, for the person who runs into it.</param>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class IncompleteAttribute(string story, string missing) : Attribute
{
    public string Story => story;

    public string Missing => missing;
}
