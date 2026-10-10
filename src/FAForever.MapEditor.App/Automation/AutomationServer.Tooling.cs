using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using FAForever.MapEditor.App.Controls;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using Point = Avalonia.Point;

namespace FAForever.MapEditor.App.Automation;

/// <summary>
/// Automation's tools for checking a change: setting a control by its label as a person would
/// (<c>set</c>), framing things in both views (<c>focus {ref}</c>), reading any grid of the map
/// (<c>grid</c>), and a screenshot of one part of the window (<c>screenshot {part}</c>).
/// </summary>
public sealed partial class AutomationServer
{
    /// <summary>
    /// These commands, for <c>help</c>.
    /// </summary>
    private static readonly string[] ToolingCommands =
    [
        "set {label, value, under?, commit?: enter|blur|both}",
        "focus {ref? | refs? | selection?: true} (else {x, z, radius?}: camera.focus)",
        "grid {target: heightmap|types|stratum:N|water:foam|water:flatness|water:depthBias|mask:<name>, x, z} or {target, x0, z0, x1, z1, step?}",
        "screenshot {path, part: window|options|inspector|panel|dock|rail|top|3d, settle?}",
    ];

    /// <summary>
    /// The parts of the window <c>screenshot {part}</c> takes.
    /// </summary>
    private static readonly string[] Parts = ["window", "options", "inspector", "panel", "dock", "rail", "top", "3d"];

    /// <summary>
    /// The tooling commands; false for any other.
    /// </summary>
    private async Task<bool> RunToolingAsync(string command, JsonObject arguments, JsonObject result)
    {
        switch (command)
        {
            case "set":
                result["set"] = await SetAsync(arguments);
                return true;
            case "focus" when arguments.ContainsKey("ref") || arguments.ContainsKey("refs") || arguments.ContainsKey("selection"):
            {
                // frame things in both views: the command view.frame (the selection when no refs)
                JsonObject frame = [];
                if (arguments["refs"] is JsonArray refs)
                {
                    frame["refs"] = refs.DeepClone();
                }
                else if (arguments["ref"]?.GetValue<string>() is string one)
                {
                    frame["refs"] = new JsonArray(one);
                }
                await RunCommandAsync("view.frame", frame, result, "framed");
                return true;
            }
            case "grid":
                result["grid"] = ReadGrid(arguments);
                return true;
            case "screenshot" when arguments["part"]?.GetValue<string>() is string part && part != "3d":
                if (arguments["settle"]?.GetValue<bool>() == true)
                {
                    result["settled"] = await SettleAsync(false);
                }
                result["file"] = ScreenshotPart(Text(arguments, "path"), part);
                return true;
            case "screenshot" when arguments["part"]?.GetValue<string>() == "3d":
                // the 3D view's frame read back from the card: a picture of the window does not reach it
                arguments.Remove("part");
                arguments["gpu"] = true;
                return false;
            default:
                return false;
        }
    }

