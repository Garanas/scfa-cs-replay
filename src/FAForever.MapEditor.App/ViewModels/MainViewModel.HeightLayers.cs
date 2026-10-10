using FAForever.MapEditor.App.Tools;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The core's side of the height layers (user stories T-5, T-4): the Terrain panel's list, and the
/// chosen layer as the one the sculpt tool writes into.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// The height layers of the open map and the chosen one (user story T-5).
    /// </summary>
    public HeightLayersViewModel HeightStack { get; private set; } = null!;

    private void InitializeHeightLayers()
    {
        HeightStack = new HeightLayersViewModel(RunWithAsync);
        DocumentChanged += (_, _) => HeightStack.Refresh(ShownDocument);
        MapChanged += (_, _) =>
        {
            HeightStack.Refresh(ShownDocument);
            FollowChosenHeightLayer();
        };
        HeightStack.Changed += (_, _) =>
        {
            FollowChosenHeightLayer();
            Tool<CurveTool>().OnLayerChanged();
        };
    }

    /// <summary>
    /// The chosen layer is the one changes of the heightmap go into (the sculpt tool's).
    /// </summary>
    private void FollowChosenHeightLayer()
    {
        if (Map is not null)
        {
            Map.Session.SculptLayer = HeightStack.Chosen;
        }
    }
}
