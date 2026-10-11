using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using FAForever.MapEditor.App.Controls;
using FAForever.MapEditor.App.ViewModels;
using Point = Avalonia.Point;

namespace FAForever.MapEditor.App.Automation;

/// <summary>
/// Automation's answers to the questions over the window (<c>dialog {button}</c>), a click on a
/// control by its label (<c>click {label}</c>), and what the window shows over the views for the
/// state (the question, the overlays' chips, the palette's results).
/// </summary>
public sealed partial class AutomationServer
{
    /// <summary>
    /// These commands, for <c>help</c>.
    /// </summary>
    private static readonly string[] DialogCommands =
    [
        "dialog {button?, text?}: presses a button of the question over the window (state.dialog), its text field holding text first; without a button, the question",
        "click {label, under?, button?: left|right, doubleClick?}: shows the control (behind More, scrolled into view), clicks its centre once the mouse reaches it there (else refuses with why), and waits for the commands the click started (finished)",
    ];

    /// <summary>
    /// The commands of this part; false for any other.
    /// </summary>
    private async Task<bool> RunDialogToolingAsync(string command, JsonObject arguments, JsonObject result)
    {
        switch (command)
        {
            case "dialog":
            {
                QuestionViewModel question = model.Question;
                if (!question.IsOpen)
                {
                    throw new InvalidOperationException("No question is open");
                }
                if (arguments["text"]?.GetValue<string>() is string text)
                {
                    if (!question.HasField)
                    {
                        throw new ArgumentException($"The question '{question.Title}' has no text field");
                    }
                    question.FieldText = text;
                }
                if (arguments["button"]?.GetValue<string>() is { Length: > 0 } button)
                {
                    string title = question.Title;
                    if (!question.Answer(button))
                    {
                        throw new ArgumentException($"The question has no button '{button}': {string.Join(", ", question.Choices.Select(choice => choice.Text))}");
                    }
                    result["answered"] = new JsonObject { ["title"] = title, ["button"] = button };
                    // what the answer started (saving, closing the map) runs on
                    await LetInputSettleAsync();
                }
                return true;
            }
            case "click":
                result["clicked"] = await ClickLabelAsync(arguments);
                return true;
            default:
                return await RunLayoutToolingAsync(command, arguments, result);
        }
    }

    /// <summary>
    /// <c>click {label}</c>: the control named, labelled or reading the label (one can use rather
    /// than its label's text), shown first when it lies behind the options row's More or outside a
    /// scroll viewer, then clicked in its centre as the mouse does.
    /// </summary>
    private async Task<JsonObject> ClickLabelAsync(JsonObject arguments)
    {
        string label = Text(arguments, "label");
        // in the editor's window or a floating panel's (user story V-4)
        Visual? under = UnderOf(arguments);
        Control control = (under is not null ? UiTree.Find(under, label) : FindInWindows(label))
            ?? throw new ArgumentException($"No control is named, labelled or reads '{label}'");
        // shown, settled and reachable by the mouse, else refused with why (never a click elsewhere)
        (TopLevel top, Point centre, _) = await AimAsync(control, label, HeaderCentre);
        Avalonia.Input.MouseButton button = WindowInput.ParseButton(arguments["button"]?.GetValue<string>());
        int clicks = arguments["doubleClick"]?.GetValue<bool>() == true ? 2 : 1;
        WindowInput input = InputOf(top);
        int running = model.Commands.Running;
        input.Move(centre);
        for (int i = 0; i < clicks; i++)
        {
            input.Down(centre, button);
            input.Up(centre, button);
        }
        await LetInputSettleAsync();
        bool finished = await CommandsDoneAsync(running);
        return new JsonObject
        {
            ["type"] = control.GetType().Name,
            ["label"] = Avalonia.Automation.AutomationProperties.GetName(control) is { Length: > 0 } name ? name : label,
            ["at"] = new JsonArray(Math.Round(centre.X), Math.Round(centre.Y)),
            ["window"] = top == window ? null : top is Views.FloatingPanelWindow floating ? ViewModels.LayoutViewModel.Name(floating.Panel) : "other",
            ["checked"] = control is ToggleButton toggle ? toggle.IsChecked : null,
            ["focused"] = Focused(),
            ["finished"] = finished,
        };
    }

    /// <summary>
    /// Where a click on a control lands: its centre, but an item of a tree on its own line (the
    /// header's text), not in the middle of the items open below it.
    /// </summary>
    private static Point HeaderCentre(Control control)
    {
        if (control is TreeViewItem item
            && (item.GetVisualDescendants().OfType<Control>().FirstOrDefault(part => part.Name == "PART_HeaderPresenter")
                ?? item.GetVisualDescendants().OfType<Control>().FirstOrDefault(part => part.Name == "PART_Header")) is Control header
            && header.TranslatePoint(new Point(header.Bounds.Width / 2, header.Bounds.Height / 2), item) is Point inItem)
        {
            return inItem;
        }
        return new Point(control.Bounds.Width / 2, control.Bounds.Height / 2);
    }

    /// <summary>
    /// Waits (at most <see cref="ClickWaitSeconds"/>) until the commands a click started are done,
    /// so the answer's status says what the click did (writing files takes a moment); false when one
    /// still runs: it asks a question, opened the picker or a system's dialog, or takes longer.
    /// </summary>
    /// <param name="before">How many commands ran before the click.</param>
    private async Task<bool> CommandsDoneAsync(int before)
    {
        System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
        while (model.Commands.Running > before)
        {
            if (model.Question.IsOpen || model.Picker.IsOpen || watch.Elapsed.TotalSeconds > ClickWaitSeconds)
            {
                return false;
            }
            await Task.Delay(20);
            await LetInputSettleAsync();
        }
        return true;
    }

    /// <summary>
    /// How long <c>click</c> waits at most for the commands its click started.
    /// </summary>
    private const double ClickWaitSeconds = 10;

    /// <summary>
    /// The overlays' chips above the views, by id, with whether each is on.
    /// </summary>
    private JsonObject OverlaysState() =>
        new JsonObject([.. model.Overlays.Select(overlay => KeyValuePair.Create(overlay.Id, (JsonNode?)JsonValue.Create(overlay.IsOn)))]);

    /// <summary>
    /// The palette's query and first results (command ids) while it is open, else null.
    /// </summary>
    private JsonObject? PaletteState() => model.Palette.IsOpen
        ? new JsonObject
        {
            ["query"] = model.Palette.Query,
            ["selected"] = model.Palette.Selection?.Id,
            ["results"] = new JsonArray([.. model.Palette.Results.Take(20).Select(command => (JsonNode)JsonValue.Create(command.Id))]),
        }
        : null;
}
