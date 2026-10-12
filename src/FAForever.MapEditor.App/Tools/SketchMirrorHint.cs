using FAForever.MapEditor.App.ViewModels;

namespace FAForever.MapEditor.App.Tools;

/// <summary>
/// What the Mirror boxes of the Sketch mode's tools say about the map's symmetry (user story N-1): a tool
/// repeats what it does by the map's symmetry only while the map has one and the views' bar mirrors edits,
/// so the box shows as off, and cannot be changed, while it does not, with a tip that says why.
/// </summary>
internal static class SketchMirrorHint
{
    /// <summary>
    /// Why a tool cannot mirror now; empty while it can.
    /// </summary>
    public static string Why(SymmetryViewModel symmetry) =>
        !symmetry.IsOn ? "The map has no symmetry: choose one in the views' bar to repeat what you draw in the other parts of the map"
        : !symmetry.MirrorsEdits ? "Mirror edits is off in the views' bar: turn that chip on to repeat what you draw by the symmetry"
        : "";

    /// <summary>
    /// The tip of a Mirror box: what it does while it can, else why it cannot.
    /// </summary>
    public static string Tip(SymmetryViewModel symmetry, string what) => Why(symmetry) is { Length: > 0 } why ? why : what;

    /// <summary>
    /// Calls <paramref name="changed"/> when what a Mirror box says may have changed: the symmetry is
    /// chosen or turned off, or the bar's Mirror chip is switched.
    /// </summary>
    public static void Watch(SymmetryViewModel symmetry, Action changed) => symmetry.PropertyChanged += (_, e) =>
    {
        if (e.PropertyName is nameof(SymmetryViewModel.IsOn) or nameof(SymmetryViewModel.IsMirroring))
        {
            changed();
        }
    };
}
