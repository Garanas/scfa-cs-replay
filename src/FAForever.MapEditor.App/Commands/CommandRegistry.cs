using System.Reflection;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// A command of the editor, found on a controller (<see cref="EditorCommandAttribute"/>).
/// </summary>
/// <param name="Incomplete">Set when the command is not built yet: what it is to do, and its story.</param>
public sealed record EditorCommand(
    string Id,
    string Title,
    string Menu,
    bool SeparatorBefore,
    string? Gesture,
    string? Story,
    IncompleteAttribute? Incomplete,
    Func<bool> CanRun,
    Func<Task> Run)
{
    public bool IsIncomplete => Incomplete is not null;

    /// <summary>
    /// The title with its menu, as the palette shows it: <c>Terrain: Curve</c>.
    /// </summary>
    public string FullTitle => Menu.Length > 0 ? $"{Menu}: {Title}" : Title;
}

/// <summary>
/// The controller of model, view and controller (user story F-1): every command of the editor, by
/// id. Menus, shortcuts, the command palette and automation all run commands through it, so they
/// behave the same; Lua plugins will too.
/// </summary>
public sealed class CommandRegistry
{
    private readonly List<EditorCommand> _commands = [];
    private readonly Dictionary<string, EditorCommand> _byId = new Dictionary<string, EditorCommand>(StringComparer.Ordinal);

    /// <summary>
    /// Raised when a command that is not built yet is run, with what it is to do.
    /// </summary>
    public event Action<EditorCommand>? IncompleteRun;

    /// <summary>
    /// Raised after a command ran.
    /// </summary>
    public event Action<EditorCommand>? Ran;

    public IReadOnlyList<EditorCommand> All => _commands;

    /// <summary>
    /// Finds the commands of a controller: its methods with <see cref="EditorCommandAttribute"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">A command's id is taken, its method takes
    /// arguments, or its <c>CanRun</c> property does not exist.</exception>
    public void Add(object controller)
    {
        foreach (MethodInfo method in controller.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public))
        {
            if (method.GetCustomAttribute<EditorCommandAttribute>() is not EditorCommandAttribute attribute)
            {
                continue;
            }
            if (method.GetParameters().Length > 0)
            {
                throw new InvalidOperationException($"The command {attribute.Id} ({method.Name}) takes arguments");
            }
            if (_byId.ContainsKey(attribute.Id))
            {
                throw new InvalidOperationException($"There are two commands {attribute.Id}");
            }

            Func<bool> canRun = () => true;
            if (attribute.CanRun is string name)
            {
                PropertyInfo property = controller.GetType().GetProperty(name)
                    ?? throw new InvalidOperationException($"The command {attribute.Id} names a property {name} that {controller.GetType().Name} does not have");
                canRun = () => (bool)property.GetValue(controller)!;
            }
            Func<Task> run = () => method.Invoke(controller, null) is Task task ? task : Task.CompletedTask;

            EditorCommand command = new EditorCommand(
                attribute.Id, attribute.Title, attribute.Menu, attribute.SeparatorBefore, attribute.Gesture,
                attribute.Story ?? method.GetCustomAttribute<IncompleteAttribute>()?.Story,
                method.GetCustomAttribute<IncompleteAttribute>() ?? controller.GetType().GetCustomAttribute<IncompleteAttribute>(),
                canRun, run);
            _commands.Add(command);
            _byId[command.Id] = command;
        }
    }

    /// <summary>
    /// Adds a command that is made rather than found on a controller, such as choosing a tool.
    /// </summary>
    /// <exception cref="InvalidOperationException">Its id is taken.</exception>
    public void Register(EditorCommand command)
    {
        if (!_byId.TryAdd(command.Id, command))
        {
            throw new InvalidOperationException($"There are two commands {command.Id}");
        }
        _commands.Add(command);
    }

    public EditorCommand? Find(string id) => _byId.GetValueOrDefault(id);

    /// <summary>
    /// Runs a command: a command not built yet only reports what it is to do (<see cref="IncompleteRun"/>).
    /// </summary>
    /// <returns>Whether it ran: false when it cannot run now or is not built yet.</returns>
    /// <exception cref="ArgumentException">There is no such command.</exception>
    public async Task<bool> RunAsync(string id)
    {
        EditorCommand command = Find(id) ?? throw new ArgumentException($"There is no command '{id}'; 'commands' lists them");
        if (command.Incomplete is not null)
        {
            IncompleteRun?.Invoke(command);
            return false;
        }
        if (!command.CanRun())
        {
            return false;
        }
        await command.Run();
        Ran?.Invoke(command);
        return true;
    }

    /// <summary>
    /// The commands whose title, menu or id contain every word of <paramref name="query"/>, the
    /// ones that can run first: the command palette's list.
    /// </summary>
    public IEnumerable<EditorCommand> Search(string query)
    {
        string[] words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return _commands
            .Where(command => words.All(word =>
                command.FullTitle.Contains(word, StringComparison.OrdinalIgnoreCase) || command.Id.Contains(word, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(command => command.IsIncomplete)
            .ThenBy(command => command.Menu, StringComparer.Ordinal)
            .ThenBy(command => command.Title, StringComparer.Ordinal);
    }
}
