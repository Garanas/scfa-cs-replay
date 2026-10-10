using System.Diagnostics;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Files.InMemory;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Takes the heights of an image of any size into the map, resampled to the map's size (user story
/// T-3), one step of undo: what World Machine, Gaea or another map hands over. A 16-bit grey PNG
/// (8 bits and colour are read too) or raw 16-bit samples of a square (<c>.r16</c>, <c>.raw</c>);
/// a sample is the height divided by the map's height scale, as in the project's heightmap.
/// </summary>
/// <param name="Path">Left out, the editor asks for the file.</param>
[EditorCommand("terrain.importHeightmap", "Import a heightmap...", Story = "T-3", Needs = Needs.Map | Needs.NoStroke | Needs.Idle)]
public sealed record ImportHeightmap(string? Path = null) : IRequest;

public sealed class ImportHeightmapHandler(MainViewModel model, IEditorDialogs dialogs) : IRequestHandler<ImportHeightmap>
{
    public static IReadOnlyList<string> Extensions { get; } = [".png", ".r16", ".raw", ".tif", ".tiff"];

    public async Task Handle(ImportHeightmap request, CancellationToken cancellationToken)
    {
        MapDocument document = model.ShownDocument ?? throw new InvalidOperationException("No map is open");
        string? path = request.Path ?? await dialogs.PickFileAsync("Import a heightmap", Extensions, model.Map?.ProjectFolder);
        if (path is null)
        {
            return;
        }
        long start = Stopwatch.GetTimestamp();
        (MapDocument imported, (int Width, int Height) original) = await Task.Run(() =>
        {
            using Performance.Span measure = Performance.Measure("model.import-heightmap");
            MapDocument result = HeightmapFiles.Import(document, path, out (int Width, int Height) size);
            return (result, size);
        }, cancellationToken);
        Heightmap heights = imported.Terrain.Heightmap;
        model.EditMap($"Import the heightmap {System.IO.Path.GetFileName(path)}", d => d with { Terrain = d.Terrain with { Heightmap = heights } });
        double milliseconds = Math.Round(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        EditorMetrics.OperationDuration.Record(milliseconds, EditorMetrics.Tag("operation", "import-heightmap"), EditorMetrics.Tag("result", "ok"));
        string resampled = original == (heights.Samples.Width, heights.Samples.Height)
            ? ""
            : $", resampled from {original.Width} by {original.Height}";
        model.Status = $"Imported the heightmap {path}{resampled}";
        model.Logger("Files").LogInformation("Imported the heightmap {File} of {Width} by {Height} samples into {MapWidth} by {MapHeight} ({Milliseconds} ms)",
            path, original.Width, original.Height, heights.Samples.Width, heights.Samples.Height, milliseconds);
    }
}

/// <summary>
/// Writes the map's heightmap as an image for other tools (user story T-3): a 16-bit grey PNG, or
/// raw 16-bit samples for <c>.r16</c> and <c>.raw</c>, (width + 1) by (height + 1) samples.
/// </summary>
/// <param name="Path">Left out, the editor asks where.</param>
/// <returns>The file written.</returns>
[EditorCommand("terrain.exportHeightmap", "Export the heightmap...", Story = "T-3", Needs = Needs.Map)]
public sealed record ExportHeightmap(string? Path = null) : IRequest<string?>;

public sealed class ExportHeightmapHandler(MainViewModel model, IEditorDialogs dialogs) : IRequestHandler<ExportHeightmap, string?>
{
    public async Task<string?> Handle(ExportHeightmap request, CancellationToken cancellationToken)
    {
        MapDocument document = model.ShownDocument ?? throw new InvalidOperationException("No map is open");
        string? startIn = model.Map?.ProjectFolder is string project ? System.IO.Path.Combine(project, MapProjectLayout.ProceduralTemp) : null;
        string? path = request.Path ?? await dialogs.PickSaveFileAsync("Export the heightmap", $"{document.Name}-heightmap.png", [".png", ".r16"], startIn);
        if (path is null)
        {
            return null;
        }
        Heightmap heights = document.Terrain.Heightmap;
        await Task.Run(() => HeightmapFiles.Write(path, heights), cancellationToken);
        model.Status = $"Exported the heightmap to {path} ({heights.Samples.Width} by {heights.Samples.Height} samples)";
        model.Logger("Files").LogInformation("Exported the heightmap to {File}", path);
        return path;
    }
}
