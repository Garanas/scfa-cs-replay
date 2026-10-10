using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Adds an empty mask for tools (user story K-1), one step of undo, and chooses it: the masks tool
/// paints it. It is saved as <c>masks/NAME.png</c> in the project, for World Machine, Gaea and the
/// editor's rules; the export leaves it out.
/// </summary>
/// <param name="Name">Letters, digits, spaces, dashes and underscores; left out, the name in the
/// panel's field, else a free one (<c>Mask</c>, <c>Mask 2</c>).</param>
/// <param name="Half">Half the map's size, as the stratum masks mostly are; left out, as the panel
/// says (the map's size unless chosen).</param>
/// <returns>The new mask's name.</returns>
[UndoStep]
[EditorCommand("paint.addMask", "Add a mask", Story = "K-1")]
public sealed record AddMask(string? Name = null, bool? Half = null) : IRequest<string>;

public sealed class AddMaskHandler(MainViewModel model, CurrentEdit edit) : IRequestHandler<AddMask, string>
{
    public Task<string> Handle(AddMask request, CancellationToken cancellationToken)
    {
        MasksViewModel masks = model.Masks;
        string name = request.Name?.Trim() is { Length: > 0 } given
            ? given
            : masks.NewName.Trim() is { Length: > 0 } typed ? typed : MaskLayers.FreeName(edit.Transaction.Document);
        edit.Transaction.Apply(document => MaskLayers.Add(document, name, request.Half ?? masks.Half));
        masks.NewName = "";
        masks.Chosen = name;
        model.Status = $"Added the mask {name}";
        return Task.FromResult(name);
    }
}
