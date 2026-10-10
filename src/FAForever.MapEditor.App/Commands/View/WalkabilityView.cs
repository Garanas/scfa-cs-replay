using System.Text.Json.Nodes;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.App.ViewModels;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Chooses what the walkability overlay shows (user story T-7), as the Terrain panel's choices do:
/// the layer of units (<c>Land</c>, <c>Hover</c>, <c>Amphibious</c>, <c>Water</c> for naval units)
/// and whether what changed since the time before shows over it. It shows the overlay.
/// </summary>
/// <returns>The walkability as <c>generate.walkability</c> answers it.</returns>
[EditorCommand("view.walkability", "Walkability's layer", Story = "T-7")]
public sealed record ChooseWalkabilityView(string? Layer = null, bool? Changes = null) : IRequest<JsonObject>;

public sealed class ChooseWalkabilityViewHandler(MainViewModel model, WalkabilityViewModel walkability) : IRequestHandler<ChooseWalkabilityView, JsonObject>
{
    public Task<JsonObject> Handle(ChooseWalkabilityView request, CancellationToken cancellationToken)
    {
        if (request.Layer is string layer)
        {
            walkability.Layer = Enum.TryParse(layer, ignoreCase: true, out NavLayer chosen)
                ? chosen
                : throw new ArgumentException($"There is no layer '{layer}': {string.Join(", ", walkability.Layers)}");
        }
        if (request.Changes is bool changes)
        {
            walkability.ShowChanges = changes;
        }
        model.ShowWalkability = true;
        return Task.FromResult(GenerateWalkabilityHandler.Describe(walkability));
    }
}
