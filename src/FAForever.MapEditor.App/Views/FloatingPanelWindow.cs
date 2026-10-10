using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using FAForever.MapEditor.App.ViewModels;

namespace FAForever.MapEditor.App.Views;

/// <summary>
/// A panel of the editor floating in a window of its own (user story V-4): owned by the editor's
/// window, so it stays above it and goes with it, with the editor's shortcuts. Closing it puts the
/// panel back where it lay (<see cref="LayoutViewModel.Return"/>); where it is and how large is
/// remembered as the person leaves it.
/// </summary>
public sealed class FloatingPanelWindow : Window
{
    private readonly LayoutViewModel _layout;
    private readonly DispatcherTimer _remember;
    private bool _closingByLayout;

    public FloatingPanelWindow(MovablePanel panel, LayoutViewModel layout, Control content, object? dataContext, IEnumerable<KeyBinding> shortcuts)
    {
        Panel = panel;
        _layout = layout;
        Title = $"{LayoutViewModel.TitleOf(panel)} - FAF map editor";
        DataContext = dataContext;
        ShowInTaskbar = false;
        FloatingBounds bounds = layout.FloatingBoundsOf(panel);
        Width = bounds.Width;
        Height = bounds.Height;
        if (bounds.X is int x && bounds.Y is int y)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint(x, y);
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        Content = content;
        foreach (KeyBinding shortcut in shortcuts)
        {
            KeyBindings.Add(new KeyBinding { Gesture = shortcut.Gesture, Command = shortcut.Command });
        }
        Avalonia.Automation.AutomationProperties.SetName(this, LayoutViewModel.TitleOf(panel));
        // moved or resized: remembered a moment after the person stops
        _remember = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _remember.Tick += (_, _) =>
        {
            _remember.Stop();
            Remember();
        };
        PositionChanged += (_, _) => _remember.Start();
        Resized += (_, _) => _remember.Start();
    }

    /// <summary>
    /// The panel it holds.
    /// </summary>
    public MovablePanel Panel { get; }

    /// <summary>
    /// Closes it because the panel lies elsewhere now (or is hidden): the layout stays as it is.
    /// </summary>
    public void CloseForLayout()
    {
        _closingByLayout = true;
        Close();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        _remember.Stop();
        Remember();
        // the panel goes back into the editor's window first, so the content is free again
        Content = null;
        if (!_closingByLayout)
        {
            // closed by the person: the panel goes back where it lay
            Dispatcher.UIThread.Post(() => _layout.Return(Panel));
        }
    }

    private void Remember()
    {
        if (Bounds.Width > 0 && Bounds.Height > 0)
        {
            _layout.SetFloatingBounds(Panel, new FloatingBounds(Position.X, Position.Y, Bounds.Width, Bounds.Height));
        }
    }
}
