using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.App.Commands;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// A line of the keyboard shortcuts page: the keys or mouse, what they do, and where.
/// </summary>
/// <param name="Where">The menu of a command, the tools a key works in, or the view of a mouse gesture.</param>
public sealed record ShortcutItem(string Keys, string What, string Where)
{
    /// <summary>
    /// What a screen reader says of the line.
    /// </summary>
    public string Label => $"{Keys}: {What} ({Where})";
}

/// <summary>
/// A part of the keyboard shortcuts page under its heading.
/// </summary>
public sealed record ShortcutSection(string Title, IReadOnlyList<ShortcutItem> Items);

/// <summary>
/// The keyboard shortcuts page (Help, Keyboard shortcuts; user story F-1): every command's shortcut
/// from the registry, so a plugin's or a changed one shows as it is; the keys the tools take in the
/// views; and the mouse in each view. A search narrows every part to the lines holding each word.
/// </summary>
public sealed partial class ShortcutsViewModel(CommandRegistry commands) : ObservableObject
{
    /// <summary>
    /// The keys the views give the active tool (<c>MainViewModel.ToolKey</c>, <c>BrushKeys</c>, the
    /// tools' own <c>Key</c>) and those the views keep.
    /// </summary>
    public static IReadOnlyList<ShortcutItem> ToolKeys { get; } =
    [
        new ShortcutItem("[ and ]", "The brush's radius smaller or larger", "Brushes, stamps"),
        new ShortcutItem("Shift+[ and Shift+]", "The brush's strength lower or higher (a stamp's height)", "Brushes, stamps"),
        new ShortcutItem("Ctrl+wheel", "The brush's radius over a view; with Shift its strength", "Brushes, stamps"),
        new ShortcutItem("Q and E", "Turn the brush's picture 15 degrees left or right (Shift: 90), wherever the keyboard is but a text field; a round brush stays", "Brushes with a picture"),
        new ShortcutItem("Alt+wheel", "Turn the brush's picture 5 degrees a notch over a view", "Brushes with a picture, stamps"),
        new ShortcutItem("Q and E", "Turn the selection 15 degrees left or right (Shift: 90)", "Select tools"),
        new ShortcutItem("Q and E", "Turn what is placed: a preset, a unit, a stamp", "Place tools, presets, stamps"),
        new ShortcutItem("Shift and a drag", "Keep a line of units to 45 degree steps", "Units, Place in a line"),
        new ShortcutItem("Escape", "Cancel the line or rectangle of units being dragged", "Units, Place in a line or rectangle"),
        new ShortcutItem("[ and ]", "The scatter brush's radius smaller or larger; with Shift how many units a click scatters", "Units, Scatter"),
        new ShortcutItem("1 to 9, 0", "Pick a slot of the chosen palette: its unit or preset is placed next", "Units place tool"),
        new ShortcutItem("Tab and Shift+Tab", "The next or previous palette", "Units place tool"),
        new ShortcutItem("Arrow keys", "Move the selection an ogrid (Shift: 8)", "Select tools"),
        new ShortcutItem("R and a drag", "Turn the selection about its middle with the mouse", "Select tools"),
        new ShortcutItem("X and a drag", "Move the selection along x only", "Select tools"),
        new ShortcutItem("Z and a drag", "Move the selection along z only", "Select tools"),
        new ShortcutItem("Delete", "Delete the selection, a wave generator or a curve's point", "Select, Waves, Curve, Areas"),
        new ShortcutItem("+ and -", "Raise or lower the chosen points of a curve by 1 (Shift: 0.1, Ctrl: 5)", "Curve"),
        new ShortcutItem("Shift and a drag", "Set a curve's point height: drag it up or down on the screen; the status line shows the height, one undo step", "Curve, both views"),
        new ShortcutItem("Click a point", "Choose a curve's point (the inspector's table follows)", "Curve"),
        new ShortcutItem("Escape", "Select nothing, stop a curve or a chain, close a page or question", "Every tool"),
        new ShortcutItem("Shift and a drag", "Draw a box: select what is in it, or capture a stamp", "Select tools, Stamp"),
        new ShortcutItem("S", "What a drag does to the selected decals: move, scale, turn", "Decals select"),
        new ShortcutItem("+ and -", "Scale the selected decals (Shift: finer)", "Decals select"),
    ];

