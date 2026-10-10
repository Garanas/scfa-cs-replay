using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FAForever.MapEditor.App.ViewModels;

namespace FAForever.MapEditor.App.Controls;

/// <summary>
/// Dragging units and groups in the Units mode's tree (user story U-3): a unit or group pressed and
/// moved six pixels follows the mouse; the group or army under it lights up (class
/// <c>unitdroptarget</c>) and the status line says what a drop does; letting go there drops it
/// (<see cref="ArmiesViewModel.Drop"/>, one step of undo). The drag stays inside the tree with the
/// pointer captured, so the window's own input (automation's <c>mouse action=drag</c>) drives it as
/// the system's mouse does. Escape or the capture lost cancels it.
/// </summary>
public static class UnitTreeDrag
{
    /// <summary>
    /// How far the mouse moves with the button down before a press becomes a drag, in pixels.
    /// </summary>
    private const double Threshold = 6;

    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<TreeView, bool>("IsEnabled", typeof(UnitTreeDrag));

    private static readonly AttachedProperty<DragState?> StateProperty =
        AvaloniaProperty.RegisterAttached<TreeView, DragState?>("State", typeof(UnitTreeDrag));

    static UnitTreeDrag()
    {
        IsEnabledProperty.Changed.AddClassHandler<TreeView>((tree, e) =>
        {
            if (e.NewValue is true)
            {
                tree.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
                tree.AddHandler(InputElement.PointerMovedEvent, OnMoved, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
                tree.AddHandler(InputElement.PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
                tree.AddHandler(InputElement.PointerCaptureLostEvent, OnCaptureLost, RoutingStrategies.Direct | RoutingStrategies.Bubble, handledEventsToo: true);
                tree.AddHandler(InputElement.KeyDownEvent, OnKey, RoutingStrategies.Tunnel, handledEventsToo: true);
            }
        });
    }

    public static void SetIsEnabled(TreeView tree, bool value) => tree.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(TreeView tree) => tree.GetValue(IsEnabledProperty);

    private sealed class DragState(object dragged, Point start)
    {
        public object Dragged { get; } = dragged;

        public Point Start { get; } = start;

        public bool Dragging { get; set; }

        public TreeViewItem? Target { get; set; }

        public GroupNode? TargetNode { get; set; }
    }

    private static void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not TreeView tree || !e.GetCurrentPoint(tree).Properties.IsLeftButtonPressed)
        {
            return;
        }
        Finish(tree, drop: false);
        if (ItemAt(e.Source as Visual) is { DataContext: (GroupNode or UnitNode) and object node })
        {
            tree.SetValue(StateProperty, new DragState(node, e.GetPosition(tree)));
        }
    }

    private static void OnMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not TreeView tree || tree.GetValue(StateProperty) is not DragState state)
        {
            return;
        }
        Point at = e.GetPosition(tree);
        if (!state.Dragging)
        {
            if (!e.GetCurrentPoint(tree).Properties.IsLeftButtonPressed)
            {
                tree.SetValue(StateProperty, null);
                return;
            }
            if (Math.Abs(at.X - state.Start.X) < Threshold && Math.Abs(at.Y - state.Start.Y) < Threshold)
            {
                return;
            }
            state.Dragging = true;
            e.Pointer.Capture(tree);
        }
        // the item under the mouse, found by its bounds: the pointer is captured by the tree
        TreeViewItem? item = tree.GetVisualDescendants().OfType<TreeViewItem>()
            .Where(candidate => candidate.IsEffectivelyVisible && candidate.DataContext is GroupNode && HeaderBounds(candidate, tree) is Rect bounds && bounds.Contains(at))
            .LastOrDefault();
        GroupNode? target = item?.DataContext as GroupNode;
        if (item != state.Target)
        {
            state.Target?.Classes.Remove("unitdroptarget");
            state.Target = item;
            state.TargetNode = target;
            item?.Classes.Add("unitdroptarget");
        }
        if (Armies(tree) is (MainViewModel model, ArmiesViewModel armies))
        {
            string what = state.Dragged switch
            {
                GroupNode group => $"the group {group.PathText}",
                UnitNode unit => unit.Name,
                _ => "it",
            };
            model.Status = target is null
                ? $"Drag {what} onto a group or an army"
                : armies.DropProblem(state.Dragged, target) is string problem
                    ? problem
                    : $"Drop {what} into {(target.IsArmy ? target.Army : $"{target.PathText} of {target.Army}")}";
        }
        e.Handled = true;
    }

    private static void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is TreeView tree && tree.GetValue(StateProperty) is DragState { Dragging: true })
        {
            Finish(tree, drop: true);
            e.Pointer.Capture(null);
            e.Handled = true;
        }
        else if (sender is TreeView other)
        {
            other.SetValue(StateProperty, null);
        }
    }

    private static void OnCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (sender is TreeView tree)
        {
            Finish(tree, drop: false);
        }
    }

    private static void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && sender is TreeView tree && tree.GetValue(StateProperty) is DragState { Dragging: true })
        {
            Finish(tree, drop: false);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Ends a drag: drops it on the group under the mouse when <paramref name="drop"/> and it can go
    /// there, else says why.
    /// </summary>
    private static void Finish(TreeView tree, bool drop)
    {
        if (tree.GetValue(StateProperty) is not DragState state)
        {
            return;
        }
        tree.SetValue(StateProperty, null);
        state.Target?.Classes.Remove("unitdroptarget");
        if (!drop || !state.Dragging || Armies(tree) is not (MainViewModel model, ArmiesViewModel armies))
        {
            return;
        }
        if (state.TargetNode is not GroupNode target)
        {
            model.Status = "Dropped outside a group: nothing moved";
            return;
        }
        if (armies.DropProblem(state.Dragged, target) is string problem)
        {
            model.Status = problem;
            return;
        }
        _ = armies.Drop(state.Dragged, target);
    }

    /// <summary>
    /// The bounds of an item's own row (without its children) in the tree's coordinates.
    /// </summary>
    private static Rect? HeaderBounds(TreeViewItem item, Visual tree)
    {
        // Fluent's item: a row (PART_LayoutRoot) holding the header, then the children's panel
        Control? header = item.GetVisualDescendants().OfType<Control>().FirstOrDefault(control => control.Name is "PART_LayoutRoot" or "PART_Header");
        Visual row = (Visual?)header ?? item;
        return row.TransformToVisual(tree) is Matrix transform ? new Rect(row.Bounds.Size).TransformToAABB(transform) : null;
    }

    private static TreeViewItem? ItemAt(Visual? visual) => visual as TreeViewItem ?? visual?.FindAncestorOfType<TreeViewItem>();

    private static (MainViewModel, ArmiesViewModel)? Armies(Visual tree) =>
        tree.FindAncestorOfType<UserControl>()?.DataContext is MainViewModel model ? (model, model.Armies) : null;
}
