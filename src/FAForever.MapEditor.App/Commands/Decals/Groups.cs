using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Makes a decal group of the selected decals (user story D-2), one step of undo; without a
/// selection the group starts empty.
/// </summary>
/// <param name="Name">Left out, <c>Group 1</c> (or the next number free); a name taken gets a number.</param>
/// <returns>The new group's id.</returns>
[UndoStep]
[EditorCommand("decals.addGroup", "Add a decal group", Story = "D-2")]
public sealed record AddDecalGroup(string? Name = null) : IRequest<int>;

public sealed class AddDecalGroupHandler(MainViewModel model, SelectionViewModel selection, CurrentEdit edit) : IRequestHandler<AddDecalGroup, int>
{
    public Task<int> Handle(AddDecalGroup request, CancellationToken cancellationToken)
    {
        DecalGroup added = null!;
        edit.Transaction.Apply(document => document.AddDecalGroup(request.Name ?? "Group 1", selection.Items, out added));
        model.Status = $"Made the decal group {added.Name} of {added.DecalIds.Length} decal{(added.DecalIds.Length == 1 ? "" : "s")}";
        return Task.FromResult(added.Id);
    }
}

/// <summary>
/// Renames a decal group (user story D-2), one step of undo: what typing in its name field does.
/// </summary>
/// <param name="Group">The group's id.</param>
[UndoStep]
[EditorCommand("decals.renameGroup", "Rename a decal group", Story = "D-2")]
public sealed record RenameDecalGroup(int Group, string Name) : IRequest;

public sealed class RenameDecalGroupHandler(CurrentEdit edit) : IRequestHandler<RenameDecalGroup>
{
    public Task Handle(RenameDecalGroup request, CancellationToken cancellationToken)
    {
        edit.Transaction.Apply(document => document.RenameDecalGroup(request.Group, request.Name));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Removes a decal group (user story D-2), one step of undo; its decals stay on the map.
/// </summary>
/// <param name="Group">The group's id.</param>
[UndoStep]
[EditorCommand("decals.deleteGroup", "Delete a decal group", Story = "D-2")]
public sealed record DeleteDecalGroup(int Group) : IRequest;

public sealed class DeleteDecalGroupHandler(CurrentEdit edit) : IRequestHandler<DeleteDecalGroup>
{
    public Task Handle(DeleteDecalGroup request, CancellationToken cancellationToken)
    {
        edit.Transaction.Apply(document => document.RemoveDecalGroup(request.Group));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Adds the selected decals to a decal group (user story D-2), one step of undo.
/// </summary>
/// <param name="Group">The group's id.</param>
[UndoStep]
[EditorCommand("decals.addToGroup", "Add the selection to a decal group", Story = "D-2", Needs = Needs.Selection)]
public sealed record AddToDecalGroup(int Group) : IRequest;

public sealed class AddToDecalGroupHandler(SelectionViewModel selection, CurrentEdit edit) : IRequestHandler<AddToDecalGroup>
{
    public Task Handle(AddToDecalGroup request, CancellationToken cancellationToken)
    {
        System.Collections.Immutable.ImmutableArray<ObjectRef> decals = SelectedDecals.Of(selection);
        edit.Transaction.Apply(document => document.AddToDecalGroup(request.Group, decals));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Takes the selected decals out of a decal group (user story D-2), one step of undo; they stay on the map.
/// </summary>
/// <param name="Group">The group's id.</param>
[UndoStep]
[EditorCommand("decals.removeFromGroup", "Take the selection out of a decal group", Story = "D-2", Needs = Needs.Selection)]
public sealed record RemoveFromDecalGroup(int Group) : IRequest;

public sealed class RemoveFromDecalGroupHandler(SelectionViewModel selection, CurrentEdit edit) : IRequestHandler<RemoveFromDecalGroup>
{
    public Task Handle(RemoveFromDecalGroup request, CancellationToken cancellationToken)
    {
        System.Collections.Immutable.ImmutableArray<ObjectRef> decals = SelectedDecals.Of(selection);
        edit.Transaction.Apply(document => document.RemoveFromDecalGroup(request.Group, decals));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Selects the decals of a decal group (user story D-2), those shown.
/// </summary>
/// <param name="Group">The group's id.</param>
/// <param name="Add">Whether to add them to the selection instead of replacing it.</param>
/// <returns>How many it selected.</returns>
[EditorCommand("decals.selectGroup", "Select a decal group", Story = "D-2", Needs = Needs.Map)]
public sealed record SelectDecalGroup(int Group, bool Add = false) : IRequest<int>;

public sealed class SelectDecalGroupHandler(MainViewModel model, SelectionViewModel selection, DecalsViewModel decals) : IRequestHandler<SelectDecalGroup, int>
{
    public Task<int> Handle(SelectDecalGroup request, CancellationToken cancellationToken)
    {
        MapDocument document = model.ShownDocument!;
        DecalGroup group = document.FindDecalGroup(request.Group) ?? throw new ArgumentException($"The map has no decal group {request.Group}");
        ObjectRef[] things = [.. document.DecalsOf(group).Where(decals.IsShown)];
        selection.Apply(things, request.Add ? SelectionChange.Add : SelectionChange.Replace);
        model.Status = things.Length == 0 && !group.DecalIds.IsEmpty
            ? $"The decals of {group.Name} are hidden: show the group to select them"
            : $"Selected {things.Length} decal{(things.Length == 1 ? "" : "s")} of {group.Name}";
        return Task.FromResult(things.Length);
    }
}

/// <summary>
/// Shows or hides a decal group's decals in the views (user story D-2): an editor setting, never
/// saved or exported. Hidden decals are not drawn in 3D and cannot be picked.
/// </summary>
/// <param name="Group">The group's id.</param>
/// <param name="On">Whether it shows; left out, the other way than now.</param>
/// <returns>Whether it shows now.</returns>
[EditorCommand("decals.showGroup", "Show or hide a decal group", Story = "D-2", Needs = Needs.Map)]
public sealed record ShowDecalGroup(int Group, bool? On = null) : IRequest<bool>;

public sealed class ShowDecalGroupHandler(MainViewModel model, DecalsViewModel decals) : IRequestHandler<ShowDecalGroup, bool>
{
    public Task<bool> Handle(ShowDecalGroup request, CancellationToken cancellationToken)
    {
        bool on = request.On ?? decals.HiddenGroups.Contains(request.Group);
        decals.SetShown(model.ShownDocument!, request.Group, on);
        return Task.FromResult(on);
    }
}
