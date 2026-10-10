using CommunityToolkit.Mvvm.ComponentModel;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// What a turn or a scale with the handles (or Q and E) goes about: the selection's centre, or
/// each thing's own.
/// </summary>
public enum HandlePivot
{
    /// <summary>
    /// The middle of the selection's box: the things go round it, or spread from it.
    /// </summary>
    Selection,

    /// <summary>
    /// Each thing about its own centre.
    /// </summary>
    Each,
}

/// <summary>
/// A grid a move snaps to while shift is held, in ogrids.
/// </summary>
public sealed record HandleSnap(float Step, string Title)
{
    public override string ToString() => Title;
}

/// <summary>
/// How the handles of the selection work, shared by the select tools of every mode (user story
/// F-11): whether they show, what turns and scales go about, and the grid a move snaps to with
/// shift (turns snap to 15 degrees, scales to tenths).
/// </summary>
public sealed partial class HandleSettings : ObservableObject
{
    /// <summary>
    /// The step a turn snaps to with shift, in degrees.
    /// </summary>
    public const float SnapDegrees = 15;

    /// <summary>
    /// The step a scale snaps to with shift.
    /// </summary>
    public const float ScaleSnap = 0.1f;

    public IReadOnlyList<HandleSnap> Snaps { get; } =
    [
        new HandleSnap(0.5f, "½ ogrid"),
        new HandleSnap(1, "1 ogrid"),
        new HandleSnap(4, "4 ogrids"),
    ];

    public IReadOnlyList<HandlePivot> Pivots { get; } = Enum.GetValues<HandlePivot>();

    public HandleSettings()
    {
        Snap = Snaps[1];
    }

    /// <summary>
    /// Whether the views draw the handles of the selection (and the select tools take them).
    /// </summary>
    [ObservableProperty]
    public partial bool Show { get; set; } = true;

    /// <summary>
    /// What turns and scales go about.
    /// </summary>
    [ObservableProperty]
    public partial HandlePivot Pivot { get; set; }

    /// <summary>
    /// The grid a move snaps to with shift.
    /// </summary>
    [ObservableProperty]
    public partial HandleSnap Snap { get; set; }

    /// <summary>
    /// Raised when a setting changes: the views draw the handles again.
    /// </summary>
    public event EventHandler? Changed;

    partial void OnShowChanged(bool value) => Changed?.Invoke(this, EventArgs.Empty);

    partial void OnPivotChanged(HandlePivot value) => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// The snap whose step is nearest a number of ogrids.
    /// </summary>
    public HandleSnap SnapOf(float step) => Snaps.MinBy(snap => MathF.Abs(snap.Step - step))!;
}
