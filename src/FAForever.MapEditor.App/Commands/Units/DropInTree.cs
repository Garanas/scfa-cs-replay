using System.Collections.Immutable;
using System.Text.Json.Nodes;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// What dropping in the Units mode's tree does (user story U-3), one step of undo: units dragged
/// onto a group go into it (each keeps its place), a group dragged onto a group or an army goes
/// into it with its units and groups (a name taken there gets a number).
/// </summary>
/// <param name="Army">The army dropped on.</param>
/// <param name="Group">The path of the group dropped on; empty for the army's root group.</param>
/// <param name="Units">The units dragged, by name.</param>
/// <param name="FromArmy">The army of the group dragged.</param>
/// <param name="FromGroup">The path of the group dragged.</param>
/// <returns>The army and the path the units or the group went to, and how many units moved.</returns>
[UndoStep]
[EditorCommand("units.drop", "Drop units or a group in the tree", Story = "U-3")]
public sealed record DropInUnitTree(string Army, string Group, IReadOnlyList<string>? Units = null, string? FromArmy = null, string? FromGroup = null) : IRequest<JsonObject>;

public sealed class DropInUnitTreeHandler(MainViewModel model, ArmiesViewModel armies, CurrentEdit edit) : IRequestHandler<DropInUnitTree, JsonObject>
{
    public Task<JsonObject> Handle(DropInUnitTree request, CancellationToken cancellationToken)
    {
        MapDocument document = edit.Transaction.Document;
        UnitCommands.RequireArmy(document, request.Army);
        ImmutableArray<string> target = UnitGroupEdits.ParsePath(request.Group);
        if (document.FindGroup(request.Army, target) is null)
        {
            throw new ArgumentException($"The army {request.Army} has no group {request.Group}");
        }
        string where = target.IsEmpty ? request.Army : $"{request.Group} of {request.Army}";
        if (request.FromGroup is string from)
        {
            string fromArmy = request.FromArmy ?? request.Army;
            ImmutableArray<string> source = UnitGroupEdits.ParsePath(from);
            if (source.IsEmpty)
            {
                throw new ArgumentException("An army's root group stays where it is: drag one of its groups");
            }
            if (fromArmy == request.Army && target.Length >= source.Length && target.Take(source.Length).SequenceEqual(source))
            {
                throw new ArgumentException($"The group {from} cannot go into itself or a group in it");
            }
            if (fromArmy == request.Army && source[..^1].SequenceEqual(target))
            {
                throw new ArgumentException($"The group {from} is in {where} already");
            }
            ImmutableArray<string> moved = [];
            UnitCommands.Apply(edit, d => d.MoveGroup(fromArmy, source, request.Army, target, out moved));
            armies.ChooseWhenShown(request.Army, moved);
            model.Status = $"Moved the group {from} of {fromArmy} into {where}";
            edit.Transaction.Description = $"Move the group {from} of {fromArmy} into {where}";
            return Task.FromResult(new JsonObject { ["army"] = request.Army, ["group"] = UnitGroupEdits.PathText(moved) });
        }
        string[] names = [.. (request.Units ?? []).Distinct(StringComparer.Ordinal)];
        if (names.Length == 0)
        {
            throw new ArgumentException("Drop units (the argument units) or a group (fromGroup)");
        }
        int count = 0;
        UnitCommands.Apply(edit, d => d.MoveUnits(names, request.Army, target, out count));
        if (count == 0)
        {
            throw new ArgumentException($"None of the units is on the map or outside {where}");
        }
        armies.ChooseWhenShown(request.Army, target);
        model.Status = count == 1 ? $"Moved a unit into {where}" : $"Moved {count} units into {where}";
        edit.Transaction.Description = count == 1 ? $"Move a unit into {where}" : $"Move {count} units into {where}";
        return Task.FromResult(new JsonObject { ["army"] = request.Army, ["group"] = request.Group, ["units"] = count });
    }
}
