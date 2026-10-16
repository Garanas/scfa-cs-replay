using System.Text.Json.Nodes;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Workspaces;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Turns regenerating on their own on or off for the editor, for every map (the switch in the views'
/// bar), remembered in the user's settings. On, each result the map lets regenerate on its own does
/// so a moment after a change to what it is made from, while the views show it; off, results are
/// made only when asked. The maps' own settings stay as they are.
/// </summary>
/// <param name="On">Left out, the other way.</param>
/// <returns>Whether results regenerate on their own now.</returns>
[EditorCommand("generate.automatic", "Regenerate results on their own", Story = "F-7")]
public sealed record GenerateAutomatic(bool? On = null) : IRequest<bool>;

public sealed class GenerateAutomaticHandler(MainViewModel model) : IRequestHandler<GenerateAutomatic, bool>
{
    public Task<bool> Handle(GenerateAutomatic request, CancellationToken cancellationToken)
    {
        bool on = request.On ?? !model.RegenerateOnTheirOwn;
        model.SetRegenerateOnTheirOwn(on);
        model.Status = on
            ? "Results regenerate on their own while the views show them (each map says which: the Procedural entries' check boxes)"
            : "Results regenerate only when asked; the maps keep their settings for when it is on again";
        return Task.FromResult(on);
    }
}

/// <summary>
/// Makes the open map's Procedural settings the workspace's defaults (Q5): what regenerates on its
/// own and what the export makes or writes, written into <c>workspace.json</c>. Maps made or imported
/// in the workspace from then on start with them; projects keep their own. With an id only that
/// result's choices (a Procedural entry's Use as workspace default); the others keep their defaults.
/// </summary>
/// <param name="Id">One generated result, e.g. <c>walkability</c>; left out, every one.</param>
/// <returns>The workspace's defaults now.</returns>
[EditorCommand("generate.useAsWorkspaceDefault", "Use this map's settings as the workspace default", Story = "F-7", Needs = Needs.Map | Needs.Workspace)]
public sealed record UseAsWorkspaceDefault(string? Id = null) : IRequest<JsonObject>;

public sealed class UseAsWorkspaceDefaultHandler(MainViewModel model) : IRequestHandler<UseAsWorkspaceDefault, JsonObject>
{
    public Task<JsonObject> Handle(UseAsWorkspaceDefault request, CancellationToken cancellationToken)
    {
        ProceduralDefaults defaults = model.UseAsWorkspaceDefault(request.Id);
        model.Status = request.Id is string id
            ? $"New and imported maps of the workspace {model.WorkspaceDefaultOf(id, defaults)}"
            : $"New and imported maps of the workspace start with this map's settings: {model.DescribeDefaults(defaults)}";
        return Task.FromResult(WorkspaceProceduralHandler.Describe(defaults));
    }
}

/// <summary>
/// Changes the workspace's default for one generated result (the Workspace settings page's check
/// boxes): whether it regenerates on its own, whether the export makes or writes it (the normal
/// map, the path networks). Maps made or imported from then on start with it.
/// </summary>
/// <param name="Id">The generator, e.g. <c>walkability</c>, <c>navigationMesh</c>, <c>masksFromRules</c>.</param>
/// <param name="Auto">Whether it regenerates on its own.</param>
/// <param name="OnExport">Whether the export makes or writes it.</param>
/// <returns>The workspace's defaults now.</returns>
[EditorCommand("workspace.setProcedural", "Procedural defaults of the workspace", Story = "F-7", Needs = Needs.Workspace)]
public sealed record WorkspaceProcedural(string Id, bool? Auto = null, bool? OnExport = null) : IRequest<JsonObject>;

public sealed class WorkspaceProceduralHandler(MainViewModel model) : IRequestHandler<WorkspaceProcedural, JsonObject>
{
    public Task<JsonObject> Handle(WorkspaceProcedural request, CancellationToken cancellationToken)
    {
        ProceduralDefaults defaults = model.SetWorkspaceProcedural(request.Id, request.Auto, request.OnExport);
        model.Status = $"New and imported maps of the workspace start {model.DescribeDefaults(defaults)}";
        return Task.FromResult(Describe(defaults));
    }

    /// <summary>
    /// The workspace's Procedural defaults as automation reads them.
    /// </summary>
    public static JsonObject Describe(ProceduralDefaults defaults) => new JsonObject
    {
        ["autoGenerate"] = new JsonArray([.. defaults.AutoGenerate.Select(id => (JsonNode)JsonValue.Create(id))]),
        ["onExport"] = new JsonArray([.. defaults.OnExport.Select(id => (JsonNode)JsonValue.Create(id))]),
        ["isDefault"] = defaults.IsDefault,
    };
}
