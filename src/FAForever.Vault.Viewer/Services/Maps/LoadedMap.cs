using System.Numerics;
using FAForever.FileFormats.Map;

namespace FAForever.Vault.Viewer.Services.Maps;

/// <summary>
/// A vault map read in the browser: its three files parsed, the navigational mesh generated and
/// the distances between start positions and mass spots measured. The thresholds of the extractor
/// roles come from the URL, so the roles are classified on the page from <see cref="Extractors"/>.
/// </summary>
public sealed record LoadedMap(
    string Folder,
    MapScenario Scenario,
    MapSave Save,
    Scmap Scmap,
    MapNavigation Navigation,
    MapArea? PlayableArea,
    IReadOnlyList<MapMarker> Starts,
    ExtractorDistances Extractors,
    MapSymmetryKind? Symmetry)
{
    public IReadOnlyList<MapMarker> Mass { get; } = [.. Save.GetMarkers("Mass")];

    public IReadOnlyList<MapMarker> Hydrocarbons { get; } = [.. Save.GetMarkers("Hydrocarbon")];

    /// <summary>The name players know: the scenario's, else one made from the folder.</summary>
    public string Name => Scenario.Name is { Length: > 0 } name ? name : MapPreviews.DisplayName(Folder) ?? Folder;

    public int Width => Scmap.Width;

    public int Height => Scmap.Height;

    /// <summary>
    /// The two start positions closest to each other along the routes (the first two of a two player
    /// map); null with fewer than two.
    /// </summary>
    public (int A, int B)? ClosestStarts { get; init; }

    private readonly Dictionary<NavLayer, NavRoute?> routes = [];

    /// <summary>
    /// The shortest route between <see cref="ClosestStarts"/> over a layer, or null where there is none.
    /// Searched the first time it is asked for: on a 20 km map one search takes a moment in the browser.
    /// </summary>
    public NavRoute? RouteFor(NavLayer layer)
    {
        if (!routes.TryGetValue(layer, out NavRoute? route))
        {
            route = ClosestStarts is var (a, b) ? NavPaths.FindRoute(Navigation[layer], Starts[a].Position, Starts[b].Position) : null;
            routes[layer] = route;
        }
        return route;
    }

    /// <summary>The straight line between <see cref="ClosestStarts"/>, in ogrids.</summary>
    public float StraightDistance => ClosestStarts is var (a, b)
        ? Vector2.Distance(new Vector2(Starts[a].Position.X, Starts[a].Position.Z), new Vector2(Starts[b].Position.X, Starts[b].Position.Z))
        : 0;

    /// <summary>The share of the map below the water surface, 0 to 1.</summary>
    public double UnderWater { get; init; }

    /// <summary>The share of ogrids with a side too steep for land units, 0 to 1.</summary>
    public double Steep { get; init; }

    /// <summary>The route of the layer that links the start positions, if any.</summary>
    public NavRoute? ConnectingRoute => Extractors.Layer is { } layer ? RouteFor(layer) : null;
}
