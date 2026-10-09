using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FAForever.MapEditor.App.Diagnostics;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// An argument of a command: a property of its request.
/// </summary>
public sealed record CommandParameter(string Name, Type Type, bool IsOptional)
{
    public override string ToString() => IsOptional ? $"{Name}?" : Name;
}

/// <summary>
/// A command of the editor: what it is called, where it shows and when it can run, and how to make
/// its request from arguments.
/// </summary>
public sealed class EditorCommand
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public string? Gesture { get; init; }

    public string? Story { get; init; }

    public Needs Needs { get; init; }

    /// <summary>
    /// Set when the command is not built yet: what it is to do, and its story.
    /// </summary>
    public IncompleteAttribute? Incomplete { get; init; }

    /// <summary>
    /// What the command takes; automation, the CLI and Lua give them by name.
    /// </summary>
    public IReadOnlyList<CommandParameter> Parameters { get; init; } = [];

    /// <summary>
    /// Makes the request from the arguments; null while the command is not built.
    /// </summary>
    public Func<JsonObject, object>? MakeRequest { get; init; }

    /// <summary>
    /// The menu it is in (<see cref="Menus.Bar"/>), empty for none: such a command is for
    /// automation and plugins, and the palette leaves it out.
    /// </summary>
    public string Menu { get; internal set; } = "";

    public bool IsIncomplete => Incomplete is not null;

    /// <summary>
    /// The title with its menu, as the palette shows it: <c>Terrain: Curve</c>.
    /// </summary>
    public string FullTitle => Menu.Length > 0 ? $"{Menu}: {Title}" : Title;

    /// <summary>
    /// The command as <c>help</c> lists it: <c>file.open {folder?}</c>.
    /// </summary>
    public string Usage => Parameters.Count == 0 ? Id : $"{Id} {{{string.Join(", ", Parameters)}}}";
}

/// <summary>
/// What running a command came to.
/// </summary>
/// <param name="Ran">False when it could not run now or is not built yet.</param>
/// <param name="Result">What the command answers, such as the folder an export wrote; null for most.</param>
public sealed record CommandRun(bool Ran, object? Result);

