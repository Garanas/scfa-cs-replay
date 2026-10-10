using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Files.InMemory;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Reads a skybox preset of the FAF map editor (a JSON file: horizon and zenith colours and heights,
/// sky objects from an atlas with a glow, cirrus layers) into the map's version 60 sky (user story
/// L-1), one step of undo. Textures it names in another map's folder are taken from this map's folder
/// when it has a file at the same place. The sky cube map is made from it.
/// </summary>
/// <param name="Path">The JSON file; left out, a file is asked for.</param>
/// <returns>How many sky objects and cirrus layers it has; null when no file was chosen.</returns>
[EditorCommand("lighting.importSkybox", "Read a skybox preset...", Story = "L-1", Needs = Needs.Map | Needs.NoStroke)]
public sealed record ImportSkybox(string? Path = null) : IRequest<string?>;

public sealed class ImportSkyboxHandler(MainViewModel model, IEditorDialogs dialogs) : IRequestHandler<ImportSkybox, string?>
{
    public async Task<string?> Handle(ImportSkybox request, CancellationToken cancellationToken)
    {
        string? path = request.Path ?? await dialogs.PickFileAsync("Read a skybox preset", ["json"], model.Map?.ProjectFolder);
        if (path is null)
        {
            model.Status = "Chose no file";
            return null;
        }
        Skybox skybox = model.ImportSkybox(path);
        string what = $"{skybox.Planets.Length} sky objects, {skybox.CirrusLayers.Length} cirrus layers";
        model.Status = $"Read the skybox {System.IO.Path.GetFileName(path)}: {what}";
        return what;
    }
}
