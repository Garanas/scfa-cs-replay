using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The handles' side of the core (user story F-11): what the status bar says about the selection,
/// and focusing the 3D camera on it.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// For the status bar: how many things are selected and the size of their bounds, e.g.
    /// <c>3 selected, 12 × 8 ogrids</c>; empty without a selection.
    /// </summary>
    [ObservableProperty]
    public partial string SelectionStatus { get; private set; } = "";

    private void InitializeHandles()
    {
        Selection.Changed += (_, _) => UpdateSelectionStatus();
        DocumentChanged += (_, _) => UpdateSelectionStatus();
    }

    private void UpdateSelectionStatus()
    {
        using Performance.Span measure = Performance.Measure("model.selection-status");
        SelectionFrame? frame = ShownDocument is MapDocument document && Selection.HasAny ? SelectionFrame.Of(document, Selection.Items) : null;
        SelectionStatus = frame is null
            ? ""
            : string.Create(CultureInfo.InvariantCulture, $"{Selection.Count} selected, {2 * frame.HalfSize.X:0.#} × {2 * frame.HalfSize.Y:0.#} ogrids");
    }

    /// <summary>
    /// Sends the 3D camera to see the whole selection, as F does in the 3D view (the FAF map
    /// editor's focus); false when nothing is selected.
    /// </summary>
    public bool FocusOnSelection()
    {
        if (ShownDocument is not MapDocument document || !Selection.HasAny || SelectionFrame.Of(document, Selection.Items) is not SelectionFrame frame)
        {
            return false;
        }
        FocusCamera(frame.Centre.X, frame.Centre.Z, Math.Max(frame.HalfSize.Length() * 1.3f, 12));
        return true;
    }
}
