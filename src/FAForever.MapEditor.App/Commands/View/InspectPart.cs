using FAForever.MapEditor.App.ViewModels;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Shows an entry of the Water or Lighting panel's list in the inspector, as a click on it does:
/// <c>water.elevations</c>, <c>water.surface</c>, <c>water.textures</c>, <c>water.foam</c>,
/// <c>water.flatness</c>, <c>water.depthBias</c>, <c>water.waves</c>, <c>lighting.sun</c>,
/// <c>lighting.colours</c>, <c>lighting.fog</c>, <c>lighting.bloom</c>, <c>lighting.sky</c>,
/// <c>lighting.shadowDecal</c>. Its mode is chosen first; a water map chooses the Paint water tool
/// painting it.
/// </summary>
[EditorCommand("view.inspectPart", "Show a part of the panel in the inspector", Story = "F-11")]
public sealed record InspectPart(string Part) : IRequest<string>;

public sealed class InspectPartHandler(MainViewModel model) : IRequestHandler<InspectPart, string>
{
    public Task<string> Handle(InspectPart request, CancellationToken cancellationToken)
    {
        model.InspectPart(request.Part);
        return Task.FromResult(request.Part);
    }
}
