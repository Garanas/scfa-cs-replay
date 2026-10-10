using System.Collections.Immutable;
using System.Text.Json.Nodes;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// The path layers named in a command.
/// </summary>
internal static class PathLayerNames
{
    public static PathLayer Parse(string name) =>
        Enum.TryParse(name, ignoreCase: true, out PathLayer layer) && Enum.IsDefined(layer)
            ? layer
            : throw new ArgumentException($"There is no layer '{name}': Land, Water, Amphibious or Air");

    public static PathLayer? ParseOrNull(string? name) => name is null ? null : Parse(name);
}

/// <summary>
/// Generates the AI's path networks from the navigation mesh (user stories R-3, F-7), on a thread of
/// the pool, as a preview: drawn faintly in the views (the overlay <c>generatedPathNodes</c>), never
/// put into the map; the export writes them into the markers of the files when the map says so. The
/// settings given are kept with the map first (one step of undo, which also turns writing on export
/// on the first time); what is left out stays as the map has it. The edits by hand are applied on
/// top of the result.
/// </summary>
/// <param name="Layers"><c>Land</c>, <c>Water</c>, <c>Amphibious</c>, <c>Air</c>.</param>
/// <param name="Spacing">About how far apart the nodes lie, in ogrids, 4 to 512; 0 for the map's size (a sixteenth, 16 to 64).</param>
/// <param name="AirSpacing">How far apart the air nodes lie; 0 for a grid of 16 by 16.</param>
/// <param name="Smoothing">Rounds of smoothing, 0 to 10.</param>
/// <param name="Replace">Whether the export removes the map's own path nodes of these layers.</param>
/// <returns>Per layer the nodes and links of the preview, with the edits applied.</returns>
[EditorCommand("markers.connectPaths", "Generate path networks", Story = "R-3", Needs = Needs.Map | Needs.NoStroke)]
public sealed record ConnectPathNodes(string[]? Layers = null, float? Spacing = null, float? AirSpacing = null, int? Smoothing = null, bool? Replace = null) : IRequest<JsonObject>;

public sealed class ConnectPathNodesHandler(MainViewModel model) : IRequestHandler<ConnectPathNodes, JsonObject>
{
    public async Task<JsonObject> Handle(ConnectPathNodes request, CancellationToken cancellationToken)
    {
        PathNodeSettings before = model.Map!.Document.Procedural.PathNodes;
        PathNodeSettings settings = SetPathNodesHandler.Change(before, request.Layers, request.Spacing, request.AirSpacing, request.Smoothing, null, null, request.Replace, null);
        if (settings.OnExport is null)
        {
            // generating says the map wants them: the export writes them from now on
            settings = settings with { OnExport = true };
        }
        if (settings.Layers.IsEmpty)
        {
            throw new ArgumentException("Choose a layer to generate: Land, Water, Amphibious or Air");
        }
        model.EditPathNodes("Generate the path networks", _ => settings);

        await model.GeneratePathNetworksAsync();
        model.ShowGeneratedPathNodes = true;
        EditedPathNetwork preview = model.PathNodes.Preview!;
        string summary = model.PathNodes.Result;
        model.Status = $"Generated path networks: {summary}" + (preview.Unmatched.Count > 0 ? $"; {preview.Unmatched.Count} edits by hand found no node" : "");
        return new JsonObject
        {
            ["layers"] = new JsonArray([.. preview.Network.Layers.Select(layer => (JsonNode)new JsonObject
            {
                ["layer"] = layer.Layer.ToString(),
                ["spacing"] = layer.Spacing,
                ["nodes"] = layer.Nodes.Length,
                ["links"] = layer.Links.Length,
            })]),
            ["unmatched"] = preview.Unmatched.Count,
            ["onExport"] = settings.WritesOnExport,
        };
    }
}

/// <summary>
/// Changes how the path networks are generated and whether the export writes them (the Procedural
/// panel's fields): kept with the map, one step of undo; the generated networks are then out of date.
/// What is left out stays.
/// </summary>
/// <param name="Layers"><c>Land</c>, <c>Water</c>, <c>Amphibious</c>, <c>Air</c>.</param>
/// <param name="Spacing">4 to 512 ogrids; 0 for the map's size.</param>
/// <param name="AirSpacing">4 to 512 ogrids; 0 for a grid of 16 by 16.</param>
/// <param name="Smoothing">0 to 10 rounds.</param>
/// <param name="MinimumDistance">How close two nodes may lie, in ogrids; 0 for 0.4 of the spacing.</param>
/// <param name="Fill">Whether open ground away from the skeleton gets nodes.</param>
/// <param name="Replace">Whether the export removes the map's own path nodes of these layers.</param>
/// <param name="OnExport">Whether the export writes the generated networks.</param>
[EditorCommand("generate.setPathNodes", "Path network settings", Story = "R-3", Needs = Needs.Map | Needs.NoStroke)]
public sealed record SetPathNodes(string[]? Layers = null, float? Spacing = null, float? AirSpacing = null, int? Smoothing = null, float? MinimumDistance = null,
    bool? Fill = null, bool? Replace = null, bool? OnExport = null) : IRequest;

