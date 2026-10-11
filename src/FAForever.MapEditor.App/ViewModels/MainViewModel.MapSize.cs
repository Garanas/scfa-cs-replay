using System.Collections.Immutable;
using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.Rendering;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;
using FAForever.MapEditor.Project.Workspaces;
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
    /// The results the map was opened with in its own files are its own, made for that version: the
    /// preview, the normal map, the rules layers of its masks and strata, the props its rules placed
    /// and the generated sky cube map count as current, so the status tells when what they are made
    /// from changed since. A normal map of one colour everywhere (a new map's or the sandbox's
    /// placeholder) on terrain that is not flat was never generated and stays missing, as do rules
    /// the map has not; on flat terrain that one colour is what generating makes.
    /// </summary>
    private void KeepMapPreview(EditedMap map)
    {
        MapDocument document = map.Document;
        void Keep(string id, object value)
        {
            if (Generation.Results.Find(id) is IGenerator generator)
            {
                Generation.Results.Keep(GeneratedResult.Of(generator, document, value));
            }
        }
        Keep(MapPreview.GeneratorId, document.Terrain.Preview);
        if (!IsOneColour(document.Terrain.NormalMap) || IsOneValue(document.Terrain.Heightmap.Samples))
        {
            Keep(NormalMapGenerator.GeneratorId, document.Terrain.NormalMap);
        }
        // the rules layers hold what their rules made last
        (string Target, string Layer, TiledBuffer<byte> Grid)[] rulesLayers = [.. document.CoverageStacks
            .SelectMany(entry => entry.Value.Layers
                .Where(layer => layer.Source is RulesSource && layer.Coverage is not null)
                .Select(layer => (entry.Key, layer.Name, layer.Coverage!)))];
        if (rulesLayers.Length > 0)
        {
            Keep(MaskRulesGenerator.GeneratorId, (IReadOnlyList<(string Target, string Layer, TiledBuffer<byte> Grid)>)rulesLayers);
        }
        if (!document.PropRules.IsEmpty)
        {
            Keep(PropRulesGenerator.GeneratorId, (ImmutableArray<Prop>)[.. document.Props.Where(prop => prop.Rule is not null)]);
        }
        if (document.Files.TryGetValue(SkyCubeMaps.FilePath, out ImmutableArray<byte> cube))
        {
            try
            {
                DecodedTexture strip = CubeMaps.Decode([.. cube]);
                Keep(SkyCubeGenerator.GeneratorId, new SkyCubeResult([.. cube], SkyCubeGenerator.Small(strip, SkyCubeGenerator.PreviewFace), strip.Height,
                    "kept with the map", document.Sky.Skybox is null ? "made from the lighting" : "the map's skybox"));
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                FilesLog.LogWarning(exception, "The map's sky cube map {File} does not read; it counts as never made: {Reason}", SkyCubeMaps.FilePath, exception.Message);
            }
        }
        Generation.Refresh(document);
    }

    /// <summary>
    /// Whether every pixel of a picture is the same; it stops at the first that differs.
    /// </summary>
    public static bool IsOneColour(TiledBuffer<Color32> picture) => IsOneValue(picture);

    /// <summary>
    /// Whether every value of a grid is the same (a flat heightmap); it stops at the first that differs.
    /// </summary>
    public static bool IsOneValue<T>(TiledBuffer<T> picture) where T : unmanaged, IEquatable<T>
    {
        const int Size = TiledBuffer<T>.TileSize;
        T first = picture.Tile(0, 0)[0];
        for (int y = 0; y < picture.TilesY; y++)
        {
            for (int x = 0; x < picture.TilesX; x++)
            {
                // the part of an edge tile outside the grid does not count
                ReadOnlySpan<T> tile = picture.Tile(x, y);
                int columns = Math.Min(Size, picture.Width - x * Size);
                int rows = Math.Min(Size, picture.Height - y * Size);
                for (int row = 0; row < rows; row++)
                {
                    if (tile.Slice(row * Size, columns).ContainsAnyExcept(first))
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