/// <summary>
/// The controller of model, view and controller (user story F-1): every command of the editor, by
/// id. Menus, shortcuts, the command palette, automation and later Lua run commands through it, so
/// they behave the same. It makes a command's request from its arguments and sends it to the
/// request's handler through MediatR (<see cref="CommandServices"/>).
/// </summary>
public sealed class CommandRegistry(ISender sender, DialogGuard dialogs, Func<Needs, bool> meets)
{
    /// <summary>
    /// How arguments become requests: names in camel case, numbers also as text; an argument the
    /// request does not have, or one it needs that is missing, is an error.
    /// </summary>
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
    };

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
    /// Finds the commands of an assembly: its requests with <see cref="EditorCommandAttribute"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">A command's id is taken.</exception>
    public void AddFrom(Assembly assembly)
    {
        foreach (Type type in assembly.GetTypes())
        {
            if (type.GetCustomAttribute<EditorCommandAttribute>() is not EditorCommandAttribute attribute)
            {
                continue;
            }
            IncompleteAttribute? incomplete = type.GetCustomAttribute<IncompleteAttribute>();
            bool undoStep = type.GetCustomAttribute<UndoStepAttribute>() is not null;
            Register(new EditorCommand
            {
                Id = attribute.Id,
                Title = attribute.Title,
                Gesture = attribute.Gesture,
                Story = attribute.Story ?? incomplete?.Story,
                Needs = attribute.Needs | (undoStep ? Needs.Map | Needs.NoStroke : Needs.None),
                Incomplete = incomplete,
                Parameters = ParametersOf(type),
                MakeRequest = incomplete is null ? arguments => Deserialize(arguments, type) : null,
            });
        }
    }

    /// <summary>
    /// Adds a command that is made rather than found, such as choosing a tool.
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

    /// <summary>
    /// Puts the commands in their menus: <see cref="EditorCommand.Menu"/> becomes the title of the
    /// menu, or submenu, that lists them.
    /// </summary>
    /// <exception cref="InvalidOperationException">A menu names a command that does not exist.</exception>
    public void Arrange(IEnumerable<MenuLayout> menus)
    {
        foreach (MenuLayout menu in menus)
        {
            foreach (object item in menu.Items)
            {
                switch (item)
                {
                    case MenuLayout submenu:
                        Arrange([submenu]);
                        break;
                    case string id when id != Menus.Separator:
                        (Find(id) ?? throw new InvalidOperationException($"The menu {menu.Title} names a command {id} that does not exist")).Menu = menu.Title;
                        break;
                }
            }
        }
    }

    public EditorCommand? Find(string id) => _byId.GetValueOrDefault(id);

    /// <summary>
    /// Whether a command can run now: it is built and the editor has what it needs.
    /// </summary>
    public bool CanRun(EditorCommand command) => !command.IsIncomplete && meets(command.Needs);

    /// <summary>
    /// Runs a command as a menu item does.
    /// </summary>
    /// <returns>Whether it ran: false when it cannot run now or is not built yet.</returns>
    public async Task<bool> RunAsync(string id) => (await RunAsync(id, null, interactive: true)).Ran;

    /// <summary>
    /// Runs a command with its arguments: a command not built yet only reports what it is to do
    /// (<see cref="IncompleteRun"/>). Every run is counted by its result (<see cref="EditorMetrics.Commands"/>).
    /// </summary>
    /// <param name="arguments">The request's properties by name; left out, the command asks for what it needs.</param>
    /// <param name="interactive">False for automation and plugins: a command that would ask a
    /// question fails instead, saying which argument to give.</param>
    /// <exception cref="ArgumentException">There is no such command, or the arguments do not fit it.</exception>
    public async Task<CommandRun> RunAsync(string id, JsonObject? arguments, bool interactive)
    {
        EditorCommand command = Find(id) ?? throw new ArgumentException($"There is no command '{id}'; 'commands' lists them");
        if (command.MakeRequest is null)
        {
            Count(command, "not_built");
            IncompleteRun?.Invoke(command);
            return new CommandRun(false, null);
        }
        if (!meets(command.Needs))
        {
            Count(command, "cannot_run");
            return new CommandRun(false, null);
        }

        object request = command.MakeRequest(arguments ?? []);
        bool wasQuiet = dialogs.Quiet;
        dialogs.Quiet = !interactive;
        object? result;
        try
        {
            result = await sender.Send(request);
        }
        catch
        {
            Count(command, "failed");
            throw;
        }
        finally
        {
            dialogs.Quiet = wasQuiet;
        }
        Count(command, "ran");
        Ran?.Invoke(command);
        return new CommandRun(true, result is Unit ? null : result);
    }

    /// <summary>
    /// The commands of the menus whose title, menu or id contain every word of <paramref name="query"/>,
    /// the ones that can run first: the command palette's list.
    /// </summary>
    public IEnumerable<EditorCommand> Search(string query)
    {
        string[] words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return _commands
            .Where(command => command.Menu.Length > 0)
            .Where(command => words.All(word =>
                command.FullTitle.Contains(word, StringComparison.OrdinalIgnoreCase) || command.Id.Contains(word, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(command => command.IsIncomplete)
            .ThenBy(command => command.Menu, StringComparer.Ordinal)
            .ThenBy(command => command.Title, StringComparer.Ordinal);
    }

    private static void Count(EditorCommand command, string result) => EditorMetrics.Commands.Add(1,
        EditorMetrics.Tag("command", command.Id), EditorMetrics.Tag("result", result));

    private static object Deserialize(JsonObject arguments, Type type)
    {
        try
        {
            return arguments.Deserialize(type, Json) ?? throw new ArgumentException($"The arguments of {type.Name} are empty");
        }
        catch (JsonException exception)
        {
            throw new ArgumentException($"The arguments do not fit {type.Name}: {exception.Message}", exception);
        }
    }

    /// <summary>
    /// The arguments of a request: the parameters of its record's constructor.
    /// </summary>
    private static CommandParameter[] ParametersOf(Type type) => type.GetConstructors()
        .OrderByDescending(constructor => constructor.GetParameters().Length)
        .FirstOrDefault()?.GetParameters()
        .Select(parameter => new CommandParameter(JsonNamingPolicy.CamelCase.ConvertName(parameter.Name!), parameter.ParameterType, parameter.IsOptional))
        .ToArray() ?? [];
}
