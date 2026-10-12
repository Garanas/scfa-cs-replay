using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FAForever.MapEditor.App.ViewModels;

namespace FAForever.MapEditor.App.Views.SketchPanels;

/// <summary>
/// The text box of a label being written (user story N-4), over the top view; see <c>LabelEditor.axaml</c>.
/// It takes the keyboard when a label is begun; Enter keeps the text, Escape drops it.
/// </summary>
public sealed partial class LabelEditor : UserControl
{
    private SketchViewModel? _sketch;

    public LabelEditor()
    {
        InitializeComponent();
        // before the text box uses the keys itself
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        DataContextChanged += (_, _) =>
        {
            if (_sketch is not null)
            {
                _sketch.WritingStarted -= OnWritingStarted;
            }
            _sketch = DataContext as SketchViewModel;
            if (_sketch is not null)
            {
                _sketch.WritingStarted += OnWritingStarted;
            }
        };
    }

    private void OnWritingStarted(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() =>
        {
            Box.Focus();
            Box.SelectAll();
        }, DispatcherPriority.Background);

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (_sketch is not { IsWriting: true })
        {
            return;
        }
        if (e.Key is Key.Enter && e.KeyModifiers == KeyModifiers.None)
        {
            _ = _sketch.Keep();
            e.Handled = true;
        }
        else if (e.Key is Key.Escape)
        {
            _sketch.Drop();
            e.Handled = true;
        }
    }
}
