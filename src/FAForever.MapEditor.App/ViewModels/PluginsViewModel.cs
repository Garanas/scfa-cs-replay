using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.Plugins;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Checks;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;
using FAForever.MapEditor.Project.Workspaces.Serialized;
using Lua;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The workspace's Lua plugins (user story F-15, and F-1's and F-13's Lua part): each a folder of
/// <c>plugins/</c> with its <c>plugin.lua</c>, loaded when the workspace is ready and again when its
/// files change, on or off by the user (remembered in the settings). A plugin registers commands
/// (in the Plugins menu, the palette and automation like any command; a run is one step of undo),
/// problem checks (the Problems tab), a panel of fields (the Plugins mode's inspector) and handlers
/// of the editor's events, which may stop a save or export. Its Lua runs on the window's thread with
/// a time limit per call; an error shows in the Console and under the plugin in the Plugins mode, and
/// never stops the editor.
/// </summary>
public sealed partial class PluginsViewModel : ObservableObject, IPluginHost
{
    /// <summary>
    /// The events a plugin can listen to with <c>editor.on</c>.
    /// </summary>
    public static readonly IReadOnlyList<string> Events = ["mapOpened", "mapSaved", "mapExported", "changed", "beforeSave", "beforeExport"];

    private readonly MainViewModel _model;
    private readonly SynchronizationContext? _ui;
    private readonly HashSet<string> _off = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, string>> _values = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
    private ProjectWatcher? _watcher;
    private int _runs;

    public PluginsViewModel(MainViewModel model)
    {
        _model = model;
        _ui = SynchronizationContext.Current;
        Log = model.Logger("Plugins");
        model.Events.WorkspaceOpened += Open;
        model.Events.MapOpened += map => Raise("mapOpened");
        model.Events.MapSaved += map => Raise("mapSaved");
        model.Events.MapExported += (map, folder) => Raise("mapExported", folder);
        model.Events.DocumentChanged += change =>
        {
            // a plugin's own run tells once, when it ends
            if (_runs == 0)
            {
                Raise("changed", change.Description, change.Reason.ToString().ToLowerInvariant());
            }
        };
        model.Events.BeforeSave += (_, e) => Veto("beforeSave", e);
        model.Events.BeforeExport += (_, e) => Veto("beforeExport", e);
        model.MapChanged += (_, _) => RefreshPanels();
        Parts = new PanelParts([new PanelPart(OverviewPart, "All plugins", PluginsMode.OverviewGroup, new PluginsOverviewPart(this),
            "The workspace's Lua plugins: a folder each in the plugins folder with its plugin.lua, read again when its files change.")]);
        SyncParts();
    }

    public ILogger Log { get; }

