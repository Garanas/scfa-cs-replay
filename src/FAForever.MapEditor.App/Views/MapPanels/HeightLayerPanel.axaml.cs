using Avalonia.Controls;
using FAForever.MapEditor.App.ViewModels;

namespace FAForever.MapEditor.App.Views.MapPanels;

public sealed partial class HeightLayerPanel : UserControl
{
    private CurveNodesViewModel? _nodes;
    private bool _syncing;

    public HeightLayerPanel()
    {
        InitializeComponent();
        // the table's chosen rows are the chosen points (T-4): both ways, by index, so rows out of view count too
        NodeList.SelectionChanged += (_, _) =>
        {
            if (!_syncing && _nodes is not null)
            {
                _syncing = true;
                try
                {
                    _nodes.Choose(NodeList.Selection.SelectedIndexes, NodeList.Selection.SelectedIndex >= 0 ? NodeList.Selection.SelectedIndex : null);
                }
                finally
                {
                    _syncing = false;
                }
            }
        };
        DataContextChanged += (_, _) => Follow((DataContext as MainViewModel)?.HeightStack.Nodes);
    }

    private void Follow(CurveNodesViewModel? nodes)
    {
        if (_nodes is not null)
        {
            _nodes.ChosenChanged -= OnChosenChanged;
        }
        _nodes = nodes;
        if (_nodes is not null)
        {
            _nodes.ChosenChanged += OnChosenChanged;
        }
        OnChosenChanged(null, EventArgs.Empty);
    }

    private void OnChosenChanged(object? sender, EventArgs e)
    {
        if (_syncing || _nodes is null)
        {
            return;
        }
        _syncing = true;
        try
        {
            NodeList.Selection.BeginBatchUpdate();
            NodeList.Selection.Clear();
            foreach (int index in _nodes.Chosen)
            {
                NodeList.Selection.Select(index);
            }
            NodeList.Selection.EndBatchUpdate();
            if (_nodes.Current >= 0 && _nodes.Current < _nodes.Rows.Count)
            {
                NodeList.ScrollIntoView(_nodes.Current);
            }
        }
        finally
        {
            _syncing = false;
        }
    }
}
