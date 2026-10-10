using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
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

    // a double click or Enter on a problem goes to its place (user story F-14)
    private void OnProblemDoubleTapped(object? sender, TappedEventArgs e) => ShowSelectedProblem();

    private void OnProblemKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ShowSelectedProblem())
        {
            e.Handled = true;
        }
    }

    private bool ShowSelectedProblem()
    {
        if (_model is null || ProblemList.SelectedIndex < 0)
        {
            return false;
        }
        _ = _model.RunWithAsync("problems.show", new JsonObject { ["index"] = ProblemList.SelectedIndex });
        return true;
    }

    private void ShowNewest()
    {
        if (_model?.Diagnostics.ShownConsole is { Count: > 0 } lines)
        {
            ConsoleList.ScrollIntoView(lines.Count - 1);
        }
    }
}
