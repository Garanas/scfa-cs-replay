using System.Globalization;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// The height layer a command works on: the one named, else the one the Terrain panel chose.
/// </summary>
internal static class HeightLayerArgument
{
    /// <exception cref="ArgumentException">The map has no such layer, or none is chosen.</exception>
    public static HeightLayer Of(MainViewModel model, string? name)
    {
        MapDocument document = model.ShownDocument ?? throw new InvalidOperationException("No map is open");
        string chosen = name ?? model.HeightStack.Chosen ?? throw new ArgumentException(document.HeightLayers.IsEmpty
            ? "The map has no height layers: add one first (terrain.addLayer)"
            : "Choose a height layer, or name one with layer");
        return document.HeightLayers[HeightLayerEdits.IndexOf(document, chosen)];
    }
}

/// <summary>
/// Adds a height layer (user story T-5), one step of undo, and chooses it. The first layer starts
/// the stack: the map's heights become its base, so nothing changes until the new layer does. A
/// sculpting layer adds nothing until the sculpt tool writes into it; an imported one is the
/// heightmap of World Machine, Gaea or another tool (resampled to the map), read again when the
/// file changes while it lies in the project folder; a curve layer gets its points from the curve
/// tool (or <c>terrain.addCurve</c>).
/// </summary>
/// <param name="Kind"><c>sculpt</c> (left out), <c>imported</c> or <c>curve</c>.</param>
/// <param name="Name">Left out, a free one (<c>Sculpt 2</c>, the file's name, <c>Curve</c>).</param>
/// <param name="Path">An imported layer's file (PNG, <c>.r16</c>, ...); left out, the editor asks.</param>
/// <param name="Combine"><c>Add</c>, <c>Maximum</c>, <c>Minimum</c> or <c>Replace</c>; left out, the
/// kind's own (sculpting adds, an image replaces).</param>
/// <returns>The new layer's name, or null when no file was chosen.</returns>
[EditorCommand("terrain.addLayer", "Add a height layer...", Story = "T-5", Needs = Needs.Map | Needs.NoStroke)]
public sealed record AddHeightLayer(string? Kind = null, string? Name = null, string? Path = null, string? Combine = null) : IRequest<string?>;

public sealed class AddHeightLayerHandler(MainViewModel model, IEditorDialogs dialogs) : IRequestHandler<AddHeightLayer, string?>
{
    public async Task<string?> Handle(AddHeightLayer request, CancellationToken cancellationToken)
    {
        MapDocument document = model.ShownDocument ?? throw new InvalidOperationException("No map is open");
        string kind = (request.Kind ?? "sculpt").Trim().ToLowerInvariant();
        HeightCombine? combine = request.Combine is null ? null : HeightLayerSettings.ParseCombine(request.Combine);
        HeightLayer layer;
        switch (kind)
        {
            case "sculpt":
            {
                string name = request.Name?.Trim() ?? HeightLayerEdits.FreeName(document, "Sculpt");
                layer = HeightLayerEdits.NewSculpt(document, name, combine ?? HeightCombine.Add);
                break;
            }
            case "imported" or "image":
            {
                string? path = request.Path ?? await dialogs.PickFileAsync("Add a heightmap as a layer", ImportHeightmapHandler.Extensions, model.Map?.ProjectFolder);
                if (path is null)
                {
                    model.Status = "Chose no file";
                    return null;
                }
                path = System.IO.Path.GetFullPath(path);
                (ushort[] samples, (int Width, int Height) original) = await Task.Run(() =>
                {
                    using Performance.Span measure = Performance.Measure("model.import-heightmap");
                    ushort[] read = HeightmapFiles.ReadResampled(document, path, out (int Width, int Height) size);
                    return (read, size);
                }, cancellationToken);
                string stem = new string([.. System.IO.Path.GetFileNameWithoutExtension(path).Select(c => char.IsAsciiLetterOrDigit(c) || c is ' ' or '-' or '_' ? c : '-')]).Trim();
                string name = request.Name?.Trim() ?? HeightLayerEdits.FreeName(document, stem.Length == 0 ? "Image" : stem);
                layer = HeightLayerEdits.NewImported(document, name, samples, SourceOf(model.Map?.ProjectFolder, path));
                if (combine is HeightCombine chosen)
                {
                    layer = layer with { Combine = chosen };
                }
                model.Logger("Files").LogInformation("Added the heightmap {File} of {Width} by {Height} samples as the height layer {Layer}", path, original.Width, original.Height, name);
                break;
            }
            case "curve":
            {
                string name = request.Name?.Trim() ?? HeightLayerEdits.FreeName(document, "Curve");
                layer = HeightLayerEdits.NewCurve(document, name, new HeightCurve());
                if (combine is HeightCombine chosen)
                {
                    layer = layer with { Combine = chosen };
                }
                break;
            }
            default:
                throw new ArgumentException($"A height layer is sculpt, imported or curve, not '{request.Kind}'");
        }
        string? above = model.HeightStack.Chosen is string chosenLayer && HeightLayers.Find(document, chosenLayer) is not null ? chosenLayer : null;
        using Performance.Span span = Performance.Measure("model.height-layers");
        model.EditMap($"Add the height layer {layer.Name}", d => HeightLayerEdits.Add(d, layer, above));
        model.HeightStack.Refresh(model.ShownDocument);
        model.HeightStack.Choose(layer.Name);
        model.Status = $"Added the height layer {layer.Name}";
        return layer.Name;
    }

