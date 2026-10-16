using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The path networks of the Procedural mode (user story R-3): generated from the navigation mesh,
/// previewed in the views (never in the document), edited by hand on top (the edits are the map's,
/// each a step of undo), and written as markers into the files an export writes.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// The path networks' settings, how they stand, the edits and the preview.
    /// </summary>
    public PathNodesViewModel PathNodes { get; private set; } = null!;

    /// <summary>
    /// Whether the views draw the generated path networks, faintly (the overlay
    /// <c>generatedPathNodes</c>).
    /// </summary>
    [ObservableProperty]
    public partial bool ShowGeneratedPathNodes { get; set; }

    partial void OnShowGeneratedPathNodesChanged(bool value)
    {
        RefreshOverlays();
        Procedural?.Refresh();
    }

    /// <summary>
    /// The id of the export's step that writes the generated path networks.
    /// </summary>
    public const string PathNodesExportStep = "pathNodes";

    private void InitializePathNodes()
    {
        PathNodes = new PathNodesViewModel(Generation, () => Map?.Document, RunWithAsync);
        PathNodes.PreviewChanged += (_, _) => RefreshOverlays();
        Procedural.PreviewWith(PathNetworkGenerator.GeneratorId, "generatedPathNodes");
        Procedural.OnExport(PathNetworkGenerator.GeneratorId, ExportUse.Written,
            settings => settings.PathNodes.WritesOnExport,
            (settings, on) => settings with { PathNodes = settings.PathNodes with { OnExport = on } });
        PropertyChanged += (_, e) =>
        {
            // the tool works on what it shows
            if (e.PropertyName == nameof(ActiveTool) && ActiveTool?.Tool is Tools.PathNodeEditTool)
            {
                ShowGeneratedPathNodes = true;
            }
            // the layers drawn follow the tool coming and going (GeneratedPathKinds)
            if (e.PropertyName == nameof(ActiveTool) && (ActiveTool?.Tool is Tools.PathNodeEditTool) != _pathToolShown)
            {
                _pathToolShown = !_pathToolShown;
                RefreshOverlays();
            }
        };
        DocumentChanged += (_, _) => PathNodes.Refresh();
        MapChanged += (_, _) => PathNodes.Refresh();

        Export.Register(new ExportStep(PathNodesExportStep, "Write the generated path networks", ExportStep.PathNodesOrder)
        {
            Applies = run =>
            {
                PathNodeSettings settings = run.Document.Procedural.PathNodes;
                if (!settings.WritesOnExport || settings.Layers.IsEmpty)
                {
                    run.Note = "off (the map's setting)";
                    return false;
                }
                return true;
            },
            Run = async run =>
            {
                GeneratedResult? result = Generation.Results.ResultOf(PathNetworkGenerator.GeneratorId);
                if (result is null || result.IsStale(Map!.Document))
                {
                    result = await GeneratePathNetworksAsync();
                }
                PathNodeSettings settings = run.Document.Procedural.PathNodes;
                EditedPathNetwork edited = PathNetworkEdits.Apply((PathNetwork)result.Value, settings);
                run.Document = PathNetworks.Apply(run.Document, edited.Network, settings.Replace);
                run.Note = $"{Units(edited.Network.Nodes, "node")}, {Units(edited.Network.Links, "link")}"
                    + (settings.Edits.IsEmpty ? "" : $", {Units(settings.Edits.Length, "edit")} by hand")
                    + (settings.Replace ? ", the map's own path nodes of these layers replaced" : "");
                if (edited.Unmatched.Count > 0)
                {
                    Logger("Generation").LogWarning("{Count} edits of the path networks found no node at export", edited.Unmatched.Count);
                }
            },
        });
    }

    /// <summary>
    /// Generates the path networks from the map as it is (the preview), handing the generator the
    /// current walkability and navigation mesh so the mesh is not made again.
    /// </summary>
    public async Task<GeneratedResult> GeneratePathNetworksAsync()
    {
        PathNetworkGenerator generator = (PathNetworkGenerator)Generation.Results.Find(PathNetworkGenerator.GeneratorId)!;
        generator.Walkability = Generation.Results.ResultOf(WalkabilityGenerator.GeneratorId);
        generator.NavigationMesh = Generation.Results.ResultOf(NavigationMeshGenerator.GeneratorId);
        GeneratedResult result = await GenerateAsync(PathNetworkGenerator.GeneratorId);
        PathNodes.Refresh();
        return result;
    }

    /// <summary>
    /// Changes the map's path network settings (a step of undo); nothing when they stay the same.
    /// </summary>
    /// <returns>Whether they changed.</returns>
    public bool EditPathNodes(string description, Func<PathNodeSettings, PathNodeSettings> change)
    {
        PathNodeSettings before = RequireMap().Document.Procedural.PathNodes;
        PathNodeSettings after = change(before);
        if (after == before)
        {
            return false;
        }
        EditMap(description, document => document with { Procedural = document.Procedural with { PathNodes = change(document.Procedural.PathNodes) } });
        PathNodes.Refresh();
        return true;
    }

    /// <summary>
    /// The previewed node nearest to a place within a distance (of a layer, or any); throws when
    /// there is none.
    /// </summary>
    /// <exception cref="ArgumentException">No node lies there, or nothing is generated.</exception>
    public PreviewPathNode RequireGeneratedNode(float x, float z, float within, PathLayer? layer = null)
    {
        if (PathNodes.Preview is null)
        {
            throw new ArgumentException("Generate the path networks first (markers.connectPaths)");
        }
        return PathNodes.NodeAt(x, z, within, layer)
            ?? throw new ArgumentException($"No generated {(layer is PathLayer named ? named.ToString().ToLowerInvariant() + " " : "")}path node lies within {EditorCulture.Format(within)} ogrids of ({EditorCulture.Format(x)}, {EditorCulture.Format(z)})");
    }

    /// <summary>
    /// A place on the map: the map's bounds hold it.
    /// </summary>
    private Vector2 OnTheMap(float x, float z)
    {
        MapDocument document = RequireMap().Document;
        return new Vector2(Math.Clamp(x, 0, document.Width), Math.Clamp(z, 0, document.Height));
    }

    /// <summary>
    /// How far from the image of a generated node its mirror partner may lie, in ogrids: the network
    /// of a symmetric map is symmetric only as far as its skeleton is.
    /// </summary>
    public const float PathNodePartnerReach = 4;

    /// <summary>
    /// The other parts of the map the edits by hand of the path networks are mirrored into, as
    /// markers placed by hand are (user story F-12): while the symmetry mirrors edits, else none.
    /// </summary>
    private IReadOnlyList<MapSymmetryTransform> PathNodeImages() =>
        Map?.Document is MapDocument document ? [.. EditTransforms(document).Skip(1)] : [];

    /// <summary>
    /// The generated node nearest the image of a node in another part, of its layer, within
    /// <see cref="PathNodePartnerReach"/>; null when there is none or it is the node itself.
    /// </summary>
    private PreviewPathNode? PathNodePartner(PreviewPathNode node, MapSymmetryTransform transform)
    {
        Vector2 image = transform.Apply(node.Node.Place);
        PreviewPathNode? partner = PathNodes.NodeAt(image.X, image.Y, PathNodePartnerReach, node.Layer);
        return partner is null || partner.Index == node.Index ? null : partner;
    }

    /// <summary>
    /// Adds a node by hand to a generated network (a step of undo), linked to its nearest generated
    /// neighbours, and chooses it; while the symmetry mirrors edits, with a node at its image in
    /// every other part (one inside the map and not on the node itself).
    /// </summary>
    /// <returns>How many nodes were added.</returns>
    public int AddGeneratedPathNode(PathLayer layer, float x, float z)
    {
        Vector2 at = OnTheMap(x, z);
        MapDocument document = RequireMap().Document;
        List<Vector2> places = [at];
        foreach (MapSymmetryTransform transform in PathNodeImages())
        {
            Vector2 image = transform.Apply(at);
            if (image.X >= 0 && image.Y >= 0 && image.X <= document.Width && image.Y <= document.Height
                && places.All(place => Vector2.Distance(place, image) > 0.5f))
            {
                places.Add(image);
            }
        }
        EditPathNodes(places.Count == 1 ? $"Add a {layer.ToString().ToLowerInvariant()} path node" : $"Add {places.Count} {layer.ToString().ToLowerInvariant()} path nodes, mirrored",
            settings => places.Aggregate(settings, (edited, place) => PathNetworkEdits.AddNode(edited, layer, place.X, place.Y)));
        PathNodes.Chosen = PathNodes.NodeAt(at.X, at.Y, 0.01f, layer);
        return places.Count;
    }

    /// <summary>
    /// Moves a node of a generated network (a step of undo); while the symmetry mirrors edits, its
    /// mirror partners to the images of the new place.
    /// </summary>
    public void MoveGeneratedPathNode(PreviewPathNode node, float x, float z)
    {
        Vector2 at = OnTheMap(x, z);
        List<(PreviewPathNode Node, Vector2 To)> moves = [(node, at)];
        foreach (MapSymmetryTransform transform in PathNodeImages())
        {
            if (PathNodePartner(node, transform) is PreviewPathNode partner && moves.All(move => move.Node.Index != partner.Index))
            {
                moves.Add((partner, transform.Apply(at)));
            }
        }
        EditPathNodes(moves.Count == 1 ? "Move a path node" : $"Move {moves.Count} path nodes, mirrored",
            settings => moves.Aggregate(settings, (edited, move) => PathNetworkEdits.MoveNode(edited, move.Node.Layer, move.Node.Node.Ref, move.To.X, move.To.Y)));
        PathNodes.Chosen = PathNodes.NodeAt(at.X, at.Y, 0.01f, node.Layer);
    }

    /// <summary>
    /// Removes a node of a generated network with its links (a step of undo); while the symmetry
    /// mirrors edits, its mirror partners too.
    /// </summary>
    public void RemoveGeneratedPathNode(PreviewPathNode node)
    {
        List<PreviewPathNode> removed = [node];
        foreach (MapSymmetryTransform transform in PathNodeImages())
        {
            if (PathNodePartner(node, transform) is PreviewPathNode partner && removed.All(other => other.Index != partner.Index))
            {
                removed.Add(partner);
            }
        }
        EditPathNodes(removed.Count == 1 ? "Remove a path node" : $"Remove {removed.Count} path nodes, mirrored",
            settings => removed.Aggregate(settings, (edited, gone) => PathNetworkEdits.RemoveNode(edited, gone.Layer, gone.Node.Ref)));
        PathNodes.Chosen = null;
    }

    /// <summary>
    /// Links two nodes of a generated network, or cuts their link (a step of undo); while the
    /// symmetry mirrors edits, their mirror partners in every other part too.
    /// </summary>
    /// <exception cref="ArgumentException">They are of different layers, or the same node.</exception>
    public void LinkGeneratedPathNodes(PreviewPathNode a, PreviewPathNode b, bool cut)
    {
        if (a.Layer != b.Layer)
        {
            throw new ArgumentException($"Nodes of different layers never link ({a.Layer}, {b.Layer})");
        }
        if (a.Index == b.Index)
        {
            throw new ArgumentException("A node does not link to itself");
        }
        List<(PreviewPathNode A, PreviewPathNode B)> links = [(a, b)];
        foreach (MapSymmetryTransform transform in PathNodeImages())
        {
            if (PathNodePartner(a, transform) is PreviewPathNode pa && PathNodePartner(b, transform) is PreviewPathNode pb && pa.Index != pb.Index
                && links.All(link => !((link.A.Index == pa.Index && link.B.Index == pb.Index) || (link.A.Index == pb.Index && link.B.Index == pa.Index))))
            {
                links.Add((pa, pb));
            }
        }
        string what = cut ? "Cut a path link" : "Link two path nodes";
        EditPathNodes(links.Count == 1 ? what : $"{what}, mirrored ({links.Count})",
            settings => links.Aggregate(settings, (edited, link) => PathNetworkEdits.Link(edited, a.Layer, link.A.Node.Ref, link.B.Node.Ref, cut)));
    }

    /// <summary>
    /// The path networks for automation (<c>state.pathNodes</c>): the settings, how the result
    /// stands, the preview per layer, the edits and those without their node.
    /// </summary>
    public JsonObject DescribePathNodes()
    {
        PathNodeSettings settings = Map?.Document.Procedural.PathNodes ?? PathNodeSettings.Default;
        return new JsonObject
        {
            ["layers"] = new JsonArray([.. settings.Layers.Select(layer => (JsonNode)JsonValue.Create(layer.ToString()))]),
            ["spacing"] = settings.Spacing,
            ["spacingUsed"] = Map is null ? null : PathNetworks.SpacingOf(Map.Document, settings),
            ["airSpacing"] = settings.AirSpacing,
            ["smoothing"] = settings.Smoothing,
            ["minimumDistance"] = settings.MinimumDistance,
            ["fill"] = settings.Fill,
            ["replace"] = settings.Replace,
            ["onExport"] = settings.WritesOnExport,
            ["status"] = PathNodes.Status,
            ["showing"] = ShowGeneratedPathNodes,
            ["preview"] = PathNodes.Preview is EditedPathNetwork preview
                ? new JsonArray([.. preview.Network.Layers.Select(layer => (JsonNode)new JsonObject
                {
                    ["layer"] = layer.Layer.ToString(),
                    ["spacing"] = layer.Spacing,
                    ["nodes"] = layer.Nodes.Length,
                    ["links"] = layer.Links.Length,
                    ["added"] = layer.Nodes.Count(node => node.Role == PathNodeRole.Added),
                })])
                : null,
            ["edits"] = new JsonArray([.. settings.Edits.Select(edit => (JsonNode)JsonValue.Create($"{edit.Kind} {edit.Layer} {edit.Node}"
                + (edit.Kind is PathEditKind.Link or PathEditKind.Cut ? $" and {edit.Other}" : "")
                + (edit.Kind == PathEditKind.Move ? $" to ({EditorCulture.Format(edit.ToX)}, {EditorCulture.Format(edit.ToZ)})" : "")))]),
            ["unmatched"] = PathNodes.Preview?.Unmatched.Count ?? 0,
            ["chosen"] = PathNodes.Chosen is PreviewPathNode chosen
                ? new JsonObject { ["layer"] = chosen.Layer.ToString(), ["x"] = chosen.Node.X, ["z"] = chosen.Node.Z, ["role"] = chosen.Node.Role.ToString() }
                : null,
        };
    }

    /// <summary>
    /// The previewed networks as path-node markers for the views to draw (heights 0: the 3D view puts
    /// them on the ground). Made once per preview.
    /// </summary>
    public MarkerSet? PreviewMarkers => PathNodes.PreviewMarkers;

    /// <summary>
    /// Which layers of the generated path networks the views draw: while the Path nodes tool is
    /// active only the layer it edits (the air's grid never covers the land's nodes being edited),
    /// else the path-node layers the Markers mode shows.
    /// </summary>
    public MarkerKinds GeneratedPathKinds => ActiveTool?.Tool is Tools.PathNodeEditTool tool
        ? MarkerEdits.KindOf(tool.Layer)
        : Markers.ShownKinds & MarkerKinds.PathNodes;

    /// <summary>
    /// Whether the Path nodes tool was active when the overlays were last drawn.
    /// </summary>
    private bool _pathToolShown;
}
