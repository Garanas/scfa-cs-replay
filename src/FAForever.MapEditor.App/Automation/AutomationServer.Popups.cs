using System.Reflection;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using FAForever.MapEditor.App.Controls;

namespace FAForever.MapEditor.App.Automation;

/// <summary>
/// What is open over the editor's windows: a combo box's list, a menu, the options row's More, a
/// popup, a context menu or a flyout. They show in the state's <c>popups</c> with their items, in
/// <c>ui</c> as nodes of type <c>Popup</c>, <c>click</c> and <c>set</c> reach their controls, and
/// screenshots draw the popup windows over the editor's.
/// </summary>
public sealed partial class AutomationServer
{
    /// <summary>
    /// One popup that is open.
    /// </summary>
    /// <param name="Kind"><c>list</c>, <c>menu</c>, <c>more</c>, <c>popup</c>, <c>context menu</c> or <c>flyout</c>.</param>
    /// <param name="What">What it is, for the state: the control it belongs to.</param>
    /// <param name="Content">Its controls (null for none to describe: a combo box's list is its owner's items).</param>
    private sealed record OpenPopup(string Kind, string What, Control? Content)
    {
        /// <summary>
        /// The window the content lies in: the editor's own for a popup drawn inside it, a window of its own for the
        /// operating system's popups.
        /// </summary>
        public TopLevel? Root => Content is null ? null : TopLevel.GetTopLevel(Content);
    }

    /// <summary>
    /// The popups open over the editor's windows, in the order of the trees.
    /// </summary>
    private List<OpenPopup> OpenPopups()
    {
        List<OpenPopup> open = [];
        foreach (Window each in Windows)
        {
            foreach (Control control in each.GetVisualDescendants().OfType<Control>())
            {
                switch (control)
                {
                    case ComboBox { IsDropDownOpen: true } box:
                        open.Add(new OpenPopup("list", $"the list of {Describe(box)}", box.GetVisualDescendants().OfType<Popup>().FirstOrDefault()?.Child as Control));
                        break;
                    case MenuItem { IsSubMenuOpen: true } item:
                        open.Add(new OpenPopup("menu", $"the menu {Describe(item)}", item.GetVisualDescendants().OfType<Popup>().FirstOrDefault()?.Child as Control));
                        break;
                    case ToolRow { IsExpanded: true } row:
                        open.Add(new OpenPopup("more", $"More of {(Avalonia.Automation.AutomationProperties.GetName(row) is { Length: > 0 } name ? name : row.Name is { Length: > 0 } named ? named : "the tool's options row")}", null));
                        break;
                    case Popup { IsOpen: true } popup when popup.TemplatedParent is not (ComboBox or MenuItem):
                        open.Add(new OpenPopup("popup", $"a popup in {Describe(popup.Parent as Visual ?? popup)}", popup.Child as Control));
                        break;
                }
                if (control.ContextMenu is { IsOpen: true } menu)
                {
                    open.Add(new OpenPopup("context menu", $"the context menu of {Describe(control)}", menu));
                }
                foreach (FlyoutBase? flyout in new[] { (control as Button)?.Flyout, control.ContextFlyout, FlyoutBase.GetAttachedFlyout(control) }.Distinct())
                {
                    if (flyout is { IsOpen: true })
                    {
                        open.Add(new OpenPopup("flyout", $"the flyout of {Describe(control)}", FlyoutContent(flyout)));
                    }
                }
            }
        }
        return open;
    }

    /// <summary>
    /// The control a flyout shows (its presenter: the menu's items), found through its popup.
    /// </summary>
    private static Control? FlyoutContent(FlyoutBase flyout)
    {
        PropertyInfo? popupProperty = typeof(PopupFlyoutBase).GetProperty("Popup", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return (popupProperty?.GetValue(flyout) as Popup)?.Child as Control;
    }

    /// <summary>
    /// The windows of the operating system's popups that are open (a context menu, a flyout, a
    /// combo box's list), which lie apart from the editor's windows.
    /// </summary>
    private IEnumerable<TopLevel> PopupWindows() => OpenPopups().Select(popup => popup.Root)
        .OfType<TopLevel>().Where(root => !Windows.Contains(root)).Distinct();

    /// <summary>
    /// The items of a popup's content, for the state: the menu items and what lists in it (the label,
    /// text and whether it is enabled).
    /// </summary>
    private static JsonArray ItemsOf(Control content) => [.. content.GetVisualDescendants().OfType<Control>()
        .Where(control => control is MenuItem or ComboBoxItem or ListBoxItem or CheckBox || control is Button and not RepeatButton)
        .Select(control => (JsonNode)new JsonObject
        {
            ["type"] = control.GetType().Name,
            ["label"] = Avalonia.Automation.AutomationProperties.GetName(control) is { Length: > 0 } name ? name : null,
            ["text"] = UiTree.TextOf(control),
            ["enabled"] = control.IsEnabled,
        })];

    /// <summary>
    /// The popups open over the windows for the state: what each is and its items (null when none
    /// is open; <c>key key=Escape</c> closes one).
    /// </summary>
    private JsonArray? PopupsState()
    {
        List<OpenPopup> open = OpenPopups();
        return open.Count == 0 ? null : [.. open.Select(popup => (JsonNode)new JsonObject
        {
            ["kind"] = popup.Kind,
            ["what"] = popup.What,
            ["items"] = popup.Content is null || popup.Kind is "list" or "menu" ? null : ItemsOf(popup.Content),
        })];
    }

    /// <summary>
    /// The open popups as nodes of <c>ui</c>: their controls, places relative to the popup's own window.
    /// </summary>
    private IEnumerable<JsonNode> DescribePopups(bool hidden) => OpenPopups().Where(popup => popup.Content is not null && popup.Kind is not "more").Select(popup => (JsonNode)new JsonObject
    {
        ["type"] = "Popup",
        ["name"] = popup.Kind,
        ["label"] = popup.What,
        ["note"] = "places inside are relative to the popup's own window",
        ["children"] = UiTree.Describe(popup.Content!, hidden),
    });

    /// <summary>
    /// A screenshot of the window with the operating system's popups drawn over it where they lie.
    /// </summary>
    private void DrawPopups(RenderTargetBitmap bitmap, double scale)
    {
        using Avalonia.Media.DrawingContext context = bitmap.CreateDrawingContext(false);
        foreach (TopLevel popup in PopupWindows())
        {
            if (!popup.IsVisible)
            {
                continue;
            }
            PixelPoint at = popup.PointToScreen(default);
            Point inWindow = window.PointToClient(at);
            PixelSize size = new PixelSize(Math.Max(1, (int)(popup.Bounds.Width * scale)), Math.Max(1, (int)(popup.Bounds.Height * scale)));
            using RenderTargetBitmap drawn = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
            drawn.Render(popup);
            context.DrawImage(drawn, new Rect(drawn.Size), new Rect(inWindow.X, inWindow.Y, popup.Bounds.Width, popup.Bounds.Height));
        }
    }
}
