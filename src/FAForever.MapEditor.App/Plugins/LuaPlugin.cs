using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.App.ViewModels;
using Lua;

namespace FAForever.MapEditor.App.Plugins;

/// <summary>
/// What a plugin says about itself in <c>plugin.json</c> next to its <c>plugin.lua</c>; each part
/// may be left out (the name is then the folder's).
/// </summary>
public sealed record PluginManifest(string Name, string Version, string Description)
{
    public const string File = "plugin.json";

    public const string Script = "plugin.lua";

    private sealed record Dto(string? Name, string? Version, string? Description);

    /// <exception cref="InvalidDataException">The manifest is no JSON of that shape.</exception>
    public static PluginManifest Read(string folder)
    {
        string path = Path.Combine(folder, File);
        string name = Path.GetFileName(folder);
        if (!System.IO.File.Exists(path))
        {
            return new PluginManifest(name, "", "");
        }
        try
        {
            Dto dto = JsonSerializer.Deserialize<Dto>(System.IO.File.ReadAllText(path), JsonSerializerOptions.Web) ?? new Dto(null, null, null);
            return new PluginManifest(string.IsNullOrWhiteSpace(dto.Name) ? name : dto.Name.Trim(), dto.Version?.Trim() ?? "", dto.Description?.Trim() ?? "");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"{File} is not valid: {exception.Message}", exception);
        }
    }
}

/// <summary>
/// A command a plugin registered (<c>plugin.command{...}</c>): in the Plugins menu, the palette
/// and automation as <c>plugin.&lt;folder&gt;.&lt;id&gt;</c>.
/// </summary>
public sealed record PluginCommand(string Id, string Title, string? Description, LuaFunction Run);

/// <summary>
/// A check a plugin registered (<c>plugin.check{...}</c>): in the Problems tab as <c>plugin.&lt;folder&gt;.&lt;id&gt;</c>.
/// </summary>
public sealed record PluginCheck(string Id, string Title, LuaFunction Run);

/// <summary>
/// A tool a plugin registered (<c>plugin.tool{...}</c>): the left mouse in the views while it is
/// active, in a mode's tools as <c>plugin.&lt;folder&gt;.&lt;id&gt;</c> (chosen by <c>tool.plugin.&lt;folder&gt;.&lt;id&gt;</c>).
/// </summary>
/// <param name="Mode">The mode whose tools it joins: <c>terrain</c>, <c>props</c>, ...</param>
/// <param name="Radius">The circle the views draw around the mouse, in ogrids; 0 for none.</param>
/// <param name="RadiusField">A number field of the plugin whose value is the radius, instead.</param>
/// <param name="Fields">The keys of the plugin's fields its options row shows.</param>
/// <param name="Press">Called with the place and the keys held when the mouse is pressed; the stroke's edits start.</param>
/// <param name="Drag">Called at each place the mouse is dragged to.</param>
/// <param name="Release">Called when the mouse is let go; the stroke's edits become one step of undo.</param>
public sealed record PluginToolSpec(string Id, string Title, string Mode, string? Description, double Radius, string? RadiusField,
    IReadOnlyList<string> Fields, LuaFunction? Press, LuaFunction? Drag, LuaFunction? Release);

/// <summary>
/// A field of a plugin's panel (<c>plugin.field{...}</c>), shown on the Plugins page; its value is
/// the user's, remembered in the settings.
/// </summary>
/// <param name="Type"><c>number</c>, <c>text</c>, <c>bool</c> or <c>choice</c>.</param>
public sealed record PluginField(string Key, string Label, string Type, string Default, double? Minimum, double? Maximum, IReadOnlyList<string> Choices, string? Help);

/// <summary>
/// A plugin of the workspace (user story F-15): a folder of <c>plugins/</c> with its <c>plugin.lua</c>,
/// on or off, what it registered when it was loaded, and its errors. A line of the Plugins page.
/// </summary>
public sealed partial class LuaPlugin : ObservableObject
{
    private readonly List<string> _errors = [];

    public LuaPlugin(string folder)
    {
        Folder = Path.GetFullPath(folder);
        Id = Path.GetFileName(Folder);
        Manifest = new PluginManifest(Id, "", "");
    }

    /// <summary>
    /// The plugin's folder; Lua reads nothing outside it.
    /// </summary>
    public string Folder { get; }

