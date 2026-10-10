using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.Rendering;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The map's size, playable area and version (user stories M-3, M-8): growing or cropping the map,
/// choosing the playable area, raising the version on export and rendering the preview before it.
/// </summary>
public sealed partial class MainViewModel
{
    private (int Width, int Height)? _shownSize;

    private void InitializeMapSize()
    {
        // a map of another size (opened, resized, undone): the views and the size tool start over
        DocumentChanged += (_, _) => FollowMapSize(raise: true);
        MapChanged += (_, _) => FollowMapSize(raise: false);
        Events.MapOpened += KeepMapPreview;
    }

    private void FollowMapSize(bool raise)
    {
        (int, int)? size = ShownDocument is MapDocument document ? (document.Width, document.Height) : null;
        if (size == _shownSize)
        {
            return;
        }
        bool resized = _shownSize is not null && size is not null;
        _shownSize = size;
        Tool<MapSizeTool>().Reset();
        if (resized && raise)
        {
            ResetCamera();
            MapChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// The preview and the normal map the map was opened with are its own: generated for that
    /// version, so the status tells when the terrain changed since and an export renders the
    /// preview again. A normal map of one colour everywhere (a new map's or the sandbox's
    /// placeholder) was never generated and stays missing.
    /// </summary>
    private void KeepMapPreview(EditedMap map)
    {
        if (Generation.Results.Find(MapPreview.GeneratorId) is IGenerator preview)
        {
            Generation.Results.Keep(GeneratedResult.Of(preview, map.Document, map.Document.Terrain.Preview));
        }
        if (Generation.Results.Find(NormalMapGenerator.GeneratorId) is IGenerator normalMap && !IsOneColour(map.Document.Terrain.NormalMap))
        {
            Generation.Results.Keep(GeneratedResult.Of(normalMap, map.Document, map.Document.Terrain.NormalMap));
        }
        Generation.Refresh(map.Document);
    }

    /// <summary>
    /// Whether every pixel of a picture is the same; it stops at the first that differs.
    /// </summary>
    public static bool IsOneColour(TiledBuffer<Color32> picture)
    {
        Color32 first = picture.Tile(0, 0)[0];
        for (int y = 0; y < picture.TilesY; y++)
        {
            for (int x = 0; x < picture.TilesX; x++)
            {
                foreach (Color32 pixel in picture.Tile(x, y))
                {
                    if (!pixel.Equals(first))
                    {
                        return false;
                    }
                }
            }
        }
        return true;
    }

    /// <summary>
    /// Grows or crops the map (user story M-3) on a thread of the pool, then puts the result in as
    /// one step of undo.
    /// </summary>
    /// <returns>What happened, for the status line.</returns>
    public async Task<MapResizeResult> ResizeMapAsync(MapResizeSettings settings)
    {
        EditedMap map = RequireMap();
        if (_stroke is not null)
        {
            throw new InvalidOperationException("Wait until the stroke ends");
        }
        MapDocument before = map.Document;
        MapResizeResult? result = null;
        await BusyAsync("resize", "Resizing the map...", async () =>
        {
            result = await Task.Run(() =>
            {
                using Performance.Span measure = Performance.Measure("model.resize-map");
                return MapResize.Resize(before, settings);
            });
        });
        MapResizeResult done = result!;
        string removed = done.Removed.Total > 0 ? $"; removed what lay outside: {done.Removed}" : "";
        string spawns = done.SpawnsMoved > 0 ? $"; moved {done.SpawnsMoved} spawns inside" : "";
        string description = $"Resize the map to {settings.Width} by {settings.Height}";
        if (!ReferenceEquals(map.Document, before))
        {
            throw new InvalidOperationException("The map changed while it was resized; try again");
        }
        EditMap(description, _ => done.Document);
        Status = $"Resized the map to {settings.Width} by {settings.Height} ogrids ({MapSizeTool.Kilometres(settings.Width)} by {MapSizeTool.Kilometres(settings.Height)} km){removed}{spawns}. The normal map and preview are out of date";
        Logger("Editor").LogInformation("Resized {Map} from {OldWidth} by {OldHeight} to {Width} by {Height} at {OffsetX}, {OffsetZ}, removing {Removed} things",
            before.Scenario.Name, before.Width, before.Height, settings.Width, settings.Height, done.OffsetX, done.OffsetZ, done.Removed.Total);
        return done;
    }

    /// <summary>
    /// Renders the preview before an export when it is out of date (user story M-8).
    /// </summary>
    private async Task RenderPreviewWhenStaleAsync()
    {
        if (Generation.States.FirstOrDefault(state => state.Generator.Id == MapPreview.GeneratorId) is { Status: GeneratedStatus.Stale })
        {
            await GenerateAsync(MapPreview.GeneratorId);
        }
    }

    /// <summary>
    /// The map's size, playable area and version for automation's <c>state</c>.
    /// </summary>
    public JsonObject DescribeMapSize()
    {
        MapDocument? document = ShownDocument;
        MapSizeTool tool = Tool<MapSizeTool>();
        return new JsonObject
        {
            ["width"] = document?.Width,
            ["height"] = document?.Height,
            ["playableArea"] = document?.PlayableArea?.Name,
            ["playableAreaNamed"] = document?.Scenario.PlayableArea is not null,
            ["version"] = document?.Version,
            ["nextVersion"] = document?.NextVersion,
            ["folder"] = document?.Folder.ToString(),
            ["tool"] = new JsonObject
            {
                ["width"] = tool.NewWidth,
                ["height"] = tool.NewHeight,
                ["anchor"] = tool.Anchor.ToString(),
                ["offsetX"] = tool.OffsetX,
                ["offsetZ"] = tool.OffsetZ,
                ["customOffset"] = tool.CustomOffset,
                ["fillHeight"] = tool.FillWithHeight ? tool.FillHeight : null,
                ["fitPlayableArea"] = tool.FitPlayableArea,
                ["summary"] = tool.Summary,
            },
        };
    }
}
