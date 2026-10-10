using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Makes a map from scratch (user story M-2): flat terrain at a height, water or none, the strata
/// of a biome, spawns on a circle and the playable area over the whole map. It is no project until
/// it is saved. Without arguments, with a person, it shows the page of a new map, which runs it with
/// the choices; with arguments it makes the map at once, without asking about the open map's
/// changes (as <c>file.open</c> with a folder).
/// </summary>
/// <param name="Size">In ogrids: 256 (5 km), 512, 1024, 2048 or 4096 (81 km).</param>
/// <param name="Height">Of the terrain, in game units.</param>
/// <param name="WaterElevation">The water's surface; deep and abyss follow 5 and 15 below unless given.</param>
/// <param name="Biome"><c>evergreen</c>, <c>desert</c>, <c>tundra</c>, <c>lava</c>, <c>tropical</c> or <c>red-barrens</c>.</param>
/// <param name="Players">The spawns, 1 to 16.</param>
[EditorCommand("file.new", "New map...", Gesture = "Ctrl+N", Story = "M-2", Needs = Needs.Idle | Needs.NoStroke)]
public sealed record NewMapRequest(
    string? Name = null,
    int? Size = null,
    float? Height = null,
    bool? Water = null,
    float? WaterElevation = null,
    float? DeepElevation = null,
    float? AbyssElevation = null,
    string? Biome = null,
    int? Players = null) : IRequest
{
    public bool IsEmpty => this == new NewMapRequest();
}

public sealed class NewMapHandler(MainViewModel model, IEditorDialogs dialogs) : IRequestHandler<NewMapRequest>
{
    public Task Handle(NewMapRequest request, CancellationToken cancellationToken)
    {
        if (request.IsEmpty && dialogs is not DialogGuard { Quiet: true })
        {
            model.NewMapPage.Open();
            return Task.CompletedTask;
        }
        NewMapSettings defaults = new NewMapSettings();
        NewMapSettings settings = new NewMapSettings
        {
            Name = request.Name ?? defaults.Name,
            Size = request.Size ?? defaults.Size,
            Height = request.Height ?? defaults.Height,
            Water = request.Water ?? defaults.Water,
            WaterElevation = request.WaterElevation ?? defaults.WaterElevation,
            DeepElevation = request.DeepElevation,
            AbyssElevation = request.AbyssElevation,
            Biome = request.Biome ?? defaults.Biome,
            Players = request.Players ?? defaults.Players,
            Author = model.VersionControl.AuthorName,
        };
        model.OpenNewMap(NewMap.Create(settings));
        return Task.CompletedTask;
    }
}
