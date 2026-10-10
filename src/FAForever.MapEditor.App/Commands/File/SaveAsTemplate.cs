using System.Text.Json.Nodes;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Saves the open map as a template of the workspace (user story M-2), which the New map page then
/// offers: by default a small JSON file of its settings (its size, mean height, water, strata,
/// number of players, extra armies and lighting); with <c>whole</c> the whole map as a project in a
/// folder of the templates, which a new map copies. A template of that name is replaced.
/// </summary>
/// <param name="Name">Left out: the map's name.</param>
/// <param name="Whole">Whether the whole map is the template, not only its settings.</param>
/// <returns>The template's name, kind and file or folder.</returns>
[EditorCommand("file.saveAsTemplate", "Save as a template", Story = "M-2", Needs = Needs.Map | Needs.Workspace | Needs.NoStroke)]
public sealed record SaveAsTemplate(string? Name = null, bool? Whole = null) : IRequest<JsonObject>;

public sealed class SaveAsTemplateHandler(MainViewModel model) : IRequestHandler<SaveAsTemplate, JsonObject>
{
    public Task<JsonObject> Handle(SaveAsTemplate request, CancellationToken cancellationToken)
    {
        Workspace workspace = model.Setup.Workspace ?? throw new InvalidOperationException("Templates are kept in the workspace: set one up first");
        MapDocument document = model.ShownDocument ?? throw new InvalidOperationException("No map is open");
        string name = MapTemplates.FileName(request.Name ?? document.Scenario.Name);
        if (name.Equals(MapTemplates.SandboxName, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"{MapTemplates.SandboxName} is the editor's own template: choose another name");
        }
        bool whole = request.Whole ?? false;
        string path;
        try
        {
            path = whole
                ? MapTemplates.SaveProject(workspace.TemplatesFolder, name, document)
                : MapTemplates.SaveSettings(workspace.TemplatesFolder, name, document);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or MapProjectException)
        {
            model.Logger("Files").LogError(exception, "Saving the template {Name} failed: {Reason}", name, exception.Message);
            throw new InvalidOperationException($"The template {name} cannot be saved: {exception.Message}", exception);
        }
        model.Logger("Files").LogInformation("Saved the map as the template {Name} in {Path}", name, path);
        model.Status = whole
            ? $"Saved the map as the template {name}: File, New map copies it"
            : $"Saved the map's settings as the template {name}: File, New map fills its fields with them";
        return Task.FromResult(new JsonObject { ["name"] = name, ["kind"] = whole ? nameof(MapTemplateKind.Project) : nameof(MapTemplateKind.Settings), ["path"] = path });
    }
}