public sealed class SetPathNodesHandler(MainViewModel model) : IRequestHandler<SetPathNodes>
{
    public Task Handle(SetPathNodes request, CancellationToken cancellationToken)
    {
        PathNodeSettings before = model.Map!.Document.Procedural.PathNodes;
        PathNodeSettings after = Change(before, request.Layers, request.Spacing, request.AirSpacing, request.Smoothing, request.MinimumDistance, request.Fill, request.Replace, request.OnExport);
        model.Status = model.EditPathNodes("Change the path network settings", _ => after)
            ? $"Path networks: {Describe(after)}"
            : "The path network settings stay as they are";
        return Task.CompletedTask;
    }

    internal static string Describe(PathNodeSettings settings) =>
        $"{string.Join(", ", settings.Layers).ToLowerInvariant()}, spacing {(settings.Spacing is float spacing ? EditorCulture.Format(spacing) : "by the map's size")}, "
        + $"{settings.Smoothing} rounds of smoothing, {(settings.WritesOnExport ? "written" : "not written")} on export";

    /// <summary>
    /// The settings with what a command gives; a number out of range throws.
    /// </summary>
    internal static PathNodeSettings Change(PathNodeSettings settings, string[]? layers, float? spacing, float? airSpacing, int? smoothing, float? minimum, bool? fill, bool? replace, bool? onExport)
    {
        static float? Distance(float value, string what) => value == 0 ? null
            : value is >= 4 and <= 512 ? value : throw new ArgumentException($"The {what} is from 4 to 512 ogrids, or 0 for the default");
        if (layers is not null)
        {
            settings = settings with { Layers = [.. layers.Select(PathLayerNames.Parse).Distinct()] };
        }
        if (spacing is float s)
        {
            settings = settings with { Spacing = Distance(s, "spacing") };
        }
        if (airSpacing is float a)
        {
            settings = settings with { AirSpacing = Distance(a, "air spacing") };
        }
        if (smoothing is int rounds)
        {
            settings = settings with { Smoothing = rounds is >= 0 and <= 10 ? rounds : throw new ArgumentException("The smoothing is from 0 to 10 rounds") };
        }
        if (minimum is float m)
        {
            settings = settings with { MinimumDistance = m == 0 ? null : m is > 0 and <= 512 ? m : throw new ArgumentException("The minimum distance is from 0 to 512 ogrids") };
        }
        if (fill is bool f)
        {
            settings = settings with { Fill = f };
        }
        if (replace is bool r)
        {
            settings = settings with { Replace = r };
        }
        if (onExport is bool e)
        {
            settings = settings with { OnExport = e };
        }
        return settings;
    }
}

/// <summary>
/// Adds a path node by hand to a generated network (the Path nodes tool's click on empty ground):
/// kept with the map as an edit, one step of undo, linked to up to three of its nearest generated
/// neighbours it sees, and applied again after every generation.
/// </summary>
/// <param name="Layer"><c>Land</c> (left out), <c>Water</c>, <c>Amphibious</c>, <c>Air</c>.</param>
[EditorCommand("generate.addPathNode", "Add a path node to the generated network", Story = "R-3", Needs = Needs.Map | Needs.NoStroke)]
public sealed record AddGeneratedPathNode(float X, float Z, string? Layer = null) : IRequest;

public sealed class AddGeneratedPathNodeHandler(MainViewModel model) : IRequestHandler<AddGeneratedPathNode>
{
    public Task Handle(AddGeneratedPathNode request, CancellationToken cancellationToken)
    {
        PathLayer layer = PathLayerNames.ParseOrNull(request.Layer) ?? PathLayer.Land;
        if (model.PathNodes.Preview is null)
        {
            throw new ArgumentException("Generate the path networks first (markers.connectPaths)");
        }
        model.AddGeneratedPathNode(layer, request.X, request.Z);
        model.Status = $"Added a {layer.ToString().ToLowerInvariant()} path node at ({EditorCulture.Format(request.X)}, {EditorCulture.Format(request.Z)})";
        return Task.CompletedTask;
    }
}

/// <summary>
/// Moves the generated path node nearest to a place (within <c>within</c> ogrids) to another: an
/// edit kept with the map, one step of undo. A generated node is found again after the next
/// generation by where it was generated.
/// </summary>
/// <param name="Layer">Only nodes of this layer; left out, any.</param>
/// <param name="Within">How far from the place the node may lie; left out, 4 ogrids.</param>
[EditorCommand("generate.movePathNode", "Move a generated path node", Story = "R-3", Needs = Needs.Map | Needs.NoStroke)]
public sealed record MoveGeneratedPathNode(float X, float Z, float ToX, float ToZ, string? Layer = null, float? Within = null) : IRequest;

