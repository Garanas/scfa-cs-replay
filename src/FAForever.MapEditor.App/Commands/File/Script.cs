using System.Text;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Opens the map's script (the project's <c>script.lua</c>) in the person's own editor, the
/// system's application for <c>.lua</c> files (user story M-6). What that editor saves is read
/// back into the map as a step of undo, as every file of the project. A map without a script gets
/// the script of a skirmish map first, written into the project (and read back the same way). Run
/// without a person it opens nothing and answers the file's path.
/// </summary>
/// <returns>The script's path.</returns>
[EditorCommand("file.openScript", "Open the script", Story = "M-6", Needs = Needs.Project)]
public sealed record OpenScript : IRequest<string>;

public sealed class OpenScriptHandler(MainViewModel model, IEditorDialogs dialogs) : IRequestHandler<OpenScript, string>
{
    public async Task<string> Handle(OpenScript request, CancellationToken cancellationToken)
    {
        string folder = model.Map?.ProjectFolder ?? throw new InvalidOperationException("Save the map as a project first: its script is a file of the project");
        string script = Path.Combine(folder, MapProjectLayout.Script);
        if (!File.Exists(script))
        {
            if (model.Map.Document.Script is not null)
            {
                throw new InvalidOperationException("The map's script is not saved yet: save the project first");
            }
            await File.WriteAllTextAsync(script, NewMap.DefaultScript.ReplaceLineEndings("\n"), new UTF8Encoding(false), cancellationToken);
            model.Logger("Files").LogInformation("Wrote the script of a skirmish map into {File}", script);
        }
        if (dialogs is DialogGuard { Quiet: true })
        {
            model.Status = $"The script is {script}";
        }
        else
        {
            model.OpenFile(script);
            model.Status = $"Opened {script} in the editor for .lua files; what it saves comes into the map";
        }
        return script;
    }
}

/// <summary>
/// Shows the other files of the map's folder (the project's <c>files</c>) in the system's file
/// manager (user story M-6): the Map panel's button.
/// </summary>
[EditorCommand("file.showMapFiles", "Show the map's files", Story = "M-6", Needs = Needs.Project)]
public sealed record ShowMapFiles : IRequest;

public sealed class ShowMapFilesHandler(MainViewModel model) : IRequestHandler<ShowMapFiles>
{
    public Task Handle(ShowMapFiles request, CancellationToken cancellationToken)
    {
        string project = model.Map?.ProjectFolder ?? throw new InvalidOperationException("Save the map as a project first");
        string files = Path.Combine(project, MapProjectLayout.Files);
        model.ShowFolder(Directory.Exists(files) ? files : project);
        return Task.CompletedTask;
    }
}