    /// <summary>
    /// The folder's name, which its commands and checks are named after.
    /// </summary>
    public string Id { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(Caption))]
    public partial PluginManifest Manifest { get; internal set; }

    public string Title => Manifest.Version.Length > 0 ? $"{Manifest.Name} {Manifest.Version}" : Manifest.Name;

    /// <summary>
    /// The description and what it registered, as the page shows it under the title.
    /// </summary>
    public string Caption
    {
        get
        {
            List<string> parts = [];
            if (Manifest.Description.Length > 0)
            {
                parts.Add(Manifest.Description);
            }
            if (IsOn && IsLoaded)
            {
                parts.Add(Registered);
            }
            return string.Join(" ", parts);
        }
    }

    /// <summary>
    /// E.g. <c>2 commands, 1 check, a panel of 3 fields.</c>
    /// </summary>
    public string Registered
    {
        get
        {
            List<string> parts = [];
            if (Commands.Count > 0)
            {
                parts.Add($"{Commands.Count} command{(Commands.Count == 1 ? "" : "s")}");
            }
            if (Tools.Count > 0)
            {
                parts.Add($"{Tools.Count} tool{(Tools.Count == 1 ? "" : "s")}");
            }
            if (Checks.Count > 0)
            {
                parts.Add($"{Checks.Count} check{(Checks.Count == 1 ? "" : "s")}");
            }
            if (Fields.Count > 0)
            {
                parts.Add($"a panel of {Fields.Count} field{(Fields.Count == 1 ? "" : "s")}");
            }
            return parts.Count == 0 ? "Registers nothing." : string.Join(", ", parts) + ".";
        }
    }

    /// <summary>
    /// Whether the user has it on; the settings remember the plugins turned off.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Caption))]
    public partial bool IsOn { get; set; } = true;

    /// <summary>
    /// Whether its script ran and what it registered is in the editor.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Caption))]
    public partial bool IsLoaded { get; internal set; }

    /// <summary>
    /// The last errors, newest last (at most <see cref="MaxErrors"/>): loading, a run, a check, an event.
    /// </summary>
    public IReadOnlyList<string> Errors => _errors;

    public const int MaxErrors = 5;

    /// <summary>
    /// The errors as the page shows them, one per line; empty without.
    /// </summary>
    public string ErrorText => string.Join(Environment.NewLine, _errors);

    public bool HasErrors => _errors.Count > 0;

    public List<PluginCommand> Commands { get; } = [];

    public List<PluginCheck> Checks { get; } = [];

    /// <summary>
    /// The tools it registered (<c>plugin.tool{...}</c>).
    /// </summary>
    public List<PluginToolSpec> Tools { get; } = [];

    /// <summary>
    /// Its tools as the editor has them in the modes, while it is loaded.
    /// </summary>
    internal List<Tools.PluginTool> MadeTools { get; } = [];

    public List<PluginField> Fields { get; } = [];

    /// <summary>
    /// The functions given to <c>editor.on</c>, by event.
    /// </summary>
    public Dictionary<string, List<LuaFunction>> Handlers { get; } = new Dictionary<string, List<LuaFunction>>(StringComparer.Ordinal);

    /// <summary>
    /// The panel's fields as a group of settings, for the page; null without fields.
    /// </summary>
    [ObservableProperty]
    public partial SettingGroup? Panel { get; internal set; }

    /// <summary>
    /// The commands as buttons for the page.
    /// </summary>
    public ObservableCollection<PluginCommandItem> CommandItems { get; } = [];

    public bool HasCommands => CommandItems.Count > 0;

    /// <summary>
    /// The Lua state the script runs in; one per plugin, made again when it is loaded again.
    /// </summary>
    internal LuaState? State { get; set; }

    /// <summary>
    /// What its Lua may do now: set while its Lua runs, null otherwise.
    /// </summary>
    internal PluginContext? Context { get; set; }

    /// <summary>
    /// Whether its Lua runs now (a command that waits for another command): its checks and events
    /// wait, since a Lua state runs one thing at a time.
    /// </summary>
    public bool IsRunning => Context is not null;

    internal void AddError(string error)
    {
        _errors.Add(error);
        if (_errors.Count > MaxErrors)
        {
            _errors.RemoveAt(0);
        }
        OnPropertyChanged(nameof(Errors));
        OnPropertyChanged(nameof(ErrorText));
        OnPropertyChanged(nameof(HasErrors));
    }

    internal void ClearErrors()
    {
        _errors.Clear();
        OnPropertyChanged(nameof(Errors));
        OnPropertyChanged(nameof(ErrorText));
        OnPropertyChanged(nameof(HasErrors));
    }

    /// <summary>
    /// Forgets what it registered: it is turned off or loaded again.
    /// </summary>
    internal void Forget()
    {
        Commands.Clear();
        Tools.Clear();
        MadeTools.Clear();
        Checks.Clear();
        Fields.Clear();
        Handlers.Clear();
        CommandItems.Clear();
        Panel = null;
        State?.Dispose();
        State = null;
        IsLoaded = false;
        OnPropertyChanged(nameof(HasCommands));
        OnPropertyChanged(nameof(Registered));
    }

    /// <summary>
    /// Says again that it is on or off, for a check box that turned over by itself.
    /// </summary>
    internal void Refresh() => OnPropertyChanged(nameof(IsOn));

    internal void Registrations()
    {
        OnPropertyChanged(nameof(HasCommands));
        OnPropertyChanged(nameof(Registered));
        OnPropertyChanged(nameof(Caption));
    }
}

/// <summary>
/// A button of the Plugins page that runs a plugin's command.
/// </summary>
/// <param name="CommandId">The registry's id, <c>plugin.&lt;folder&gt;.&lt;id&gt;</c>.</param>
public sealed record PluginCommandItem(string CommandId, string Title, string? Description);
