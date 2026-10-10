using System.Collections.Immutable;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.Project.Editing;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// A unit in the Units mode's tree (user story U-3), under its group: dragged onto a group it goes
/// there; chosen, it is selected on the map.
/// </summary>
/// <param name="Path">Its group's path below the army's root group.</param>
public sealed record UnitNode(string Army, ImmutableArray<string> Path, string Name, string Blueprint)
{
    /// <summary>
    /// What the tree shows: <c>UNIT_5 · uec1101</c>.
    /// </summary>
    public string Label => $"{Name} · {Blueprint}";

    /// <summary>
    /// What a screen reader says.
    /// </summary>
    public string Description => $"Unit {Name}, {Blueprint}, in {(Path.IsEmpty ? Army : $"{UnitGroupEdits.PathText(Path)} of {Army}")}";

    /// <summary>
    /// The key of its group's node.
    /// </summary>
    public string GroupKey => $"{Army}:{UnitGroupEdits.PathText(Path)}";

    /// <summary>
    /// A unit holds nothing to show: it stays closed, whatever the tree asks.
    /// </summary>
    public bool IsExpanded
    {
        get => false;
        set { }
    }
}

/// <summary>
/// Dragging in the Units mode's tree (user story U-3): units and groups dropped onto a group or an
/// army go there, one step of undo (<c>units.drop</c>); a unit chosen in the tree is selected.
/// </summary>
public sealed partial class ArmiesViewModel
{
    private bool _syncingNode;

    /// <summary>
    /// What is chosen in the tree: a <see cref="GroupNode"/> (also <see cref="ChosenGroup"/>) or a
    /// <see cref="UnitNode"/> (its group becomes <see cref="ChosenGroup"/>).
    /// </summary>
    [ObservableProperty]
    public partial object? ChosenNode { get; set; }

    /// <summary>
    /// The names of the units selected on the map: a unit dragged while selected takes the other
    /// selected units along.
    /// </summary>
    public Func<IReadOnlyList<string>> SelectedUnits { get; set; } = () => [];

    partial void OnChosenNodeChanged(object? value)
    {
        if (_syncingNode)
        {
            return;
        }
        _syncingNode = true;
        try
        {
            switch (value)
            {
                case GroupNode group:
                    ChosenGroup = group;
                    break;
                case UnitNode unit:
                    ChosenGroup = Tree.SelectMany(node => node.SelfAndBelow()).FirstOrDefault(node => node.Key == unit.GroupKey) ?? ChosenGroup;
                    _ = _run("select.set", new JsonObject { ["refs"] = new JsonArray(JsonValue.Create(ObjectRef.Unit(unit.Name).ToString())) });
                    break;
            }
        }
        finally
        {
            _syncingNode = false;
        }
    }

    /// <summary>
    /// The tree's choice follows a group chosen otherwise (a command, the tree made again), unless
    /// a unit of that group is chosen.
    /// </summary>
    private void FollowChosenGroup(GroupNode? group)
    {
        if (_syncingNode || (ChosenNode is UnitNode unit && group is not null && unit.GroupKey == group.Key))
        {
            return;
        }
        _syncingNode = true;
        try
        {
            ChosenNode = group;
        }
        finally
        {
            _syncingNode = false;
        }
    }

    /// <summary>
    /// Why what is dragged cannot be dropped onto a group, or null when it can: a group onto itself,
    /// a group in it or the group it is in, a unit onto its own group, an army's root group anywhere.
    /// </summary>
    public string? DropProblem(object dragged, GroupNode target) => dragged switch
    {
        GroupNode { IsArmy: true } => "An army's root group stays where it is",
        GroupNode group when group.Army == target.Army && target.Path.Length >= group.Path.Length && target.Path.Take(group.Path.Length).SequenceEqual(group.Path)
            => $"The group {group.PathText} cannot go into itself",
        GroupNode group when group.Army == target.Army && group.Path[..^1].SequenceEqual(target.Path) => $"The group {group.PathText} is there already",
        UnitNode unit when unit.GroupKey == target.Key && UnitsDragged(unit).Count == 1 => $"{unit.Name} is in that group already",
        GroupNode or UnitNode => null,
        _ => "Only units and groups can be dragged",
    };

    /// <summary>
    /// Drops a group or units onto a group of the tree (<c>units.drop</c>, one step of undo); what
    /// cannot go there says why in the status line.
    /// </summary>
    public Task Drop(object dragged, GroupNode target)
    {
        JsonObject arguments = new JsonObject { ["army"] = target.Army, ["group"] = target.PathText };
        switch (dragged)
        {
            case GroupNode group:
                arguments["fromArmy"] = group.Army;
                arguments["fromGroup"] = group.PathText;
                break;
            case UnitNode unit:
                arguments["units"] = new JsonArray([.. UnitsDragged(unit).Select(name => (JsonNode)JsonValue.Create(name))]);
                break;
            default:
                return Task.CompletedTask;
        }
        return _run("units.drop", arguments);
    }

    /// <summary>
    /// The units a unit dragged takes along: the selected units when it is one of them, else itself.
    /// </summary>
    private IReadOnlyList<string> UnitsDragged(UnitNode unit)
    {
        IReadOnlyList<string> selected = SelectedUnits();
        return selected.Contains(unit.Name) ? selected : [unit.Name];
    }
}
