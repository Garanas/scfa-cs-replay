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
/// folder of the templates, which a new map copies. A template of that name is replaced. Run by a
/// person without arguments (the menu, the palette), it asks over the window for the name and
/// whether the settings or the whole map are the template.
/// </summary>
/// <param name="Name">Left out: the map's name (or the name the person gives).</param>
/// <param name="Whole">Whether the whole map is the template, not only its settings.</param>
/// <returns>The template's name, kind and file or folder.</returns>
[EditorCommand("file.saveAsTemplate", "Save as a template", Story = "M-2", Needs = Needs.Map | Needs.Workspace | Needs.NoStroke)]
public sealed record SaveAsTemplate(string? Name = null, bool? Whole = null) : IRequest<JsonObject>;

public sealed class SaveAsTemplateHandler(MainViewModel model, IEditorDialogs dialogs) : IRequestHandler<SaveAsTemplate, JsonObject>
{
    /// <summary>
    /// The buttons of the question, in this order: the settings (the accent, Enter in the name), the whole map.
    /// </summary>
    internal static readonly string[] Buttons = ["Save the settings", "Save the whole map"];

    public async Task<JsonObject> Handle(SaveAsTemplate request, CancellationToken cancellationToken)
    {
        Workspace workspace = model.Setup.Workspace ?? throw new InvalidOperationException("Templates are kept in the workspace: set one up first");
        MapDocument document = model.ShownDocument ?? throw new InvalidOperationException("No map is open");
        string? given = request.Name;
        bool whole = request.Whole ?? false;
        if (given is null && request.Whole is null && dialogs is not DialogGuard { Quiet: true })
        {
            string suggested = document.Scenario.Name.Equals(MapTemplates.SandboxName, StringComparison.OrdinalIgnoreCase) ? $"My {document.Scenario.Name}" : document.Scenario.Name;
            string message = "The settings are a small file of the map's size, height, water, strata, players and lighting, which fill New map's fields; "
                + "the whole map is a copy of everything on it, which a new map starts as.";
            for (string error = ""; ; )
            {
                if (await dialogs.AskTextAsync("Save as a template", error + message, "Name of the template", suggested, Buttons) is not (int button, string text))
                {
                    model.Status = "Saving as a template cancelled";
                    return new JsonObject { ["cancelled"] = true };
                }
                whole = button == 1;
                suggested = text;
                error = WhyNot(text) is string why ? why + ". " : "";
                if (error.Length == 0)
                {
                    given = text.Trim();
                    break;
                }
            }
        }
        string name = MapTemplates.FileName(given ?? document.Scenario.Name);
        if (WhyNot(name) is string refused)
        {
            throw new ArgumentException(refused);
        }
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
        return new JsonObject { ["name"] = name, ["kind"] = whole ? nameof(MapTemplateKind.Project) : nameof(MapTemplateKind.Settings), ["path"] = path };
    }

    /// <summary>
    /// Why a template cannot have this name, or null when it can.
    /// </summary>
    private static string? WhyNot(string name) =>
        name.Trim().Length == 0 ? "A template needs a name"
        : MapTemplates.FileName(name).Equals(MapTemplates.SandboxName, StringComparison.OrdinalIgnoreCase) ? $"{MapTemplates.SandboxName} is the editor's own template: choose another name"
        : null;
}
