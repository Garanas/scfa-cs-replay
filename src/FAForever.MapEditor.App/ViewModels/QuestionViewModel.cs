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
    public async Task<int> AskAsync(string title, string message, IReadOnlyList<string> buttons, int cancel, int focus, int accent = 0)
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
        FocusIndex = Math.Clamp(focus, 0, buttons.Count - 1);
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
        IsOpen = false;
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
        }
        : null;
}