    /// <summary>
    /// <c>set {label, value}</c>: sets a control found by its label, name or text as a person
    /// would, through the control itself so its bindings, coercion and commands behave as for a
    /// person: a slider gets the keyboard and its value; a text field gets the keyboard, the text
    /// typed (replacing what was there) and Enter, then loses the keyboard (<c>commit</c>); a check
    /// box or toggle is clicked when it differs; a combo box, list or tree chooses the item whose
    /// text is the value (or its index). A setting behind the tool row's More is shown first.
    /// </summary>
    private async Task<JsonObject> SetAsync(JsonObject arguments)
    {
        string label = Text(arguments, "label");
        JsonNode value = arguments["value"] ?? throw new ArgumentException("value is missing");
        Visual root = arguments["under"]?.GetValue<string>() is { Length: > 0 } under
            ? UiTree.Find(window, under) ?? throw new ArgumentException($"No control is named, labelled or reads '{under}'")
            : window;
        Control control = FindSettable(root, label) ?? throw new ArgumentException($"No control one can set is named, labelled or reads '{label}'");
        bool revealed = ToolRow.Reveal(control);
        if (revealed)
        {
            await LetInputSettleAsync();
        }
        control.BringIntoView();
        await LetInputSettleAsync();

        switch (control)
        {
            case RangeBase range:
            {
                // a slider gives its value only while a person touches it (it has the keyboard)
                double number = ValueNumber(value);
                control.Focus(NavigationMethod.Pointer);
                range.Value = Math.Clamp(number, range.Minimum, range.Maximum);
                await LetInputSettleAsync();
                break;
            }
            case TextBox box:
            {
                string text = value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : value.ToJsonString();
                string commit = arguments["commit"]?.GetValue<string>() ?? "both";
                box.Focus(NavigationMethod.Pointer);
                box.SelectAll();
                await LetInputSettleAsync();
                if (box.SelectedText.Length > 0 || text.Length == 0)
                {
                    Input.Key(Key.Back);
                }
                if (text.Length > 0)
                {
                    Input.Type(text);
                }
                if (commit is "enter" or "both")
                {
                    Input.Key(Key.Enter);
                }
                await LetInputSettleAsync();
                if (commit is "blur" or "both" && box.IsKeyboardFocusWithin)
                {
                    // leaving the field: the keyboard goes to the view, as after a click there
                    (window.MapView.IsEffectivelyVisible ? (Control)window.MapView : window.View3D).Focus();
                }
                await LetInputSettleAsync();
                break;
            }
            case ToggleButton toggle:
            {
                bool wanted = value.GetValueKind() switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => bool.Parse(value.ToString()),
                };
                if (toggle.IsChecked != wanted)
                {
                    await ClickAsync(toggle);
                }
                if (toggle.IsChecked != wanted)
                {
                    throw new InvalidOperationException($"'{label}' did not become {wanted} when clicked");
                }
                break;
            }
            case ComboBox combo:
                combo.SelectedIndex = IndexOf(combo.Items.Cast<object?>().ToList(), value, label);
                await LetInputSettleAsync();
                break;
            case ListBox list:
                list.SelectedIndex = IndexOf(list.Items.Cast<object?>().ToList(), value, label);
                await LetInputSettleAsync();
                break;
            case TreeView tree:
            {
                TreeViewItem item = tree.GetVisualDescendants().OfType<TreeViewItem>()
                    .FirstOrDefault(item => string.Equals(ItemText(item.DataContext, item), value.ToString(), StringComparison.OrdinalIgnoreCase))
                    ?? throw new ArgumentException($"'{label}' has no item '{value}' shown (expand its parent first)");
                tree.SelectedItem = item.DataContext ?? item;
                await LetInputSettleAsync();
                break;
            }
            case ListBoxItem or TreeViewItem:
                // the item itself: choose it as a click does
                await ClickAsync(control);
                break;
            default:
                throw new ArgumentException($"'{label}' is a {control.GetType().Name}, which set does not know");
        }

