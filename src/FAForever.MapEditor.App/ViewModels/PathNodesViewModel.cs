using System.Collections.Immutable;
using System.ComponentModel;
using System.Numerics;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The Procedural panel's part for path networks.
/// </summary>
public sealed record PathNetworkSection() : PanelSection("Path networks");

/// <summary>
/// A node of the previewed network as the views and the edit tool see it.
/// </summary>
/// <param name="Index">Its place in its layer's network.</param>
public sealed record PreviewPathNode(PathLayer Layer, int Index, GeneratedPathNode Node);

/// <summary>
/// The path networks generated from the navigation mesh (user story R-3) in the Procedural mode, a
/// part of its own (its settings, how the result stands, the edits by hand, the preview) so it can
/// be shown in a panel or an inspector alike (<c>Views/ProceduralPanels/PathNodesPanel</c>).
/// </summary>
/// <remarks>
/// The settings are the map's (<see cref="ProceduralSettings.PathNodes"/>); a field changed here runs
/// <c>generate.setPathNodes</c>, a step of undo. The preview is the last generated network with the
/// map's edits applied (<see cref="PathNetworkEdits"/>), made again only when the result or the
/// edits change; the views draw it faintly while the overlay <c>generatedPathNodes</c> is on.
/// </remarks>
public sealed partial class PathNodesViewModel : ObservableObject
{
    private readonly GenerationViewModel _generation;
    private readonly Func<MapDocument?> _document;
    private readonly Func<string, JsonObject, Task> _run;
    private bool _showing;

    public PathNodesViewModel(GenerationViewModel generation, Func<MapDocument?> document, Func<string, JsonObject, Task> run)
    {
        _generation = generation;
        _document = document;
        _run = run;
        generation.PropertyChanged += OnGenerationChanged;
    }

    [ObservableProperty]
    public partial bool Land { get; set; } = true;

    [ObservableProperty]
    public partial bool Water { get; set; } = true;

    [ObservableProperty]
    public partial bool Amphibious { get; set; } = true;

    [ObservableProperty]
    public partial bool Air { get; set; } = true;

    /// <summary>
    /// About how far apart the nodes lie, in ogrids, as typed; empty for the map's size.
    /// </summary>
    [ObservableProperty]
    public partial string Spacing { get; set; } = "";

    /// <summary>
    /// How far apart the air nodes lie, as typed; empty for a grid of 16 by 16.
    /// </summary>
    [ObservableProperty]
    public partial string AirSpacing { get; set; } = "";

    /// <summary>
    /// The rounds of smoothing, as typed.
    /// </summary>
    [ObservableProperty]
    public partial string Smoothing { get; set; } = PathNodeSettings.DefaultSmoothing.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// How close two nodes may lie, as typed; empty for 0.4 of the spacing.
    /// </summary>
    [ObservableProperty]
    public partial string MinimumDistance { get; set; } = "";

    [ObservableProperty]
    public partial bool Fill { get; set; } = true;

    /// <summary>
    /// Whether the export removes the map's own path nodes of the generated layers.
    /// </summary>
    [ObservableProperty]
    public partial bool Replace { get; set; }

    /// <summary>
    /// Whether the export writes the generated networks into the map's markers.
    /// </summary>
    [ObservableProperty]
    public partial bool OnExport { get; set; }

    /// <summary>
    /// How the generated networks stand: never generated, up to date, out of date and why.
    /// </summary>
    [ObservableProperty]
    public partial string Status { get; private set; } = "Not generated for this map";

    [ObservableProperty]
    public partial bool IsStale { get; private set; }

    /// <summary>
    /// What the preview holds, per layer.
    /// </summary>
    [ObservableProperty]
    public partial string Result { get; private set; } = "";

    /// <summary>
    /// How many edits by hand the map keeps, and how many found no node.
    /// </summary>
    [ObservableProperty]
    public partial string EditsText { get; private set; } = "No edits by hand";

    [ObservableProperty]
    public partial bool HasEdits { get; private set; }

    [ObservableProperty]
    public partial bool HasUnmatched { get; private set; }

