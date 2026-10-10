using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// A line of the Procedural mode's list of regions of the navigation mesh.
/// </summary>
/// <param name="Swatch">The index of its colour in the overlay's palette (<see cref="NavigationViewModel.PaletteSize"/>), from 0.</param>
public sealed record NavRegionRow(NavRegion Region, int Swatch, bool IsChosen)
{
    public string Name => $"Region {Region.Id}";

    public string Size => $"{Region.Area.ToString("0.00", CultureInfo.InvariantCulture)} km²";

    public string Starts => Region.Starts.Count == 0 ? "no start" : string.Join(", ", Region.Starts);

    public string Deposits => $"{Region.Mass} mass, {Region.Hydrocarbons} hydrocarbon";

    public string Label => $"{Name}: {Size}, {Starts}, {Deposits}";
}

/// <summary>
/// A line of the Procedural mode's list of routes between starts.
/// </summary>
public sealed record NavRouteRow(NavStartRoute Route)
{
    public string Text => Route.Route is null
        ? $"{Route.From} to {Route.To}: no way over this layer"
        : $"{Route.From} to {Route.To}: {Route.Route.Length.ToString("0", CultureInfo.InvariantCulture)} ogrids, {((Route.Detour ?? 1) * 100 - 100).ToString("0", CultureInfo.InvariantCulture)} % longer than straight";
}

/// <summary>
/// The navigation mesh in the Procedural mode: the last generated mesh of the open map, the layer
/// shown (its regions coloured in the views, the overlay <c>navigation</c>), the list of that layer's
/// regions with their starts and deposits, the routes between starts, and the region chosen (a click
/// with the Regions tool, or the list).
/// </summary>
public sealed partial class NavigationViewModel : ObservableObject
{
    /// <summary>
    /// The regions listed, the largest first; the rest are counted.
    /// </summary>
    public const int ListedRegions = 12;

    /// <summary>
    /// How many colours the overlay takes the regions in, in turn (<c>MapRegion1Brush</c> to <c>MapRegion8Brush</c>).
    /// </summary>
    public const int PaletteSize = 8;

    private readonly GenerationViewModel _generation;
    private readonly Func<MapDocument?> _document;

    public NavigationViewModel(GenerationViewModel generation, Func<MapDocument?> document)
    {
        _generation = generation;
        _document = document;
        generation.PropertyChanged += OnGenerationChanged;
    }

    /// <summary>
    /// The layers one can show, in the order of the panel's choice.
    /// </summary>
    public IReadOnlyList<NavLayer> Layers => NavigationMesh.Layers;

    /// <summary>
    /// The layer shown and listed: land unless chosen.
    /// </summary>
    [ObservableProperty]
    public partial NavLayer Layer { get; set; } = NavLayer.Land;

    /// <summary>
    /// The choices of routes, for the panel.
    /// </summary>
    public IReadOnlyList<RoutePairs> RouteChoices { get; } = [RoutePairs.Nearest, RoutePairs.All, RoutePairs.None];

    /// <summary>
    /// The last generated mesh, or null.
    /// </summary>
    public NavigationMesh? Mesh { get; private set; }

    /// <summary>
    /// The shown layer's grid of <see cref="Mesh"/>.
    /// </summary>
    public NavGrid? Grid => Mesh?.Navigation[Layer];

    /// <summary>
    /// The region chosen (by its label's id in the shown layer), drawn stronger; null for none.
    /// </summary>
    [ObservableProperty]
    public partial int? ChosenRegion { get; set; }

    public IReadOnlyList<NavRegionRow> Regions { get; private set; } = [];

    public IReadOnlyList<NavRouteRow> Routes { get; private set; } = [];

    /// <summary>
    /// For the panel, e.g. <c>Land: 3 regions; 12 smaller ones not listed. Starts on no region: ARMY_2</c>.
    /// </summary>
    public string Summary { get; private set; } = "Not generated yet: generate it to see the regions and routes.";

    /// <summary>
    /// The routes of the shown layer that have a way, for the views.
    /// </summary>
    public IReadOnlyList<NavRoute> RouteLines => [.. (Mesh?.RoutesOf(Layer) ?? []).Select(route => route.Route).OfType<NavRoute>()];

    /// <summary>
    /// Raised when the views draw the mesh again: another result, layer or chosen region.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// The colour of a region in the overlay's palette, from 0.
    /// </summary>
    public static int SwatchOf(int id) => (id - 1) % PaletteSize;

    /// <summary>
    /// Chooses the region of the shown layer at a place of the map.
    /// </summary>
    /// <returns>The region, or null where units of the layer cannot go (and nothing is chosen).</returns>
    public NavRegion? ChooseAt(float x, float z)
    {
        NavRegion? region = Mesh?.RegionAt(Layer, x, z);
        ChosenRegion = region?.Id;
        return region;
    }

    partial void OnLayerChanged(NavLayer value)
    {
        ChosenRegion = null;
        Show();
    }

    partial void OnChosenRegionChanged(int? value) => Show();

    private void OnGenerationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(GenerationViewModel.States))
        {
            return;
        }
        NavigationMesh? kept = _generation.Results.ResultOf(NavigationMeshGenerator.GeneratorId)?.Value as NavigationMesh;
        if (!ReferenceEquals(kept, Mesh))
        {
            Mesh = kept;
            ChosenRegion = null;
            Show();
        }
    }

    private void Show()
    {
        if (Mesh is not NavigationMesh mesh)
        {
            Regions = [];
            Routes = [];
            Summary = "Not generated yet: generate it to see the regions and routes.";
        }
        else
        {
            IReadOnlyList<NavRegion> all = mesh.RegionsOf(Layer);
            Regions = [.. all.Take(ListedRegions).Select(region => new NavRegionRow(region, SwatchOf(region.Id), region.Id == ChosenRegion))];
            Routes = [.. mesh.RoutesOf(Layer).Select(route => new NavRouteRow(route))];
            List<string> parts = [all.Count == 1 ? $"{Layer}: 1 region" : $"{Layer}: {all.Count} regions"];
            if (all.Count > ListedRegions)
            {
                parts.Add($"{all.Count - ListedRegions} smaller ones not listed");
            }
            if (!mesh.Navigation.HasWater && Layer != NavLayer.Water)
            {
                parts.Add("without water every kind moves as land units");
            }
            IReadOnlyList<string> stranded = _document() is MapDocument document ? mesh.StrandedOf(Layer, document.Markers.Spawns) : [];
            string summary = string.Join("; ", parts) + ".";
            Summary = stranded.Count == 0 ? summary : $"{summary} Starts in no region (units cannot leave them): {string.Join(", ", stranded)}.";
        }
        OnPropertyChanged(nameof(Mesh));
        OnPropertyChanged(nameof(Grid));
        OnPropertyChanged(nameof(Regions));
        OnPropertyChanged(nameof(Routes));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(RouteLines));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