        return new JsonObject
        {
            ["type"] = control.GetType().Name,
            ["label"] = Avalonia.Automation.AutomationProperties.GetName(control) is { Length: > 0 } name ? name : label,
            ["value"] = control switch
            {
                RangeBase range => Math.Round(range.Value, 3),
                TextBox box => box.Text,
                ToggleButton toggle => toggle.IsChecked,
                ComboBox combo => combo.SelectedItem?.ToString(),
                ListBox list => list.SelectedItem?.ToString(),
                TreeView tree => tree.SelectedItem?.ToString(),
                ListBoxItem item => item.IsSelected,
                TreeViewItem item => item.IsSelected,
                _ => null,
            },
            ["revealed"] = revealed,
            ["focused"] = Focused(),
        };
    }

    /// <summary>
    /// The first control one can set whose name, label or text is <paramref name="label"/>: the
    /// control itself, or the one a label's text stands beside (the slider after "Radius: 12").
    /// </summary>
    private static Control? FindSettable(Visual root, string label)
    {
        static bool Settable(Control control) => control is RangeBase or TextBox or ToggleButton or ComboBox or ListBox or TreeView or ListBoxItem or TreeViewItem;
        List<Control> controls = [.. root.GetVisualDescendants().OfType<Control>().Where(control => control.IsEffectivelyVisible && control.IsEffectivelyEnabled)];
        bool Matches(Control control) =>
            string.Equals(control.Name, label, StringComparison.OrdinalIgnoreCase)
            || string.Equals(Avalonia.Automation.AutomationProperties.GetName(control), label, StringComparison.OrdinalIgnoreCase)
            || control is ContentControl { Content: string content } && string.Equals(content, label, StringComparison.OrdinalIgnoreCase);
        if (controls.FirstOrDefault(control => Settable(control) && Matches(control)) is Control found)
        {
            return found;
        }
        // an item of a list or tree by its text
        return controls.FirstOrDefault(control => control is ListBoxItem or TreeViewItem
            && string.Equals(ItemText(control.DataContext, control), label, StringComparison.OrdinalIgnoreCase));
    }

    private async Task ClickAsync(Control control)
    {
        Point centre = control.TranslatePoint(new Point(Math.Min(control.Bounds.Width / 2, 10), control.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("The control is not in the window");
        await NextFrameAsync();
        Input.Move(centre);
        Input.Down(centre);
        Input.Up(centre);
        await LetInputSettleAsync();
    }

    private static string? ItemText(object? item, Control? container) => item switch
    {
        null => container is null ? null : string.Join(" ", container.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text).Where(text => !string.IsNullOrWhiteSpace(text)).Take(1)),
        string text => text,
        _ when container is not null && container.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(block => !string.IsNullOrWhiteSpace(block.Text)) is TextBlock block
            && !string.Equals(block.Text, item.GetType().FullName, StringComparison.Ordinal) => block.Text,
        _ => item.ToString(),
    };

    /// <summary>
    /// The index of the item whose text is the value (ignoring case), or the value as an index.
    /// </summary>
    private static int IndexOf(List<object?> items, JsonNode value, string label)
    {
        string text = value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : value.ToJsonString();
        int found = items.FindIndex(item => string.Equals(item?.ToString(), text, StringComparison.OrdinalIgnoreCase));
        if (found >= 0)
        {
            return found;
        }
        if (value.GetValueKind() == JsonValueKind.Number && value.GetValue<int>() is int index && index >= 0 && index < items.Count)
        {
            return index;
        }
        throw new ArgumentException($"'{label}' has no item '{text}': {string.Join(", ", items.Take(30).Select(item => item?.ToString()))}");
    }

    private static double ValueNumber(JsonNode value) => value.GetValueKind() == JsonValueKind.Number
        ? value.GetValue<double>()
        : double.Parse(value.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// <c>grid {target, x, z}</c>: one value of a grid of the map at a place in ogrids (a grid
    /// smaller than the map, such as a half-size mask, is read at the cell under the place); with
    /// <c>x0, z0, x1, z1</c> the values of a rectangle every <c>step</c> ogrids (at most 64 by 64)
    /// with their least, greatest and mean.
    /// </summary>
    private JsonObject ReadGrid(JsonObject arguments)
    {
        MapDocument document = model.ShownDocument ?? throw new InvalidOperationException("No map is open");
        string target = Text(arguments, "target");
        Func<double, double, double> read = GridReader(document, target, out int width, out int height);
        JsonObject answer = new JsonObject { ["target"] = target, ["size"] = new JsonArray(width, height) };
        if (Number(arguments, "x0") is double x0)
        {
            double z0 = Number(arguments, "z0") ?? throw new ArgumentException("z0 is missing");
            double x1 = Number(arguments, "x1") ?? throw new ArgumentException("x1 is missing");
            double z1 = Number(arguments, "z1") ?? throw new ArgumentException("z1 is missing");
            double step = Math.Max(Number(arguments, "step") ?? Math.Max(1, Math.Max(x1 - x0, z1 - z0) / 16), Math.Max(x1 - x0, z1 - z0) / 63);
            JsonArray rows = [];
            double least = double.MaxValue, most = double.MinValue, sum = 0;
            int count = 0;
            for (double z = z0; z <= z1 + 1e-9; z += step)
            {
                JsonArray row = [];
                for (double x = x0; x <= x1 + 1e-9; x += step)
                {
                    double v = read(x, z);
                    row.Add(Math.Round(v, 3));
                    least = Math.Min(least, v);
                    most = Math.Max(most, v);
                    sum += v;
                    count++;
                }
                rows.Add(row);
            }
            answer["step"] = Math.Round(step, 3);
            answer["rows"] = rows;
            answer["min"] = Math.Round(least, 3);
            answer["max"] = Math.Round(most, 3);
            answer["mean"] = count == 0 ? null : Math.Round(sum / count, 3);
            return answer;
        }
        double px = Number(arguments, "x") ?? throw new ArgumentException("x (or x0, z0, x1, z1) is missing");
        double pz = Number(arguments, "z") ?? throw new ArgumentException("z is missing");
        answer["value"] = Math.Round(read(px, pz), 3);
        return answer;
    }

    /// <summary>
    /// How to read a grid at a place in ogrids, and its size.
    /// </summary>
    internal static Func<double, double, double> GridReader(MapDocument document, string target, out int width, out int height)
    {
        string[] parts = target.Split(':', 2);
        string kind = parts[0].ToLowerInvariant();
        string? which = parts.Length > 1 ? parts[1] : null;
        if (kind is "heightmap" or "height" or "heights")
        {
            Heightmap heights = document.Terrain.Heightmap;
            width = document.Width + 1;
            height = document.Height + 1;
            return (x, z) => heights.GetHeight(Math.Clamp((int)Math.Round(x), 0, document.Width), Math.Clamp((int)Math.Round(z), 0, document.Height));
        }
        PaintTarget paint = kind switch
        {
            "types" or "terraintypes" or "terrain" => PaintTarget.TerrainTypes,
            "stratum" or "strata" or "layer" => PaintTarget.Stratum(int.TryParse(which, out int layer) ? layer : throw new ArgumentException("stratum:N, N from 1 to 8")),
            "water" => PaintTarget.Water(Enum.TryParse(which, ignoreCase: true, out WaterMask mask) ? mask : throw new ArgumentException("water:foam, water:flatness or water:depthBias")),
            "mask" => PaintTarget.Mask(which is { Length: > 0 } ? which : throw new ArgumentException("mask:<name> of a mask for tools")),
            _ => throw new ArgumentException($"There is no grid '{target}': heightmap, types, stratum:N, water:foam|flatness|depthBias or mask:<name>"),
        };
        TiledBuffer<byte> grid = paint.Read(document);
        int w = grid.Width, h = grid.Height;
        width = w;
        height = h;
        return (x, z) => grid[Math.Clamp((int)(x * w / document.Width), 0, w - 1), Math.Clamp((int)(z * h / document.Height), 0, h - 1)];
    }

    /// <summary>
    /// The control of a part of the window, for <c>screenshot {part}</c>.
    /// </summary>
    private Control PartOf(string part) => part.ToLowerInvariant() switch
    {
        "window" => window,
        "options" => Descendant<ToolOptions>(),
        "inspector" => Descendant<Inspector>(),
        "panel" => Descendant<ModePanel>(),
        "dock" => Descendant<Views.Dock>(),
        "rail" => Descendant<ModeRail>(),
        "top" => window.MapView,
        "3d" => window.View3D,
        _ => throw new ArgumentException($"There is no part '{part}': {string.Join(", ", Parts)}"),
    };

    private T Descendant<T>() where T : Control =>
        window.GetVisualDescendants().OfType<T>().FirstOrDefault(control => control.IsEffectivelyVisible)
        ?? throw new InvalidOperationException($"The window shows no {typeof(T).Name} now");

    /// <summary>
    /// A picture of one part of the window: the window drawn, cut to the part's place (with, for
    /// the options, the settings shown under More over the views).
    /// </summary>
    private string ScreenshotPart(string path, string part)
    {
        Control control = PartOf(part);
        Rect place = new Rect(control.TranslatePoint(default, window) ?? default, control.Bounds.Size);
        if (control is ToolOptions && control.GetVisualDescendants().OfType<ToolRow>().FirstOrDefault(row => row.IsExpanded) is ToolRow open)
        {
            foreach (Control shown in open.Children.Where(child => child.IsVisible && child.Bounds.Height > 0))
            {
                if (shown.TranslatePoint(default, window) is Point at)
                {
                    place = place.Union(new Rect(at, shown.Bounds.Size));
                }
            }
        }
        place = place.Intersect(new Rect(window.Bounds.Size));
        double scale = window.RenderScaling;
        Size size = window.Bounds.Size;
        using RenderTargetBitmap whole = new RenderTargetBitmap(new PixelSize(Math.Max(1, (int)(size.Width * scale)), Math.Max(1, (int)(size.Height * scale))), new Vector(96 * scale, 96 * scale));
        whole.Render(window);
        PixelSize pixels = new PixelSize(Math.Max(1, (int)(place.Width * scale)), Math.Max(1, (int)(place.Height * scale)));
        using RenderTargetBitmap cut = new RenderTargetBitmap(pixels, new Vector(96 * scale, 96 * scale));
        using (Avalonia.Media.DrawingContext context = cut.CreateDrawingContext())
        {
            context.DrawImage(whole, place, new Rect(place.Size));
        }
        string full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        cut.Save(full, new PngBitmapEncoderOptions());
        return full;
    }
}
