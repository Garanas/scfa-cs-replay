using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// A button of a question: its text, and whether it is the one that agrees (drawn as the accent).
/// </summary>
public sealed record QuestionChoice(string Text, int Index, bool IsAccent);

/// <summary>
/// The question the editor asks over its window (the unsaved changes before a map closes, the
/// confirmation of an upload): a title, a message and its buttons, answered with a click, the
/// keyboard (Tab between the buttons, Enter presses the one with the keyboard, Escape cancels) or
/// automation's <c>dialog {button}</c>. One question shows at a time; a second waits for the first.
/// </summary>
public sealed partial class QuestionViewModel : ObservableObject
{
    private TaskCompletionSource<int>? _answer;

    [ObservableProperty]
    public partial bool IsOpen { get; private set; }

    [ObservableProperty]
    public partial string Title { get; private set; } = "";

    [ObservableProperty]
    public partial string Message { get; private set; } = "";

    [ObservableProperty]
    public partial IReadOnlyList<QuestionChoice> Choices { get; private set; } = [];

    /// <summary>
    /// The label of the question's text field (a name to give), or null when it has none.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasField))]
    public partial string? FieldLabel { get; private set; }

    /// <summary>
    /// What the text field holds: what the person typed, read when a button is pressed.
    /// </summary>
    [ObservableProperty]
    public partial string FieldText { get; set; } = "";

    public bool HasField => FieldLabel is not null;

    /// <summary>
    /// The button Enter in the text field presses: the accent.
    /// </summary>
    public int AccentIndex { get; private set; }

    /// <summary>
    /// The button Escape stands for.
    /// </summary>
    public int CancelIndex { get; private set; }

    /// <summary>
    /// The button that has the keyboard when the question opens.
    /// </summary>
    public int FocusIndex { get; private set; }

    /// <summary>
    /// Raised when a question opens, for the window to give its button the keyboard.
    /// </summary>
    public event EventHandler? Opened;

    /// <summary>
    /// Asks a question and waits for its answer.
    /// </summary>
    /// <param name="buttons">The buttons' texts, in their order from left to right.</param>
    /// <param name="cancel">The button Escape presses.</param>
    /// <param name="focus">The button that has the keyboard first.</param>
    /// <param name="accent">The button that agrees, drawn as the accent; none when negative.</param>
    /// <returns>The index of the button pressed.</returns>
    public Task<int> AskAsync(string title, string message, IReadOnlyList<string> buttons, int cancel, int focus, int accent = 0) =>
        AskAsync(title, message, null, "", buttons, cancel, focus, accent);

    /// <summary>
    /// Asks a question with a text field, which has the keyboard first (its text selected), and
    /// waits for the button pressed; Enter in the field presses the accent. The text is then in
    /// <see cref="FieldText"/>.
    /// </summary>
    /// <param name="label">The field's label, e.g. <c>Name</c>.</param>
    /// <param name="text">What the field holds first.</param>
    public async Task<(int Button, string Text)> AskWithTextAsync(string title, string message, string label, string text, IReadOnlyList<string> buttons, int cancel, int accent = 0)
    {
        int button = await AskAsync(title, message, label, text, buttons, cancel, -1, accent);
        return (button, _answeredText);
    }

    private string _answeredText = "";

    private async Task<int> AskAsync(string title, string message, string? label, string text, IReadOnlyList<string> buttons, int cancel, int focus, int accent)
    {
        ArgumentOutOfRangeException.ThrowIfZero(buttons.Count);
        while (_answer is TaskCompletionSource<int> asked)
        {
            await asked.Task;
        }
        TaskCompletionSource<int> answer = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        _answer = answer;
        Title = title;
        Message = message;
        Choices = [.. buttons.Select((text, index) => new QuestionChoice(text, index, index == accent))];
        CancelIndex = Math.Clamp(cancel, 0, buttons.Count - 1);
        // the field has the keyboard first when there is one
        FocusIndex = label is null ? Math.Clamp(focus, 0, buttons.Count - 1) : -1;
        AccentIndex = Math.Clamp(accent, 0, buttons.Count - 1);
        FieldLabel = label;
        FieldText = text;
        IsOpen = true;
        Opened?.Invoke(this, EventArgs.Empty);
        return await answer.Task;
    }

    /// <summary>
    /// Presses a button of the open question.
    /// </summary>
    [RelayCommand]
    public void Choose(QuestionChoice? choice)
    {
        if (choice is not null)
        {
            Answer(choice.Index);
        }
    }

    /// <summary>
    /// Presses the button with that index; false when no question is open or it has no such button.
    /// </summary>
    public bool Answer(int index)
    {
        if (_answer is not TaskCompletionSource<int> answer || index < 0 || index >= Choices.Count)
        {
            return false;
        }
        _answer = null;
        _answeredText = FieldText;
        IsOpen = false;
        FieldLabel = null;
        answer.SetResult(index);
        return true;
    }

    /// <summary>
    /// Presses the button whose text this is (ignoring case and the access key's underscore).
    /// </summary>
    public bool Answer(string button)
    {
        string wanted = button.Replace("_", "", StringComparison.Ordinal).Trim();
        QuestionChoice? choice = Choices.FirstOrDefault(choice => string.Equals(choice.Text.Replace("_", "", StringComparison.Ordinal), wanted, StringComparison.OrdinalIgnoreCase));
        return choice is not null && Answer(choice.Index);
    }

    /// <summary>
    /// Presses the button Escape stands for.
    /// </summary>
    public void Cancel() => Answer(CancelIndex);

    /// <summary>
    /// The open question for automation's state, or null.
    /// </summary>
    public JsonObject? Describe() => IsOpen
        ? new JsonObject
        {
            ["title"] = Title,
            ["message"] = Message,
            ["buttons"] = new JsonArray([.. Choices.Select(choice => (JsonNode)JsonValue.Create(choice.Text))]),
            ["cancel"] = Choices[CancelIndex].Text,
            ["field"] = HasField ? new JsonObject { ["label"] = FieldLabel, ["text"] = FieldText } : null,
        }
        : null;
}
