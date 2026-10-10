using Avalonia.Controls;
using Avalonia.Threading;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.ViewModels;

namespace FAForever.MapEditor.App.Views;

/// <summary>
/// Where the panels lie (user story V-4): each movable panel (the mode's panel, the inspector, the
/// dock and the dock's tabs) in the left, right or bottom side of the shell, or in a window of its
/// own (<see cref="FloatingPanelWindow"/>), as <see cref="LayoutViewModel"/> says. The panels are
/// the same controls wherever they lie: they are moved, never made again, so they keep their state
/// and bindings. Each has a context menu that moves it.
/// </summary>
public sealed partial class MainWindow
{
    private readonly Dictionary<MovablePanel, FloatingPanelWindow> _floating = [];
    private readonly Dictionary<DockTab, Border> _tabHosts = [];

    /// <summary>
    /// The windows of the panels that float now, for automation: <c>ui</c>, <c>click</c>, <c>set</c>
    /// and <c>screenshot part</c> look in them too.
    /// </summary>
    public IReadOnlyList<Window> FloatingWindows => [.. _floating.Values];

    /// <summary>
    /// The control that is a movable panel wherever it lies: the panel itself, or a tab's host (its
    /// name over its content) while the tab lies outside the dock.
    /// </summary>
    private Control PanelControl(MovablePanel panel) => panel switch
    {
        MovablePanel.ModePanel => ModePanelView,
        MovablePanel.Inspector => InspectorView,
        MovablePanel.Dock => DockView,
        _ => TabHost(LayoutViewModel.TabOf(panel)),
    };

    /// <summary>
    /// The host of a tab outside the dock: a surface with the tab's name over its content.
    /// </summary>
    private Border TabHost(DockTab tab)
    {
        if (!_tabHosts.TryGetValue(tab, out Border? host))
        {
            TextBlock name = new TextBlock { Text = LayoutViewModel.TitleOf(LayoutViewModel.PanelOf(tab)) };
            name.Classes.Add("panelname");
            Border head = new Border { Child = name };
            head.Classes.Add("panelhead");
            DockPanel.SetDock(head, Avalonia.Controls.Dock.Top);
            host = new Border { Child = new DockPanel { Children = { head } } };
            host.Classes.Add("layouttab");
            Avalonia.Automation.AutomationProperties.SetName(host, LayoutViewModel.TitleOf(LayoutViewModel.PanelOf(tab)));
            _tabHosts[tab] = host;
        }
        return host;
    }

    private void OnPlacesChanged(object? sender, EventArgs e) => ArrangePanels();

    /// <summary>
    /// Puts every panel in its place: the sides' children in the order of <see cref="MovablePanel"/>,
    /// one above the other on the left and right, side by side at the bottom; a floating panel in its
    /// window (made or closed as needed); a tab in the dock or in its host. Then the columns and rows.
    /// </summary>
    private void ArrangePanels()
    {
        if (_model is null)
        {
            return;
        }
        using Performance.Span measure = Performance.Measure("window.arrange-panels");
        LayoutViewModel layout = _model.Layout;

        // the tabs first: in the dock, or out of it into their hosts
        foreach (DockTab tab in Enum.GetValues<DockTab>())
        {
            MovablePanel panel = LayoutViewModel.PanelOf(tab);
            Border host = TabHost(tab);
            DockPanel inside = (DockPanel)host.Child!;
            Control content = DockView.ContentOf(tab);
            if (layout.PlaceOf(panel) == PanelPlace.Dock)
            {
                inside.Children.Remove(content);
                Detach(host);
                DockView.ReturnTab(tab);
            }
            else if (!inside.Children.Contains(content))
            {
                DockView.TakeTab(tab);
                inside.Children.Add(content);
            }
            DockView.TabItemOf(tab).ContextMenu = PanelMenu(panel);
        }
        // the tab that shows is one in the dock
        IReadOnlyList<DockTab> inDock = layout.TabsInDock;
        if (inDock.Count > 0 && !inDock.Contains(_model.Dock))
        {
            _model.Dock = inDock[0];
        }

        Dictionary<PanelPlace, Grid> sides = new Dictionary<PanelPlace, Grid>
        {
            [PanelPlace.Left] = LeftSide,
            [PanelPlace.Right] = RightSide,
            [PanelPlace.Bottom] = BottomSide,
        };
        foreach (MovablePanel panel in Enum.GetValues<MovablePanel>())
        {
            Control control = PanelControl(panel);
            control.ContextMenu = PanelMenu(panel);
            PanelPlace place = layout.PlaceOf(panel);
            bool shown = layout.IsShown(panel);
            if (place == PanelPlace.Float)
            {
                if (!shown)
                {
                    // hidden while it floats: its window closes, and opens again when it is shown
                    if (_floating.Remove(panel, out FloatingPanelWindow? hidden))
                    {
                        hidden.CloseForLayout();
                    }
                    Detach(control);
                }
                else if (!_floating.ContainsKey(panel))
                {
                    Detach(control);
                    control.IsVisible = true;
                    FloatingPanelWindow window = new FloatingPanelWindow(panel, layout, control, _model, KeyBindings);
                    window.Closed += (_, _) =>
                    {
                        if (_floating.TryGetValue(panel, out FloatingPanelWindow? open) && open == window)
                        {
                            _floating.Remove(panel);
                        }
                    };
                    _floating[panel] = window;
                    window.Show(this);
                }
                continue;
            }
            if (_floating.Remove(panel, out FloatingPanelWindow? floating))
            {
                floating.CloseForLayout();
            }
            if (place == PanelPlace.Dock)
            {
                continue;
            }
            Grid side = sides[place];
            if (control.Parent != side)
            {
                Detach(control);
                side.Children.Add(control);
            }
            control.IsVisible = shown;
        }

        foreach ((PanelPlace place, Grid side) in sides)
        {
            Stack(side, place == PanelPlace.Bottom);
        }
        ModePanelSplitter.IsVisible = LeftSide.Children.Any(child => child.IsVisible);
        InspectorSplitter.IsVisible = RightSide.Children.Any(child => child.IsVisible);
        DockSplitter.IsVisible = BottomSide.Children.Any(child => child.IsVisible);
        Avalonia.Automation.AutomationProperties.SetName(ModePanelSplitter, SplitterName(layout, PanelPlace.Left, "the mode panel"));
        Avalonia.Automation.AutomationProperties.SetName(InspectorSplitter, SplitterName(layout, PanelPlace.Right, "the inspector"));
        Avalonia.Automation.AutomationProperties.SetName(DockSplitter, SplitterName(layout, PanelPlace.Bottom, "the dock"));
        ApplyLayout();
    }

