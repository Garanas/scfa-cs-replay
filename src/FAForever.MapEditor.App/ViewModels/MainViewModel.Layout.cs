using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The window's layout and toolbar (user stories V-4, F-1): the core's side of
/// <see cref="LayoutViewModel"/>.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// Which panels show, their sizes, the views side by side and the toolbar (user stories V-4, F-1).
    /// </summary>
    public LayoutViewModel Layout { get; private set; } = null!;

    /// <summary>
    /// Whether the top view shows: alone or beside the 3D view.
    /// </summary>
    public bool ShowsTopView => View is MapViewTab.Top or MapViewTab.SideBySide;

    /// <summary>
    /// Whether the 3D view shows: alone or beside the top view.
    /// </summary>
    public bool Shows3DView => View is MapViewTab.ThreeD or MapViewTab.SideBySide;

    private void InitializeLayout()
    {
        Layout = new LayoutViewModel(Commands, Logger("Editor"));
        // after the commands made from data, which the toolbar may name too
        Layout.SetToolbar(null);
        Layout.View = View;
    }

    /// <summary>
    /// Takes the layout the user's settings remember, with the view that showed.
    /// </summary>
    private void LoadLayout(UserSettings settings)
    {
        Layout.Load(settings);
        if (Layout.RememberedView is MapViewTab view)
        {
            View = view;
        }
    }

    partial void OnViewChanged(MapViewTab value)
    {
        if (Layout is not null)
        {
            Layout.View = value;
        }
        OnPropertyChanged(nameof(ShowsTopView));
        OnPropertyChanged(nameof(Shows3DView));
    }

    /// <summary>
    /// Takes a command off the toolbar, as its button's context menu does.
    /// </summary>
    [RelayCommand]
    private Task RemoveFromToolbar(string id) => RunWithAsync("view.removeFromToolbar", new JsonObject { ["id"] = id });
}
