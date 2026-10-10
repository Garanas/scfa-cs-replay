using CommunityToolkit.Mvvm.ComponentModel;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The objects on the map in the views: which kinds show (the overlays <c>props</c> and
/// <c>units</c>; the markers are <see cref="ShowMarkers"/> and <see cref="ShowPathNodes"/>).
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// Whether the 3D view shows the props.
    /// </summary>
    [ObservableProperty]
    public partial bool ShowProps { get; set; } = true;

    /// <summary>
    /// Whether the 3D view shows the units.
    /// </summary>
    [ObservableProperty]
    public partial bool ShowUnits { get; set; } = true;

    partial void OnShowPropsChanged(bool value) => RefreshOverlays();

    partial void OnShowUnitsChanged(bool value) => RefreshOverlays();
}
