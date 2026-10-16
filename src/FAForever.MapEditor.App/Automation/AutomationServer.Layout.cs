using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Automation;

/// <summary>
/// Automation and the panels that float in windows of their own (user story V-4): <c>ui</c>,
/// <c>click</c>, <c>set</c> and <c>screenshot part</c> look in those windows after the editor's,
/// each place relative to the window the control lies in (<c>ui</c> gives a floating window as a
/// node of type <c>FloatingWindow</c> with its panel's name), and the mouse a click gives goes to
/// that window. <c>expand {label}</c> opens tree items so <c>ui</c> lists what lies in them. The
/// state's <c>floatingWindows</c> and <c>waves</c> (the Waves tool, user story W-1).
/// </summary>
public sealed partial class AutomationServer
{
    /// <summary>
    /// These commands, for <c>help</c>.
    /// </summary>
    private static readonly string[] LayoutCommands =
    [
        "expand {label, under?, all?: true for everything in it too, collapse?: true}: opens tree items (or an expander) so ui lists their children",
    ];

    private readonly Dictionary<TopLevel, WindowInput> _windowInputs = [];

    /// <summary>
    /// The editor's window, then the floating panels' windows.
    /// </summary>
    private IEnumerable<Window> Windows => Locator.Windows;

    private UiLocator? _locator;

    /// <summary>
    /// Where things are in the window, shared with the guided tours (<see cref="UiLocator"/>).
    /// </summary>
    private UiLocator Locator => _locator ??= new UiLocator(window, model);

    /// <summary>
    /// The first control named, labelled or reading the text, in the editor's window or a floating one.
    /// </summary>
    private Control? FindInWindows(string text, bool hidden = false) => Locator.FindInWindows(text, hidden);

    /// <summary>
    /// Where to look for <paramref name="arguments"/>' <c>under</c>: that control, else null for every window.
    /// </summary>
    private Visual? UnderOf(JsonObject arguments) => Locator.Under(arguments["under"]?.GetValue<string>());

    /// <summary>
    /// The first control or the part under a section heading named, labelled or reading the text, in
    /// the editor's window or a floating one.
    /// </summary>
    private Control? FindPartInWindows(string text, bool hidden = false) => Locator.FindPartInWindows(text, hidden);

    /// <summary>
    /// The window a control lies in: the editor's or a floating panel's.
    /// </summary>
    private TopLevel WindowOf(Visual control) => Locator.WindowOf(control);

    /// <summary>
    /// The input of the window a control lies in.
    /// </summary>
    private WindowInput InputOf(TopLevel top)
    {
        if (top == window)
        {
            return Input;
        }
        if (!_windowInputs.TryGetValue(top, out WindowInput? input))
        {
            input = new WindowInput(top);
            _windowInputs[top] = input;
        }
        return input;
    }

    /// <summary>
    /// The floating windows for <c>ui</c>: a node each, its panel's controls in it.
    /// </summary>
    private IEnumerable<JsonNode> DescribeFloatingWindows(bool hidden) => window.FloatingWindows.Select(floating => (JsonNode)new JsonObject
    {
        ["type"] = "FloatingWindow",
        ["name"] = floating is Views.FloatingPanelWindow panel ? ViewModels.LayoutViewModel.Name(panel.Panel) : null,
        ["label"] = floating.Title,
        ["place"] = new JsonArray(floating.Position.X, floating.Position.Y, Math.Round(floating.Bounds.Width), Math.Round(floating.Bounds.Height)),
        ["note"] = "places inside are relative to this window",
        ["children"] = UiTree.Describe(floating, hidden),
    });

    /// <summary>
    /// The floating windows for the state: each panel, its window's place on the screen and size.
    /// </summary>
    private JsonArray FloatingWindowsState() => [.. window.FloatingWindows.OfType<Views.FloatingPanelWindow>().Select(floating => (JsonNode)new JsonObject
    {
        ["panel"] = ViewModels.LayoutViewModel.Name(floating.Panel),
        ["title"] = floating.Title,
        ["x"] = floating.Position.X,
        ["y"] = floating.Position.Y,
        ["width"] = Math.Round(floating.Bounds.Width),
        ["height"] = Math.Round(floating.Bounds.Height),
        ["active"] = floating.IsActive,
    })];