    /// <summary>
    /// What a side's splitter is called: after the panels on it ("Resize the mode panel and the inspector").
    /// </summary>
    private static string SplitterName(LayoutViewModel layout, PanelPlace side, string fallback)
    {
        IReadOnlyList<MovablePanel> panels = layout.PanelsAt(side);
        return panels.Count == 0
            ? $"Resize {fallback}"
            : "Resize " + string.Join(" and ", panels.Select(panel => "the " + (LayoutViewModel.IsDockTab(panel) ? LayoutViewModel.TitleOf(panel) : LayoutViewModel.TitleOf(panel).ToLowerInvariant())));
    }

    /// <summary>
    /// Shares a side among the panels that show on it: a row each (left, right) or a column each (bottom).
    /// </summary>
    private static void Stack(Grid side, bool across)
    {
        List<Control> shown = [.. side.Children.Where(child => child.IsVisible)];
        side.RowDefinitions.Clear();
        side.ColumnDefinitions.Clear();
        for (int i = 0; i < shown.Count; i++)
        {
            if (across)
            {
                side.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            }
            else
            {
                side.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Star));
            }
            Grid.SetRow(shown[i], across ? 0 : i);
            Grid.SetColumn(shown[i], across ? i : 0);
        }
        side.IsVisible = shown.Count > 0;
    }

    /// <summary>
    /// Takes a control out of where it lies, so it can lie elsewhere.
    /// </summary>
    private static void Detach(Control control)
    {
        switch (control.Parent)
        {
            case Panel panel:
                panel.Children.Remove(control);
                break;
            case ContentControl content when ReferenceEquals(content.Content, control):
                content.Content = null;
                break;
            case Decorator decorator when ReferenceEquals(decorator.Child, control):
                decorator.Child = null;
                break;
        }
    }

    /// <summary>
    /// The context menu of a panel (and of a tab's header in the dock): where to move it, float it or
    /// put it back, hide it, and reset the layout.
    /// </summary>
    private ContextMenu PanelMenu(MovablePanel panel)
    {
        MainViewModel model = _model!;
        LayoutViewModel layout = model.Layout;
        string name = LayoutViewModel.Name(panel);
        string title = LayoutViewModel.IsDockTab(panel) ? LayoutViewModel.TitleOf(panel) : $"the {LayoutViewModel.TitleOf(panel).ToLowerInvariant()}";
        PanelPlace place = layout.PlaceOf(panel);
        List<object> items = [];
        MenuItem Item(string header, string command, string? argument = null)
        {
            MenuItem item = new MenuItem { Header = header, Command = model.LayoutMenuCommand, CommandParameter = argument is null ? command : $"{command}:{argument}" };
            return item;
        }
        foreach ((PanelPlace to, string header) in new[] { (PanelPlace.Left, "Move to the left"), (PanelPlace.Right, "Move to the right"), (PanelPlace.Bottom, "Move to the bottom") })
        {
            MenuItem item = Item(header, "move", $"{name}:{to.ToString().ToLowerInvariant()}");
            item.IsEnabled = place != to;
            items.Add(item);
        }
        if (LayoutViewModel.IsDockTab(panel))
        {
            MenuItem back = Item("Into the dock", "move", $"{name}:dock");
            back.IsEnabled = place != PanelPlace.Dock;
            items.Add(back);
        }
        items.Add(place == PanelPlace.Float
            ? Item("Back into the editor's window", "return", name)
            : Item("Float in a window", "move", $"{name}:float"));
        if (!LayoutViewModel.IsDockTab(panel))
        {
            items.Add(Item($"Hide {title}", "hide", name));
        }
        items.Add(new Separator());
        items.Add(Item("Reset layout", "reset"));
        return new ContextMenu { ItemsSource = items };
    }

    /// <summary>
    /// Closes the floating panels' windows without moving their panels back, so they float again
    /// the next time the editor starts.
    /// </summary>
    private void CloseFloatingWindows()
    {
        foreach (FloatingPanelWindow window in _floating.Values.ToList())
        {
            window.CloseForLayout();
        }
        _floating.Clear();
    }

    /// <summary>
    /// A floating panel's window brought to the front, as choosing its tab does in the dock.
    /// </summary>
    private void ActivateFloating(MovablePanel panel)
    {
        if (_floating.TryGetValue(panel, out FloatingPanelWindow? window))
        {
            Dispatcher.UIThread.Post(window.Activate);
        }
    }
}
