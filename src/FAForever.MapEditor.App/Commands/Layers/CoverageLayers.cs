using System.Globalization;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// The grid of coverage a <c>layers.*</c> command works on, and its layer.
/// </summary>
internal static class CoverageLayerArguments
{
    /// <summary>
    /// The grid a target names: a stack's key (<c>mask-forest</c>, <c>stratum-2</c>, <c>water-foam</c>),
    /// a mask by any of its names (<c>forest</c>, <c>mask:forest</c>, <c>stratum:2</c>,
    /// <c>stratum 2</c>), a water map (<c>foam</c>, <c>flatness</c>, <c>depthBias</c>); left out, the
    /// one the mode shows (the Masks mode's mask, the Paint mode's stratum, the Water mode's map).
    /// </summary>
    /// <exception cref="ArgumentException">There is no such grid, or none is shown.</exception>
    public static LayerTarget Target(MainViewModel model, string? target)
    {
        MapDocument document = model.ShownDocument ?? throw new InvalidOperationException("No map is open");
        LayerTarget? found = target?.Trim() is { Length: > 0 } text ? Parse(document, text) : model.ShownCoverageTarget;
        if (found is null)
        {
            throw new ArgumentException(target is null
                ? "Name the grid (target): a mask, a stratum (stratum:2) or a water map (foam), or choose one in the Masks, Paint or Water mode"
                : $"The map has no mask, stratum or water map '{target}'");
        }
        if (!CoverageLayers.Exists(document, found))
        {
            throw new ArgumentException($"The map has no {found.Grid?.Title ?? found.Key}");
        }
        return found;
    }

    private static LayerTarget? Parse(MapDocument document, string text)
    {
        if (LayerTarget.FromKey(text) is LayerTarget keyed && !keyed.IsHeights && CoverageLayers.Exists(document, keyed))
        {
            return keyed;
        }
        string water = text.StartsWith("water:", StringComparison.OrdinalIgnoreCase) ? text[6..] : text;
        if (Enum.TryParse(water.Replace(" ", "").Replace("-", ""), ignoreCase: true, out WaterMask mask) && Enum.IsDefined(mask) && !document.Masks.ContainsKey(text))
        {
            return LayerTarget.Water(mask);
        }
        string? key = LayerStacks.KeyOf(document, text) ?? LayerStacks.KeyOf(document, RuleLayers.Parse(text));
        return key is null ? null : LayerTarget.FromKey(LayerStacks.StackKeyOf(key)!);
    }

    /// <summary>
    /// The layer named, else the one chosen in the target's stack control.
    /// </summary>
    /// <exception cref="ArgumentException">There is no such layer.</exception>
    public static Layer Layer(MainViewModel model, LayerTarget target, string? name)
    {
        LayerStack stack = CoverageLayers.StackOf(model.ShownDocument!, target);
        string chosen = name ?? model.CoverageStackOf(target)?.Chosen ?? throw new ArgumentException($"Choose a {CoverageLayers.What(target)}, or name one with layer");
        return stack.Layers[LayerStackEdits.IndexOf(stack, chosen, CoverageLayers.What(target))];
    }

    /// <summary>
    /// What a target is for people: <c>the mask forest</c>, <c>stratum 2</c>, <c>water foam</c>.
    /// </summary>
    public static string Title(LayerTarget target) => target.Grid?.Title ?? target.Key;
}

/// <summary>
/// Adds a layer to the stack of a mask for tools, a stratum's mask or a water map (user stories
/// K-1, K-2, S-1, W-2), one step of undo, above the chosen layer, and chooses it. The first layer
/// starts the stack from the grid's own painted layer, so nothing changes until the new one does:
/// a painted layer replaces where it is painted, the others keep the higher of theirs and what is
/// below. Its settings and what a mask reference reads are in the inspector (<c>layers.set</c>).
/// </summary>
/// <param name="Target">The grid; left out, the one the mode shows (see <c>layers.set</c>).</param>
/// <param name="Source"><c>painted</c> (left out), <c>image</c>, <c>mask</c> (a mask for tools),
/// <c>stratum</c> (a stratum's mask) or <c>rules</c>.</param>
/// <param name="Mask">What a mask or stratum layer reads (any name of a mask: <c>forest</c>,
/// <c>stratum:3</c>); left out, the first other mask, or the first other stratum.</param>
/// <param name="Path">An image layer's file; left out, the editor asks.</param>
/// <returns>The new layer's name, or null when no file was chosen.</returns>
[EditorCommand("layers.add", "Add a layer to a mask, stratum or water map...", Story = "K-1", Needs = Needs.Map | Needs.NoStroke)]
public sealed record AddCoverageLayer(string? Target = null, string? Source = null, string? Name = null, string? Mask = null, string? Path = null) : IRequest<string?>;

