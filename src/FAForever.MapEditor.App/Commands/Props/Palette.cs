using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Workspaces;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Adds a prop to the palette the paint tool paints (user story P-2), chosen in the picker, or
/// gives one in it another weight. Run without a person, the key is the argument.
/// </summary>
/// <param name="Key">A prop's blueprint path, to add without asking.</param>
/// <param name="Weight">How often it is painted against the others; left out, 1 for a new one.</param>
/// <returns>The prop's key; null when the picker was cancelled.</returns>
[EditorCommand("props.addToPalette", "Add a prop to the palette...", Story = "P-2", Needs = Needs.Workspace)]
public sealed record AddToPropPalette(string? Key = null, double? Weight = null) : IRequest<string?>;

public sealed class AddToPropPaletteHandler(MainViewModel model, IEditorDialogs dialogs) : IRequestHandler<AddToPropPalette, string?>
{
    public async Task<string?> Handle(AddToPropPalette request, CancellationToken cancellationToken)
    {
        if (request.Weight is double weight && (!double.IsFinite(weight) || weight < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(request.Weight), "A weight is 0 or more");
        }
        string? chosen = request.Key ?? await dialogs.PickAssetAsync(AssetKind.Prop, "Add a prop to the palette", model.Props.Palette.LastOrDefault()?.Key);
        if (chosen is null)
        {
            model.Status = "Chose nothing";
            return null;
        }
        PaletteItem item = model.Props.AddToPalette(chosen, request.Weight);
        model.Status = $"{item.Name} is in the palette with a weight of {item.Weight:0.##}";
        return item.Key;
    }
}

/// <summary>
/// Takes a prop out of the palette (user story P-2): the cross of its line in the Props panel.
/// </summary>
/// <param name="Key">The prop's blueprint path.</param>
/// <returns>Whether it was in the palette.</returns>
[EditorCommand("props.removeFromPalette", "Take a prop out of the palette", Story = "P-2")]
public sealed record RemoveFromPropPalette(string Key) : IRequest<bool>;

public sealed class RemoveFromPropPaletteHandler(MainViewModel model) : IRequestHandler<RemoveFromPropPalette, bool>
{
    public Task<bool> Handle(RemoveFromPropPalette request, CancellationToken cancellationToken) =>
        Task.FromResult(model.Props.RemoveFromPalette(request.Key));
}