    /// <summary>
    /// The previewed network: the last generated one with the edits applied; null before the first
    /// generation of this map.
    /// </summary>
    public EditedPathNetwork? Preview { get; private set; }

    /// <summary>
    /// The preview as path-node markers for the views (heights 0), ids from 1 in the order of the
    /// layers and their nodes; null without a preview.
    /// </summary>
    public MarkerSet? PreviewMarkers { get; private set; }

    /// <summary>
    /// The networks as path-node markers, linked both ways.
    /// </summary>
    public static MarkerSet AsMarkers(PathNetwork network)
    {
        List<PathNode> nodes = [];
        int first = 1;
        foreach (PathLayerNetwork layer in network.Layers)
        {
            IReadOnlyList<int>[] neighbours = layer.Neighbours();
            for (int i = 0; i < layer.Nodes.Length; i++)
            {
                nodes.Add(new PathNode
                {
                    Id = first + i,
                    Name = $"{PathNetworks.Prefix(layer.Layer)}{i}",
                    Layer = layer.Layer,
                    Position = new Vector3(layer.Nodes[i].X, 0, layer.Nodes[i].Z),
                    AdjacentTo = [.. neighbours[i].Select(other => first + other)],
                });
            }
            first += layer.Nodes.Length;
        }
        return new MarkerSet { PathNodes = [.. nodes] };
    }

    /// <summary>
    /// The node the edit tool has chosen (it is drawn marked); null for none.
    /// </summary>
    [ObservableProperty]
    public partial PreviewPathNode? Chosen { get; set; }

    /// <summary>
    /// Raised when the preview or the chosen node changes, for the views.
    /// </summary>
    public event EventHandler? PreviewChanged;

    private PathNetwork? _previewOf;
    private ImmutableArray<PathEdit> _editsOf = [];

    /// <summary>
    /// The chosen layers, in order.
    /// </summary>
    public IEnumerable<PathLayer> Layers
    {
        get
        {
            if (Land) yield return PathLayer.Land;
            if (Water) yield return PathLayer.Water;
            if (Amphibious) yield return PathLayer.Amphibious;
            if (Air) yield return PathLayer.Air;
        }
    }

    /// <summary>
    /// Shows the map's settings and makes the preview again when the result or the edits changed.
    /// </summary>
    public void Refresh()
    {
        MapDocument? document = _document();
        PathNodeSettings settings = document?.Procedural.PathNodes ?? PathNodeSettings.Default;
        _showing = true;
        try
        {
            Land = settings.Layers.Contains(PathLayer.Land);
            Water = settings.Layers.Contains(PathLayer.Water);
            Amphibious = settings.Layers.Contains(PathLayer.Amphibious);
            Air = settings.Layers.Contains(PathLayer.Air);
            Spacing = settings.Spacing is float spacing ? EditorCulture.Format(spacing) : "";
            AirSpacing = settings.AirSpacing is float air ? EditorCulture.Format(air) : "";
            Smoothing = settings.Smoothing.ToString(System.Globalization.CultureInfo.InvariantCulture);
            MinimumDistance = settings.MinimumDistance is float minimum ? EditorCulture.Format(minimum) : "";
            Fill = settings.Fill;
            Replace = settings.Replace;
            OnExport = settings.WritesOnExport;
        }
        finally
        {
            _showing = false;
        }

        GeneratedState? state = document is null ? null : _generation.States.FirstOrDefault(s => s.Generator.Id == PathNetworkGenerator.GeneratorId);
        Status = state?.Status switch
        {
            GeneratedStatus.Current => "Up to date",
            GeneratedStatus.Stale => $"Out of date: {string.Join(", ", state.ChangedInputs)} changed",
            _ => "Not generated for this map",
        };
        IsStale = state?.Status == GeneratedStatus.Stale;

        PathNetwork? network = state?.Result?.Value as PathNetwork;
        if (!ReferenceEquals(network, _previewOf) || _editsOf != settings.Edits)
        {
            _previewOf = network;
            _editsOf = settings.Edits;
            Preview = network is null ? null : PathNetworkEdits.Apply(network, settings);
            PreviewMarkers = Preview is null ? null : AsMarkers(Preview.Network);
            Chosen = null;
            PreviewChanged?.Invoke(this, EventArgs.Empty);
        }
        Result = Preview is null ? ""
            : string.Join(", ", Preview.Network.Layers.Select(layer => $"{layer.Layer.ToString().ToLowerInvariant()} {layer.Nodes.Length} nodes, {layer.Links.Length} links"));
        int unmatched = Preview?.Unmatched.Count ?? 0;
        HasEdits = !settings.Edits.IsEmpty;
        HasUnmatched = unmatched > 0;
        EditsText = settings.Edits.IsEmpty ? "No edits by hand"
            : (settings.Edits.Length == 1 ? "1 edit by hand" : $"{settings.Edits.Length} edits by hand") + (unmatched > 0 ? $", {unmatched} without their node" : "");
    }

