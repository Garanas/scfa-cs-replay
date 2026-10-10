using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Generation;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Generates the normal map the game lights the terrain with from the heightmap (user story S-4),
/// on a thread of the pool, and puts it in the map: one step of undo.
/// </summary>
[EditorCommand("generate.normalMap", "Normal map", Story = "S-4", Needs = Needs.Map)]
public sealed record GenerateNormalMap : IRequest;

public sealed class GenerateNormalMapHandler(MainViewModel model) : IRequestHandler<GenerateNormalMap>
{
    public async Task Handle(GenerateNormalMap request, CancellationToken cancellationToken)
    {
        await model.GenerateAsync(NormalMapGenerator.GeneratorId);
        model.Status = "Generated the normal map from the heightmap";
    }
}