public sealed class AddCoverageLayerHandler(MainViewModel model, IEditorDialogs dialogs) : IRequestHandler<AddCoverageLayer, string?>
{
    public async Task<string?> Handle(AddCoverageLayer request, CancellationToken cancellationToken)
    {
        LayerTarget target = CoverageLayerArguments.Target(model, request.Target);
        MapDocument document = model.ShownDocument!;
        LayerStack stack = CoverageLayers.StackOf(document, target);
        string source = (request.Source ?? "painted").Trim().ToLowerInvariant();
        string Named(string stem) => request.Name?.Trim() is { Length: > 0 } given ? given : LayerStackEdits.FreeName(stack, stem);
        Layer layer;
        switch (source)
        {
            case "painted" or "paint":
                layer = CoverageLayerEdits.NewPainted(document, target, Named("Painted"));
                break;
            case "image":
            {
                string? path = request.Path ?? await dialogs.PickFileAsync($"Add an image as a layer of {CoverageLayerArguments.Title(target)}", ImportHeightmapHandler.Extensions, model.Map?.ProjectFolder);
                if (path is null)
                {
                    model.Status = "Chose no file";
                    return null;
                }
                path = System.IO.Path.GetFullPath(path);
                byte[] values = await Task.Run(() => CoverageLayerEdits.ReadImage(document, target, path), cancellationToken);
                string stem = new string([.. System.IO.Path.GetFileNameWithoutExtension(path).Select(c => char.IsAsciiLetterOrDigit(c) || c is ' ' or '-' or '_' ? c : '-')]).Trim();
                layer = CoverageLayerEdits.NewImage(document, target, Named(stem.Length == 0 ? "Image" : stem), values, AddHeightLayerHandler.SourceOf(model.Map?.ProjectFolder, path));
                model.Logger("Files").LogInformation("Added the image {File} as the layer {Layer} of {Target}", path, layer.Name, target.Key);
                break;
            }
            case "mask" or "stratum":
            {
                string mask = request.Mask ?? DefaultMask(document, target, source == "stratum")
                    ?? throw new ArgumentException(source == "stratum" ? "The map has no other stratum to read" : "The map has no other mask for tools to read: add one in the Masks mode");
                string key = LayerStacks.KeyOf(document, mask) ?? LayerStacks.KeyOf(document, RuleLayers.Parse(mask)) ?? throw new ArgumentException($"The map has no mask '{mask}'");
                layer = CoverageLayerEdits.NewMaskRef(document, Named(StemOf(key)), key);
                break;
            }
            case "rules":
                layer = CoverageLayerEdits.NewRules(document, target, Named("Rules"));
                break;
            default:
                throw new ArgumentException($"A layer of {CoverageLayerArguments.Title(target)} is painted, image, mask, stratum or rules, not '{request.Source}'");
        }
        CoverageStackViewModel? view = model.CoverageStackOf(target);
        string? above = view?.Chosen is string chosen && stack.Find(chosen) is not null ? chosen : null;
        using Performance.Span span = Performance.Measure("model.coverage-layers");
        model.EditMap($"Add the {CoverageLayers.What(target)} {layer.Name} to {CoverageLayerArguments.Title(target)}", d => CoverageLayerEdits.Add(d, target, layer, above));
        if (view is not null)
        {
            view.Refresh(model.ShownDocument);
            view.Choose(layer.Name);
        }
        model.Status = $"Added the {CoverageLayers.What(target)} {layer.Name} to {CoverageLayerArguments.Title(target)}";
        return layer.Name;
    }

    /// <summary>
    /// The mask a new reference reads: the first mask for tools (or stratum) that is not the target.
    /// </summary>
    private static string? DefaultMask(MapDocument document, LayerTarget target, bool stratum) => stratum
        ? Enumerable.Range(1, document.Terrain.StratumMasks.Layers.Length).Select(LayerStacks.StratumKey).FirstOrDefault(key => LayerStacks.StackKeyOf(key) != target.Key)
        : document.Masks.Keys.Select(LayerStacks.MaskKey).FirstOrDefault(key => LayerStacks.StackKeyOf(key) != target.Key);