    partial void OnChosenChanged(PreviewPathNode? value) => PreviewChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// The previewed node nearest to a place within a distance, of a layer or any; null for none.
    /// </summary>
    public PreviewPathNode? NodeAt(float x, float z, float within, PathLayer? layer = null)
    {
        if (Preview is null)
        {
            return null;
        }
        PreviewPathNode? best = null;
        float nearest = within;
        foreach (PathLayerNetwork network in Preview.Network.Layers.Where(network => layer is null || network.Layer == layer))
        {
            int index = network.Nearest(x, z, nearest);
            if (index >= 0)
            {
                nearest = Vector2.Distance(network.Nodes[index].Place, new Vector2(x, z));
                best = new PreviewPathNode(network.Layer, index, network.Nodes[index]);
            }
        }
        return best;
    }

    partial void OnLandChanged(bool value) => Change("layers");

    partial void OnWaterChanged(bool value) => Change("layers");

    partial void OnAmphibiousChanged(bool value) => Change("layers");

    partial void OnAirChanged(bool value) => Change("layers");

    partial void OnSpacingChanged(string value) => Change("spacing");

    partial void OnAirSpacingChanged(string value) => Change("airSpacing");

    partial void OnSmoothingChanged(string value) => Change("smoothing");

    partial void OnMinimumDistanceChanged(string value) => Change("minimumDistance");

    partial void OnFillChanged(bool value) => Change("fill");

    partial void OnReplaceChanged(bool value) => Change("replace");

    partial void OnOnExportChanged(bool value) => Change("onExport");

    /// <summary>
    /// A field changed by the person: the map's settings follow (a step of undo). An empty number
    /// field is the default; one that holds no number is left alone.
    /// </summary>
    private void Change(string field)
    {
        if (_showing || _document() is null)
        {
            return;
        }
        JsonObject arguments = [];
        switch (field)
        {
            case "layers":
                arguments["layers"] = new JsonArray([.. Layers.Select(layer => (JsonNode)JsonValue.Create(layer.ToString()))]);
                break;
            case "spacing" or "airSpacing" or "minimumDistance":
                string text = field switch { "spacing" => Spacing, "airSpacing" => AirSpacing, _ => MinimumDistance };
                if (text.Trim().Length == 0)
                {
                    arguments[field] = 0;
                }
                else if (EditorCulture.TryParse(text, out float number))
                {
                    arguments[field] = number;
                }
                else
                {
                    return;
                }
                break;
            case "smoothing":
                if (!int.TryParse(Smoothing, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int rounds))
                {
                    return;
                }
                arguments["smoothing"] = rounds;
                break;
            case "fill":
                arguments["fill"] = Fill;
                break;
            case "replace":
                arguments["replace"] = Replace;
                break;
            case "onExport":
                arguments["onExport"] = OnExport;
                break;
        }
        _ = _run("generate.setPathNodes", arguments);
    }

    [RelayCommand]
    private Task Generate() => _run("markers.connectPaths", []);

    [RelayCommand]
    private Task ClearEdits() => _run("generate.clearPathEdits", []);

    [RelayCommand]
    private Task ForgetUnmatched() => _run("generate.clearPathEdits", new JsonObject { ["unmatched"] = true });

    private void OnGenerationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GenerationViewModel.Rows) or nameof(GenerationViewModel.States))
        {
            Refresh();
        }
    }
}