    /// <summary>
    /// How long a run of a plugin's command may take, without the commands it runs.
    /// </summary>
    public TimeSpan RunLimit { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How long loading a plugin, a check or a handler of an event may take.
    /// </summary>
    public TimeSpan CallLimit { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The workspace's <c>plugins</c> folder; null before the workspace is ready.
    /// </summary>
    [ObservableProperty]
    public partial string? Folder { get; private set; }

    /// <summary>
    /// The plugins, by folder name.
    /// </summary>
    public ObservableCollection<LuaPlugin> Items { get; } = [];

    public bool IsEmpty => Items.Count == 0;

    /// <summary>
    /// E.g. <c>2 plugins, 1 off, 1 with errors</c>.
    /// </summary>
    public string Summary
    {
        get
        {
            if (Folder is null)
            {
                return "No workspace is set up.";
            }
            if (Items.Count == 0)
            {
                return "No plugins: put a folder with a plugin.lua into the plugins folder.";
            }
            List<string> parts = [$"{Items.Count} plugin{(Items.Count == 1 ? "" : "s")}"];
            int off = Items.Count(plugin => !plugin.IsOn);
            if (off > 0)
            {
                parts.Add($"{off} off");
            }
            int failing = Items.Count(plugin => plugin.HasErrors);
            if (failing > 0)
            {
                parts.Add($"{failing} with errors");
            }
            return string.Join(", ", parts);
        }
    }

    public AssetCatalog? Catalog => _model.Assets.Catalog;

    public SelectionViewModel Selection => _model.Selection;

    public void SetStatus(string text) => _model.Status = text;

    public Task<CommandRun> RunCommandAsync(string id, JsonObject arguments) => _model.Commands.RunAsync(id, arguments, interactive: false);

    public IReadOnlyList<FAForever.FileFormats.Map.MapSymmetryTransform> Mirrors(MapDocument document) => _model.EditTransforms(document);

    public MapDocument MirrorPlaced(MapDocument document, IReadOnlyList<ObjectRef> placed) => _model.MirrorPlaced(document, placed, out _);

    /// <summary>
    /// The check box of a plugin in the inspector: turns it over through <c>plugins.enable</c>.
    /// </summary>
    [RelayCommand]
    private async Task Toggle(string id)
    {
        await _model.TryAsync(() => _model.Commands.RunAsync("plugins.enable", new JsonObject { ["plugin"] = id }, interactive: true));
        // the box shows what the plugin is, also when turning it over failed
        Find(id)?.Refresh();
    }

    public LuaPlugin? Find(string id) => Items.FirstOrDefault(plugin => string.Equals(plugin.Id, id, StringComparison.OrdinalIgnoreCase));

    #region Loading

    /// <summary>
    /// Loads the plugins of a workspace that became ready, after writing the examples into it the
    /// first time, and watches their folder.
    /// </summary>
    public void Open(Workspace workspace) => Open(workspace.PluginsFolder);

    /// <summary>
    /// Loads the plugins of a folder, after writing the examples into it the first time, and
    /// watches it.
    /// </summary>
    public void Open(string folder)
    {
        ReadSettings();
        try
        {
            ExamplePlugins.Ensure(folder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.LogWarning(exception, "Could not write the example plugins into {Folder}: {Reason}", folder, exception.Message);
        }
        Folder = Path.GetFullPath(folder);
        ReloadAll();
        Watch(Folder);
    }

    /// <summary>
    /// Reads every plugin of the folder again: the ones gone are taken away, new ones loaded.
    /// </summary>
    public void ReloadAll()
    {
        foreach (LuaPlugin plugin in Items.ToList())
        {
            Unload(plugin);
        }
        Items.Clear();
        if (Folder is string folder && Directory.Exists(folder))
        {
            foreach (string sub in Directory.EnumerateDirectories(folder).Where(IsPluginFolder).Order(StringComparer.OrdinalIgnoreCase))
            {
                LuaPlugin plugin = new LuaPlugin(sub) { IsOn = !_off.Contains(Path.GetFileName(sub)) };
                Items.Add(plugin);
                Load(plugin);
            }
        }
        Changed();
    }

    /// <summary>
    /// Reads one plugin again: its script changed, or it was turned on.
    /// </summary>
    public void Reload(LuaPlugin plugin)
    {
        Unload(plugin);
        plugin.ClearErrors();
        Load(plugin);
        Changed();
    }

    private static bool IsPluginFolder(string folder) =>
        FilePaths.FindFile(folder, PluginManifest.Script) is not null && !Path.GetFileName(folder).StartsWith('.');

    /// <summary>
    /// Runs the plugin's script, which registers its commands, checks, fields and handlers; then
    /// puts them into the editor. A script that fails registers nothing.
    /// </summary>
    private void Load(LuaPlugin plugin)
    {
        try
        {
            plugin.Manifest = PluginManifest.Read(plugin.Folder);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            Fail(plugin, "loading", exception);
        }
        if (plugin.Manifest.UnknownKind is string unknown)
        {
            plugin.AddError($"{PluginManifest.File}: the kind '{unknown}' is none of {string.Join(", ", PluginKinds.All)}; it reads as {PluginKinds.Other}");
            Log.LogWarning("The plugin {Plugin} names the kind {Kind}, which the editor does not know", plugin.Id, unknown);
        }
        if (!plugin.IsOn)
        {
            return;
        }
        if (!plugin.Id.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
        {
            plugin.AddError($"The folder's name '{plugin.Id}' is the plugin's id: use letters, digits, dashes and underscores");
            return;
        }

        using Performance.Span measure = Performance.Measure("plugins.load");
        string script;
        try
        {
            script = File.ReadAllText(FilePaths.Existing(Path.Combine(plugin.Folder, PluginManifest.Script)));
        }
        catch (IOException exception)
        {
            Fail(plugin, "loading", exception);
            return;
        }
        plugin.State = PluginApi.Create(plugin, this);
        using PluginClock clock = new PluginClock(CallLimit);
        if (!CallNow(plugin, new PluginContext("loading plugin.lua", () => _model.Map?.Document, clock) { IsLoading = true }, "loading",
            () => plugin.State.DoStringAsync(script, PluginManifest.Script, clock.Token), out _))
        {
            plugin.Forget();
            return;
        }

        foreach (PluginCommand command in plugin.Commands)
        {
            if (_model.Commands.Find(command.Id) is not null)
            {
                plugin.AddError($"There is a command {command.Id} already");
                continue;
            }
            _model.Commands.Register(new EditorCommand
            {
                Id = command.Id,
                Title = command.Title,
                Story = "F-15",
                Needs = Needs.Map | Needs.NoStroke,
                Owner = plugin.Id,
                Menu = Menus.PluginsMenu,
                MakeRequest = arguments => new RunPluginCommand(command.Id, arguments),
            });
            plugin.CommandItems.Add(new PluginCommandItem(command.Id, command.Title, command.Description));
        }
        foreach (PluginToolSpec spec in plugin.Tools)
        {
            AddTool(plugin, spec);
        }
        foreach (PluginCheck check in plugin.Checks)
        {
            try
            {
                _model.Problems.Checks.Register(new LuaProblemCheck(this, plugin, check));
            }
            catch (ArgumentException exception)
            {
                plugin.AddError(exception.Message);
            }
        }
        plugin.Panel = plugin.Fields.Count == 0 ? null : new SettingGroup(plugin.Manifest.Name, [.. plugin.Fields.Select(field => FieldSetting(plugin, field))]);
        plugin.Panel?.Refresh(_model.Map?.Document);
        plugin.IsLoaded = true;
        plugin.Registrations();
        Log.LogInformation("Loaded the plugin {Plugin}: {Registered}", plugin.Id, plugin.Registered);
        if (plugin.Checks.Count > 0)
        {
            _model.Problems.Schedule();
        }
    }

    /// <summary>
    /// Takes what a plugin registered out of the editor.
    /// </summary>
    private void Unload(LuaPlugin plugin)
    {
        foreach (PluginCommand command in plugin.Commands)
        {
            if (_model.Commands.Find(command.Id) is { Owner: string owner } && owner == plugin.Id)
            {
                _model.Commands.Unregister(command.Id);
            }
        }
        foreach (PluginTool tool in plugin.MadeTools)
        {
            RemoveTool(plugin, tool);
        }
        bool hadChecks = false;
        foreach (PluginCheck check in plugin.Checks)
        {
            hadChecks |= _model.Problems.Checks.Unregister(check.Id);
        }
        plugin.Forget();
        if (hadChecks)
        {
            _model.Problems.Schedule();
        }
    }

    /// <summary>
    /// Reads the plugins whose files change on disk again.
    /// </summary>
    private void Watch(string folder)
    {
        _watcher?.Dispose();
        _watcher = null;
        try
        {
            Directory.CreateDirectory(folder);
            _watcher = new ProjectWatcher(folder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Log.LogWarning(exception, "Cannot watch the plugins in {Folder}: {Reason}", folder, exception.Message);
            return;
        }
        _watcher.Changed += files => UiThread.Run(_ui, () => OnFilesChanged(files));
    }

    /// <summary>
    /// Reads the plugins of changed files again (paths relative to the plugins folder): what the
    /// folder's watcher reports, and tests.
    /// </summary>
    public void OnFilesChanged(IReadOnlyCollection<string> files)
    {
        if (Folder is not string folder)
        {
            return;
        }
        string[] changed = [.. files.Select(file => file.Split('/')[0]).Distinct(StringComparer.OrdinalIgnoreCase)];
        Log.LogInformation("The plugins changed on disk: {Plugins}", string.Join(", ", changed));
        foreach (string name in changed)
        {
            string sub = Path.Combine(folder, name);
            LuaPlugin? plugin = Find(name);
            if (plugin is not null && plugin.IsRunning)
            {
                continue;
            }
            if (!IsPluginFolder(sub))
            {
                if (plugin is not null)
                {
                    Unload(plugin);
                    Items.Remove(plugin);
                }
                continue;
            }
            if (plugin is null)
            {
                plugin = new LuaPlugin(sub) { IsOn = !_off.Contains(name) };
                int at = Items.TakeWhile(other => string.Compare(other.Id, name, StringComparison.OrdinalIgnoreCase) < 0).Count();
                Items.Insert(at, plugin);
            }
            Unload(plugin);
            plugin.ClearErrors();
            Load(plugin);
        }
        Changed();
    }

    #endregion

    #region On, off and fields

    /// <summary>
    /// Turns a plugin on (loading it) or off (taking its commands and checks away), and remembers it.
    /// </summary>
    /// <exception cref="ArgumentException">There is no such plugin.</exception>
    public void SetOn(string id, bool on)
    {
        LuaPlugin plugin = Find(id) ?? throw new ArgumentException($"There is no plugin '{id}'; {string.Join(", ", Items.Select(p => p.Id))}");
        if (plugin.IsOn == on)
        {
            return;
        }
        if (plugin.IsRunning)
        {
            throw new InvalidOperationException($"The plugin {plugin.Id} is running");
        }
        plugin.IsOn = on;
        if (on)
        {
            _off.Remove(plugin.Id);
        }
        else
        {
            _off.Add(plugin.Id);
        }
        SaveSettings();
        Reload(plugin);
        Log.LogInformation("Turned the plugin {Plugin} {State}", plugin.Id, on ? "on" : "off");
    }

    public string ValueOf(LuaPlugin plugin, PluginField field) =>
        _values.TryGetValue(plugin.Id, out Dictionary<string, string>? values) && values.TryGetValue(field.Key, out string? value) ? value : field.Default;

    /// <summary>
    /// Sets a field of a plugin's panel, checked against its type, and remembers it.
    /// </summary>
    /// <exception cref="ArgumentException">There is no such plugin or field, or the value does not fit it.</exception>
    public void SetValue(string id, string key, JsonNode? value)
    {
        LuaPlugin plugin = Find(id) ?? throw new ArgumentException($"There is no plugin '{id}'");
        PluginField field = plugin.Fields.FirstOrDefault(f => f.Key == key) ?? throw new ArgumentException($"The plugin {id} has no field '{key}'");
        string text = value is JsonValue json && json.TryGetValue(out string? s) ? s : value?.ToJsonString() ?? "";
        text = field.Type switch
        {
            "number" => Setting.TryParse(text, out double number)
                ? Setting.Format(Math.Clamp(number, field.Minimum ?? double.MinValue, field.Maximum ?? double.MaxValue))
                : throw new ArgumentException($"The field {field.Label} takes a number, not '{text}'"),
            "bool" => text is "true" or "false" ? text : throw new ArgumentException($"The field {field.Label} takes true or false, not '{text}'"),
            "choice" => field.Choices.Contains(text) ? text : throw new ArgumentException($"The field {field.Label} takes one of {string.Join(", ", field.Choices)}, not '{text}'"),
            _ => text,
        };
        if (!_values.TryGetValue(plugin.Id, out Dictionary<string, string>? values))
        {
            _values[plugin.Id] = values = new Dictionary<string, string>(StringComparer.Ordinal);
        }
        values[key] = text;
        SaveSettings();
        plugin.Panel?.Refresh(_model.Map?.Document);
        foreach (PluginTool tool in plugin.MadeTools)
        {
            tool.Refresh(_model.Map?.Document);
        }
    }

    /// <summary>
    /// A number field's value, 0 when it reads as none.
    /// </summary>
    internal double NumberOf(LuaPlugin plugin, string key) =>
        plugin.Fields.FirstOrDefault(f => f.Key == key) is PluginField field && Setting.TryParse(ValueOf(plugin, field), out double number) ? number : 0;

    private Setting FieldSetting(LuaPlugin plugin, PluginField field)
    {
        Func<JsonObject> arguments = () => new JsonObject { ["plugin"] = plugin.Id, ["field"] = field.Key };
        Func<string, JsonObject, Task> run = (id, args) => _model.TryAsync(() => _model.Commands.RunAsync(id, args, interactive: true));
        string Read() => ValueOf(plugin, field);
        return field.Type switch
        {
            "number" => new NumberSetting(field.Label, "plugins.setField", "value",
                _ => Setting.TryParse(Read(), out double number) ? number : 0, run, arguments)
            {
                Help = field.Help,
                Minimum = field.Minimum ?? 0,
                Maximum = field.Maximum ?? 1,
                HasSlider = field.Minimum is not null && field.Maximum is not null,
            },
            "bool" => new BoolSetting(field.Label, "plugins.setField", "value", _ => Read() == "true", run, arguments) { Help = field.Help },
            "choice" => new ChoiceSetting(field.Label, "plugins.setField", "value", field.Choices, _ => Read(), run, arguments) { Help = field.Help },
            _ => new TextSetting(field.Label, "plugins.setField", "value", _ => Read(), run, arguments) { Help = field.Help },
        };
    }

    private void RefreshPanels()
    {
        foreach (LuaPlugin plugin in Items)
        {
            plugin.Panel?.Refresh(_model.Map?.Document);
            foreach (PluginTool tool in plugin.MadeTools)
            {
                tool.Refresh(_model.Map?.Document);
            }
        }
    }

    private void ReadSettings()
    {
        if (_model.Diagnostics.Settings is not UserSettings settings)
        {
            return;
        }
        PluginSettingsDto plugins = settings.Plugins;
        _off.Clear();
        _off.UnionWith(plugins.Off ?? []);
        _values.Clear();
        foreach ((string plugin, Dictionary<string, string> values) in plugins.Values ?? [])
        {
            _values[plugin] = new Dictionary<string, string>(values, StringComparer.Ordinal);
        }
    }

    private void SaveSettings()
    {
        if (_model.Diagnostics.Settings is not UserSettings settings)
        {
            return;
        }
        try
        {
            settings.SetPlugins(new PluginSettingsDto
            {
                Off = _off.Count == 0 ? null : [.. _off.Order(StringComparer.OrdinalIgnoreCase)],
                Values = _values.Count == 0 ? null : _values.ToDictionary(pair => pair.Key, pair => pair.Value),
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FAForever.MapEditor.Project.MapProjectException)
        {
            Log.LogWarning(exception, "Could not remember the plugins' settings in {File}: {Reason}", settings.File, exception.Message);
        }
    }

    #endregion

    #region Running

    /// <summary>
    /// Runs a plugin's command as one step of undo (<see cref="PluginRun"/>): what it edits and the
    /// commands it runs are undone at once, and nothing is kept when it fails or runs out of time.
    /// </summary>
    /// <returns>What its function returned, as JSON.</returns>
    /// <exception cref="InvalidOperationException">It failed: the message says why.</exception>
    public async Task<JsonNode?> RunAsync(string commandId, JsonObject arguments)
    {
        LuaPlugin plugin = Items.FirstOrDefault(p => p.Commands.Any(c => c.Id == commandId))
            ?? throw new ArgumentException($"No plugin has a command {commandId}");
        PluginCommand command = plugin.Commands.First(c => c.Id == commandId);
        if (plugin.State is null || !plugin.IsLoaded)
        {
            throw new InvalidOperationException($"The plugin {plugin.Id} is not loaded");
        }
        if (plugin.IsRunning)
        {
            throw new InvalidOperationException($"The plugin {plugin.Id} is running already");
        }
        EditedMap map = _model.Map ?? throw new InvalidOperationException("No map is open");
        string title = $"{plugin.Manifest.Name}: {command.Title}";

        using Performance.Span measure = Performance.Measure("plugins.run");
        using PluginRun run = new PluginRun(map.Session, title);
        using PluginClock clock = new PluginClock(RunLimit);
        plugin.Context = new PluginContext($"the command {command.Title}", () => run.Document, clock, run);
        _runs++;
        string? before = _model.Status;
        LuaValue[] results;
        bool changed;
        try
        {
            results = await plugin.State.CallAsync(command.Run, [LuaConvert.ToLua(arguments)], clock.Token);
            changed = run.Commit();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            string message = Describe(exception, clock, $"The command {command.Title}");
            plugin.AddError(message);
            Changed();
            Log.LogError(exception, "The plugin {Plugin} failed in {Command}: {Reason}", plugin.Id, command.Id, message);
            throw new InvalidOperationException($"{plugin.Manifest.Name}: {message}; nothing was changed", exception);
        }
        finally
        {
            plugin.Context = null;
            _runs--;
        }
        _model.Selection.Prune(map.Document);
        Log.LogInformation("The plugin {Plugin} ran {Command}{Changed}", plugin.Id, command.Id, changed ? " and changed the map" : "");
        if (_model.Status == before)
        {
            _model.Status = changed ? $"{title}: done" : $"{title}: the map is as it was";
        }
        if (changed)
        {
            Raise("changed", title, "edit");
        }
        return results.Length > 0 ? LuaConvert.ToJson(results[0]) : null;
    }

    /// <summary>
    /// Runs a plugin's check on a document, at once: a check only reads, and may not wait.
    /// </summary>
    internal IEnumerable<Problem> Check(LuaPlugin plugin, PluginCheck check, ProblemContext context)
    {
        if (plugin.State is null || plugin.IsRunning)
        {
            return [];
        }
        using Performance.Span measure = Performance.Measure("plugins.check");
        using PluginClock clock = new PluginClock(CallLimit);
        if (!CallNow(plugin, new PluginContext($"The check {check.Title}", () => context.Document, clock), $"the check {check.Title}",
            () => plugin.State.CallAsync(check.Run, [], clock.Token), out LuaValue[] results))
        {
            throw new InvalidOperationException(plugin.Errors[^1]);
        }
        if (results.Length == 0 || results[0].Type == LuaValueType.Nil)
        {
            return [];
        }
        if (results[0].Type != LuaValueType.Table)
        {
            throw new InvalidOperationException($"The check {check.Title} returned a {results[0].TypeToString()}: it returns a list of problems");
        }
        return [.. LuaConvert.Items(results[0].Read<LuaTable>()).Select(item => ProblemOf(check, item))];
    }

    private static Problem ProblemOf(PluginCheck check, LuaValue item)
    {
        if (item.Type == LuaValueType.String)
        {
            return new Problem(check.Id, ProblemSeverity.Warning, item.Read<string>());
        }
        if (item.Type != LuaValueType.Table)
        {
            throw new InvalidOperationException($"A problem of {check.Title} is a text or a table, not a {item.TypeToString()}");
        }
        LuaTable table = item.Read<LuaTable>();
        string message = LuaConvert.Text(table, "message") ?? throw new InvalidOperationException($"A problem of {check.Title} has no message");
        ProblemSeverity severity = LuaConvert.Text(table, "severity")?.ToLowerInvariant() switch
        {
            "error" => ProblemSeverity.Error,
            "info" or "note" => ProblemSeverity.Info,
            _ => ProblemSeverity.Warning,
        };
        ObjectRef? thing = LuaConvert.Text(table, "thing") is string text ? ObjectRef.Parse(text) : null;
        Vector2? place = LuaConvert.Number(table, "x") is double x && LuaConvert.Number(table, "z") is double z ? new Vector2((float)x, (float)z) : null;
        return new Problem(check.Id, severity, message, thing, place);
    }

    /// <summary>
    /// Calls the handlers of an event in every plugin that is on and not running; they only read.
    /// </summary>
    /// <returns>What the handlers returned, the texts (a reason to stop a save or export).</returns>
    private List<(LuaPlugin Plugin, string Text)> Raise(string name, params LuaValue[] arguments)
    {
        List<(LuaPlugin, string)> answers = [];
        foreach (LuaPlugin plugin in Items.ToList())
        {
            if (plugin.State is null || plugin.IsRunning || !plugin.Handlers.TryGetValue(name, out List<LuaFunction>? handlers))
            {
                continue;
            }
            foreach (LuaFunction handler in handlers.ToList())
            {
                using Performance.Span measure = Performance.Measure("plugins.event");
                using PluginClock clock = new PluginClock(CallLimit);
                if (CallNow(plugin, new PluginContext($"The handler of {name}", () => _model.Map?.Document, clock), $"the handler of {name}",
                    () => plugin.State.CallAsync(handler, arguments, clock.Token), out LuaValue[] results)
                    && results.Length > 0 && results[0].Type == LuaValueType.String && results[0].Read<string>() is { Length: > 0 } text)
                {
                    answers.Add((plugin, text));
                }
            }
        }
        return answers;
    }

    private void Veto(string name, VetoableEventArgs e)
    {
        foreach ((LuaPlugin plugin, string reason) in Raise(name, e.Folder is string folder ? folder : LuaValue.Nil))
        {
            e.Veto($"{plugin.Manifest.Name}: {reason}");
        }
    }

    /// <summary>
    /// Calls a plugin's Lua that must end at once (loading, a check, an event): an error or a call
    /// that waits is the plugin's error, logged and shown on the page.
    /// </summary>
    /// <returns>Whether it succeeded.</returns>
    private bool CallNow(LuaPlugin plugin, PluginContext context, string what, Func<ValueTask<LuaValue[]>> call, out LuaValue[] results)
    {
        results = [];
        plugin.Context = context;
        try
        {
            ValueTask<LuaValue[]> pending = call();
            if (!pending.IsCompleted)
            {
                // a call that waits (editor.run is for commands) would run on later: stop it
                throw new InvalidOperationException("it waited for something, which only a plugin's command may do");
            }
            results = pending.Result;
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Fail(plugin, what, exception, context.Clock);
            return false;
        }
        finally
        {
            plugin.Context = null;
        }
    }

    private void Fail(LuaPlugin plugin, string what, Exception exception, PluginClock? clock = null)
    {
        string message = Describe(exception, clock, char.ToUpperInvariant(what[0]) + what[1..]);
        plugin.AddError(message);
        Log.LogError(exception, "The plugin {Plugin} failed in {What}: {Reason}", plugin.Id, what, message);
        Changed();
    }

    /// <summary>
    /// What went wrong in a plugin's Lua, in words: <c>plugin.lua:3: attempt to index a nil value</c>.
    /// </summary>
    private static string Describe(Exception exception, PluginClock? clock, string what)
    {
        if (exception is OperationCanceledException && clock?.IsUp == true)
        {
            return $"{what} took longer than {clock.Limit.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)} s and was stopped";
        }
        string message = exception.Message;
        const string Prefix = "Lua-CSharp: ";
        if (message.StartsWith(Prefix, StringComparison.Ordinal))
        {
            message = message[Prefix.Length..];
        }
        message = message.Replace("[string \"", "", StringComparison.Ordinal).Replace("\"]:", ":", StringComparison.Ordinal);
        return $"{what} failed: {message}";
    }

    #endregion

    /// <summary>
    /// The plugins as automation's <c>state</c> shows them: each on or off, loaded, its errors and
    /// what it registered, with its fields' values.
    /// </summary>
    public JsonObject Describe() => new JsonObject
    {
        ["folder"] = Folder,
        ["open"] = _model.SelectedMode?.Definition.Id == PluginsMode.Id,
        ["summary"] = Summary,
        ["chosen"] = (Parts.Chosen.Content as PluginPart)?.Plugin.Id,
        ["items"] = new JsonArray([.. Items.Select(plugin => (JsonNode)new JsonObject
        {
            ["id"] = plugin.Id,
            ["name"] = plugin.Manifest.Name,
            ["version"] = plugin.Manifest.Version,
            ["category"] = plugin.ListedUnder,
            ["kind"] = plugin.Kind,
            ["on"] = plugin.IsOn,
            ["loaded"] = plugin.IsLoaded,
            ["running"] = plugin.IsRunning,
            ["errors"] = new JsonArray([.. plugin.Errors.Select(error => (JsonNode)JsonValue.Create(error))]),
            ["commands"] = new JsonArray([.. plugin.Commands.Select(command => (JsonNode)JsonValue.Create(command.Id))]),
            ["checks"] = new JsonArray([.. plugin.Checks.Select(check => (JsonNode)JsonValue.Create(check.Id))]),
            ["tools"] = new JsonArray([.. plugin.Tools.Select(tool => (JsonNode)new JsonObject { ["id"] = tool.Id, ["mode"] = tool.Mode, ["title"] = tool.Title, ["fields"] = new JsonArray([.. tool.Fields.Select(key => (JsonNode)JsonValue.Create(key))]) })]),
            ["events"] = new JsonArray([.. plugin.Handlers.Keys.Order(StringComparer.Ordinal).Select(name => (JsonNode)JsonValue.Create(name))]),
            ["fields"] = new JsonObject([.. plugin.Fields.Select(field => new KeyValuePair<string, JsonNode?>(field.Key, ValueOf(plugin, field)))]),
        })]),
    };

    private void Changed()
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(IsEmpty));
        SyncParts();
    }
}

/// <summary>
/// A plugin's check (<c>plugin.check{...}</c>) among the editor's (user stories F-14, F-15).
/// </summary>
internal sealed class LuaProblemCheck(PluginsViewModel host, LuaPlugin plugin, PluginCheck check) : IProblemCheck
{
    public string Id => check.Id;

    public string Title => check.Title;

    public IEnumerable<Problem> Check(ProblemContext context) => host.Check(plugin, check, context);
}
