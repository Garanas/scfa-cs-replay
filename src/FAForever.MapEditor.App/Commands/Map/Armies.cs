using System.Numerics;
using System.Text.Json.Nodes;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Shows the map's armies and teams (user story M-4): the Map mode, whose panel lists them, with an
/// army's settings in the inspector (the one asked for, else the chosen army, else the first). Its
/// answer lists the armies, as automation's <c>state</c> does.
/// </summary>
/// <param name="Army">An army to show.</param>
[EditorCommand("map.armies", "Armies and teams...", Story = "M-4", Needs = Needs.Map)]
public sealed record EditArmies(string? Army = null) : IRequest<JsonArray>;

public sealed class EditArmiesHandler(MainViewModel model, ArmiesViewModel armies) : IRequestHandler<EditArmies, JsonArray>
{
    public Task<JsonArray> Handle(EditArmies request, CancellationToken cancellationToken)
    {
        string? army = request.Army ?? armies.ChosenArmy?.Name ?? armies.Rows.FirstOrDefault()?.Name;
        if (request.Army is not null && !armies.Rows.Any(row => row.Name == request.Army))
        {
            throw new ArgumentException($"The map has no army {request.Army}");
        }
        if (army is null)
        {
            model.SelectMode("map");
        }
        else
        {
            model.InspectPart(MapPartsViewModel.ArmyPart(army));
        }
        return Task.FromResult(armies.DescribeArmies());
    }
}

/// <summary>
/// Adds an army without units (user story M-4), one step of undo: playable, at the end of the
/// playable armies with a spawn at a place, or extra (civilians), without one.
/// </summary>
/// <param name="Name">Left out, the next free <c>ARMY_n</c>.</param>
/// <param name="Extra">Whether no player takes it.</param>
/// <param name="X">Where a playable army's spawn goes, in ogrids; left out, the middle of the map.</param>
/// <returns>The army's name.</returns>
[UndoStep]
[EditorCommand("map.addArmy", "Add an army", Story = "M-4")]
public sealed record AddArmy(string? Name = null, bool Extra = false, float? X = null, float? Z = null) : IRequest<string>;

public sealed class AddArmyHandler(MainViewModel model, ArmiesViewModel armies, CurrentEdit edit) : IRequestHandler<AddArmy, string>
{
    public Task<string> Handle(AddArmy request, CancellationToken cancellationToken)
    {
        Vector2? spawn = (request.X, request.Z) is (float x, float z) ? new Vector2(x, z) : null;
        string added = "";
        UnitCommands.Apply(edit, d => d.AddArmy(request.Name, !request.Extra, spawn, out added));
        armies.ChooseArmyWhenShown(added);
        model.ShowAddedArmy(added);
        model.Status = request.Extra ? $"Added the extra army {added}" : $"Added the army {added} with its spawn";
        return Task.FromResult(added);
    }
}

/// <summary>
/// Removes an army (user story M-4), one step of undo, with its units and its spawn, and every
/// mention of it: the scenario's lists, its factions, its team, the other armies' alliances.
/// </summary>
[UndoStep]
[EditorCommand("map.removeArmy", "Remove an army", Story = "M-4")]
public sealed record RemoveArmy(string Army) : IRequest;

public sealed class RemoveArmyHandler(MainViewModel model, CurrentEdit edit) : IRequestHandler<RemoveArmy>
{
    public Task Handle(RemoveArmy request, CancellationToken cancellationToken)
    {
        UnitCommands.Apply(edit, d => d.RemoveArmy(request.Army));
        model.Status = $"Removed the army {request.Army}, its units and its spawn";
        return Task.CompletedTask;
    }
}

/// <summary>
/// Renames an army everywhere it is named (user story M-4), one step of undo: the scenario's
/// lists, its factions and team, the other armies' alliances, and its spawn marker.
/// </summary>
/// <param name="Name">Letters, digits and underscores, not starting with a digit.</param>
[UndoStep]
[EditorCommand("map.renameArmy", "Rename an army", Story = "M-4")]
public sealed record RenameArmy(string Army, string Name) : IRequest;

public sealed class RenameArmyHandler(MainViewModel model, ArmiesViewModel armies, CurrentEdit edit) : IRequestHandler<RenameArmy>
{
    public Task Handle(RenameArmy request, CancellationToken cancellationToken)
    {
        UnitCommands.Apply(edit, d => d.RenameArmy(request.Army, request.Name));
        armies.ChooseArmyWhenShown(request.Name.Trim());
        model.Status = $"Renamed the army {request.Army} to {request.Name.Trim()}";
        return Task.CompletedTask;
    }
}

/// <summary>
/// Changes an army (user story M-4), one step of undo: whether players take it (a playable army
/// gets a spawn when it has none; an extra one leaves its team and factions), the factions it is
/// limited to, and its team. What is left out stays.
/// </summary>
/// <param name="Factions">Of <c>uef</c>, <c>aeon</c>, <c>cybran</c>, <c>seraphim</c>; none or all four lifts the limit.</param>
/// <param name="Team">The team's name, made when there is none; empty for no team.</param>
[UndoStep]
[EditorCommand("map.setArmy", "Change an army", Story = "M-4")]
public sealed record SetArmy(string Army, bool? Playable = null, string[]? Factions = null, string? Team = null) : IRequest;

public sealed class SetArmyHandler(MainViewModel model, CurrentEdit edit) : IRequestHandler<SetArmy>
{
    public Task Handle(SetArmy request, CancellationToken cancellationToken)
    {
        UnitCommands.Apply(edit, d =>
        {
            if (request.Playable is bool playable)
            {
                d = d.SetArmyPlayable(request.Army, playable);
            }
            if (request.Factions is string[] factions)
            {
                d = d.SetArmyFactions(request.Army, factions);
            }
            if (request.Team is string team)
            {
                d = d.SetTeam(request.Army, team);
            }
            return d;
        });
        model.Status = $"Changed the army {request.Army}";
        return Task.CompletedTask;
    }
}

/// <summary>
/// Sets how an army stands towards another (user story M-4), one step of undo, and the other
/// towards it too unless <paramref name="Mutual"/> is false.
/// </summary>
/// <param name="Stance"><c>Ally</c>, <c>Enemy</c> or <c>Neutral</c>; left out, none.</param>
[UndoStep]
[EditorCommand("map.setAlliance", "Set an alliance", Story = "M-4")]
public sealed record SetAlliance(string Army, string Other, string? Stance = null, bool Mutual = true) : IRequest;

public sealed class SetAllianceHandler(MainViewModel model, CurrentEdit edit) : IRequestHandler<SetAlliance>
{
    public Task Handle(SetAlliance request, CancellationToken cancellationToken)
    {
        UnitCommands.Apply(edit, d => d.SetAlliance(request.Army, request.Other, request.Stance, request.Mutual));
        model.Status = request.Stance is null
            ? $"{request.Army} has no stance towards {request.Other}"
            : $"{request.Army} is {request.Stance.ToLowerInvariant()} to {request.Other}{(request.Mutual ? " and back" : "")}";
        return Task.CompletedTask;
    }
}