    /// <summary>
    /// The Waves tool and the map's wave generators, for the state (user story W-1).
    /// </summary>
    private JsonObject WavesState()
    {
        WaveTool tool = model.Tool<WaveTool>();
        MapDocument? document = model.ShownDocument;
        return new JsonObject
        {
            ["heading"] = tool.Heading,
            ["speed"] = tool.Speed,
            ["selected"] = tool.Selected,
            ["generators"] = new JsonArray([.. (document?.Water.WaveGenerators ?? []).Take(100).Select((generator, index) => (JsonNode)new JsonObject
            {
                ["index"] = index,
                ["x"] = Math.Round(generator.Position.X, 2),
                ["z"] = Math.Round(generator.Position.Z, 2),
                ["heading"] = Math.Round(WaveGeneratorEdits.HeadingOf(WaveGeneratorEdits.DirectionOf(generator)), 1),
            })]),
        };
    }

    /// <summary>
    /// The commands of this part; false for any other.
    /// </summary>
    private Task<bool> RunLayoutToolingAsync(string command, JsonObject arguments, JsonObject result)
    {
        switch (command)
        {
            case "expand":
                result["expanded"] = Expand(arguments);
                return Task.FromResult(true);
            default:
                return Task.FromResult(false);
        }
    }

    /// <summary>
    /// <c>expand {label}</c>: the tree item (or expander) named, labelled or reading the label opened
    /// (with <c>all</c>, every item in it too; with <c>collapse</c>, closed), and the items it holds
    /// made, so <c>ui</c> lists them. A tree view itself opens its items.
    /// </summary>
    private JsonObject Expand(JsonObject arguments)
    {
        string label = Text(arguments, "label");
        bool all = arguments["all"]?.GetValue<bool>() == true;
        bool open = arguments["collapse"]?.GetValue<bool>() != true;
        Visual? under = UnderOf(arguments);
        Control control = (under is not null ? UiTree.Find(under, label) : FindInWindows(label))
            ?? FindItem(under, label)
            ?? throw new ArgumentException($"No tree item, tree or expander is named, labelled or reads '{label}'");
        int changed = 0;
        void Set(TreeViewItem item, bool deep)
        {
            if (item.IsExpanded != open)
            {
                item.IsExpanded = open;
                changed++;
            }
            if (deep && open)
            {
                item.ApplyTemplate();
                item.UpdateLayout();
                foreach (TreeViewItem child in item.GetLogicalChildren().OfType<TreeViewItem>().ToList())
                {
                    Set(child, deep);
                }
            }
        }
        switch (control.FindAncestorOfType<TreeViewItem>(includeSelf: true) ?? (control as TreeView as Control) ?? control.FindAncestorOfType<Expander>(includeSelf: true))
        {
            case TreeViewItem item:
                // its parents open first, so it shows
                foreach (TreeViewItem parent in item.GetVisualAncestors().OfType<TreeViewItem>().Reverse())
                {
                    if (open && !parent.IsExpanded)
                    {
                        parent.IsExpanded = true;
                        changed++;
                    }
                }
                Set(item, all);
                break;
            case TreeView tree:
                tree.UpdateLayout();
                foreach (TreeViewItem item in tree.GetLogicalChildren().OfType<TreeViewItem>().ToList())
                {
                    Set(item, all);
                }
                break;
            case Expander expander:
                if (expander.IsExpanded != open)
                {
                    expander.IsExpanded = open;
                    changed++;
                }
                break;
            default:
                throw new ArgumentException($"'{label}' is no tree item, tree or expander, nor lies in one ({control.GetType().Name})");
        }
        window.UpdateLayout();
        return new JsonObject
        {
            ["label"] = label,
            ["type"] = control.GetType().Name,
            ["expanded"] = open,
            ["changed"] = changed,
        };
    }

    /// <summary>
    /// A tree item whose text is the label, also among those hidden in closed items.
    /// </summary>
    private Control? FindItem(Visual? under, string label) =>
        (under is not null ? [under] : Windows.Cast<Visual>())
        .SelectMany(root => root.GetVisualDescendants().OfType<TreeViewItem>())
        .FirstOrDefault(item => item.Header is string header && string.Equals(header, label, StringComparison.OrdinalIgnoreCase)
            || item.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault() is TextBlock text && string.Equals(text.Text, label, StringComparison.OrdinalIgnoreCase));
}
