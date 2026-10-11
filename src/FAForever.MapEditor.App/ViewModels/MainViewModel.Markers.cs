using System.ComponentModel;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Generation;

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
        // the list's headings Markers and Path nodes carry the check boxes of their overlays
        Markers.UseOverlays(Overlays);
        DocumentChanged += (_, _) => Markers.Refresh(ShownDocument);
        MapChanged += (_, _) => Markers.Refresh(ShownDocument);
        PropertyChanged += OnMarkerToolChosen;

        // generated path networks (R-3; the Procedural mode's PathNodes part), markers presets (F-10), the adaptive script's groups (R-5)
        Generation.Register(new PathNetworkGenerator(), "markers.connectPaths");
        Presets.Register(MarkerPresetTool.Kind(this));
        DocumentChanged += (_, _) => Markers.RefreshOptionalGroups(ShownDocument);
        MapChanged += (_, _) => Markers.RefreshOptionalGroups(ShownDocument);
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
