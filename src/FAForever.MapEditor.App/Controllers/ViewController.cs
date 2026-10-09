using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.ViewModels;

namespace FAForever.MapEditor.App.Controllers;

/// <summary>
/// The View menu: which view shows, what it shows over the terrain, the dock and the palette.
/// </summary>
public sealed class ViewController(MainViewModel model)
{
    [EditorCommand("view.top", "Top view", Menu = "View", Gesture = "Ctrl+D1", Story = "V-1")]
    public void Top() => model.ViewIndex = 0;

    [EditorCommand("view.3d", "3D view", Menu = "View", Gesture = "Ctrl+D2", Story = "V-2")]
    public void ThreeD() => model.ViewIndex = 1;

    [Incomplete("V-4", "The top and 3D views side by side")]
    [EditorCommand("view.sideBySide", "Side by side", Menu = "View", Gesture = "Ctrl+D3", Story = "V-4")]
    public void SideBySide()
    {
    }

    [EditorCommand("view.fit", "Fit the map", Menu = "View", Gesture = "Ctrl+D0", Story = "V-1")]
    public void Fit() => model.RequestFit();

    [EditorCommand("view.palette", "Command palette", Menu = "View", Gesture = "Ctrl+K", Story = "F-2", SeparatorBefore = true)]
    public void Palette() => model.OpenPalette();

    [EditorCommand("view.problems", "Problems", Menu = "View", Story = "F-14", SeparatorBefore = true)]
    public void Problems() => model.DockIndex = 0;

    [EditorCommand("view.history", "History", Menu = "View", Story = "F-4")]
    public void History() => model.DockIndex = 1;

    [EditorCommand("view.assets", "Assets", Menu = "View", Story = "F-8")]
    public void Assets() => model.DockIndex = 2;

    [EditorCommand("view.console", "Console", Menu = "View", Story = "F-1")]
    public void Console() => model.DockIndex = 3;

    [EditorCommand("view.overlay.markers", "Show markers", Menu = "View", Story = "R-6", SeparatorBefore = true)]
    public void ToggleMarkers() => model.ShowMarkers = !model.ShowMarkers;

    [EditorCommand("view.overlay.pathNodes", "Show path nodes", Menu = "View", Story = "R-6")]
    public void TogglePathNodes() => model.ShowPathNodes = !model.ShowPathNodes;

    [EditorCommand("view.overlay.camera", "Show the 3D camera", Menu = "View", Story = "V-1")]
    public void ToggleCamera() => model.ShowCamera = !model.ShowCamera;

    [Incomplete("T-7", "Show where land units can and cannot go, and what changed")]
    [EditorCommand("view.overlay.walkability", "Show walkability", Menu = "View", Story = "T-7")]
    public void ToggleWalkability()
    {
    }

    [Incomplete("K-1", "Show a mask over the terrain")]
    [EditorCommand("view.overlay.mask", "Show a mask", Menu = "View", Story = "K-1")]
    public void ToggleMask()
    {
    }

    [Incomplete("F-12", "Show the symmetry's guides")]
    [EditorCommand("view.overlay.symmetry", "Show symmetry", Menu = "View", Story = "F-12")]
    public void ToggleSymmetry()
    {
    }

    [Incomplete("V-3", "Show a grid over the terrain")]
    [EditorCommand("view.overlay.grid", "Show the grid", Menu = "View", Story = "V-3")]
    public void ToggleGrid()
    {
    }

    [Incomplete("M-5", "Show the areas")]
    [EditorCommand("view.overlay.areas", "Show areas", Menu = "View", Story = "M-5")]
    public void ToggleAreas()
    {
    }

    [Incomplete("P-1", "Show the props in the top view")]
    [EditorCommand("view.overlay.props", "Show props", Menu = "View", Story = "P-1")]
    public void ToggleProps()
    {
    }

    [Incomplete("U-2", "Show the units")]
    [EditorCommand("view.overlay.units", "Show units", Menu = "View", Story = "U-2")]
    public void ToggleUnits()
    {
    }
}