    /// <summary>
    /// What the mouse does in each view (the views keep it; the left button is the active tool's).
    /// </summary>
    public static IReadOnlyList<ShortcutItem> Mouse { get; } =
    [
        new ShortcutItem("Left button", "The active tool: paint, place, select, drag a handle", "Both views"),
        new ShortcutItem("Shift+click", "Add to the selection", "Both views"),
        new ShortcutItem("Ctrl+click", "Add or take away from the selection", "Both views"),
        new ShortcutItem("Right or middle drag", "Move the map", "Top view"),
        new ShortcutItem("Wheel", "Zoom about the mouse", "Top view"),
        new ShortcutItem("Alt+click", "Send the 3D camera to the place", "Top view"),
        new ShortcutItem("Wheel", "Move along the ray under the mouse, towards what it points at", "3D view"),
        new ShortcutItem("Middle drag", "Drag the ground with the mouse", "3D view"),
        new ShortcutItem("Space and the mouse", "Turn and tilt about the place looked at", "3D view"),
        new ShortcutItem("F", "Focus on the selection, else on the place under the mouse", "3D view"),
        new ShortcutItem("Home", "Show the whole map", "3D view"),
    ];

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    /// <summary>
    /// Words that each must be in a line's keys, what it does or where.
    /// </summary>
    [ObservableProperty]
    public partial string Query { get; set; } = "";

    /// <summary>
    /// The parts of the page as the search leaves them: commands, tool keys, the mouse; a part with
    /// no line left is left out.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Count), nameof(IsEmpty))]
    public partial IReadOnlyList<ShortcutSection> Sections { get; private set; } = [];

    /// <summary>
    /// How many lines show.
    /// </summary>
    public int Count => Sections.Sum(section => section.Items.Count);

    public bool IsEmpty => Sections.Count == 0;

    /// <summary>
    /// Opens the page, read from the registry as it is now.
    /// </summary>
    public void Open(string? query = null)
    {
        Query = query ?? "";
        Refresh();
        IsOpen = true;
    }

    [RelayCommand]
    private void Close() => IsOpen = false;

    partial void OnQueryChanged(string value) => Refresh();

    /// <summary>
    /// Every command with a shortcut, by its menu, as the menus show them.
    /// </summary>
    public IReadOnlyList<ShortcutItem> CommandShortcuts() => [.. commands.All
        .Where(command => command.Gesture is not null)
        .OrderBy(command => command.Menu.Length == 0 ? "~" : command.Menu, StringComparer.Ordinal)
        .ThenBy(command => command.Title, StringComparer.CurrentCultureIgnoreCase)
        .Select(command => new ShortcutItem(KeysOf(command.Gesture!), command.Title, command.Menu.Length == 0 ? "The views" : command.Menu))];

    private void Refresh()
    {
        string[] words = Query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        bool Matches(ShortcutItem item) => words.All(word =>
            item.Keys.Contains(word, StringComparison.OrdinalIgnoreCase) || item.What.Contains(word, StringComparison.OrdinalIgnoreCase)
            || item.Where.Contains(word, StringComparison.OrdinalIgnoreCase));
        Sections = [.. new[]
            {
                new ShortcutSection("Commands", CommandShortcuts()),
                new ShortcutSection("Keys of the tools", ToolKeys),
                new ShortcutSection("The mouse in the views", Mouse),
            }
            .Select(section => section with { Items = [.. section.Items.Where(Matches)] })
            .Where(section => section.Items.Count > 0)];
    }

    /// <summary>
    /// A gesture as people read it: <c>Ctrl+OemCloseBrackets</c> is <c>Ctrl+]</c>, <c>Ctrl+D1</c> is <c>Ctrl+1</c>.
    /// </summary>
    public static string KeysOf(string gesture) => string.Join("+", gesture.Split('+').Select(key => key switch
    {
        "OemOpenBrackets" => "[",
        "OemCloseBrackets" => "]",
        "OemPlus" => "+",
        "OemMinus" => "-",
        "OemComma" => ",",
        "OemPeriod" => ".",
        _ when key.Length == 2 && key[0] == 'D' && char.IsDigit(key[1]) => key[1..],
        _ => key,
    }));

    /// <summary>
    /// The page for automation: open, the search, and the lines that show.
    /// </summary>
    public JsonObject Describe() => new JsonObject
    {
        ["open"] = IsOpen,
        ["query"] = Query,
        ["count"] = Count,
        ["sections"] = new JsonArray([.. Sections.Select(section => (JsonNode)new JsonObject
        {
            ["title"] = section.Title,
            ["items"] = new JsonArray([.. section.Items.Select(item => (JsonNode)JsonValue.Create(item.Label))]),
        })]),
    };
}
