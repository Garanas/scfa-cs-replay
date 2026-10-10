using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace FAForever.MapEditor.App.Automation;

/// <summary>
/// The window as an agent needs to see it: the controls one can read or use, with their name
/// (<c>x:Name</c>), label (<see cref="AutomationProperties.NameProperty"/>, what a screen reader
/// says), text or value, place in the window and whether they are enabled and focused. Layout
/// that holds nothing of interest is left out, its children taking its place; a button, menu
/// item or tab gives its text and is not opened up into its template.
/// </summary>
public static class UiTree
{
    /// <summary>
    /// Describes <paramref name="root"/> and what lies in it, places relative to it.
    /// </summary>
    /// <param name="hidden">Whether to describe hidden controls too.</param>
    public static JsonArray Describe(Visual root, bool hidden = false) => [.. Nodes(root, root, hidden)];

    /// <summary>
    /// Describes <paramref name="visual"/> and what lies in it, places relative to <paramref name="root"/>
    /// (the window): one part of the window, as automation's <c>ui {under}</c> gives it.
    /// </summary>
    public static JsonArray Describe(Visual visual, Visual root, bool hidden) => [.. Nodes(visual, root, hidden)];

    /// <summary>
    /// The first control (in the order of the tree) whose <c>x:Name</c>, label or text is
    /// <paramref name="text"/>, ignoring case; hidden controls only when <paramref name="hidden"/>.
    /// </summary>
    public static Control? Find(Visual root, string text, bool hidden = false) => root.GetVisualDescendants().OfType<Control>()
        .Where(control => hidden || control.IsEffectivelyVisible)
        .FirstOrDefault(control => string.Equals(control.Name, text, StringComparison.OrdinalIgnoreCase)
            || string.Equals(AutomationProperties.GetName(control), text, StringComparison.OrdinalIgnoreCase)
            || string.Equals(TextOf(control), text, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<JsonNode> Nodes(Visual visual, Visual root, bool hidden)
    {
        if (visual is Control { IsVisible: false } && !hidden)
        {
            yield break;
        }

        if (visual is Control control && Describes(control) is JsonObject node)
        {
            if (!IsLeaf(control))
            {
                JsonArray children = [.. visual.GetVisualChildren().SelectMany(child => Nodes(child, root, hidden))];
                if (children.Count > 0)
                {
                    node["children"] = children;
                }
            }
            node["bounds"] = Bounds(control, root);
            yield return node;
            yield break;
        }

        foreach (Visual child in visual.GetVisualChildren())
        {
            foreach (JsonNode inner in Nodes(child, root, hidden))
            {
                yield return inner;
            }
        }
    }

    /// <summary>
    /// The control's description, or null when it is layout that holds nothing to read or use.
    /// </summary>
    private static JsonObject? Describes(Control control)
    {
        // the names of the editor's own controls, not those inside a control's template; nor the access text of a menu item
        if (control is AccessText || control.TemplatedParent is not null && AutomationProperties.GetName(control) is not { Length: > 0 } && TextOf(control) is null && !control.Focusable)
        {
            return null;
        }
        string? name = control.Name is { Length: > 0 } x && control.TemplatedParent is null ? x : null;
        string? label = AutomationProperties.GetName(control) is { Length: > 0 } l ? l : null;
        string? text = TextOf(control);
        bool usable = control is Button or MenuItem or TabItem or TextBox or Slider or ComboBox or ToggleButton or ListBox or ListBoxItem
            or TreeView or TreeViewItem or ProgressBar || control.Focusable && control is not ScrollViewer and not Window;
        if (name is null && label is null && text is null && !usable)
        {
            return null;
        }

        JsonObject node = new JsonObject { ["type"] = control.GetType().Name };
        if (name is not null)
        {
            node["name"] = name;
        }
        if (label is not null)
        {
            node["label"] = label;
        }
        if (AutomationProperties.GetHelpText(control) is { Length: > 0 } help)
        {
            node["help"] = help;
        }
        if (text is not null)
        {
            node["text"] = text;
        }
        switch (control)
        {
            case RangeBase range:
                node["value"] = Math.Round(range.Value, 3);
                node["minimum"] = range.Minimum;
                node["maximum"] = range.Maximum;
                break;
            case ToggleButton toggle:
                node["checked"] = toggle.IsChecked;
                break;
            case MenuItem { ToggleType: not MenuItemToggleType.None } item:
                node["checked"] = item.IsChecked;
                break;
            case TabItem tab:
                node["selected"] = tab.IsSelected;
                break;
            case ListBoxItem item:
                node["selected"] = item.IsSelected;
                break;
            case ComboBox combo:
                node["selected"] = combo.SelectedItem?.ToString();
                break;
        }
        if (!control.IsEffectivelyEnabled)
        {
            node["enabled"] = false;
        }
        if (control.IsFocused || control.IsKeyboardFocusWithin && control is TextBox)
        {
            node["focused"] = true;
        }
        if (!control.IsVisible)
        {
            node["visible"] = false;
        }
        if (Controls.ToolRow.IsInOverflow(control))
        {
            // a tool's setting that does not fit in the options row: a person clicks More to reach it
            node["overflow"] = Controls.ToolRow.IsOverflowHidden(control) ? "hidden" : "shown";
        }
        return node;
    }

    /// <summary>
    /// Controls whose insides are their own template: their text says what they are.
    /// </summary>
    private static bool IsLeaf(Control control) =>
        control is Button or ToggleButton or TabItem or TextBox or Slider or ComboBox or ListBoxItem or ProgressBar or TextBlock
        || control is MenuItem { ItemCount: 0 };

    private static string? TextOf(Control control)
    {
        string? text = control switch
        {
            TextBox box => box.Text,
            TextBlock block => block.Text,
            HeaderedSelectingItemsControl { Header: string header } => header,
            HeaderedContentControl { Header: string header } => header,
            ContentControl { Content: string content } => content,
            Button or ToggleButton or ListBoxItem or TabItem => string.Join(" ", control.GetVisualDescendants().OfType<TextBlock>()
                .Select(block => block.Text).Where(t => !string.IsNullOrWhiteSpace(t))),
            _ => null,
        };
        // menu headers carry the access key's underscore
        return string.IsNullOrWhiteSpace(text) ? null : text.Replace("_", "", StringComparison.Ordinal);
    }

    private static JsonArray Bounds(Control control, Visual root)
    {
        Point at = control.TranslatePoint(default, root) ?? default;
        return [Math.Round(at.X), Math.Round(at.Y), Math.Round(control.Bounds.Width), Math.Round(control.Bounds.Height)];
    }
}
