using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.App.Rendering;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.Project.Editing;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The core's side of painting the terrain's grids (user stories S-1, T-6, W-2, K-1): the masks
/// for tools, and the grid the top view lays over the terrain.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// The masks for tools of the open map and the one chosen in the Paint panel (user story K-1).
    /// </summary>
    public MasksViewModel Masks { get; } = new MasksViewModel();

    /// <summary>
    /// Whether the top view shows the chosen mask for tools (the overlay <c>mask</c>) while no
    /// painting tool shows its own grid.
    /// </summary>
    [ObservableProperty]
    public partial bool ShowMask { get; set; }

    /// <summary>
    /// What the top view lays over the terrain: the preview of the mask rules being edited (K-2),
    /// else the grid the active painting tool paints (a
    /// layer's or the water's mask, a mask for tools, the terrain's types), else the chosen mask
    /// for tools while the overlay <c>mask</c> is on, else nothing.
    /// </summary>
    public GridOverlay? ShownGrid =>
        MaskRules?.PreviewOverlay
        ?? (ActiveTool?.Tool as GridPaintTool)?.Shown
        ?? (ShowMask && Masks.Chosen is string name ? GridOverlay.Mask(PaintTarget.Mask(name)) : null);

    partial void OnShowMaskChanged(bool value)
    {
        RefreshOverlays();
        OnPropertyChanged(nameof(ShownGrid));
    }

    private void InitializePainting()
    {
        DocumentChanged += (_, _) => Masks.Refresh(ShownDocument);
        Masks.Changed += (_, _) =>
        {
            Tool<MaskPaintTool>().OnMaskChanged();
            OnPropertyChanged(nameof(ShownGrid));
        };
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ActiveTool))
            {
                OnPropertyChanged(nameof(ShownGrid));
            }
        };
        foreach (GridPaintTool tool in _tools.Values.OfType<GridPaintTool>())
        {
            tool.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(GridPaintTool.Shown) && ActiveTool?.Tool == tool)
                {
                    OnPropertyChanged(nameof(ShownGrid));
                }
            };
        }
    }
}
