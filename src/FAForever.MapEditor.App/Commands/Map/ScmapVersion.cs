using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Chooses the version of the <c>.scmap</c> the map is written as (user story M-1), one step of
/// undo: 60, which stores the skybox the game draws (a map without one gets the skybox the game
/// gave it, so it looks as before), or 56, whose maps get the game's default dome. A map of
/// version 53 keeps its version. What the Map mode's Information field File version does.
/// </summary>
/// <param name="Version">60 or 56.</param>
[EditorCommand("map.setScmapVersion", "Choose the file version", Story = "M-1", Needs = Needs.Map | Needs.NoStroke)]
public sealed record SetScmapVersion(int Version) : IRequest;

public sealed class SetScmapVersionHandler(MainViewModel model) : IRequestHandler<SetScmapVersion>
{
    public Task Handle(SetScmapVersion request, CancellationToken cancellationToken)
    {
        MapDocument document = model.ShownDocument ?? throw new InvalidOperationException("No map is open");
        // checked before the step, so a refusal makes none
        MapDocument changed = ScmapVersions.Set(document, request.Version);
        if (ReferenceEquals(changed, document))
        {
            model.Status = $"The map is written as version {request.Version} already";
            return Task.CompletedTask;
        }
        bool skybox = document.Sky.Skybox is null && changed.Sky.Skybox is not null;
        model.EditMap($"Write the map as version {request.Version}", d => ScmapVersions.Set(d, request.Version));
        model.Status = skybox
            ? $"The map is written as version {request.Version}, with the skybox the game gave it (Lighting mode, Skybox)"
            : $"The map is written as version {request.Version}";
        return Task.CompletedTask;
    }
}
