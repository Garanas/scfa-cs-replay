using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Chooses the terrain shader the game draws the map's terrain with (user story S-4), one step of
/// undo: a technique of the game's <c>effects/terrain.fx</c> (<see cref="TerrainShaderCatalog"/>),
/// which decides how many strata blend, whether a stratum's mask counts whole or from its middle,
/// and whether stratum 8 and the upper layer are textures or map-wide data. The 3D view and the
/// generated preview blend the layers as it does. A name the editor does not know is kept as it
/// is (a mod's shader), with a warning. Without a name it shows the Paint panel, which lists them.
/// </summary>
/// <param name="Name">The shader's name, e.g. <c>TTerrainXP</c> or <c>Terrain250</c>.</param>
[EditorCommand("paint.shader", "Terrain shader...", Story = "S-4", Needs = Needs.Map | Needs.NoStroke)]
public sealed record ChooseTerrainShader(string? Name = null) : IRequest;

public sealed class ChooseTerrainShaderHandler(MainViewModel model) : IRequestHandler<ChooseTerrainShader>
{
    public Task Handle(ChooseTerrainShader request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            model.SelectMode("paint");
            string current = model.ShownDocument?.Terrain.Shader ?? "";
            model.Status = $"The terrain shader is {current}; the Paint panel lists the others";
            return Task.CompletedTask;
        }
        string name = request.Name.Trim();
        if (name.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException($"A shader's name has no spaces: '{name}'");
        }
        TerrainShaderInfo? known = TerrainShaderCatalog.Find(name);
        name = known?.Name ?? name;
        bool changed = model.EditMap($"Terrain shader: {name}", document => document with { Terrain = document.Terrain with { Shader = name } });
        if (known is null)
        {
            model.Logger("Paint").LogWarning("The terrain shader {Shader} is not one the editor knows; the views draw it as TTerrainXP", name);
            model.Status = $"The terrain shader is {name}, which the editor does not know: the views draw it as TTerrainXP";
        }
        else
        {
            model.Status = changed ? $"The terrain shader is {name}: {known.Summary}" : $"The terrain shader already is {name}";
        }
        return Task.CompletedTask;
    }
}
