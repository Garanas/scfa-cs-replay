using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
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
        AutoGeneration.Allow(PathNetworkGenerator.GeneratorId);
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
    /// Adds a node by hand to a generated network (a step of undo), linked to its nearest generated
    /// neighbours, and chooses it.
    /// </summary>
    public void AddGeneratedPathNode(PathLayer layer, float x, float z)
    {
        Vector2 at = OnTheMap(x, z);
        EditPathNodes($"Add a {layer.ToString().ToLowerInvariant()} path node", settings => PathNetworkEdits.AddNode(settings, layer, at.X, at.Y));
        PathNodes.Chosen = PathNodes.NodeAt(at.X, at.Y, 0.01f, layer);
    }

    /// <summary>
    /// Moves a node of a generated network (a step of undo).
    /// </summary>
    public void MoveGeneratedPathNode(PreviewPathNode node, float x, float z)
    {
        Vector2 at = OnTheMap(x, z);
        EditPathNodes("Move a path node", settings => PathNetworkEdits.MoveNode(settings, node.Layer, node.Node.Ref, at.X, at.Y));
        PathNodes.Chosen = PathNodes.NodeAt(at.X, at.Y, 0.01f, node.Layer);
    }

    /// <summary>
    /// Removes a node of a generated network with its links (a step of undo).
    /// </summary>
    public void RemoveGeneratedPathNode(PreviewPathNode node)
    {
        EditPathNodes("Remove a path node", settings => PathNetworkEdits.RemoveNode(settings, node.Layer, node.Node.Ref));
        PathNodes.Chosen = null;
    }

    /// <summary>
    /// Links two nodes of a generated network, or cuts their link (a step of undo).
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
        EditPathNodes(cut ? "Cut a path link" : "Link two path nodes", settings => PathNetworkEdits.Link(settings, a.Layer, a.Node.Ref, b.Node.Ref, cut));
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
