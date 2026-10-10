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

    /// <summary>
    /// The plugin that registered it at run time (user story F-15), or null for the editor's own;
    /// such a command may be taken away while it is in a menu.
    /// </summary>
    public string? Owner { get; init; }

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
/// <param name="Why">Why it did not run: what it needs that is missing (<c>nothing to undo</c>), or
/// that it is not built yet; null when it ran.</param>
/// <param name="Error">What the command threw when it refused (<see cref="CommandRegistry.TryRunAsync"/>); null otherwise.</param>
public sealed record CommandRun(bool Ran, object? Result, string? Why = null, Exception? Error = null);

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

    /// <summary>
    /// Raised when what the commands need may have changed (<see cref="CanRun"/>): a map was opened,
    /// the editor became busy or idle, a step was done or undone, a stroke started or ended, the
    /// workspace became ready. The menus and shortcuts ask again on it, and on nothing else.
    /// </summary>
    public event EventHandler? CanRunChanged;

    /// <summary>
    /// Raised when a command was added or taken away after the start (a plugin's): the menus and
    /// shortcuts are made again.
    /// </summary>
    public event EventHandler? CommandsChanged;

    /// <summary>
    /// Says that <see cref="CanRun"/> may answer otherwise now; the editor calls it wherever what
    /// <see cref="Needs"/> asks for changes.
    /// </summary>
    public void NotifyCanRunChanged() => CanRunChanged?.Invoke(this, EventArgs.Empty);

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
        CommandsChanged?.Invoke(this, EventArgs.Empty);
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

    /// <summary>
    /// Takes a command made at run time away again, as a plugin that is turned off does with its own.
    /// </summary>
    /// <returns>Whether there was such a command.</returns>
    /// <exception cref="InvalidOperationException">The command is in the menu bar's layout
    /// (<see cref="Menus.Bar"/>): the menu bar would name a command that is gone.</exception>
    public bool Unregister(string id)
    {
        if (Find(id) is not EditorCommand command)
        {
            return false;
        }
        if (command.Menu.Length > 0 && command.Owner is null)
        {
            throw new InvalidOperationException($"The command {id} is in the menu {command.Menu}");
        }
        _byId.Remove(id);
        _commands.Remove(command);
        CommandsChanged?.Invoke(this, EventArgs.Empty);
        return true;
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
            return new CommandRun(false, null, $"not built yet (user story {command.Incomplete!.Story}): {command.Incomplete.Missing}");
        }
        if (!meets(command.Needs))
        {
            Count(command, "cannot_run");
            return new CommandRun(false, null, WhyNot(command.Needs));
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
    /// Runs a command as <see cref="RunAsync(string, JsonObject?, bool)"/> does, but a command that
    /// refuses (its arguments do not fit, or its handler says why it cannot: nothing to paste, no
    /// such army, a file it cannot write) answers <c>Ran: false</c> with the reason in
    /// <see cref="CommandRun.Why"/> and the exception in <see cref="CommandRun.Error"/>, rather than
    /// throwing: for automation, whose <c>run</c> answers so.
    /// </summary>
    /// <exception cref="ArgumentException">There is no such command.</exception>
    public async Task<CommandRun> TryRunAsync(string id, JsonObject? arguments, bool interactive)
    {
        if (Find(id) is null)
        {
            throw new ArgumentException($"There is no command '{id}'; 'commands' lists them");
        }
        try
        {
            return await RunAsync(id, arguments, interactive);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException
            or NotSupportedException or FAForever.MapEditor.Project.MapProjectException)
        {
            return new CommandRun(false, null, exception.Message, exception);
        }
    }

    /// <summary>
    /// What of <paramref name="needs"/> is missing now, in words: <c>nothing to undo</c>, <c>no map is open</c>.
    /// </summary>
    public string WhyNot(Needs needs)
    {
        string[] missing = [.. Enum.GetValues<Needs>()
            .Where(need => need != Needs.None && needs.HasFlag(need) && !meets(need))
            .Select(need => need switch
            {
                Needs.Map => "no map is open",
                Needs.Idle => "the editor is busy",
                Needs.Undo => "nothing to undo (or a stroke is going on)",
                Needs.Redo => "nothing to redo (or a stroke is going on)",
                Needs.Workspace => "no workspace is set up",
                Needs.Project => "the map is no project on disk: save it as one first",
                Needs.NoStroke => "a brush stroke is going on",
                Needs.Selection => "nothing is selected",
                _ => need.ToString(),
            })];
        return missing.Length == 0 ? "it cannot run now" : string.Join("; ", missing);
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
