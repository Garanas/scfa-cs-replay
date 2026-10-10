using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.Project.Workspaces;
using FAForever.MapEditor.Project.Workspaces.Serialized;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The panels of the window that can be closed and opened again (user story V-4).
/// </summary>
public enum LayoutPanel
{
    /// <summary>The mode's panel left of the views, with its tools and sections.</summary>
    ModePanel,

    /// <summary>The inspector right of the views.</summary>
    Inspector,

    /// <summary>The dock under the views: problems, history, assets, the Console.</summary>
    Dock,

    /// <summary>The toolbar of commands in the menu bar (user story F-1).</summary>
    Toolbar,
}

/// <summary>
/// A command on the toolbar (user story F-1): a button made from the registry, as a menu item is.
/// </summary>
/// <param name="icon">The key of its icon in <c>Styles/Icons.axaml</c>, or null to show its title.</param>
public sealed partial class ToolbarItem(EditorCommand command, string? icon) : ObservableObject
{
    public EditorCommand Command => command;

    public string Id => command.Id;

    public string Title => command.Title.TrimEnd('.');

    public string? Icon => icon;

    public bool HasIcon => icon is not null;

    /// <summary>
    /// What the button's tool tip says: its title and shortcut.
    /// </summary>
    public string Tip => command.Gesture is string gesture ? $"{Title} ({gesture.Replace("+", " ")})" : Title;

    /// <summary>
    /// Whether it can run now; asked again when the registry says so.
    /// </summary>
    [ObservableProperty]
    public partial bool CanRun { get; set; }
}

/// <summary>
/// How the window is laid out (user story V-4) and its toolbar (user story F-1): which panels show,
/// their sizes, how the views share the width while side by side, and the commands on the toolbar.
/// Remembered in the user's settings (<see cref="UserSettings.Layout"/>) as soon as it changes; the
/// window applies it to its grid.
/// </summary>
public sealed partial class LayoutViewModel : ObservableObject
{
    /// <summary>
    /// The toolbar's commands until the user chooses others: undo and redo, save, the command
    /// palette and the symmetry.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultToolbar = ["edit.undo", "edit.redo", "file.save", "view.palette", "select.symmetry"];

