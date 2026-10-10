using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.App.Rendering;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tools;

/// <summary>
/// What the Walkability tool marks.
/// </summary>
public enum WalkabilityMark
{
    /// <summary>
    /// Land units must not pass.
    /// </summary>
    Blocked,

    /// <summary>
    /// Land units may pass.
    /// </summary>
    Open,
}

/// <summary>
/// Marks by hand where land units must be blocked or may pass (user story T-7): a brush that sets
/// the ogrids it covers at least half to blocked or open, or with Erase to unmarked (the library's
/// <see cref="PaintTarget.WalkabilityMarks"/>, <see cref="WalkabilityMarks"/>). Generating
/// walkability takes the marks over what the heights and terrain types make, so walkability goes
/// out of date with each stroke; the export writes blocked ogrids as a blocking terrain type. The
/// top view shows the marks while the tool is active: blocked red, open green.
/// </summary>
public sealed partial class WalkabilityMarkTool : GridPaintTool
{
    /// <summary>
    /// The colours of the marks over the terrain: none clear, blocked red, open green (as
    /// walkability's blocked and opened ogrids).
    /// </summary>
    public static IReadOnlyList<uint> Palette { get; } = [0x00000000, 0xFFE0453A, 0xFF4CC46A, .. Enumerable.Repeat(0x00000000u, 253)];

    private static readonly GridOverlay Overlay = new GridOverlay("walkability marks", document => document.WalkabilityMarks, Palette);

    private TiledBuffer<byte>? _counted;
    private (int Blocked, int Open) _counts;

    public WalkabilityMarkTool(MainViewModel model) : base(model)
    {
        Falloff = BrushFalloff.Sharp;
        Radius = 4;
    }

    public override string PresetName => "walkability";

    public IReadOnlyList<WalkabilityMark> Marks { get; } = Enum.GetValues<WalkabilityMark>();

    /// <summary>
    /// What a stroke marks, unless it erases.
    /// </summary>
    [ObservableProperty]
    public partial WalkabilityMark Mark { get; set; } = WalkabilityMark.Blocked;

    public string Help => "Paint where land units must be blocked or may pass; generate walkability again to see it, the export blocks with a terrain type";

    public override PaintTarget? Target => Model.HasMap ? PaintTarget.WalkabilityMarks : null;

    public override GridOverlay? Shown => Model.HasMap ? Overlay : null;

    protected override bool SetsCodes => true;

    protected override byte Code => Erase ? WalkabilityMarks.None : Mark == WalkabilityMark.Open ? WalkabilityMarks.Open : WalkabilityMarks.Blocked;

    protected override string Describe(PaintTarget target) => Erase ? "Erase walkability marks" : $"Mark walkability {Mark.ToString().ToLowerInvariant()}";

    /// <summary>
    /// Its settings and the map's marks, for automation's <c>state</c>.
    /// </summary>
    public JsonObject Describe()
    {
        TiledBuffer<byte>? marks = Model.ShownDocument?.WalkabilityMarks;
        if (!ReferenceEquals(marks, _counted))
        {
            _counted = marks;
            _counts = WalkabilityMarks.Count(marks);
        }
        return new JsonObject
        {
            ["mark"] = Mark.ToString(),
            ["erase"] = Erase,
            ["radius"] = Radius,
            ["blocked"] = _counts.Blocked,
            ["open"] = _counts.Open,
        };
    }
}
