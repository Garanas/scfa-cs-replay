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
    /// What the top and 3D views lay over the terrain, one grid at a time and owned by the active
    /// mode and tool: the preview of the rules being edited (K-2) while a rules layer of the mode's
    /// stack is chosen (the Masks and Paint modes), else the grid the active painting tool paints (a
    /// stratum's or the water's mask, the chosen mask for tools, the terrain's types), else the
    /// chosen mask for tools while the overlay <c>mask</c> is on, else nothing.
    /// </summary>
    public GridOverlay? ShownGrid =>
        (ShowsRulesPreview ? MaskRules?.PreviewOverlay : null)
        ?? (ActiveTool?.Tool as GridPaintTool)?.Shown
        ?? (ShowMask && Masks.Chosen is string name ? GridOverlay.Mask(PaintTarget.Mask(name)) : null);

    /// <summary>
    /// Whether the preview of the rules being edited may show: while the mode's stack (the Masks
    /// mode's mask, the Paint mode's stratum) has a rules layer chosen, whose rules the inspector shows.
    /// </summary>
    private bool ShowsRulesPreview => SelectedMode?.Definition.Id switch
    {
        "masks" => MaskStack?.IsRules == true,
        "paint" => StratumStack?.IsRules == true,
        _ => false,
    };

    partial void OnShowMaskChanged(bool value)
    {
        // one tint over the terrain at a time: a mask's violet or walkability's red
        if (value)
        {
            ShowWalkability = false;
        }
        RefreshOverlays();
        OnPropertyChanged(nameof(ShownGrid));
    }

    /// <summary>
    /// The tints over the terrain (the overlays <c>mask</c>, <c>walkability</c> and <c>navigation</c>)
    /// each mode had when it was left; a mode not shown yet starts with none.
    /// </summary>
    private readonly Dictionary<string, (bool Mask, bool Walkability, bool Navigation)> _tintsByMode = new Dictionary<string, (bool, bool, bool)>(StringComparer.Ordinal);

    /// <summary>
    /// The mode whose tints show now.
    /// </summary>
    private string? _tintsMode;

    /// <summary>
    /// The tints over the terrain belong to a mode: a mask shown in the Paint mode, walkability in
    /// the Procedural mode, do not stay over the Lighting or Masks mode's views. Leaving a mode keeps
    /// what it showed; entering one shows what it showed when it was left (none the first time).
    /// The other overlays (markers, props, the grid) stay as they are in every mode.
    /// </summary>
    private void FollowModeTints()
    {
        string? mode = SelectedMode?.Definition.Id;
        if (mode == _tintsMode)
        {
            return;
        }
        if (_tintsMode is not null)
        {
            _tintsByMode[_tintsMode] = (ShowMask, ShowWalkability, ShowNavigation);
        }
        _tintsMode = mode;
        (bool mask, bool walkability, bool navigation) = mode is not null && _tintsByMode.TryGetValue(mode, out var kept) ? kept : default;
        // the ones turned off first: one turned on turns the others off
        if (!mask)
        {
            ShowMask = false;
        }
        if (!walkability)
        {
            ShowWalkability = false;
        }
        if (!navigation)
        {
            ShowNavigation = false;
        }
        ShowMask = ShowMask || mask;
        ShowWalkability = ShowWalkability || walkability;
        ShowNavigation = ShowNavigation || navigation;
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
            if (e.PropertyName == nameof(SelectedMode))
            {
                FollowModeTints();
            }
            if (e.PropertyName is nameof(ActiveTool) or nameof(SelectedMode))
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
