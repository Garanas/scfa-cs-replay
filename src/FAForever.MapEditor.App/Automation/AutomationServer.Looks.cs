using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.Automation;

/// <summary>
/// The state of the map's look (<c>state.looks</c>): the terrain shader (S-4), the sky's cube maps
/// and whether the 3D view drew them (L-1), the grid and the decals in the views (V-3), the units'
/// icons and cube maps of the workspace (U-1, L-1) and the cube maps the map names that it lacks (F-8).
/// </summary>
public sealed partial class AutomationServer
{
    private JsonObject LooksState()
    {
        MapDocument? document = model.ShownDocument;
        TerrainShaderInfo? shader = TerrainShaderCatalog.Find(document?.Terrain.Shader);
        AssetCatalog catalog = model.Assets.Catalog;
        (bool sky, bool reflection) = window.View3D.SkyState;
        return new JsonObject
        {
            ["shader"] = document is null ? null : new JsonObject
            {
                ["name"] = document.Terrain.Shader,
                ["known"] = shader is not null,
                ["strata"] = shader?.Strata,
                ["fullMaskRange"] = shader?.FullMaskRange,
            },
            ["sky"] = document is null ? null : new JsonObject
            {
                ["cubemap"] = document.Sky.Cubemap,
                ["environment"] = SkyFields.EnvironmentOf(document.Sky),
                ["background"] = document.Sky.Background,
                ["water"] = document.Water.Cubemap,
                ["drawn"] = sky,
                ["reflected"] = reflection,
            },
            ["grid"] = new JsonObject { ["shown"] = model.ShowGrid, ["spacing"] = model.GridSpacing, ["linesTop"] = window.MapView.GridLinesDrawn },
            ["decals"] = new JsonObject { ["shown"] = model.ShowDecals, ["outlinesTop"] = window.MapView.DecalOutlinesDrawn },
            ["unitIcons"] = catalog.Entries.Count(entry => entry.Icon is not null),
            ["cubemaps"] = catalog.Entries.Count(entry => entry.Kind == AssetKind.Cubemap),
            ["missingCubemaps"] = new JsonArray([.. model.Assets.AllMissing.Where(missing => missing.Kind == AssetKind.Cubemap).Select(missing => (JsonNode)JsonValue.Create(missing.Path))]),
        };
    }
}