    private static string StemOf(string key) =>
        key.StartsWith(LayerStacks.MaskPrefix, StringComparison.Ordinal)
            ? new string([.. key[LayerStacks.MaskPrefix.Length..].Select(c => char.IsAsciiLetterOrDigit(c) || c is ' ' or '-' or '_' ? c : '-')])
            : "Stratum " + key[LayerStacks.StratumPrefix.Length..];
}

/// <summary>
/// Changes a layer of a mask's, stratum's or water map's stack (user stories K-1, S-1, W-2), one
/// step of undo; the steps that change the same fields of the same layer one after the other are
/// one step (a slider dragged). What the inspector's fields and the stack control's boxes do.
/// </summary>
/// <param name="Target">The grid: a mask by name, a stratum (<c>stratum:2</c>), a water map
/// (<c>foam</c>, <c>flatness</c>, <c>depthBias</c>) or a stack's key; left out, the one the mode shows.</param>
/// <param name="Layer">Left out, the chosen one.</param>
/// <param name="Combine"><c>Add</c>, <c>Subtract</c>, <c>Maximum</c>, <c>Minimum</c>, <c>Replace</c> or <c>Multiply</c>.</param>
/// <param name="Strength">0 to 1.</param>
/// <param name="Clip"><c>Everywhere</c> (or empty) for none, else any name of a mask or stratum.</param>
/// <param name="Reads">What a mask reference reads: any name of a mask or stratum.</param>
/// <param name="Threshold">A mask reference's threshold, 0 to 1.</param>
/// <param name="Invert">Whether a mask reference reads where the mask is not.</param>
[EditorCommand("layers.set", "Change a layer of a mask, stratum or water map", Story = "K-1", Needs = Needs.Map | Needs.NoStroke)]
public sealed record SetCoverageLayer(string? Target = null, string? Layer = null, string? Name = null, string? Combine = null, float? Strength = null, string? Clip = null,
    bool? Visible = null, bool? Locked = null, string? Reads = null, float? Threshold = null, bool? Invert = null) : IRequest;

public sealed class SetCoverageLayerHandler(MainViewModel model) : IRequestHandler<SetCoverageLayer>
{
    public Task Handle(SetCoverageLayer request, CancellationToken cancellationToken)
    {
        LayerTarget target = CoverageLayerArguments.Target(model, request.Target);
        Layer layer = CoverageLayerArguments.Layer(model, target, request.Layer);
        MapDocument document = model.ShownDocument!;
        LayerCombine? combine = request.Combine is null ? null : HeightLayerSettings.ParseCombine(request.Combine);
        string? clip = request.Clip is null ? null : LayerStackViewModel.MaskKeyOf(document, request.Clip);
        string? reads = request.Reads is null ? null : LayerStackViewModel.MaskKeyOf(document, request.Reads) ?? throw new ArgumentException("A mask layer reads a mask: name one");
        string? name = request.Name?.Trim() is { Length: > 0 } given && given != layer.Name ? given : null;
        if (request.Strength is < 0 or > 1 || request.Threshold is < 0 or > 1)
        {
            throw new ArgumentException("A layer's strength and a mask layer's threshold are 0 to 1");
        }
        string fields = HeightLayerSettings.Fields(("name", name), ("combine", request.Combine), ("strength", request.Strength), ("clip", request.Clip),
            ("visible", request.Visible), ("locked", request.Locked), ("reads", request.Reads), ("threshold", request.Threshold), ("invert", request.Invert));
        if (fields.Length == 0)
        {
            return Task.CompletedTask;
        }
        string current = layer.Name;
        string what = CoverageLayers.What(target);
        using Performance.Span span = Performance.Measure("model.coverage-layers");
        model.EditMap($"{char.ToUpperInvariant(what[0])}{what[1..]} {layer.Name} of {CoverageLayerArguments.Title(target)}: {fields.Replace(",", ", ")}", d =>
        {
            if (combine is LayerCombine way && way != layer.Combine)
            {
                d = CoverageLayerEdits.SetCombine(d, target, current, way);
            }
            if (request.Strength is float strength && strength != layer.Strength)
            {
                d = CoverageLayerEdits.SetStrength(d, target, current, strength);
            }
            if (request.Clip is not null && clip != layer.Clip)
            {
                d = CoverageLayerEdits.SetClip(d, target, current, clip);
            }
            if (request.Visible is bool visible)
            {
                d = CoverageLayerEdits.SetVisible(d, target, current, visible);
            }
            if (request.Locked is bool locked)
            {
                d = CoverageLayerEdits.SetLocked(d, target, current, locked);
            }
            if (reads is not null || request.Threshold is not null || request.Invert is not null)
            {
                d = CoverageLayerEdits.SetMaskRef(d, target, current, reads, request.Threshold, request.Invert);
            }
            return name is null ? d : CoverageLayerEdits.Rename(d, target, current, name);
        }, $"layers.set:{target.Key}:{layer.Name}:{fields}");
        if (name is not null && model.CoverageStackOf(target) is CoverageStackViewModel view && view.Chosen == current)
        {
            view.Refresh(model.ShownDocument);
            view.Choose(name);
        }
        return Task.CompletedTask;
    }
}

