using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Threading;

namespace FAForever.MapEditor.App.Automation;

/// <summary>
/// The menu bar and the window itself for automation: <c>menu {path}</c> reads a menu's items or
/// clicks one as a person does (its popup lies outside the window's tree, so <c>ui</c> and
/// <c>click</c> cannot reach it), and <c>window {width, height, close}</c> sizes the window or
/// closes it as its close button does, unsaved changes asked about.
/// </summary>
public sealed partial class AutomationServer
{
    /// <summary>
    /// These commands, for <c>help</c>.
    /// </summary>
    private static readonly string[] ShellCommands =
    [
        "menu {path?}: without a path the menu bar's menus; a menu's path (\"File\") its items (header, shortcut, enabled, notBuilt); an item's path (\"File/Save as a template\") clicks it",
        "window {width?, height?, close?}: sizes the window in device-independent pixels; close: true closes it as its close button does (the unsaved-changes question shows)",
    ];

    /// <summary>
    /// The commands of this part; false for any other.
    /// </summary>
    private async Task<bool> RunShellAsync(string command, JsonObject arguments, JsonObject result)
    {
        switch (command)
        {
            case "menu":
                result["menu"] = await MenuAsync(arguments["path"]?.GetValue<string>());
                return true;
            case "window":
                result["window"] = await WindowAsync(arguments);
                return true;
            default:
                return false;
        }
    }

    private async Task<JsonNode> MenuAsync(string? path)
    {
        IEnumerable<MenuItem> items = window.MainMenu.Items.OfType<MenuItem>();
        string[] parts = path is null ? [] : path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        MenuItem? item = null;
        foreach (string part in parts)
        {
            item = items.FirstOrDefault(candidate => string.Equals(HeaderOf(candidate), part, StringComparison.OrdinalIgnoreCase))
                ?? items.FirstOrDefault(candidate => HeaderOf(candidate).TrimEnd('.').Equals(part.TrimEnd('.'), StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"There is no menu item '{part}' in {(item is null ? "the menu bar" : HeaderOf(item))}: {string.Join(", ", items.Select(HeaderOf))}");
            items = item.Items.OfType<MenuItem>();
        }
        if (item is null || item.Items.Count > 0)
        {
            // a menu: what it holds, as its popup shows it
            return new JsonArray([.. (item?.Items ?? window.MainMenu.Items).Select(entry => (JsonNode?)(entry is MenuItem menuItem
                ? new JsonObject
                {
                    ["header"] = HeaderOf(menuItem),
                    ["shortcut"] = menuItem.InputGesture?.ToString(),
                    ["enabled"] = menuItem.Command?.CanExecute(menuItem.CommandParameter) ?? true,
                    ["notBuilt"] = menuItem.Classes.Contains("planned") ? true : null,
                    ["submenu"] = menuItem.Items.Count > 0 ? true : null,
                }
                : new JsonObject { ["separator"] = true }))]);
        }
        if (item.Command is not { } run || !run.CanExecute(item.CommandParameter))
        {
            throw new InvalidOperationException($"The menu item {path} is disabled now");
        }
        run.Execute(item.CommandParameter);
        await LetInputSettleAsync();
        return new JsonObject { ["clicked"] = path };
    }

    private static string HeaderOf(MenuItem item) => (item.Header as string ?? "").Replace("_", "", StringComparison.Ordinal);

    private async Task<JsonObject> WindowAsync(JsonObject arguments)
    {
        if (arguments["close"]?.GetValue<bool>() == true)
        {
            // as the close button: the window asks about unsaved changes first (state.dialog)
            Dispatcher.UIThread.Post(window.Close, DispatcherPriority.Background);
            await LetInputSettleAsync();
            return new JsonObject { ["closing"] = true };
        }
        if (Number(arguments, "width") is double width)
        {
            window.Width = Math.Max(window.MinWidth, width);
        }
        if (Number(arguments, "height") is double height)
        {
            window.Height = Math.Max(window.MinHeight, height);
        }
        await LetInputSettleAsync();
        await NextFrameAsync();
        return new JsonObject { ["width"] = Math.Round(window.Bounds.Width), ["height"] = Math.Round(window.Bounds.Height) };
    }
}