    /// <summary>
    /// How a layer names its file: relative to the project folder when it lies in it.
    /// </summary>
    public static string SourceOf(string? projectFolder, string path)
    {
        if (projectFolder is null)
        {
            return path;
        }
        string relative = System.IO.Path.GetRelativePath(projectFolder, path);
        return relative.StartsWith("..", StringComparison.Ordinal) || System.IO.Path.IsPathRooted(relative) ? path : relative.Replace('\\', '/');
    }
}

/// <summary>
/// Adds a curve layer through points (user story T-4), one step of undo, and chooses it: a ridge,
/// valley, road or cliff edge along a Catmull-Rom spline. What the curve tool's first click does.
/// </summary>
/// <param name="Points">The points in ogrids, each <c>[x, z]</c> or <c>[x, z, height]</c>.</param>
/// <param name="Height">The height of points without one: what a ridge, valley or cliff rises or
/// sinks; a road's points without one lie on the terrain.</param>
[EditorCommand("terrain.addCurve", "Add a curve", Story = "T-4", Needs = Needs.Map | Needs.NoStroke)]
public sealed record AddHeightCurve(float[][] Points, string? Profile = null, float? Width = null, float? Falloff = null, float? Height = null,
    bool Closed = false, string? Name = null) : IRequest<string>;

public sealed class AddHeightCurveHandler(MainViewModel model) : IRequestHandler<AddHeightCurve, string>
{
    public Task<string> Handle(AddHeightCurve request, CancellationToken cancellationToken)
    {
        MapDocument document = model.ShownDocument ?? throw new InvalidOperationException("No map is open");
        CurveTool tool = model.Tool<CurveTool>();
        CurveProfile profile = request.Profile is null ? tool.Profile : HeightLayerSettings.ParseProfile(request.Profile);
        HeightCurve curve = new HeightCurve
        {
            Profile = profile,
            Width = request.Width ?? (float)tool.CurveWidth,
            Falloff = request.Falloff ?? (float)tool.Falloff,
            Closed = request.Closed,
            Points = [.. request.Points.Select(point => HeightLayerSettings.PointOf(document, point, profile, request.Height ?? (float)tool.Height))],
        };
        string name = request.Name?.Trim() ?? HeightLayerEdits.FreeName(document, profile.ToString());
        string? above = model.HeightStack.Chosen is string chosen && HeightLayers.Find(document, chosen) is not null ? chosen : null;
        using Performance.Span span = Performance.Measure("model.height-layers");
        model.EditMap($"Add the curve {name}", d => HeightLayerEdits.Add(d, HeightLayerEdits.NewCurve(d, name, curve), above));
        model.HeightStack.Refresh(model.ShownDocument);
        model.HeightStack.Choose(name);
        model.Status = string.Create(CultureInfo.InvariantCulture, $"Added the curve {name}: a {profile.ToString().ToLowerInvariant()} through {curve.Points.Length} point{(curve.Points.Length == 1 ? "" : "s")}");
        return Task.FromResult(name);
    }
}

