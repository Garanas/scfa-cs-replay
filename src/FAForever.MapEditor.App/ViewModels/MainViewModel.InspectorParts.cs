namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// What the inspector shows of the mode: the parts of the active tool (a part with
/// <see cref="PanelSection.ForTools"/> only for those tools), and the selection only in the modes
/// that work on things on the map.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// The modes whose tools pick things on the map, where the inspector shows the selection.
    /// </summary>
    private static readonly HashSet<string> ModesOfThings = new HashSet<string>(StringComparer.Ordinal) { "map", "markers", "props", "decals", "units" };

    /// <summary>
    /// The mode's parts of the inspector for the active tool: the Paint mode's strata tool shows
    /// the stratum, its masks tool the mask rules.
    /// </summary>
    public IReadOnlyList<PanelSection> InspectorParts =>
        SelectedMode is not ModeItem mode ? []
        : [.. mode.Definition.Inspector.Where(part => part.ForTools is null || (ActiveTool is ToolItem tool && part.ForTools.Contains(tool.Definition.Id)))];

    /// <summary>
    /// Whether the inspector shows the selection: something is selected and the mode works on
    /// things on the map (a mode of the terrain, paint, water or lighting shows its own parts).
    /// </summary>
    public bool InspectsSelection => Selection.HasAny && SelectedMode is ModeItem mode && ModesOfThings.Contains(mode.Definition.Id);

    /// <summary>
    /// Whether the inspector shows the mode's parts: neither an asset of the shelf nor the selection.
    /// </summary>
    public bool InspectsMode => !Assets.HasSelection && !InspectsSelection;

    private void InitializeInspectorParts()
    {
        Selection.Changed += (_, _) => NotifyInspected();
        Assets.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AssetsViewModel.HasSelection))
            {
                OnPropertyChanged(nameof(InspectsMode));
            }
        };
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SelectedMode) or nameof(ActiveTool))
            {
                OnPropertyChanged(nameof(InspectorParts));
                NotifyInspected();
            }
        };
    }

    private void NotifyInspected()
    {
        OnPropertyChanged(nameof(InspectsSelection));
        OnPropertyChanged(nameof(InspectsMode));
    }
}
