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
        "dialog {button?}: presses a button of the question over the window (state.dialog); without one, the question",
        "click {label, under?, button?: left|right, doubleClick?}: scrolls the control into view and clicks its centre",
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
        if (ToolRow.Reveal(control))
        {
            await LetInputSettleAsync();
        }
        control.BringIntoView();
        await LetInputSettleAsync();
        await NextFrameAsync();
        TopLevel top = WindowOf(control);
        Point centre = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), top)
            ?? throw new InvalidOperationException($"'{label}' is not in the window");
        if (!new Rect(top.Bounds.Size).Contains(centre))
        {
            throw new InvalidOperationException($"'{label}' lies outside the window at {centre.X:0}, {centre.Y:0}");
        }
        Avalonia.Input.MouseButton button = WindowInput.ParseButton(arguments["button"]?.GetValue<string>());
        int clicks = arguments["doubleClick"]?.GetValue<bool>() == true ? 2 : 1;
        WindowInput input = InputOf(top);
        input.Move(centre);
        for (int i = 0; i < clicks; i++)
        {
            input.Down(centre, button);
            input.Up(centre, button);
        }
        await LetInputSettleAsync();
        return new JsonObject
        {
            ["type"] = control.GetType().Name,
            ["label"] = Avalonia.Automation.AutomationProperties.GetName(control) is { Length: > 0 } name ? name : label,
            ["at"] = new JsonArray(Math.Round(centre.X), Math.Round(centre.Y)),
            ["window"] = top == window ? null : top is Views.FloatingPanelWindow floating ? ViewModels.LayoutViewModel.Name(floating.Panel) : "other",
            ["checked"] = control is ToggleButton toggle ? toggle.IsChecked : null,
            ["focused"] = Focused(),
        };
    }

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
