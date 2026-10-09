using System.Text.Json.Nodes;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Workspaces;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Shows the asset shelf in the dock, with the keyboard in its search field (user story F-9); the
/// arguments set its filters.
/// </summary>
/// <param name="Kind"><c>prop</c>, <c>unit</c>, <c>decal</c> or <c>texture</c>.</param>
/// <param name="Scope"><c>inMap</c>, <c>narrow</c> (the map's biomes, civilian units) or <c>everything</c>.</param>
/// <param name="Query">Words each shown asset has in its name, code, biome or category.</param>
/// <param name="Group">A biome (<c>evergreen</c>) or faction (<c>UEF</c>); empty for all.</param>
/// <param name="Category">A category (<c>trees</c>, <c>structure</c>, <c>splats</c>); empty for all.</param>
/// <param name="Families">Whether a prop's variants share a tile.</param>
/// <returns>What the shelf then shows: its filters and up to 200 tiles.</returns>
[EditorCommand("assets.browse", "Browse the assets", Gesture = "Ctrl+Shift+A", Story = "F-9")]
public sealed record BrowseAssets(
    string? Kind = null, string? Scope = null, string? Query = null, string? Group = null, string? Category = null, bool? Families = null)
    : IRequest<JsonObject>;

public sealed class BrowseAssetsHandler(MainViewModel model, AssetsViewModel assets) : IRequestHandler<BrowseAssets, JsonObject>
{
    public Task<JsonObject> Handle(BrowseAssets request, CancellationToken cancellationToken)
    {
        if (request.Kind is string kind)
        {
            assets.Kind = Enum.TryParse(kind, ignoreCase: true, out AssetKind parsed)
                ? parsed
                : throw new ArgumentException($"There is no kind of asset '{kind}': prop, unit, decal or texture");
        }
        if (request.Scope is string scope)
        {
            assets.Scope = Enum.TryParse(scope, ignoreCase: true, out AssetScope parsed)
                ? parsed
                : throw new ArgumentException($"There is no scope '{scope}': inMap, narrow or everything");
        }
        if (request.Query is not null)
        {
            assets.Query = request.Query;
        }
        if (request.Group is not null)
        {
            assets.Group = request.Group.Length == 0 ? null : request.Group;
        }
        if (request.Category is not null)
        {
            assets.Category = request.Category.Length == 0 ? null : request.Category;
        }
        if (request.Families is bool families)
        {
            assets.Families = families;
        }
        model.Dock = DockTab.Assets;
        assets.Focus();
        return Task.FromResult(Describe(assets));
    }

    /// <summary>
    /// The shelf as automation reads it.
    /// </summary>
    public static JsonObject Describe(AssetsViewModel assets) => new JsonObject
    {
        ["kind"] = assets.Kind.ToString(),
        ["scope"] = assets.Scope.ToString(),
        ["query"] = assets.Query,
        ["group"] = assets.Group,
        ["category"] = assets.Category,
        ["families"] = assets.Families,
        ["summary"] = assets.Summary,
        ["kinds"] = new JsonObject([.. assets.Kinds.Select(choice => KeyValuePair.Create(choice.Value!, (JsonNode?)choice.Count))]),
        ["groups"] = new JsonObject([.. assets.Groups.Select(choice => KeyValuePair.Create(choice.Value!, (JsonNode?)choice.Count))]),
        ["categories"] = new JsonObject([.. assets.Categories.Select(choice => KeyValuePair.Create(choice.Value!, (JsonNode?)choice.Count))]),
        ["tiles"] = new JsonArray([.. assets.Tiles.Take(200).Select(tile => (JsonNode)new JsonObject
        {
            ["title"] = tile.Title,
            ["detail"] = tile.Detail,
            ["key"] = tile.Entry.Key,
            ["variants"] = tile.Members.Count,
            ["uses"] = tile.Uses,
        })]),
        ["selected"] = assets.Shown?.Key,
    };
}

/// <summary>
/// Selects an asset in the shelf by the name a map uses for it, a game path or a unit's id; the
/// inspector shows it (user story F-9).
/// </summary>
[EditorCommand("assets.select", "Select an asset", Story = "F-9")]
public sealed record SelectAsset(string Key) : IRequest;

public sealed class SelectAssetHandler(MainViewModel model, AssetsViewModel assets) : IRequestHandler<SelectAsset>
{
    public Task Handle(SelectAsset request, CancellationToken cancellationToken)
    {
        assets.Select(request.Key);
        model.Dock = DockTab.Assets;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Shows the folder of the selected asset's file in the system's file manager (user story F-8).
/// </summary>
[EditorCommand("assets.showFile", "Show the asset's file", Story = "F-8")]
public sealed record ShowAssetFile : IRequest;

public sealed class ShowAssetFileHandler(MainViewModel model, AssetsViewModel assets) : IRequestHandler<ShowAssetFile>
{
    public Task Handle(ShowAssetFile request, CancellationToken cancellationToken)
    {
        string file = assets.Shown?.File ?? throw new InvalidOperationException("Select an asset first");
        model.ShowFolder(Path.GetDirectoryName(file)!);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Reads the workspace's assets again, after a tool changed them (user story F-8).
/// </summary>
[EditorCommand("assets.reload", "Read the assets again", Story = "F-8", Needs = Needs.Workspace)]
public sealed record ReloadAssets : IRequest;

public sealed class ReloadAssetsHandler(MainViewModel model, WorkspaceSetupViewModel setup) : IRequestHandler<ReloadAssets>
{
    public async Task Handle(ReloadAssets request, CancellationToken cancellationToken)
    {
        if (setup.Workspace is { } workspace)
        {
            await model.LoadAssetsAsync(workspace);
            model.Status = $"Read the assets again: {model.Assets.Catalog.Entries.Count} assets";
        }
    }
}