/// <summary>
/// Moves a layer of a mask's, stratum's or water map's stack up or down (user stories K-1, S-1,
/// W-2), one step of undo: a higher layer works on what the lower ones make.
/// </summary>
/// <param name="To">Its new place: 0 is the bottom.</param>
/// <param name="By">Places to move, up when positive; with neither, one up.</param>
[EditorCommand("layers.move", "Move a layer of a mask, stratum or water map", Story = "K-1", Needs = Needs.Map | Needs.NoStroke)]
public sealed record MoveCoverageLayer(string? Target = null, string? Layer = null, int? To = null, int? By = null) : IRequest;

public sealed class MoveCoverageLayerHandler(MainViewModel model) : IRequestHandler<MoveCoverageLayer>
{
    public Task Handle(MoveCoverageLayer request, CancellationToken cancellationToken)
    {
        LayerTarget target = CoverageLayerArguments.Target(model, request.Target);
        Layer layer = CoverageLayerArguments.Layer(model, target, request.Layer);
        int from = CoverageLayers.StackOf(model.ShownDocument!, target).Layers.IndexOf(layer);
        int to = request.To ?? from + (request.By ?? 1);
        if (to == from)
        {
            return Task.CompletedTask;
        }
        using Performance.Span span = Performance.Measure("model.coverage-layers");
        model.EditMap($"Move the {CoverageLayers.What(target)} {layer.Name} {(to > from ? "up" : "down")}", d => CoverageLayerEdits.Move(d, target, layer.Name, to));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Removes a layer of a mask's, stratum's or water map's stack (user stories K-1, S-1, W-2), one
/// step of undo; the grid becomes what the other layers make.
/// </summary>
[EditorCommand("layers.remove", "Remove a layer of a mask, stratum or water map", Story = "K-1", Needs = Needs.Map | Needs.NoStroke)]
public sealed record RemoveCoverageLayer(string? Target = null, string? Layer = null) : IRequest;

public sealed class RemoveCoverageLayerHandler(MainViewModel model) : IRequestHandler<RemoveCoverageLayer>
{
    public Task Handle(RemoveCoverageLayer request, CancellationToken cancellationToken)
    {
        LayerTarget target = CoverageLayerArguments.Target(model, request.Target);
        Layer layer = CoverageLayerArguments.Layer(model, target, request.Layer);
        using Performance.Span span = Performance.Measure("model.coverage-layers");
        model.EditMap($"Remove the {CoverageLayers.What(target)} {layer.Name} of {CoverageLayerArguments.Title(target)}", d => CoverageLayerEdits.Remove(d, target, layer.Name));
        model.Status = $"Removed the {CoverageLayers.What(target)} {layer.Name} of {CoverageLayerArguments.Title(target)}";
        return Task.CompletedTask;
    }
}

/// <summary>
/// Chooses a layer of a mask's, stratum's or water map's stack (user stories K-1, S-1, W-2) as the
/// stack control does: its settings show in the inspector, and the brush paints it when it is a
/// painted layer. A named target is chosen in its mode too.
/// </summary>
[EditorCommand("layers.choose", "Choose a layer of a mask, stratum or water map", Story = "K-1", Needs = Needs.Map)]
public sealed record ChooseCoverageLayer(string Layer, string? Target = null) : IRequest;

public sealed class ChooseCoverageLayerHandler(MainViewModel model) : IRequestHandler<ChooseCoverageLayer>
{
    public Task Handle(ChooseCoverageLayer request, CancellationToken cancellationToken)
    {
        LayerTarget target = CoverageLayerArguments.Target(model, request.Target);
        model.ShowCoverageTarget(target);
        CoverageStackViewModel view = model.CoverageStackOf(target) ?? throw new ArgumentException($"{CoverageLayerArguments.Title(target)} has no stack control");
        view.Refresh(model.ShownDocument);
        view.Choose(request.Layer);
        model.Status = string.Create(CultureInfo.InvariantCulture, $"Chose the {CoverageLayers.What(target)} {view.Chosen} of {CoverageLayerArguments.Title(target)}");
        return Task.CompletedTask;
    }
}
