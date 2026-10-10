using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FAForever.MapEditor.App.ViewModels;

namespace FAForever.MapEditor.App.Views;

/// <summary>
/// The question over the window: the keyboard stays in it while it shows (Tab goes round its
/// buttons, Enter or space presses the one that has it, Escape cancels), so the window's
/// shortcuts and the views take no keys; when it is answered, the keyboard goes back to what had
/// it before.
/// </summary>
public sealed partial class QuestionDialog : UserControl
{
    private QuestionViewModel? _question;
    private IInputElement? _focusedBefore;
    private TopLevel? _window;

    public QuestionDialog()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDownInside, RoutingStrategies.Bubble);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _window = TopLevel.GetTopLevel(this);
        // the window's keys before anything else sees them, also those the options row's More took
        _window?.AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _window?.RemoveHandler(KeyDownEvent, OnWindowKeyDown);
        _window = null;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_question is not null)
        {
            _question.Opened -= OnOpened;
            _question.PropertyChanged -= OnQuestionChanged;
        }
        _question = DataContext as QuestionViewModel;
        if (_question is not null)
        {
            _question.Opened += OnOpened;
            _question.PropertyChanged += OnQuestionChanged;
        }
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        _focusedBefore = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        // once the buttons are made for the new choices
        Dispatcher.UIThread.Post(FocusChosenButton, DispatcherPriority.Loaded);
    }

    private void FocusChosenButton()
    {
        if (_question is { IsOpen: true, HasField: true })
        {
            Field.Focus(NavigationMethod.Tab);
            Field.SelectAll();
        }
        else if (_question is { IsOpen: true } question
            && Buttons.GetVisualDescendants().OfType<Button>().ElementAtOrDefault(question.FocusIndex) is Button button)
        {
            button.Focus(NavigationMethod.Tab);
        }
    }

    private void OnQuestionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(QuestionViewModel.IsOpen) && _question?.IsOpen == false)
        {
            if (_focusedBefore is InputElement { IsEffectivelyVisible: true } before)
            {
                before.Focus();
            }
            _focusedBefore = null;
        }
    }

    /// <summary>
    /// While the question shows: Escape cancels it wherever the keyboard is, and a key that reaches
    /// the window outside the question (nothing of it had the keyboard yet) gives its button the
    /// keyboard and goes no further.
    /// </summary>
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (_question?.IsOpen != true)
        {
            return;
        }
        if (e.Key == Key.Escape)
        {
            _question.Cancel();
            e.Handled = true;
        }
        else if (!Dialog.IsKeyboardFocusWithin)
        {
            FocusChosenButton();
            e.Handled = true;
        }
    }

    // the keys the buttons leave (Tab goes on to move between them): no shortcut behind the question
    private void OnKeyDownInside(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.Source == Field && _question is { IsOpen: true } question)
        {
            question.Answer(question.AccentIndex);
            e.Handled = true;
        }
        else if (e.Key != Key.Tab)
        {
            e.Handled = true;
        }
    }
}
