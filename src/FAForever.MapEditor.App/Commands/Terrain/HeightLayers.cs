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
    public static Layer Of(MainViewModel model, string? name)
    {
        MapDocument document = model.ShownDocument ?? throw new InvalidOperationException("No map is open");
        string chosen = name ?? model.HeightStack.Chosen ?? throw new ArgumentException(document.HeightStack.IsEmpty
            ? "The map has no height layers: add one first (terrain.addLayer)"
            : "Choose a height layer, or name one with layer");
        // the shown heightmap's version of it (its own visibility), else the other's
        (HeightStackKind kind, int index) = HeightStacks.Locate(document, chosen, model.ShownHeights);
        return HeightStacks.Of(document, kind).Layers[index];
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
/// <param name="Kind"><c>sculpt</c> (left out), <c>imported</c>, <c>curve</c> or <c>decals</c> (the masks of the placed decal sets).</param>
/// <param name="Name">Left out, a free one (<c>Sculpt 2</c>, the file's name, <c>Curve</c>).</param>
/// <param name="Path">An imported layer's file (PNG, <c>.r16</c>, ...); left out, the editor asks.</param>
/// <param name="Combine"><c>Add</c>, <c>Subtract</c>, <c>Maximum</c>, <c>Minimum</c>, <c>Replace</c> or <c>Multiply</c>; left out, the
/// kind's own (sculpting adds, an image replaces).</param>
/// <param name="Use">Which heightmaps it shapes: <c>shared</c> (both), <c>draft</c> or <c>final</c>; left
/// out, both while the final shares the draft's base, else the one shown.</param>
/// <param name="Mask">A decals layer's mask name (<c>main</c> when left out, the plain <c>_mask</c>).</param>
/// <param name="Height">A decals layer's height at a full mask, in game units (4 when left out).</param>
/// <returns>The new layer's name, or null when no file was chosen.</returns>
[EditorCommand("terrain.addLayer", "Add a height layer...", Story = "T-5", Needs = Needs.Map | Needs.NoStroke)]
public sealed record AddHeightLayer(string? Kind = null, string? Name = null, string? Path = null, string? Combine = null, string? Use = null, string? Mask = null, float? Height = null) : IRequest<string?>;

public sealed class AddHeightLayerHandler(MainViewModel model, IEditorDialogs dialogs) : IRequestHandler<AddHeightLayer, string?>
{
    public async Task<string?> Handle(AddHeightLayer request, CancellationToken cancellationToken)
    {
        MapDocument document = model.ShownDocument ?? throw new InvalidOperationException("No map is open");
        string kind = (request.Kind ?? "sculpt").Trim().ToLowerInvariant();
        LayerCombine? combine = request.Combine is null ? null : HeightLayerSettings.ParseCombine(request.Combine);
        Layer layer;
        switch (kind)
        {
            case "sculpt":
            {
                string name = request.Name?.Trim() ?? HeightLayerEdits.FreeName(document, "Sculpt");
                layer = HeightLayerEdits.NewSculpt(document, name, combine ?? LayerCombine.Add);
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
                if (combine is LayerCombine chosen)
                {
                    layer = layer with { Combine = chosen };
                }
                model.Logger("Files").LogInformation("Added the heightmap {File} of {Width} by {Height} samples as the height layer {Layer}", path, original.Width, original.Height, name);
                break;
            }
            case "curve":
            {
                string name = request.Name?.Trim() ?? HeightLayerEdits.FreeName(document, "Curve");
                layer = HeightLayerEdits.NewCurve(document, name, new HeightCurve { Symmetry = HeightLayerSettings.SymmetryOf(model, null) });
                if (combine is LayerCombine chosen)
                {
                    layer = layer with { Combine = chosen };
                }
                break;
            }
            case "decals":
            {
                // the masks of the placed decal sets, raising the terrain by Height where they are full
                string name = request.Name?.Trim() ?? HeightLayerEdits.FreeName(document, "Decal masks");
                layer = DecalMaskLayers.New(document, document.HeightStack, name, new DecalMasksSource(request.Mask ?? DecalSets.MainMask, [], request.Height ?? 4), model.DecalPictures);
                if (combine is LayerCombine chosen)
                {
                    layer = layer with { Combine = chosen };
                }
                break;
            }
            default:
                throw new ArgumentException($"A height layer is sculpt, imported, curve or decals, not '{request.Kind}'");
        }
        string? above = model.HeightStack.Chosen is string chosenLayer && HeightLayers.Find(document, chosenLayer) is not null ? chosenLayer : null;
        LayerUse? use = request.Use is null ? null : HeightLayersViewModel.ParseUse(request.Use);
        using Performance.Span span = Performance.Measure("model.height-layers");
        model.EditMap($"Add the height layer {layer.Name}", d => HeightLayerEdits.Add(d, layer, above, model.ShownHeights, use));
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
/// valley, road, cliff edge or river along a Catmull-Rom spline. What the curve tool's first click does.
/// </summary>
/// <param name="Points">The points in ogrids, each <c>[x, z]</c>, <c>[x, z, height]</c> or an object
/// <c>{x, z, height?, width?, falloffLeft?, falloffRight?, strength?, sharp?}</c> with the point's own values.</param>
/// <param name="Height">The height of points without one: what a ridge, valley or cliff rises or
/// sinks; a road's points without one lie on the terrain; a river's bed lies this far below the
/// water's elevation (0 on a map without water).</param>
/// <param name="Mirrored">Whether the layer draws the curve's images by the chosen symmetry too
/// (F-12); left out, while the symmetry mirrors edits.</param>
/// <param name="LineOnly">For a closed curve: shape its line only; left out, it fills its inside.</param>
/// <param name="BlocksWalkability">For a cliff: whether its face blocks walkability; left out, a cliff's does.</param>
[EditorCommand("terrain.addCurve", "Add a curve", Story = "T-4", Needs = Needs.Map | Needs.NoStroke)]
public sealed record AddHeightCurve(CurvePointArgument[] Points, string? Profile = null, float? Width = null, float? Falloff = null, float? Height = null,
    bool Closed = false, string? Name = null, bool? Mirrored = null, float? FalloffRight = null, string? Shape = null, int? TerraceSteps = null,
    float? CliffFace = null, string? RoadMode = null, float? Camber = null, float? NoiseAmplitude = null, float? NoiseScale = null, int? NoiseSeed = null,
    float[]? HeightVariation = null, float[]? WidthVariation = null, float? VariationScale = null, bool? LineOnly = null,
    bool? BlocksWalkability = null) : IRequest<string>, ICurveFields;

public sealed class AddHeightCurveHandler(MainViewModel model) : IRequestHandler<AddHeightCurve, string>
{
    public Task<string> Handle(AddHeightCurve request, CancellationToken cancellationToken)
    {
        MapDocument document = model.ShownDocument ?? throw new InvalidOperationException("No map is open");
        CurveTool tool = model.Tool<CurveTool>();
        CurveProfile profile = request.Profile is null ? tool.Profile : HeightLayerSettings.ParseProfile(request.Profile);
        if (request.Width is < 0 || request.Falloff is < 0)
        {
            throw new ArgumentException("A curve's width and falloff are 0 or more ogrids");
        }
        float height = request.Height ?? (float)tool.Height;
        HeightCurve curve = HeightCurveArguments.Apply(new HeightCurve
        {
            Profile = profile,
            Width = request.Width ?? (float)tool.CurveWidth,
            Falloff = request.Falloff ?? (float)tool.Falloff,
            Closed = request.Closed,
            // a new cliff's face blocks walkability unless asked otherwise
            BlocksWalkability = profile == CurveProfile.Cliff,
            Symmetry = HeightLayerSettings.SymmetryOf(model, request.Mirrored),
            Points = [.. HeightCurveArguments.PointsOf(document, request.Points, profile, height)],
        }, request);
        string name = request.Name?.Trim() ?? HeightLayerEdits.FreeName(document, profile.ToString());
        string? above = model.HeightStack.Chosen is string chosen && HeightLayers.Find(document, chosen) is not null ? chosen : null;
        using Performance.Span span = Performance.Measure("model.height-layers");
        model.EditMap($"Add the curve {name}", d => HeightLayerEdits.Add(d, HeightLayerEdits.NewCurve(d, name, curve), above, model.ShownHeights));
        model.HeightStack.Refresh(model.ShownDocument);
        model.HeightStack.Choose(name);
        // a river's bed without heights given lies below the water: say where
        string bed = profile == CurveProfile.River && request.Points.Any(point => point.Height is null)
            ? document.Water.HasWater
                ? string.Create(CultureInfo.InvariantCulture, $"; its bed lies {height:0.##} below the water's elevation {document.Water.Elevation:0.##}, at {HeightCurveArguments.RiverBase(document) - height:0.##}")
                : string.Create(CultureInfo.InvariantCulture, $"; the map has no water: its bed lies {height:0.##} below 0, at {-height:0.##}")
            : "";
        model.Status = string.Create(CultureInfo.InvariantCulture, $"Added the curve {name}: a {profile.ToString().ToLowerInvariant()} through {curve.Points.Length} point{(curve.Points.Length == 1 ? "" : "s")}{(curve.Symmetry is null ? "" : ", mirrored by the symmetry")}{(curve.Fills ? ", filling its inside" : "")}{bed}");
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
/// <param name="Points">All points anew, each <c>[x, z]</c>, <c>[x, z, height]</c> or an object
/// <c>{x, z, height?, width?, falloffLeft?, falloffRight?, strength?, sharp?}</c>.</param>
/// <param name="Mirrored">True: the layer draws the curve's images by the chosen symmetry too
/// (F-12); false: the curve alone.</param>
/// <param name="BlocksWalkability">For a cliff: whether its face blocks walkability; left out, as it was
/// (a curve that becomes a cliff blocks).</param>
[EditorCommand("terrain.setCurve", "Change a curve", Story = "T-4", Needs = Needs.Map | Needs.NoStroke)]
public sealed record SetHeightCurve(string? Layer = null, string? Profile = null, float? Width = null, float? Falloff = null, float? Height = null,
    bool? Closed = null, CurvePointArgument[]? Points = null, bool? Mirrored = null, float? FalloffRight = null, string? Shape = null, int? TerraceSteps = null,
    float? CliffFace = null, string? RoadMode = null, float? Camber = null, float? NoiseAmplitude = null, float? NoiseScale = null, int? NoiseSeed = null,
    float[]? HeightVariation = null, float[]? WidthVariation = null, float? VariationScale = null, bool? LineOnly = null,
    bool? BlocksWalkability = null) : IRequest, ICurveFields;

public sealed class SetHeightCurveHandler(MainViewModel model) : IRequestHandler<SetHeightCurve>
{
    public Task Handle(SetHeightCurve request, CancellationToken cancellationToken)
    {
        Layer layer = HeightLayerArgument.Of(model, request.Layer);
        HeightCurve curve = layer.Curve ?? throw new ArgumentException($"The height layer {layer.Name} is no curve");
        if (request.Width is < 0 || request.Falloff is < 0)
        {
            throw new ArgumentException("A curve's width and falloff are 0 or more ogrids");
        }
        MapDocument document = model.ShownDocument!;
        CurveProfile profile = request.Profile is null ? curve.Profile : HeightLayerSettings.ParseProfile(request.Profile);
        HeightCurve changed = HeightCurveArguments.Apply(curve with
        {
            Profile = profile,
            Width = request.Width ?? curve.Width,
            Falloff = request.Falloff ?? curve.Falloff,
            Closed = request.Closed ?? curve.Closed,
            // a curve that becomes a cliff is a new cliff: its face blocks walkability unless asked otherwise
            BlocksWalkability = profile == CurveProfile.Cliff && curve.Profile != CurveProfile.Cliff || curve.BlocksWalkability,
            Symmetry = request.Mirrored is bool mirrored ? HeightLayerSettings.SymmetryOf(model, mirrored) : curve.Symmetry,
            Points = request.Points is CurvePointArgument[] points
                ? [.. HeightCurveArguments.PointsOf(document, points, profile, request.Height ?? curve.Points.FirstOrDefault()?.Height ?? 10)]
                : request.Height is float height ? [.. curve.Points.Select(point => point with { Height = height })] : curve.Points,
        }, request);
        string fields = HeightLayerSettings.Fields([("profile", request.Profile), ("width", request.Width), ("falloff", request.Falloff), ("height", request.Height),
            ("closed", request.Closed), ("points", request.Points), ("mirrored", request.Mirrored), .. HeightCurveArguments.Given(request)]);
        // a point added, moved or removed is a step of its own; a field typed in again or a slider dragged is one
        int added = changed.Points.Length - curve.Points.Length;
        string description = request.Points is null ? $"Curve {layer.Name}: {fields}"
            : added == 1 ? $"Curve {layer.Name}: add a point ({changed.Points.Length})"
            : added == -1 ? $"Curve {layer.Name}: remove a point ({changed.Points.Length})"
            : $"Curve {layer.Name}: {changed.Points.Length} points";
        using Performance.Span span = Performance.Measure("model.height-layers");
        model.EditMap(description, d => HeightLayerEdits.SetCurve(d, layer.Name, changed), request.Points is null ? $"terrain.setCurve:{layer.Name}:{fields}" : null);
        model.Status = description;
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
        Layer layer = HeightLayerArgument.Of(model, request.Layer);
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
        Layer layer = HeightLayerArgument.Of(model, request.Layer);
        MapDocument document = model.ShownDocument!;
        // its place in the heightmap shown: a shared layer has one in each
        (HeightStackKind kind, int from) = HeightStacks.Locate(document, layer.Name, model.ShownHeights);
        int to = request.To ?? from + (request.By ?? 1);
        if (to == from)
        {
            return Task.CompletedTask;
        }
        using Performance.Span span = Performance.Measure("model.height-layers");
        model.EditMap(to > from ? $"Move the height layer {layer.Name} up" : $"Move the height layer {layer.Name} down", d => HeightLayerEdits.Move(d, layer.Name, to, kind));
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
/// <param name="Combine"><c>Add</c>, <c>Subtract</c>, <c>Maximum</c>, <c>Minimum</c>, <c>Replace</c> or <c>Multiply</c>.</param>
/// <param name="Strength">0 to 1.</param>
/// <param name="Mask"><c>Everywhere</c> (or empty) for none, a mask for tools by name or as
/// <c>mask:rivers</c>, or a stratum's as <c>stratum 3</c> or <c>stratum:3</c>.</param>
/// <param name="Visible">Whether it shows in the heightmap shown (a shared layer is on or off in
/// each on its own).</param>
/// <param name="Use">Which heightmaps it shapes: <c>shared</c> (both, also <c>Draft and final</c>),
/// <c>draft</c> or <c>final</c>.</param>
[EditorCommand("terrain.setLayer", "Change a height layer", Story = "T-5", Needs = Needs.Map | Needs.NoStroke)]
public sealed record SetHeightLayer(string? Layer = null, string? Name = null, string? Combine = null, float? Strength = null, string? Mask = null,
    bool? Visible = null, bool? Locked = null, string? Use = null) : IRequest;

public sealed class SetHeightLayerHandler(MainViewModel model) : IRequestHandler<SetHeightLayer>
{
    public Task Handle(SetHeightLayer request, CancellationToken cancellationToken)
    {
        Layer layer = HeightLayerArgument.Of(model, request.Layer);
        MapDocument document = model.ShownDocument!;
        LayerCombine? combine = request.Combine is null ? null : HeightLayerSettings.ParseCombine(request.Combine);
        string? mask = request.Mask is null ? null : HeightLayersViewModel.MaskKeyOf(document, request.Mask);
        string? name = request.Name?.Trim() is { Length: > 0 } given && given != layer.Name ? given : null;
        if (request.Strength is < 0 or > 1)
        {
            throw new ArgumentException("A height layer's strength is 0 to 1");
        }
        LayerUse? use = request.Use is null ? null : HeightLayersViewModel.ParseUse(request.Use);
        string fields = HeightLayerSettings.Fields(("name", name), ("combine", request.Combine), ("strength", request.Strength), ("mask", request.Mask),
            ("visible", request.Visible), ("locked", request.Locked), ("use", request.Use));
        if (fields.Length == 0)
        {
            return Task.CompletedTask;
        }
        string current = layer.Name;
        HeightStackKind shown = model.ShownHeights;
        using Performance.Span span = Performance.Measure("model.height-layers");
        model.EditMap($"Height layer {layer.Name}: {fields}", d =>
        {
            if (use is LayerUse used)
            {
                d = HeightStacks.SetUse(d, current, used);
            }
            if (combine is LayerCombine way && way != layer.Combine)
            {
                d = HeightLayerEdits.SetCombine(d, current, way, shown);
            }
            if (request.Strength is float strength && strength != layer.Strength)
            {
                d = HeightLayerEdits.SetStrength(d, current, strength);
            }
            if (request.Mask is not null && mask != layer.Clip)
            {
                d = HeightLayerEdits.SetMask(d, current, mask);
            }
            if (request.Visible is bool visible)
            {
                d = HeightLayerEdits.SetVisible(d, current, visible, shown);
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
        Layer layer = HeightLayerArgument.Of(model, request.Layer);
        if (layer.Path is not string source)
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
        if (document.HeightStack.IsEmpty)
        {
            model.Status = "The map has no height layers to bake";
            return Task.CompletedTask;
        }
        int count = document.HeightStack.Count;
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
    public static LayerCombine ParseCombine(string text) =>
        Enum.TryParse(text.Trim(), ignoreCase: true, out LayerCombine combine) && Enum.IsDefined(combine)
            ? combine
            : throw new ArgumentException($"A height layer combines by {string.Join(", ", Enum.GetNames<LayerCombine>())}, not '{text}'");

    public static CurveProfile ParseProfile(string text) =>
        Enum.TryParse(text.Trim(), ignoreCase: true, out CurveProfile profile) && Enum.IsDefined(profile)
            ? profile
            : throw new ArgumentException($"A curve's profile is {string.Join(", ", Enum.GetNames<CurveProfile>())}, not '{text}'");

    /// <summary>
    /// The symmetry a curve is drawn by: the chosen one when <paramref name="mirrored"/> is true,
    /// none when false, and while the symmetry mirrors edits when left out.
    /// </summary>
    /// <exception cref="ArgumentException">Mirrored asked while no symmetry is chosen.</exception>
    public static SymmetrySetting? SymmetryOf(MainViewModel model, bool? mirrored) => mirrored switch
    {
        null => model.Symmetry.IsMirroring ? model.Symmetry.Setting : null,
        true => model.Symmetry.Setting ?? throw new ArgumentException("Choose a symmetry first (select.symmetry) to mirror a curve by it"),
        false => null,
    };

    /// <summary>
    /// The names of the arguments given, for the history and merging steps.
    /// </summary>
    public static string Fields(params (string Name, object? Value)[] fields) =>
        string.Join(",", fields.Where(field => field.Value is not null).Select(field => field.Name));
}
