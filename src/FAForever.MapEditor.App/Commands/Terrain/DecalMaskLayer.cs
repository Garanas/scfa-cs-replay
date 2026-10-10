using System.Collections.Immutable;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Changes what a decal-masks height layer draws (user story D-1): which mask of each decal set,
/// of which sets, and how high a full mask raises the terrain; one step of undo, the layer drawn again.
/// </summary>
/// <param name="Layer">The layer; left out, the chosen one.</param>
/// <param name="Mask"><c>main</c> for the plain <c>_mask</c>, else the name after <c>_mask_</c>.</param>
/// <param name="Sets">The sets by name (<c>/env/rocky/decals/rock01</c>) separated by commas; empty for every set.</param>
/// <param name="Height">In game units at a full mask.</param>
[EditorCommand("terrain.setDecalMasks", "Set a decal-masks layer", Story = "D-1", Needs = Needs.Map | Needs.NoStroke)]
public sealed record SetDecalMasksLayer(string? Layer = null, string? Mask = null, string? Sets = null, float? Height = null) : IRequest;

public sealed class SetDecalMasksLayerHandler(MainViewModel model) : IRequestHandler<SetDecalMasksLayer>
{
    public Task Handle(SetDecalMasksLayer request, CancellationToken cancellationToken)
    {
        Layer layer = HeightLayerArgument.Of(model, request.Layer);
        if (layer.Source is not DecalMasksSource source)
        {
            throw new ArgumentException($"The height layer {layer.Name} does not draw decal masks");
        }
        string mask = request.Mask?.Trim().ToLowerInvariant() is { Length: > 0 } named ? named : source.Mask;
        ImmutableArray<string> sets = request.Sets is string list
            ? [.. list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(DecalEdits.TexturePath)]
            : source.Sets;
        DecalMasksSource changed = new DecalMasksSource(mask, sets, request.Height ?? source.Height);
        model.EditMap($"Set the height layer {layer.Name}", document =>
            document with { HeightStack = DecalMaskLayers.SetSource(document, document.HeightStack, layer.Name, changed, model.DecalPictures) });
        model.HeightStack.Refresh(model.ShownDocument);
        model.Status = $"The height layer {layer.Name} draws the {LayerStackViewModel.DecalMasksSummary(changed)}";
        return Task.CompletedTask;
    }
}
