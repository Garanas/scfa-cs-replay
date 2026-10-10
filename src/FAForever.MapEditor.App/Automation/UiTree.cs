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
    /// <remarks>
    /// A control named or labelled so comes before one that only reads the text, and a control
    /// one can use before a plain text: <c>Combine</c> finds the combo box labelled so, not the
    /// text beside it.
    /// </remarks>
    public static Control? Find(Visual root, string text, bool hidden = false)
    {
        List<Control> controls = [.. root.GetVisualDescendants().OfType<Control>().Where(control => hidden || control.IsEffectivelyVisible)];
        return controls.FirstOrDefault(control => string.Equals(control.Name, text, StringComparison.OrdinalIgnoreCase)
                || string.Equals(AutomationProperties.GetName(control), text, StringComparison.OrdinalIgnoreCase))
            ?? controls.FirstOrDefault(control => control is not TextBlock && string.Equals(TextOf(control), text, StringComparison.OrdinalIgnoreCase))
            ?? controls.FirstOrDefault(control => string.Equals(TextOf(control), text, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The part of the window <c>under</c> names: a control as <see cref="Find"/> finds it, or, for a
    /// heading (a text: <c>Presets</c>, <c>Inspector</c>, <c>Markers</c>), the part it heads: its
    /// <c>Section</c>, else the nearest panel around it that holds a control one can use.
    /// </summary>
    public static Control? FindPart(Visual root, string text, bool hidden = false)
    {
        Control? found = Find(root, text, hidden);
        if (found is not TextBlock heading)
        {
            return found;
        }
        for (Visual? visual = heading.GetVisualParent(); visual is Control part && visual != root; visual = visual.GetVisualParent())
        {
            if (part is Controls.Section || part.GetVisualDescendants().Any(control => control is Button or TextBox or ComboBox or Slider or ListBox or TreeView or ToggleButton
                && (hidden || ((Control)control).IsEffectivelyVisible)))
            {
                return part;
            }
        }
        return found;
    }

    /// <summary>
    /// The controls of a described tree that are of a type (its name, ignoring case: <c>Button</c>)
    /// and whose name, label or text holds a text (ignoring case), as a flat list in the order of
    /// the tree, each without what lies in it: <c>ui {type, text}</c>.
    /// </summary>
    public static JsonArray Filter(JsonArray tree, string? type, string? text)
    {
        JsonArray found = [];
        void Walk(JsonArray nodes)
        {
            foreach (JsonObject node in nodes.OfType<JsonObject>())
            {
                bool typeMatches = type is null || string.Equals(node["type"]?.GetValue<string>(), type, StringComparison.OrdinalIgnoreCase);
                bool textMatches = text is null || ((string[])["name", "label", "text"])
                    .Any(key => node[key]?.GetValue<string>()?.Contains(text, StringComparison.OrdinalIgnoreCase) == true);
                if (typeMatches && textMatches)
                {
                    JsonObject copy = (JsonObject)node.DeepClone();
                    copy.Remove("children");
                    found.Add(copy);
                }
                if (node["children"] is JsonArray children)
                {
                    Walk(children);
                }
            }
        }
        Walk(tree);
        return found;
    }

    /// <summary>
    /// What an item of a list or combo box shows: a text as it is, else its <c>Title</c>,
    /// <c>Name</c>, <c>Label</c> or <c>Text</c>, else its own text (a record's would list its fields).
    /// </summary>
    public static string? DisplayText(object? item)
    {
        if (item is null or string)
        {
            return (string?)item;
        }
        foreach (string property in (string[])["Title", "Label", "Name", "Text", "DisplayName"])
        {
            if (item.GetType().GetProperty(property)?.GetValue(item) is string { Length: > 0 } text)
            {
                return text;
            }
        }
        return item.ToString();
    }

    private static IEnumerable<JsonNode> Nodes(Visual visual, Visual root, bool hidden)
    {
        // hidden, transparent, or a text without room: nothing a person sees
        if (!hidden && visual is Control { IsVisible: false } or Control { Opacity: 0 } or TextBlock { Bounds.Width: 0 } or TextBlock { Bounds.Height: 0 })
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
            case Button chip when chip.Classes.Contains("chip"):
                // an overlay's chip above the views: on or off
                node["checked"] = chip.Classes.Contains("on");
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
                node["selected"] = DisplayText(combo.SelectedItem);
                node["items"] = new JsonArray([.. combo.Items.Take(40).Select(item => (JsonNode?)JsonValue.Create(DisplayText(item)))]);
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
    /// <remarks>
    /// An item of a list is one unless it holds controls of its own (a layer's check boxes).
    /// </remarks>
    private static bool IsLeaf(Control control) =>
        control is Button or ToggleButton or TabItem or TextBox or Slider or ComboBox or ProgressBar or TextBlock
        || control is ListBoxItem item && !item.GetVisualDescendants().Any(inner => inner is Button or ToggleButton or TextBox or Slider or ComboBox)
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
        // menu headers carry the access key's underscore; other texts keep theirs (rocklight_albedo)
        return string.IsNullOrWhiteSpace(text) ? null : control is MenuItem ? text.Replace("_", "", StringComparison.Ordinal) : text;
    }

    private static JsonArray Bounds(Control control, Visual root)
    {
        Point at = control.TranslatePoint(default, root) ?? default;
        return [Math.Round(at.X), Math.Round(at.Y), Math.Round(control.Bounds.Width), Math.Round(control.Bounds.Height)];
    }
}
