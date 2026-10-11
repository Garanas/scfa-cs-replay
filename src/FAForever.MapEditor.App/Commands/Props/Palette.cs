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
[EditorCommand("props.addToPalette", "Add a prop to the palette...", Story = "P-2")]
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

/// <summary>
/// Empties the painting palette (user story P-2): the Props panel's Clear beside the palette, to
/// start a new one. The palette is the tool's, like a brush's settings, and stays out of the map's
/// history; Restore (<c>props.restorePalette</c>) brings the props back while the palette stays
/// empty, and so does undo right after it, before the map is edited again.
/// </summary>
/// <returns>How many props it held.</returns>
[EditorCommand("props.clearPalette", "Clear the palette", Story = "P-2")]
public sealed record ClearPropPalette : IRequest<int>;

public sealed class ClearPropPaletteHandler(MainViewModel model) : IRequestHandler<ClearPropPalette, int>
{
    public Task<int> Handle(ClearPropPalette request, CancellationToken cancellationToken)
    {
        int count = model.Props.ClearPalette(model.Map?.Document);
        model.Status = count == 0 ? "The palette is empty already" : $"Cleared the palette of {count} {(count == 1 ? "prop" : "props")}; Restore or undo brings them back";
        model.Commands.NotifyCanRunChanged();
        return Task.FromResult(count);
    }
}

/// <summary>
/// Brings back the props the palette's Clear took away (user story P-2), with their weights: the
/// Props panel's Restore, shown while the palette stays empty after Clear.
/// </summary>
/// <returns>How many props came back.</returns>
[EditorCommand("props.restorePalette", "Restore the palette", Story = "P-2")]
public sealed record RestorePropPalette : IRequest<int>;

public sealed class RestorePropPaletteHandler(MainViewModel model) : IRequestHandler<RestorePropPalette, int>
{
    public Task<int> Handle(RestorePropPalette request, CancellationToken cancellationToken)
    {
        int count = model.Props.RestorePalette();
        model.Status = count == 0 ? "Nothing to restore: the palette was not cleared, or has props again" : $"Restored the palette: {count} {(count == 1 ? "prop" : "props")}";
        model.Commands.NotifyCanRunChanged();
        return Task.FromResult(count);
    }
}
