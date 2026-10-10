using System.ComponentModel;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.Project.Editing;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The Markers mode's side of the core (user stories R-1 to R-6): which kinds of markers the views
/// show and picking takes, and the panel's chains and adaptive map.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// The kinds of markers shown, the chains and the adaptive map of the Markers panel.
    /// </summary>
    public MarkersViewModel Markers { get; }

    /// <summary>
    /// The kinds of markers the views draw and a click may pick: those the panel shows
    /// (<see cref="MarkersViewModel.ShownKinds"/>) of what the overlays Markers and Path nodes show.
    /// </summary>
    public MarkerKinds ShownMarkerKinds => Markers.ShownKinds
        & ((ShowMarkers ? MarkerKinds.All & ~MarkerKinds.PathNodes : MarkerKinds.None) | (ShowPathNodes ? MarkerKinds.PathNodes : MarkerKinds.None));

    private void InitializeMarkers()
    {
        Markers.Changed += (_, _) => RefreshOverlays();
        DocumentChanged += (_, _) => Markers.Refresh(ShownDocument);
        MapChanged += (_, _) => Markers.Refresh(ShownDocument);
        PropertyChanged += OnMarkerToolChosen;
    }

    /// <summary>
    /// The path tool works on what it shows: choosing it shows the path nodes, and its layer.
    /// </summary>
    private void OnMarkerToolChosen(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ActiveTool) && ActiveTool?.Tool is PathTool path)
        {
            ShowPathNodes = true;
            Markers.Show(MarkerEdits.KindOf(path.Layer), true);
        }
    }
}
