using System.Collections.Immutable;
using System.Text.Json.Nodes;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Adds an empty group to an army's tree of groups (user story U-3), one step of undo. Left out,
/// the army and group chosen in the Units mode's tree; the new group is chosen there afterwards.
/// </summary>
/// <param name="Parent">The path of the group to add to (<c>INITIAL</c>); empty for the army's root group.</param>
/// <param name="Name">Left out, <c>GROUP_1</c>; a name a sibling has gets the next free number.</param>
/// <returns>The army and the new group's path.</returns>
[UndoStep]
[EditorCommand("units.addGroup", "Add a group", Story = "U-3")]
public sealed record AddUnitGroup(string? Army = null, string? Parent = null, string? Name = null) : IRequest<JsonObject>;

public sealed class AddUnitGroupHandler(MainViewModel model, ArmiesViewModel armies, CurrentEdit edit) : IRequestHandler<AddUnitGroup, JsonObject>
{
    public Task<JsonObject> Handle(AddUnitGroup request, CancellationToken cancellationToken)
    {
        string army = request.Army ?? armies.Chosen?.Army
            ?? throw new ArgumentException("Choose an army or group in the Units mode's tree, or give the argument army");
        ImmutableArray<string> parent = request.Parent is not null || request.Army is not null
            ? UnitGroupEdits.ParsePath(request.Parent)
            : armies.Chosen!.Value.Path;
        ImmutableArray<string> added = [];
        UnitCommands.Apply(edit, d => d.AddGroup(army, parent, request.Name, out added));
        armies.ChooseWhenShown(army, added);
        model.Status = $"Added the group {UnitGroupEdits.PathText(added)} to {army}";
        return Task.FromResult(new JsonObject { ["army"] = army, ["group"] = UnitGroupEdits.PathText(added) });
    }
}

/// <summary>
/// Renames a group of an army (user story U-3), one step of undo; a sibling may not have the name.
/// </summary>
/// <param name="Group">Its path: <c>INITIAL/Town</c>.</param>
[UndoStep]
[EditorCommand("units.renameGroup", "Rename a group", Story = "U-3")]
public sealed record RenameUnitGroup(string Army, string Group, string Name) : IRequest<JsonObject>;

public sealed class RenameUnitGroupHandler(MainViewModel model, ArmiesViewModel armies, CurrentEdit edit) : IRequestHandler<RenameUnitGroup, JsonObject>
{
    public Task<JsonObject> Handle(RenameUnitGroup request, CancellationToken cancellationToken)
    {
        ImmutableArray<string> path = UnitGroupEdits.ParsePath(request.Group);
        UnitCommands.Apply(edit, d => d.RenameGroup(request.Army, path, request.Name));
        ImmutableArray<string> renamed = path.IsEmpty ? path : path.SetItem(path.Length - 1, request.Name.Trim());
        armies.ChooseWhenShown(request.Army, renamed);
        model.Status = $"Renamed the group {request.Group} of {request.Army} to {request.Name.Trim()}";
        return Task.FromResult(new JsonObject { ["army"] = request.Army, ["group"] = UnitGroupEdits.PathText(renamed) });
    }
}

/// <summary>
/// Removes a group of an army (user story U-3), one step of undo. Its units and groups go to the
/// group above it, unless <paramref name="KeepUnits"/> is false: then they are removed with it.
/// </summary>
[UndoStep]
[EditorCommand("units.removeGroup", "Remove a group", Story = "U-3")]
public sealed record RemoveUnitGroup(string Army, string Group, bool KeepUnits = true) : IRequest;

public sealed class RemoveUnitGroupHandler(MainViewModel model, CurrentEdit edit) : IRequestHandler<RemoveUnitGroup>
{
    public Task Handle(RemoveUnitGroup request, CancellationToken cancellationToken)
    {
        UnitCommands.Apply(edit, d => d.RemoveGroup(request.Army, UnitGroupEdits.ParsePath(request.Group), request.KeepUnits));
        model.Status = request.KeepUnits
            ? $"Removed the group {request.Group} of {request.Army}; its units are in the group above it"
            : $"Removed the group {request.Group} of {request.Army} with its units";
        return Task.CompletedTask;
    }
}

/// <summary>
/// Moves a group with its units and groups into another group, of the same or another army (user
/// story U-3), one step of undo; a name taken there gets the next free number.
/// </summary>
/// <param name="ToArmy">Left out, the group's own army.</param>
/// <param name="ToParent">The path of the group it goes into; empty for the root group.</param>
/// <returns>The army and the group's new path.</returns>
[UndoStep]
[EditorCommand("units.moveGroup", "Move a group", Story = "U-3")]
public sealed record MoveUnitGroup(string Army, string Group, string ToParent, string? ToArmy = null) : IRequest<JsonObject>;

public sealed class MoveUnitGroupHandler(MainViewModel model, ArmiesViewModel armies, CurrentEdit edit) : IRequestHandler<MoveUnitGroup, JsonObject>
{
    public Task<JsonObject> Handle(MoveUnitGroup request, CancellationToken cancellationToken)
    {
        string toArmy = request.ToArmy ?? request.Army;
        ImmutableArray<string> moved = [];
        UnitCommands.Apply(edit, d => d.MoveGroup(request.Army, UnitGroupEdits.ParsePath(request.Group), toArmy, UnitGroupEdits.ParsePath(request.ToParent), out moved));
        armies.ChooseWhenShown(toArmy, moved);
        model.Status = $"Moved the group {request.Group} of {request.Army} to {UnitGroupEdits.PathText(moved)} of {toArmy}";
        return Task.FromResult(new JsonObject { ["army"] = toArmy, ["group"] = UnitGroupEdits.PathText(moved) });
    }
}

/// <summary>
/// Selects the units of a group and of the groups in it (user story U-3). Left out, the army and
/// group chosen in the Units mode's tree.
/// </summary>
/// <param name="Group">Its path; empty or left out (with an army given) for all the army's units.</param>
/// <returns>How many units are selected.</returns>
[EditorCommand("units.selectGroup", "Select the group's units", Story = "U-3", Needs = Needs.Map)]
public sealed record SelectUnitGroup(string? Army = null, string? Group = null) : IRequest<int>;

public sealed class SelectUnitGroupHandler(MainViewModel model, ArmiesViewModel armies, SelectionViewModel selection) : IRequestHandler<SelectUnitGroup, int>
{
    public Task<int> Handle(SelectUnitGroup request, CancellationToken cancellationToken)
    {
        MapDocument document = model.ShownDocument ?? throw new InvalidOperationException("No map is open");
        string army = request.Army ?? armies.Chosen?.Army
            ?? throw new ArgumentException("Choose an army or group in the Units mode's tree, or give the argument army");
        ImmutableArray<string> path = request.Army is not null ? UnitGroupEdits.ParsePath(request.Group) : armies.Chosen!.Value.Path;
        UnitCommands.RequireArmy(document, army);
        if (document.FindGroup(army, path) is null)
        {
            throw new ArgumentException($"The army {army} has no group {UnitGroupEdits.PathText(path)}");
        }
        ObjectRef[] units = [.. document.UnitsIn(army, path).Select(unit => ObjectRef.Unit(unit.Unit.Name))];
        selection.Set(units);
        model.Status = $"Selected the {units.Length} units of {(path.IsEmpty ? army : $"{UnitGroupEdits.PathText(path)} of {army}")}";
        return Task.FromResult(units.Length);
    }
}