public sealed class MoveGeneratedPathNodeHandler(MainViewModel model) : IRequestHandler<MoveGeneratedPathNode>
{
    public Task Handle(MoveGeneratedPathNode request, CancellationToken cancellationToken)
    {
        PreviewPathNode node = model.RequireGeneratedNode(request.X, request.Z, request.Within ?? 4, PathLayerNames.ParseOrNull(request.Layer));
        model.MoveGeneratedPathNode(node, request.ToX, request.ToZ);
        model.Status = $"Moved a {node.Layer.ToString().ToLowerInvariant()} path node to ({EditorCulture.Format(request.ToX)}, {EditorCulture.Format(request.ToZ)})";
        return Task.CompletedTask;
    }
}

/// <summary>
/// Removes the generated path node nearest to a place, with its links: an edit kept with the map,
/// one step of undo. A node added by hand goes with its edits.
/// </summary>
/// <param name="Layer">Only nodes of this layer; left out, any.</param>
/// <param name="Within">How far from the place the node may lie; left out, 4 ogrids.</param>
[EditorCommand("generate.removePathNode", "Remove a generated path node", Story = "R-3", Needs = Needs.Map | Needs.NoStroke)]
public sealed record RemoveGeneratedPathNode(float X, float Z, string? Layer = null, float? Within = null) : IRequest;

public sealed class RemoveGeneratedPathNodeHandler(MainViewModel model) : IRequestHandler<RemoveGeneratedPathNode>
{
    public Task Handle(RemoveGeneratedPathNode request, CancellationToken cancellationToken)
    {
        PreviewPathNode node = model.RequireGeneratedNode(request.X, request.Z, request.Within ?? 4, PathLayerNames.ParseOrNull(request.Layer));
        model.RemoveGeneratedPathNode(node);
        model.Status = $"Removed a {node.Layer.ToString().ToLowerInvariant()} path node at ({EditorCulture.Format(node.Node.X)}, {EditorCulture.Format(node.Node.Z)})";
        return Task.CompletedTask;
    }
}

/// <summary>
/// Links the generated path nodes nearest to two places, or cuts their link: an edit kept with the
/// map, one step of undo. A link cut and linked again (or the other way) leaves no edit.
/// </summary>
/// <param name="Layer">Only nodes of this layer; left out, the layer of the node at the first place.</param>
/// <param name="Within">How far from each place its node may lie; left out, 4 ogrids.</param>
[EditorCommand("generate.linkPathNodes", "Link or cut generated path nodes", Story = "R-3", Needs = Needs.Map | Needs.NoStroke)]
public sealed record LinkGeneratedPathNodes(float X, float Z, float ToX, float ToZ, bool Cut = false, string? Layer = null, float? Within = null) : IRequest;

public sealed class LinkGeneratedPathNodesHandler(MainViewModel model) : IRequestHandler<LinkGeneratedPathNodes>
{
    public Task Handle(LinkGeneratedPathNodes request, CancellationToken cancellationToken)
    {
        float within = request.Within ?? 4;
        PreviewPathNode a = model.RequireGeneratedNode(request.X, request.Z, within, PathLayerNames.ParseOrNull(request.Layer));
        PreviewPathNode b = model.RequireGeneratedNode(request.ToX, request.ToZ, within, a.Layer);
        model.LinkGeneratedPathNodes(a, b, request.Cut);
        model.Status = request.Cut ? $"Cut the link between two {a.Layer.ToString().ToLowerInvariant()} path nodes" : $"Linked two {a.Layer.ToString().ToLowerInvariant()} path nodes";
        return Task.CompletedTask;
    }
}

/// <summary>
/// Forgets the edits by hand of the generated path networks (one layer's, or all), or only those
/// that found no node: one step of undo.
/// </summary>
/// <param name="Layer">Only this layer's edits; left out, all.</param>
/// <param name="Unmatched">Only the edits that found no generated node.</param>
[EditorCommand("generate.clearPathEdits", "Forget the edits of the path networks", Story = "R-3", Needs = Needs.Map | Needs.NoStroke)]
public sealed record ClearPathEdits(string? Layer = null, bool Unmatched = false) : IRequest;

public sealed class ClearPathEditsHandler(MainViewModel model) : IRequestHandler<ClearPathEdits>
{
    public Task Handle(ClearPathEdits request, CancellationToken cancellationToken)
    {
        PathLayer? layer = PathLayerNames.ParseOrNull(request.Layer);
        int before = model.Map!.Document.Procedural.PathNodes.Edits.Length;
        IReadOnlyList<PathEdit> unmatched = model.PathNodes.Preview?.Unmatched ?? [];
        model.EditPathNodes(request.Unmatched ? "Forget the path edits without their node" : "Forget the path edits",
            settings => request.Unmatched
                ? PathNetworkEdits.ForgetUnmatched(settings, [.. unmatched.Where(edit => layer is null || edit.Layer == layer)])
                : PathNetworkEdits.Clear(settings, layer));
        int forgotten = before - model.Map.Document.Procedural.PathNodes.Edits.Length;
        model.Status = forgotten == 0 ? "No edits to forget" : forgotten == 1 ? "Forgot 1 edit of the path networks" : $"Forgot {forgotten} edits of the path networks";
        return Task.CompletedTask;
    }
}