    /// <summary>
    /// The icons of the commands that have one; the others show their title.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Icons = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["edit.undo"] = "Icon.Undo",
        ["edit.redo"] = "Icon.Redo",
        ["file.save"] = "Icon.Save",
        ["view.palette"] = "Icon.Search",
        ["select.symmetry"] = "Icon.Symmetry",
        ["file.commit"] = "Icon.Commit",
        ["view.top"] = "Icon.Map",
        ["view.3d"] = "Icon.Terrain",
        ["view.sideBySide"] = "Icon.SideBySide",
        ["assets.browse"] = "Icon.Tiles",
    };

    /// <summary>
    /// The smallest and largest size a panel can be given, in device-independent pixels.
    /// </summary>
    public const double MinimumPanelSize = 120;

    public const double MaximumPanelSize = 1200;

    private readonly CommandRegistry _commands;
    private readonly ILogger _log;
    private UserSettings? _settings;
    private bool _loading;

    public LayoutViewModel(CommandRegistry commands, ILogger log)
    {
        _commands = commands;
        _log = log;
        _commands.CanRunChanged += (_, _) => RefreshToolbar();
        ToolbarCommands = DefaultToolbar;
    }

    [ObservableProperty]
    public partial bool ShowModePanel { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowInspector { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowDock { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowToolbar { get; set; } = true;

    /// <summary>
    /// The width of the left side, where the mode's panel lies unless it is moved; null for the
    /// editor's own (the token <c>ModePanelWidth</c>).
    /// </summary>
    [ObservableProperty]
    public partial double? ModePanelWidth { get; set; }

    /// <summary>
    /// The width of the right side, where the inspector lies unless it is moved; null for the
    /// editor's own (<c>InspectorWidth</c>).
    /// </summary>
    [ObservableProperty]
    public partial double? InspectorWidth { get; set; }

    /// <summary>
    /// The height of the bottom, where the dock lies unless it is moved; null for the editor's own,
    /// which grows while the asset shelf shows in the dock.
    /// </summary>
    [ObservableProperty]
    public partial double? DockHeight { get; set; }

    /// <summary>
    /// The part of the width the top view takes while the views are side by side; null for half.
    /// </summary>
    [ObservableProperty]
    public partial double? SideBySideSplit { get; set; }

    /// <summary>
    /// The ids of the toolbar's commands, in order.
    /// </summary>
    public IReadOnlyList<string> ToolbarCommands { get; private set; }

    /// <summary>
    /// The toolbar's buttons: the commands of <see cref="ToolbarCommands"/> the registry has.
    /// </summary>
    public IReadOnlyList<ToolbarItem> Toolbar { get; private set; } = [];

    /// <summary>
    /// The view the editor showed when the layout was remembered, given back by <see cref="Load"/>.
    /// </summary>
    public MapViewTab? RememberedView { get; private set; }

    /// <summary>
    /// The view that shows now, remembered with the rest; the core sets it.
    /// </summary>
    public MapViewTab View
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                Save();
            }
        }
    }

    /// <summary>
    /// Raised when a size or the split changes, or the layout is reset: the window lays its grid out again.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Takes the layout the user's settings remember, and remembers every change there from now on.
    /// </summary>
    public void Load(UserSettings settings)
    {
        _settings = settings;
        LayoutSettingsDto layout;
        try
        {
            layout = settings.Layout;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _log.LogWarning(exception, "Could not read the layout from {File}: {Reason}", settings.File, exception.Message);
            return;
        }
        _loading = true;
        try
        {
            ShowModePanel = layout.ModePanel;
            ShowInspector = layout.Inspector;
            ShowDock = layout.Dock;
            ShowToolbar = layout.Toolbar;
            ModePanelWidth = Clamp(layout.ModePanelWidth);
            InspectorWidth = Clamp(layout.InspectorWidth);
            DockHeight = Clamp(layout.DockHeight);
            SideBySideSplit = layout.SideBySideSplit is double split ? Math.Clamp(split, 0.1, 0.9) : null;
            RememberedView = Enum.TryParse(layout.View, out MapViewTab view) ? view : null;
            if (RememberedView is MapViewTab remembered)
            {
                View = remembered;
            }
            SetToolbar(layout.ToolbarCommands);
            LoadPlaces(layout);
        }
        finally
        {
            _loading = false;
        }
        Changed?.Invoke(this, EventArgs.Empty);
        PlacesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Whether a panel shows.
    /// </summary>
    public bool IsShown(LayoutPanel panel) => panel switch
    {
        LayoutPanel.ModePanel => ShowModePanel,
        LayoutPanel.Inspector => ShowInspector,
        LayoutPanel.Dock => ShowDock,
        _ => ShowToolbar,
    };

    /// <summary>
    /// Shows or hides a panel; without <paramref name="on"/> it turns it the other way.
    /// </summary>
    /// <returns>Whether it shows now.</returns>
    public bool Toggle(LayoutPanel panel, bool? on = null)
    {
        bool shown = on ?? !IsShown(panel);
        switch (panel)
        {
            case LayoutPanel.ModePanel:
                ShowModePanel = shown;
                break;
            case LayoutPanel.Inspector:
                ShowInspector = shown;
                break;
            case LayoutPanel.Dock:
                ShowDock = shown;
                break;
            default:
                ShowToolbar = shown;
                break;
        }
        return shown;
    }

    /// <summary>
    /// Gives a panel a size, as its splitter does: the size of the side it lies on (the width of the
    /// left or right, the height of the bottom; <see cref="LayoutViewModel"/>'s places), between
    /// <see cref="MinimumPanelSize"/> and <see cref="MaximumPanelSize"/>.
    /// </summary>
    /// <exception cref="ArgumentException">The toolbar has no size; a floating panel's window has its own.</exception>
    public void Resize(LayoutPanel panel, double size)
    {
        MovablePanel movable = panel switch
        {
            LayoutPanel.ModePanel => MovablePanel.ModePanel,
            LayoutPanel.Inspector => MovablePanel.Inspector,
            LayoutPanel.Dock => MovablePanel.Dock,
            _ => throw new ArgumentException("The toolbar is as high as its buttons; it has no size to give", nameof(panel)),
        };
        ResizeSide(SideOf(movable), size);
    }

    /// <summary>
    /// The part of the width the top view takes while side by side, as the splitter between the views sets it.
    /// </summary>
    public void Split(double topPart)
    {
        SideBySideSplit = Math.Clamp(topPart, 0.1, 0.9);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Every panel shown in its own place at its own size (none floats), the views split in half;
    /// the toolbar keeps its commands and the view that shows stays.
    /// </summary>
    public void Reset()
    {
        _loading = true;
        try
        {
            ShowModePanel = ShowInspector = ShowDock = ShowToolbar = true;
            ModePanelWidth = InspectorWidth = DockHeight = SideBySideSplit = null;
            ResetPlaces();
        }
        finally
        {
            _loading = false;
        }
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
        PlacesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Puts these commands on the toolbar, in order; null for the editor's own
    /// (<see cref="DefaultToolbar"/>). An id the registry does not know is left out and logged.
    /// </summary>
    /// <returns>The ids it left out.</returns>
    public IReadOnlyList<string> SetToolbar(IEnumerable<string>? ids)
    {
        List<string> chosen = [.. (ids ?? DefaultToolbar).Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).Distinct(StringComparer.Ordinal)];
        List<string> unknown = [.. chosen.Where(id => _commands.Find(id) is null)];
        foreach (string id in unknown)
        {
            _log.LogWarning("The toolbar names a command {Command} that does not exist; it is left out", id);
        }
        ToolbarCommands = ids is null ? DefaultToolbar : chosen;
        Toolbar = [.. chosen.Select(_commands.Find).OfType<EditorCommand>().Select(command => new ToolbarItem(command, Icons.GetValueOrDefault(command.Id)))];
        RefreshToolbar();
        OnPropertyChanged(nameof(ToolbarCommands));
        OnPropertyChanged(nameof(Toolbar));
        Save();
        return unknown;
    }

    /// <summary>
    /// Adds a command to the end of the toolbar, or takes it off.
    /// </summary>
    /// <exception cref="ArgumentException">There is no such command.</exception>
    public void SetOnToolbar(string id, bool on)
    {
        if (_commands.Find(id) is null)
        {
            throw new ArgumentException($"There is no command '{id}'");
        }
        List<string> ids = [.. ToolbarCommands.Where(other => other != id)];
        if (on)
        {
            ids.Add(id);
        }
        SetToolbar(ids);
    }

    /// <summary>
    /// What automation's <c>state.layout</c> shows.
    /// </summary>
    public JsonObject Describe() => new JsonObject
    {
        ["view"] = View.ToString(),
        ["modePanel"] = ShowModePanel,
        ["inspector"] = ShowInspector,
        ["dock"] = ShowDock,
        ["toolbar"] = ShowToolbar,
        ["modePanelWidth"] = ModePanelWidth,
        ["inspectorWidth"] = InspectorWidth,
        ["dockHeight"] = DockHeight,
        ["sideBySideSplit"] = SideBySideSplit,
        ["toolbarCommands"] = new JsonArray([.. Toolbar.Select(item => (JsonNode)new JsonObject { ["id"] = item.Id, ["canRun"] = item.CanRun })]),
        ["settings"] = _settings?.File,
        ["places"] = DescribePlaces(),
    };

    partial void OnShowModePanelChanged(bool value) => Save();

    partial void OnShowInspectorChanged(bool value) => Save();

    partial void OnShowDockChanged(bool value) => Save();

    partial void OnShowToolbarChanged(bool value) => Save();

    partial void OnModePanelWidthChanged(double? value) => Save();

    partial void OnInspectorWidthChanged(double? value) => Save();

    partial void OnDockHeightChanged(double? value) => Save();

    partial void OnSideBySideSplitChanged(double? value) => Save();

    private void RefreshToolbar()
    {
        foreach (ToolbarItem item in Toolbar)
        {
            item.CanRun = item.Command.IsIncomplete || _commands.CanRun(item.Command);
        }
    }

    private static double? Clamp(double? size) => size is double value ? Math.Clamp(value, MinimumPanelSize, MaximumPanelSize) : null;

    /// <summary>
    /// Writes the layout into the user's settings; what cannot be written is logged and the editor
    /// goes on with it in memory.
    /// </summary>
    private void Save()
    {
        if (_loading || _settings is null)
        {
            return;
        }
        try
        {
            _settings.SetLayout(WithPlaces(new LayoutSettingsDto
            {
                ModePanel = ShowModePanel,
                Inspector = ShowInspector,
                Dock = ShowDock,
                Toolbar = ShowToolbar,
                ModePanelWidth = ModePanelWidth,
                InspectorWidth = InspectorWidth,
                DockHeight = DockHeight,
                SideBySideSplit = SideBySideSplit,
                View = View.ToString(),
                ToolbarCommands = ReferenceEquals(ToolbarCommands, DefaultToolbar) ? null : [.. ToolbarCommands],
            }));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FAForever.MapEditor.Project.MapProjectException)
        {
            _log.LogWarning(exception, "Could not remember the layout in {File}: {Reason}", _settings.File, exception.Message);
        }
    }
}
