using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Takes a part of the map's heights as a stamp (user story T-2): a 16-bit grey picture in the
/// workspace's <c>brushes/stamps</c> folder (so it is a brush of the shelf too), stretched from the
/// part's lowest to its highest sample, and chooses the Stamp tool with it, set up to put that part
/// back as it was wherever it is pressed (mode Set, the part's size, height and lowest ground).
/// The Stamp tool's shift and a drag run it. The map does not change.
/// </summary>
/// <param name="X0">A corner, in ogrids.</param>
/// <param name="Name">The picture's name; left out, the map's name and the corner. A taken name gets a number.</param>
/// <returns>The picture's file and key, its size in samples, the lowest and highest height.</returns>
[EditorCommand("terrain.captureStamp", "Take a stamp from the map", Story = "T-2", Needs = Needs.Map | Needs.Workspace)]
public sealed record CaptureStamp(int X0, int Z0, int X1, int Z1, string? Name = null) : IRequest<JsonObject>;

public sealed class CaptureStampHandler(MainViewModel model) : IRequestHandler<CaptureStamp, JsonObject>
{
    public Task<JsonObject> Handle(CaptureStamp request, CancellationToken cancellationToken)
    {
        Workspace workspace = model.Setup.Workspace ?? throw new InvalidOperationException("Stamps are kept in the workspace: set one up first");
        MapDocument document = model.ShownDocument ?? throw new InvalidOperationException("No map is open");
        HeightStampCapture capture = HeightStampCapture.Of(document.Terrain.Heightmap, request.X0, request.Z0, request.X1, request.Z1);
        string folder = Path.Combine(workspace.BrushesFolder, StampFolder);
        string wanted = Clean(request.Name ?? $"{document.Scenario.Name} {Math.Min(request.X0, request.X1)} {Math.Min(request.Z0, request.Z1)}");
        string name = wanted;
        for (int number = 2; File.Exists(Path.Combine(folder, name + ".png")); number++)
        {
            name = $"{wanted} {number}";
        }
        string file = Path.Combine(folder, name + ".png");
        try
        {
            capture.Save(file);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            model.Logger("Assets").LogError(exception, "Saving the stamp {File} failed: {Reason}", file, exception.Message);
            throw new InvalidOperationException($"The stamp cannot be saved in {folder}: {exception.Message}", exception);
        }
        string key = $"{WorkspaceLayout.Brushes}/{StampFolder}/{name.ToLowerInvariant()}.png";
        model.Logger("Assets").LogInformation("Took a stamp of {Width} by {Height} samples into {File}", capture.Width, capture.Height, file);
        model.SelectTool("terrain.stamp");
        model.Tool<StampTool>().Take(file, key, name, capture);
        model.Status = $"Took the stamp {name}: {capture.Width - 1} by {capture.Height - 1} ogrids, {capture.Highest - capture.Lowest:0.#} high; click to press it";
        return Task.FromResult(new JsonObject
        {
            ["file"] = file,
            ["key"] = key,
            ["width"] = capture.Width,
            ["height"] = capture.Height,
            ["lowest"] = capture.Lowest,
            ["highest"] = capture.Highest,
        });
    }

    /// <summary>
    /// The folder of the workspace's brushes that stamps taken from maps go into.
    /// </summary>
    public const string StampFolder = "stamps";

    private static string Clean(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string cleaned = new string([.. name.Trim().Select(c => invalid.Contains(c) || c == '/' ? '-' : c)]);
        return cleaned.Length == 0 ? "Stamp" : cleaned;
    }
}

/// <summary>
/// Saves the active brush tool's settings as a brush preset of the workspace (user stories T-2,
/// F-10): the sculpt and painting brushes, and the Stamp tool with its picture, size, height, mode,
/// base and edge. The Terrain and Paint panels' Brush presets list them.
/// </summary>
/// <param name="Name">Left out: a name made from the settings, such as <c>Stamp Set 64, crater</c>.</param>
/// <returns>The preset's kind and name.</returns>
[EditorCommand("terrain.saveBrush", "Save the brush as a preset", Story = "T-2")]
public sealed record SaveBrushPreset(string? Name = null) : IRequest<JsonObject>;

public sealed class SaveBrushPresetHandler(MainViewModel model, PresetsViewModel presets) : IRequestHandler<SaveBrushPreset, JsonObject>
{
    public Task<JsonObject> Handle(SaveBrushPreset request, CancellationToken cancellationToken)
    {
        Preset preset = presets.Save(BrushPreset.Kind, request.Name);
        model.Status = $"Saved the brush preset {preset.Name}";
        return Task.FromResult(new JsonObject { ["kind"] = preset.Kind, ["name"] = preset.Name });
    }
}
