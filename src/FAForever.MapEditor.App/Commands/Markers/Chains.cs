using System.Collections.Immutable;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Makes a chain of markers in order (user story R-4), one step of undo, and chooses it in the
/// Markers panel: of the selected markers in the order they were selected, or of the markers given.
/// </summary>
/// <param name="Name">Left out, <c>Chain_1</c> or the next free number; a name that is taken gets one too.</param>
/// <param name="Markers">The ids of the markers in order (<c>marker:12</c> is 12); left out, the selected markers.</param>
/// <returns>The chain's name.</returns>
[UndoStep]
[EditorCommand("markers.addChain", "Add a chain", Story = "R-4", Needs = Needs.Map)]
public sealed record AddMarkerChain(string? Name = null, int[]? Markers = null) : IRequest<string>;

public sealed class AddMarkerChainHandler(MarkersViewModel markers, SelectionViewModel selection, CurrentEdit edit) : IRequestHandler<AddMarkerChain, string>
{
    public Task<string> Handle(AddMarkerChain request, CancellationToken cancellationToken)
    {
        MapDocument document = edit.Transaction.Document;
        int[] ids = request.Markers ?? [.. selection.Items.Where(thing => thing.Kind == ObjectKind.Marker).Select(thing => thing.Id)];
        if (ids.Length == 0)
        {
            throw new InvalidOperationException("A chain needs markers: select them in order, or click them with the chain tool");
        }
        ChainCommands.RequireMarkers(document, ids);
        MapDocument changed = document.AddChain(new Chain { Name = request.Name?.Trim() ?? "", MarkerIds = [.. ids] }, out Chain added);
        edit.Transaction.Apply(_ => changed);
        markers.Chose(added.Name);
        return Task.FromResult(added.Name);
    }
}

/// <summary>
/// Gives a chain its markers in a new order (user story R-4), one step of undo: what the chain
/// tool and the panel's buttons (up, down, take out) do.
/// </summary>
/// <param name="Markers">The ids of its markers in order.</param>
[UndoStep]
[EditorCommand("markers.setChain", "Change a chain", Story = "R-4", Needs = Needs.Map)]
public sealed record SetMarkerChain(string Name, int[] Markers) : IRequest;

public sealed class SetMarkerChainHandler(CurrentEdit edit) : IRequestHandler<SetMarkerChain>
{
    public Task Handle(SetMarkerChain request, CancellationToken cancellationToken)
    {
        ChainCommands.RequireMarkers(edit.Transaction.Document, request.Markers);
        ImmutableArray<int> ids = [.. request.Markers];
        edit.Transaction.Apply(document => document.UpdateChain(request.Name, chain => chain with { MarkerIds = ids }));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Renames a chain (user story R-4), one step of undo: the panel's name field.
/// </summary>
/// <param name="To">The new name, which no other chain has.</param>
[UndoStep]
[EditorCommand("markers.renameChain", "Rename a chain", Story = "R-4", Needs = Needs.Map)]
public sealed record RenameMarkerChain(string Name, string To) : IRequest;

public sealed class RenameMarkerChainHandler(MarkersViewModel markers, CurrentEdit edit) : IRequestHandler<RenameMarkerChain>
{
    public Task Handle(RenameMarkerChain request, CancellationToken cancellationToken)
    {
        string to = request.To.Trim();
        edit.Transaction.Apply(document => document.UpdateChain(request.Name, chain => chain with { Name = to }));
        markers.Chose(to);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Deletes a chain (user story R-4), one step of undo; its markers stay.
/// </summary>
[UndoStep]
[EditorCommand("markers.deleteChain", "Delete a chain", Story = "R-4", Needs = Needs.Map)]
public sealed record DeleteMarkerChain(string Name) : IRequest;

public sealed class DeleteMarkerChainHandler(CurrentEdit edit) : IRequestHandler<DeleteMarkerChain>
{
    public Task Handle(DeleteMarkerChain request, CancellationToken cancellationToken)
    {
        edit.Transaction.Apply(document => document.RemoveChain(request.Name));
        return Task.CompletedTask;
    }
}

internal static class ChainCommands
{
    /// <exception cref="ArgumentException">The map has no marker of one of the ids.</exception>
    public static void RequireMarkers(MapDocument document, IEnumerable<int> ids)
    {
        foreach (int id in ids)
        {
            if (document.FindMarker(id) is null)
            {
                throw new ArgumentException($"The map has no marker {id}");
            }
        }
    }
}