/// <summary>
/// Changes a curve layer's curve (user story T-4), one step of undo; the steps that change the
/// same fields of the same curve one after the other are one step (a slider dragged), except new
/// points, which are a step each. The terrain
/// follows, the tiles near the curve only.
/// </summary>
/// <param name="Height">Every point's height.</param>
/// <param name="Points">All points anew, each <c>[x, z]</c> or <c>[x, z, height]</c>.</param>
[EditorCommand("terrain.setCurve", "Change a curve", Story = "T-4", Needs = Needs.Map | Needs.NoStroke)]
public sealed record SetHeightCurve(string? Layer = null, string? Profile = null, float? Width = null, float? Falloff = null, float? Height = null,
    bool? Closed = null, float[][]? Points = null) : IRequest;

public sealed class SetHeightCurveHandler(MainViewModel model) : IRequestHandler<SetHeightCurve>
{
    public Task Handle(SetHeightCurve request, CancellationToken cancellationToken)
    {
        HeightLayer layer = HeightLayerArgument.Of(model, request.Layer);
        HeightCurve curve = layer.Curve ?? throw new ArgumentException($"The height layer {layer.Name} is no curve");
        if (request.Width is < 0 || request.Falloff is < 0)
        {
            throw new ArgumentException("A curve's width and falloff are 0 or more ogrids");
        }
        MapDocument document = model.ShownDocument!;
        CurveProfile profile = request.Profile is null ? curve.Profile : HeightLayerSettings.ParseProfile(request.Profile);
        HeightCurve changed = curve with
        {
            Profile = profile,
            Width = request.Width ?? curve.Width,
            Falloff = request.Falloff ?? curve.Falloff,
            Closed = request.Closed ?? curve.Closed,
            Points = request.Points is float[][] points
                ? [.. points.Select(point => HeightLayerSettings.PointOf(document, point, profile, request.Height ?? curve.Points.FirstOrDefault()?.Height ?? 10))]
                : request.Height is float height ? [.. curve.Points.Select(point => point with { Height = height })] : curve.Points,
        };
        string fields = HeightLayerSettings.Fields(("profile", request.Profile), ("width", request.Width), ("falloff", request.Falloff), ("height", request.Height),
            ("closed", request.Closed), ("points", request.Points));
        // a point added, moved or removed is a step of its own; a field typed in again or a slider dragged is one
        string description = request.Points is null ? $"Curve {layer.Name}: {fields}" : $"Curve {layer.Name}: {changed.Points.Length} points";
        using Performance.Span span = Performance.Measure("model.height-layers");
        model.EditMap(description, d => HeightLayerEdits.SetCurve(d, layer.Name, changed), request.Points is null ? $"terrain.setCurve:{layer.Name}:{fields}" : null);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Removes a height layer (user story T-5), one step of undo; the base stays (<c>terrain.bake</c>
/// ends the stack).
/// </summary>
/// <param name="Layer">Left out, the chosen one.</param>
[EditorCommand("terrain.removeLayer", "Remove a height layer", Story = "T-5", Needs = Needs.Map | Needs.NoStroke)]
public sealed record RemoveHeightLayer(string? Layer = null) : IRequest;

public sealed class RemoveHeightLayerHandler(MainViewModel model) : IRequestHandler<RemoveHeightLayer>
{
    public Task Handle(RemoveHeightLayer request, CancellationToken cancellationToken)
    {
        HeightLayer layer = HeightLayerArgument.Of(model, request.Layer);
        using Performance.Span span = Performance.Measure("model.height-layers");
        model.EditMap($"Remove the height layer {layer.Name}", d => HeightLayerEdits.Remove(d, layer.Name));
        model.Status = $"Removed the height layer {layer.Name}";
        return Task.CompletedTask;
    }
}

/// <summary>
/// Moves a height layer up or down the stack (user story T-5), one step of undo: the higher works
/// on what the lower ones add up to. The base stays the lowest.
/// </summary>
/// <param name="To">Its new place: 1 is just above the base.</param>
/// <param name="By">Places to move, up when positive; with neither, one up.</param>
[EditorCommand("terrain.moveLayer", "Move a height layer", Story = "T-5", Needs = Needs.Map | Needs.NoStroke)]
public sealed record MoveHeightLayer(string? Layer = null, int? To = null, int? By = null) : IRequest;

public sealed class MoveHeightLayerHandler(MainViewModel model) : IRequestHandler<MoveHeightLayer>
{
    public Task Handle(MoveHeightLayer request, CancellationToken cancellationToken)
    {
        HeightLayer layer = HeightLayerArgument.Of(model, request.Layer);
        MapDocument document = model.ShownDocument!;
        int from = document.HeightLayers.IndexOf(layer);
        int to = request.To ?? from + (request.By ?? 1);
        if (to == from)
        {
            return Task.CompletedTask;
        }
        using Performance.Span span = Performance.Measure("model.height-layers");
        model.EditMap(to > from ? $"Move the height layer {layer.Name} up" : $"Move the height layer {layer.Name} down", d => HeightLayerEdits.Move(d, layer.Name, to));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Changes a height layer's settings (user story T-5), one step of undo; the steps that change the
/// same fields of the same layer one after the other are one step. What the inspector's fields and
/// the panel's check boxes do.
/// </summary>
/// <param name="Layer">Left out, the chosen one.</param>
/// <param name="Name">A new name.</param>
/// <param name="Combine"><c>Add</c>, <c>Maximum</c>, <c>Minimum</c> or <c>Replace</c>.</param>
/// <param name="Strength">0 to 1.</param>
/// <param name="Mask"><c>Everywhere</c> (or empty) for none, a mask for tools by name or as
/// <c>mask:rivers</c>, or a stratum's as <c>stratum 3</c> or <c>stratum:3</c>.</param>
[EditorCommand("terrain.setLayer", "Change a height layer", Story = "T-5", Needs = Needs.Map | Needs.NoStroke)]
public sealed record SetHeightLayer(string? Layer = null, string? Name = null, string? Combine = null, float? Strength = null, string? Mask = null,
    bool? Visible = null, bool? Locked = null) : IRequest;

public sealed class SetHeightLayerHandler(MainViewModel model) : IRequestHandler<SetHeightLayer>
{
    public Task Handle(SetHeightLayer request, CancellationToken cancellationToken)
    {
        HeightLayer layer = HeightLayerArgument.Of(model, request.Layer);
        MapDocument document = model.ShownDocument!;
        HeightCombine? combine = request.Combine is null ? null : HeightLayerSettings.ParseCombine(request.Combine);
        string? mask = request.Mask is null ? null : HeightLayersViewModel.MaskKeyOf(document, request.Mask);
        string? name = request.Name?.Trim() is { Length: > 0 } given && given != layer.Name ? given : null;
        if (request.Strength is < 0 or > 1)
        {
            throw new ArgumentException("A height layer's strength is 0 to 1");
        }
        string fields = HeightLayerSettings.Fields(("name", name), ("combine", request.Combine), ("strength", request.Strength), ("mask", request.Mask),
            ("visible", request.Visible), ("locked", request.Locked));
        if (fields.Length == 0)
        {
            return Task.CompletedTask;
        }
        string current = layer.Name;
        using Performance.Span span = Performance.Measure("model.height-layers");
        model.EditMap($"Height layer {layer.Name}: {fields}", d =>
        {
            if (combine is HeightCombine way && way != layer.Combine)
            {
                d = HeightLayerEdits.SetCombine(d, current, way);
            }
            if (request.Strength is float strength && strength != layer.Strength)
            {
                d = HeightLayerEdits.SetStrength(d, current, strength);
            }
            if (request.Mask is not null && mask != layer.Mask)
            {
                d = HeightLayerEdits.SetMask(d, current, mask);
            }
            if (request.Visible is bool visible)
            {
                d = HeightLayerEdits.SetVisible(d, current, visible);
            }
            if (request.Locked is bool locked)
            {
                d = HeightLayerEdits.SetLocked(d, current, locked);
            }
            return name is null ? d : HeightLayerEdits.Rename(d, current, name);
        }, $"terrain.setLayer:{layer.Name}:{fields}");
        if (name is not null && model.HeightStack.Chosen == current)
        {
            model.HeightStack.Refresh(model.ShownDocument);
            model.HeightStack.Choose(name);
        }
        return Task.CompletedTask;
    }
}

/// <summary>
/// Chooses the height layer the sculpt tool writes into and the inspector shows (user story T-5),
/// as the panel's list does.
/// </summary>
[EditorCommand("terrain.chooseLayer", "Choose a height layer", Story = "T-5", Needs = Needs.Map)]
public sealed record ChooseHeightLayer(string Layer) : IRequest;

public sealed class ChooseHeightLayerHandler(MainViewModel model) : IRequestHandler<ChooseHeightLayer>
{
    public Task Handle(ChooseHeightLayer request, CancellationToken cancellationToken)
    {
        model.HeightStack.Choose(request.Layer);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Reads an imported height layer again from its file (user stories T-5, T-3), one step of undo:
/// what the editor does by itself when the file lies in the project folder and changes.
/// </summary>
[EditorCommand("terrain.reloadLayer", "Read a height layer's file again", Story = "T-5", Needs = Needs.Map | Needs.NoStroke | Needs.Idle)]
public sealed record ReloadHeightLayer(string? Layer = null) : IRequest;

public sealed class ReloadHeightLayerHandler(MainViewModel model) : IRequestHandler<ReloadHeightLayer>
{
    public async Task Handle(ReloadHeightLayer request, CancellationToken cancellationToken)
    {
        HeightLayer layer = HeightLayerArgument.Of(model, request.Layer);
        if (layer.Kind != HeightLayerKind.Imported || layer.Source is not string source)
        {
            throw new ArgumentException($"The height layer {layer.Name} is not read from a file");
        }
        MapDocument document = model.ShownDocument!;
        string path = Path.IsPathRooted(source) || model.Map?.ProjectFolder is not string folder ? source : Path.Combine(folder, source);
        ushort[] samples = await Task.Run(() => HeightmapFiles.ReadResampled(document, path, out _), cancellationToken);
        using Performance.Span span = Performance.Measure("model.height-layers");
        model.EditMap($"Read the height layer {layer.Name} again", d => HeightLayerEdits.SetImported(d, layer.Name, samples));
        model.Status = $"Read the height layer {layer.Name} again from {source}";
    }
}

/// <summary>
/// Bakes the height layers into the heightmap (user story T-5), one step of undo: the heightmap
/// keeps what they add up to, and the map has no layers any more, as a map that never had them.
/// </summary>
[EditorCommand("terrain.bake", "Bake the layers into the heightmap", Story = "T-5", Needs = Needs.Map | Needs.NoStroke)]
public sealed record BakeHeightLayers : IRequest;

public sealed class BakeHeightLayersHandler(MainViewModel model) : IRequestHandler<BakeHeightLayers>
{
    public Task Handle(BakeHeightLayers request, CancellationToken cancellationToken)
    {
        MapDocument document = model.ShownDocument ?? throw new InvalidOperationException("No map is open");
        if (document.HeightLayers.IsEmpty)
        {
            model.Status = "The map has no height layers to bake";
            return Task.CompletedTask;
        }
        int count = document.HeightLayers.Length;
        using Performance.Span span = Performance.Measure("model.height-layers");
        model.EditMap("Bake the height layers", HeightLayerEdits.Bake);
        model.Status = string.Create(CultureInfo.InvariantCulture, $"Baked {count} height layers into the heightmap");
        return Task.CompletedTask;
    }
}

/// <summary>
/// Reading the arguments of the height layers' commands.
/// </summary>
internal static class HeightLayerSettings
{
    public static HeightCombine ParseCombine(string text) =>
        Enum.TryParse(text.Trim(), ignoreCase: true, out HeightCombine combine) && Enum.IsDefined(combine)
            ? combine
            : throw new ArgumentException($"A height layer combines by {string.Join(", ", Enum.GetNames<HeightCombine>())}, not '{text}'");

    public static CurveProfile ParseProfile(string text) =>
        Enum.TryParse(text.Trim(), ignoreCase: true, out CurveProfile profile) && Enum.IsDefined(profile)
            ? profile
            : throw new ArgumentException($"A curve's profile is {string.Join(", ", Enum.GetNames<CurveProfile>())}, not '{text}'");

    /// <summary>
    /// A point from <c>[x, z]</c> or <c>[x, z, height]</c>: without a height, a road's lies on the
    /// terrain and the others' rise by <paramref name="height"/>.
    /// </summary>
    public static CurvePoint PointOf(MapDocument document, float[] point, CurveProfile profile, float height)
    {
        if (point.Length < 2)
        {
            throw new ArgumentException("A curve's point is [x, z] or [x, z, height]");
        }
        float h = point.Length > 2 ? point[2] : profile == CurveProfile.Road ? document.Terrain.Heightmap.HeightAt(point[0], point[1]) : height;
        return new CurvePoint(point[0], point[1], h);
    }

    /// <summary>
    /// The names of the arguments given, for the history and merging steps.
    /// </summary>
    public static string Fields(params (string Name, object? Value)[] fields) =>
        string.Join(",", fields.Where(field => field.Value is not null).Select(field => field.Name));
}
