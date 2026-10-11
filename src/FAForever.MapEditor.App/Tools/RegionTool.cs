using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Generation;

namespace FAForever.MapEditor.App.Tools;

/// <summary>
/// The Procedural mode's Connected regions tool: a click chooses the connected region of the
/// navigation mesh under the mouse in the shown layer (it is drawn stronger and marked in the list),
/// and shows the connected regions.
/// It changes nothing on the map.
/// </summary>
public sealed partial class RegionTool(MainViewModel model) : ObservableObject, IMapTool
{
    public double CursorRadius => 0;

    /// <summary>
    /// What the options row shows: the chosen connected region, or how to choose one.
    /// </summary>
    [ObservableProperty]
    public partial string Text { get; private set; } = "Click the map to choose a connected region";

    public void Press(float x, float z)
    {
        if (model.Navigation.Mesh is null)
        {
            Text = "Generate the navigation mesh first (Generate in the panel)";
            model.Status = Text;
            return;
        }
        model.ShowNavigation = true;
        NavRegion? region = model.Navigation.ChooseAt(x, z);
        Text = region is null
            ? $"{model.Navigation.Layer} units cannot go there"
            : new NavRegionRow(region, NavigationViewModel.SwatchOf(region.Id), true).Label;
        model.Status = Text;
    }

    public void Drag(float x, float z)
    {
    }

    public void Release()
    {
    }
}
