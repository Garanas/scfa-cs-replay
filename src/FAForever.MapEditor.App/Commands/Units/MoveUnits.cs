using System.Collections.Immutable;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Gives the selected units to another army (user stories U-2, U-3), one step of undo: they stay
/// where they are, in a group of that army.
/// </summary>
/// <param name="Group">The group's path in that army (made when missing); left out, <c>INITIAL</c>.</param>
/// <returns>How many units moved.</returns>
[UndoStep]
[EditorCommand("units.moveToArmy", "Give the selected units to an army", Story = "U-3", Needs = Needs.Selection)]
public sealed record MoveUnitsToArmy(string Army, string? Group = null) : IRequest<int>;

public sealed class MoveUnitsToArmyHandler(MainViewModel model, SelectionViewModel selection, CurrentEdit edit) : IRequestHandler<MoveUnitsToArmy, int>
{
    public Task<int> Handle(MoveUnitsToArmy request, CancellationToken cancellationToken)
    {
        string[] names = UnitCommands.SelectedUnits(selection);
        UnitCommands.RequireArmy(edit.Transaction.Document, request.Army);
        ImmutableArray<string> group = request.Group is null ? [UnitGroupEdits.InitialGroup] : UnitGroupEdits.ParsePath(request.Group);
        int count = 0;
        UnitCommands.Apply(edit, d => d.MoveUnits(names, request.Army, group, out count));
        model.Status = $"Gave {count} units to {request.Army}";
        return Task.FromResult(count);
    }
}

/// <summary>
/// Moves the selected units into a group (user story U-3), one step of undo: of the army given, or
/// each in its own army when none is given; the group is made when missing.
/// </summary>
/// <param name="Group">The group's path: <c>INITIAL/Town</c>; empty for the root group.</param>
/// <returns>How many units moved.</returns>
[UndoStep]
[EditorCommand("units.moveToGroup", "Move the selected units to a group", Story = "U-3", Needs = Needs.Selection)]
public sealed record MoveUnitsToGroup(string Group, string? Army = null) : IRequest<int>;

public sealed class MoveUnitsToGroupHandler(MainViewModel model, SelectionViewModel selection, CurrentEdit edit) : IRequestHandler<MoveUnitsToGroup, int>
{
    public Task<int> Handle(MoveUnitsToGroup request, CancellationToken cancellationToken)
    {
        string[] names = UnitCommands.SelectedUnits(selection);
        MapDocument document = edit.Transaction.Document;
        ImmutableArray<string> group = UnitGroupEdits.ParsePath(request.Group);
        if (request.Army is not null)
        {
            UnitCommands.RequireArmy(document, request.Army);
        }
        int count = 0;
        UnitCommands.Apply(edit, d =>
        {
            foreach (IGrouping<string, string> byArmy in names.GroupBy(name => request.Army ?? d.FindUnit(name)?.Army ?? ""))
            {
                if (byArmy.Key.Length > 0)
                {
                    d = d.MoveUnits(byArmy, byArmy.Key, group, out int moved);
                    count += moved;
                }
            }
            return d;
        });
        model.Status = $"Moved {count} units to the group {(group.IsEmpty ? "at the root" : request.Group)}";
        return Task.FromResult(count);
    }
}
