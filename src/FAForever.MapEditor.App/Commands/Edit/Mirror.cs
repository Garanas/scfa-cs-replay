using System.Collections.Immutable;
using System.Text.Json.Nodes;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Copies the selection into every other part of the map by the chosen symmetry (user story F-12),
/// one step of undo: props, decals, markers and units at their mirrored places, a prop's rotation
/// mirrored, spawns as the next armies, path nodes linked as the originals are. Where a thing has a
/// mirror partner already, or its image lies on itself, nothing is added; areas stay as they are.
/// The selection becomes the things with their copies.
/// </summary>
/// <returns>How many copies were added.</returns>
[UndoStep]
[EditorCommand("edit.mirror", "Mirror the selection", Story = "F-12", Needs = Needs.Selection)]
public sealed record MirrorSelection : IRequest<JsonObject>;

public sealed class MirrorSelectionHandler(MainViewModel model, SymmetryViewModel symmetry, SelectionViewModel selection, CurrentEdit edit) : IRequestHandler<MirrorSelection, JsonObject>
{
    public Task<JsonObject> Handle(MirrorSelection request, CancellationToken cancellationToken)
    {
        if (!symmetry.IsOn)
        {
            throw new InvalidOperationException("Choose a symmetry first (Select, Symmetry on or off)");
        }
        ImmutableArray<ObjectRef> things = selection.Items;
        ImmutableArray<ObjectRef> images = [];
        edit.Transaction.Apply(document => model.MirrorAll(document, things, symmetry.Transforms(document), out images));
        selection.Set([.. things, .. images]);
        model.Status = images.IsEmpty ? "Nothing to mirror: every selected thing has its mirror partners" : $"Mirrored the selection: {images.Length} copies";
        return Task.FromResult(new JsonObject { ["added"] = images.Length, ["refs"] = new JsonArray([.. images.Take(200).Select(image => (JsonNode)image.ToString())]) });
    }
}
