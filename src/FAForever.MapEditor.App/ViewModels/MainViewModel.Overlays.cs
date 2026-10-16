using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The grid and the decals as overlays of the views (user story V-3): the grid's lines over the
/// terrain in both views, every <see cref="GridSpacing"/> ogrids. The Decals chip turns
/// <see cref="ShowDecals"/> over: the decals drawn in the 3D view and as faint outlines in the top
/// view, or not at all (and not picked).
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// The spacings the Grid chip offers, in ogrids.
    /// </summary>
    public static IReadOnlyList<int> GridSpacings { get; } = [1, 2, 4, 8, 16, 32, 64, 128];

    /// <summary>
    /// Whether the views draw the grid's lines over the terrain.
    /// </summary>
    [ObservableProperty]
    public partial bool ShowGrid { get; set; }

    partial void OnShowGridChanged(bool value) => RefreshOverlays();

    /// <summary>
    /// The ogrids between the grid's lines (<c>view.gridSpacing</c>); the Grid chip says it.
    /// </summary>
    [ObservableProperty]
    public partial int GridSpacing { get; set; } = 16;

    partial void OnGridSpacingChanged(int value) => RefreshOverlays();

    /// <summary>
    /// Runs <c>view.gridSpacing</c>: what the Grid chip's context menu does.
    /// </summary>
    [RelayCommand]
    private Task ChooseGridSpacing(int spacing) => RunWithAsync("view.gridSpacing", new JsonObject { ["spacing"] = spacing });

    /// <summary>
    /// Sets the grid's spacing and shows the grid.
    /// </summary>
    /// <exception cref="ArgumentException">The spacing is not a whole number of ogrids from 1 to 256.</exception>
    public void SetGridSpacing(int spacing)
    {
        if (spacing is < 1 or > 256)
        {
            throw new ArgumentException($"The grid's spacing lies between 1 and 256 ogrids, not {spacing}");
        }
        GridSpacing = spacing;
        ShowGrid = true;
    }
}
