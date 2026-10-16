using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FAForever.MapEditor.App.ViewModels;

namespace FAForever.MapEditor.App.Views;

/// <summary>
/// The dock under the views. The Console follows its newest line, unless the person scrolled up.
/// </summary>
public sealed partial class Dock : UserControl
{
    private MainViewModel? _model;

    public Dock()
    {
        InitializeComponent();
        // before the list box, which takes Enter itself
        ProblemList.AddHandler(KeyDownEvent, OnProblemKeyDown, RoutingStrategies.Tunnel);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_model is not null)
        {
            _model.Diagnostics.Console.CollectionChanged -= OnConsoleChanged;
            _model.Diagnostics.ConsoleProblems.CollectionChanged -= OnConsoleChanged;
            _model.Diagnostics.PropertyChanged -= OnDiagnosticsChanged;
        }
        _model = DataContext as MainViewModel;
        if (_model is not null)
        {
            _model.Diagnostics.Console.CollectionChanged += OnConsoleChanged;
            _model.Diagnostics.ConsoleProblems.CollectionChanged += OnConsoleChanged;
            _model.Diagnostics.PropertyChanged += OnDiagnosticsChanged;
        }
    }

    private void OnDiagnosticsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DiagnosticsViewModel.ShownConsole))
        {
            Dispatcher.UIThread.Post(ShowNewest, DispatcherPriority.Background);
        }
    }

    private void OnConsoleChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && ReferenceEquals(sender, _model?.Diagnostics.ShownConsole) && AtTheEnd())
        {
            Dispatcher.UIThread.Post(ShowNewest, DispatcherPriority.Background);
        }
    }

    /// <summary>
    /// Whether the Console shows its end (or has nothing to scroll), before the new line.
    /// </summary>
    private bool AtTheEnd() => ConsoleList.Scroll is not { } scroll || scroll.Offset.Y + scroll.Viewport.Height >= scroll.Extent.Height - 4;

    // a click on a problem selects its things, a double click or Enter goes to its place too (user story F-14)
    private void OnProblemTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Avalonia.Visual source && source.FindAncestorOfType<ListBoxItem>(includeSelf: true) is not null)
        {
            ShowSelectedProblem(frame: false);
        }
    }

    private void OnProblemDoubleTapped(object? sender, TappedEventArgs e) => ShowSelectedProblem();

    private void OnProblemKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ShowSelectedProblem())
        {
            e.Handled = true;
        }
    }

    private bool ShowSelectedProblem(bool frame = true)
    {
        if (_model is null || ProblemList.SelectedIndex < 0)
        {
            return false;
        }
        _ = _model.RunWithAsync("problems.show", new JsonObject { ["index"] = ProblemList.SelectedIndex, ["frame"] = frame });
        return true;
    }

    /// <summary>
    /// Takes a tab's content out of the dock, to lie elsewhere (user story V-4): its header hides,
    /// and another tab shows when it was the one showing.
    /// </summary>
    public Control TakeTab(DockTab tab)
    {
        TabItem item = TabItemOf(tab);
        if (!_contents.TryGetValue(tab, out Control? content))
        {
            content = (Control)item.Content!;
            _contents[tab] = content;
        }
        if (ReferenceEquals(item.Content, content))
        {
            item.Content = null;
        }
        item.IsVisible = false;
        return content;
    }

    /// <summary>
    /// Puts a tab's content back into the dock (user story V-4); the caller has taken it from where it lay.
    /// </summary>
    public void ReturnTab(DockTab tab)
    {
        TabItem item = TabItemOf(tab);
        if (_contents.TryGetValue(tab, out Control? content) && !ReferenceEquals(item.Content, content))
        {
            item.Content = content;
        }
        item.IsVisible = true;
    }

    /// <summary>
    /// The content of a tab, wherever it lies.
    /// </summary>
    public Control ContentOf(DockTab tab) => _contents.TryGetValue(tab, out Control? content) ? content : (Control)TabItemOf(tab).Content!;

    /// <summary>
    /// The header of a tab, in the order of <see cref="DockTab"/>.
    /// </summary>
    public TabItem TabItemOf(DockTab tab) => (TabItem)Tabs.Items[(int)tab]!;

    private readonly Dictionary<DockTab, Control> _contents = [];

    private void ShowNewest()
    {
        if (_model?.Diagnostics.ShownConsole is { Count: > 0 } lines)
        {
            ConsoleList.ScrollIntoView(lines.Count - 1);
        }
    }
}
