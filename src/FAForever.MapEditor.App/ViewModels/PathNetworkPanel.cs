using System.Globalization;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.Project.Files;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The Markers panel's generated path networks (user story R-3).
/// </summary>
public sealed record PathNetworkSection() : PanelSection("Path networks");

/// <summary>
/// What the Markers panel's "Path networks" holds (user story R-3): the layers to generate, the
/// spacing (empty: by the map's size), whether the layers' own nodes go, how the last result
/// stands, and the button that runs <c>markers.connectPaths</c> with them.
/// </summary>
public sealed partial class PathNetworkPanel(Func<string, JsonObject, Task> run) : ObservableObject
{
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
    /// Whether generating removes the layers' own nodes first; otherwise it only adds where none are.
    /// </summary>
    [ObservableProperty]
    public partial bool Replace { get; set; }

    /// <summary>
    /// How the generated networks stand: never generated, up to date, out of date and why.
    /// </summary>
    [ObservableProperty]
    public partial string Status { get; set; } = "Not generated for this map";

    [ObservableProperty]
    public partial bool IsStale { get; set; }

    /// <summary>
    /// What the last generation made, per layer.
    /// </summary>
    [ObservableProperty]
    public partial string Result { get; set; } = "";

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
    /// The spacing typed, or null when the field is empty or holds no number.
    /// </summary>
    public float? SpacingValue => float.TryParse(Spacing.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) && float.IsFinite(value) ? value : null;

    /// <summary>
    /// Shows how the generator's result stands (the generated results' line of it).
    /// </summary>
    internal void Show(GeneratedRow? row)
    {
        Status = row?.Status ?? "Not generated for this map";
        IsStale = row?.IsStale ?? false;
        if (row is null || row.State == Project.Generation.GeneratedStatus.Missing)
        {
            Result = "";
        }
    }

    [RelayCommand]
    private Task Generate() => run("markers.connectPaths", new JsonObject
    {
        ["layers"] = new JsonArray([.. Layers.Select(layer => (JsonNode)JsonValue.Create(layer.ToString()))]),
        ["spacing"] = SpacingValue,
        ["replace"] = Replace,
    });
}
