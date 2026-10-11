using System.Globalization;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// A group of the curve inspector's settings (user story T-4) shown only for the curves whose
/// profile uses it: a cliff's face for cliffs, a road's mode for roads, line only for closed curves.
/// </summary>
public sealed partial class CurveSettingGroup(SettingGroup group, Func<HeightCurve, bool> shows, bool showsTitle = true) : ObservableObject
{
    public SettingGroup Group => group;

    /// <summary>
    /// Whether the heading shows; a group that continues the one above has none.
    /// </summary>
    public bool ShowsTitle => showsTitle;

    /// <summary>
    /// Whether the chosen curve uses it.
    /// </summary>
    [ObservableProperty]
    public partial bool IsShown { get; private set; }

    /// <summary>
    /// Shows the values of the chosen curve (hidden: none, and no work).
    /// </summary>
    public void Refresh(MapDocument? document, HeightCurve? curve)
    {
        IsShown = curve is not null && shows(curve);
        group.Refresh(IsShown ? document : null);
    }
}

/// <summary>
/// The curve inspector's settings (user story T-4): every setting of a curve in groups, each run
/// through <c>terrain.setCurve</c>; typing in a field again is one step of undo, as in the other
/// fields of the inspector.
/// </summary>
public sealed partial class HeightLayersViewModel
{
    /// <summary>
    /// The choices of the fade's shape and what <c>terrain.setCurve</c> is given for each.
    /// </summary>
    public static IReadOnlyList<(string Title, string Value)> Shapes { get; } =
    [
        ("Profile's", "default"), ("Smooth", "smooth"), ("Linear", "linear"), ("Ease in", "easeIn"), ("Ease out", "easeOut"), ("Terraced", "terraced"),
    ];

    /// <summary>
    /// The choices of a road's mode and what <c>terrain.setCurve</c> is given for each.
    /// </summary>
    public static IReadOnlyList<(string Title, string Value)> RoadModes { get; } = [("Cut and fill", "both"), ("Cut only", "cutOnly"), ("Fill only", "fillOnly")];

    /// <summary>
    /// Whether <c>terrain.setCurve</c> takes <c>blocksWalkability</c> (cliff faces that block
    /// walkability): the cliff group's check box shows only then.
    /// </summary>
    public static bool HasCliffWalkability { get; } = typeof(SetHeightCurve).GetProperty("BlocksWalkability") is not null;

    /// <summary>
    /// The groups of the chosen curve's settings after <see cref="Curve"/>, in the inspector's order.
    /// </summary>
    public IReadOnlyList<CurveSettingGroup> CurveGroups { get; private set; } = [];

    /// <summary>
    /// The chosen curve's points as a table, the chosen points, the grades and the buttons that
    /// work on points.
    /// </summary>
    public CurveNodesViewModel Nodes { get; private set; } = null!;

    /// <summary>
    /// How the variation's heights and widths are shown: absolute while the points share one, else
    /// as offsets.
    /// </summary>
    [ObservableProperty]
    public partial string VariationNote { get; private set; } = "";

    private HeightCurve CurveNow(MapDocument? document) =>
        document is not null && HeightLayers.Find(document, Chosen)?.Curve is HeightCurve curve ? curve : new HeightCurve();

    /// <summary>
    /// The height the points share, or null when they differ.
    /// </summary>
    internal static float? SharedHeight(HeightCurve curve) =>
        curve.Points.Length > 0 && curve.Points.All(point => point.Height == curve.Points[0].Height) ? curve.Points[0].Height : null;

    /// <summary>
    /// The width the points share (none has its own), or null.
    /// </summary>
    internal static float? SharedWidth(HeightCurve curve) => curve.Points.All(point => point.Width is null) ? curve.Width : null;

    private SettingGroup MakeCurveGroups(Func<string, JsonObject, Task> run, Func<JsonObject> chosen)
    {
        HeightCurve CurveOf(MapDocument d) => HeightLayers.Find(d, Chosen)?.Curve ?? new HeightCurve();
        const string set = "terrain.setCurve";
        SettingGroup main = new SettingGroup("Curve",
        [
            new ChoiceSetting("Profile", set, "profile", Profiles, d => CurveOf(d).Profile.ToString(), run, chosen)
            {
                Help = "Ridge raises, valley lowers, road flattens to the points' heights, cliff raises the left side, river cuts its bed",
            },
            new NumberSetting("Width", set, "width", d => CurveOf(d).Width, run, chosen) { Minimum = 0, Maximum = 64, HasSlider = true, Help = "The part at full height, in ogrids; a point may have its own" },
            new NumberSetting("Height", set, "height", d => CurveOf(d).Points.FirstOrDefault()?.Height ?? 0, run, chosen)
            {
                // as the other fields: a slider beside it; a height typed may go beyond its end
                Minimum = 0,
                Maximum = 256,
                HasSlider = true,
                Help = "Every point's height: how much a ridge, valley or cliff rises, or the road's height in game units",
            },
        ]);
        JsonNode? Ranged(JsonNode? value, bool height, bool from)
        {
            HeightCurve curve = CurveNow(Document);
            CurveRange range = height ? curve.HeightVariation : curve.WidthVariation;
            float? shared = height ? SharedHeight(curve) : SharedWidth(curve);
            float given = value?.GetValue<double>() is double number ? (float)number - (shared ?? 0) : 0;
            return from ? new JsonArray(given, range.Max) : new JsonArray(range.Min, given);
        }
        double Shown(MapDocument d, bool height, bool from)
        {
            HeightCurve curve = CurveOf(d);
            CurveRange range = height ? curve.HeightVariation : curve.WidthVariation;
            float? shared = height ? SharedHeight(curve) : SharedWidth(curve);
            return (shared ?? 0) + (from ? range.Min : range.Max);
        }
        static JsonNode? Whole(JsonNode? value) => value?.GetValue<double>() is double number ? (int)Math.Round(number) : null;
        bool IsRoad(HeightCurve c) => c.Profile == CurveProfile.Road;
        List<Setting> cliff =
        [
            new NumberSetting("Cliff face", set, "cliffFace", d => CurveOf(d).CliffFace, run, chosen)
            {
                Minimum = 0, Maximum = 32, HasSlider = true, Help = "The drop's width in ogrids: 1 is a sheer cliff, more a slope of the fade's shape",
            },
        ];
        if (HasCliffWalkability)
        {
            cliff.Add(new BoolSetting("Cliff face blocks walkability", set, "blocksWalkability",
                d => typeof(HeightCurve).GetProperty("BlocksWalkability")?.GetValue(CurveOf(d)) is true, run, chosen)
            {
                Help = "Units cannot cross the cliff's face, even where its slope alone would let them",
            });
        }
        CurveGroups =
        [
            new CurveSettingGroup(new SettingGroup("Falloff",
            [
                new NumberSetting("Falloff", set, "falloff", d => CurveOf(d).Falloff, run, chosen)
                {
                    Minimum = 0, Maximum = 64, HasSlider = true, Help = "How far beyond the width it fades out, in ogrids (the left side's when the sides differ)",
                },
                new BoolSetting("Same on both sides", set, "falloffRight", d => CurveOf(d).FalloffRight is null, run, chosen)
                {
                    Help = "The right side fades over the same distance as the left; untick it to give the right a falloff of its own",
                    ValueOf = value => value?.GetValue<bool>() == true ? -1 : CurveNow(Document).Falloff,
                },
            ]), c => c.Profile != CurveProfile.Cliff),
            new CurveSettingGroup(new SettingGroup("Right falloff",
            [
                new NumberSetting("Right falloff", set, "falloffRight", d => CurveOf(d).FalloffRight ?? CurveOf(d).Falloff, run, chosen)
                {
                    Minimum = 0, Maximum = 64, HasSlider = true, Help = "How far beyond the width the right side (walking from the first point) fades out, in ogrids",
                },
            ]), c => c.Profile != CurveProfile.Cliff && c.FalloffRight is not null, showsTitle: false),
            new CurveSettingGroup(new SettingGroup("Fade back",
            [
                new NumberSetting("Falloff", set, "falloff", d => CurveOf(d).Falloff, run, chosen)
                {
                    Minimum = 0, Maximum = 64, HasSlider = true, Help = "How far beyond the width the cliff's high side fades back, in ogrids",
                },
            ]), c => c.Profile == CurveProfile.Cliff),
            new CurveSettingGroup(new SettingGroup("Fade",
            [
                new ChoiceSetting("Shape", set, "shape", [.. Shapes.Select(shape => shape.Title)],
                    d => CurveOf(d).FalloffShape is CurveFalloffShape shape ? Shapes.First(s => s.Value.Equals(shape.ToString(), StringComparison.OrdinalIgnoreCase)).Title : Shapes[0].Title,
                    run, chosen)
                {
                    Help = "How the fade runs from full to nothing (and a cliff's face): the profile's is smooth, a river's banks ease out",
                    Value = title => Shapes.First(shape => shape.Title == title).Value,
                },
            ]), _ => true),
            new CurveSettingGroup(new SettingGroup("Terraces",
            [
                new NumberSetting("Terrace steps", set, "terraceSteps", d => CurveOf(d).TerraceSteps, run, chosen)
                {
                    Minimum = 1, Maximum = 12, HasSlider = true, Help = "How many steps a terraced fade has", ValueOf = Whole,
                },
            ]), c => c.FalloffShape == CurveFalloffShape.Terraced, showsTitle: false),
            new CurveSettingGroup(new SettingGroup("Cliff", cliff), c => c.Profile == CurveProfile.Cliff),
            new CurveSettingGroup(new SettingGroup("Road",
            [
                new ChoiceSetting("Road mode", set, "roadMode", [.. RoadModes.Select(mode => mode.Title)],
                    d => RoadModes.First(mode => mode.Value.Equals(CurveOf(d).RoadMode.ToString(), StringComparison.OrdinalIgnoreCase)).Title, run, chosen)
                {
                    Help = "Cut and fill replaces the ground; cut only lowers it where it is higher, fill only raises it where it is lower",
                    Value = title => RoadModes.First(mode => mode.Title == title).Value,
                },
            ]), IsRoad),
            new CurveSettingGroup(new SettingGroup("Camber",
            [
                new NumberSetting("Camber", set, "camber", d => CurveOf(d).Camber, run, chosen)
                {
                    Minimum = -8, Maximum = 8, HasSlider = true, Help = "How much higher the left edge is than the right, across the width, in game units",
                },
            ]), c => HeightCurves.IsAbsolute(c.Profile), showsTitle: false),
            new CurveSettingGroup(new SettingGroup("Line",
            [
                new BoolSetting("Closed", set, "closed", d => CurveOf(d).Closed, run, chosen) { Help = "The last point joins the first; a closed curve of three points or more fills its inside" },
            ]), _ => true),
            new CurveSettingGroup(new SettingGroup("Line only",
            [
                new BoolSetting("Line only", set, "lineOnly", d => CurveOf(d).LineOnly, run, chosen) { Help = "The closed curve shapes its line only, not its inside" },
            ]), c => c.Closed, showsTitle: false),
            new CurveSettingGroup(new SettingGroup("Mirrored",
            [
                new BoolSetting("Mirrored", set, "mirrored", d => CurveOf(d).Symmetry is not null, run, chosen)
                {
                    Help = "The layer draws the curve's images in the other parts of the map too, by the chosen symmetry",
                },
            ]), _ => true, showsTitle: false),
            new CurveSettingGroup(new SettingGroup("Edge noise",
            [
                new NumberSetting("Amplitude", set, "noiseAmplitude", d => CurveOf(d).NoiseAmplitude, run, chosen)
                {
                    Minimum = 0, Maximum = 16, HasSlider = true, Help = "How far the edges wander in and out, in ogrids; 0 for straight edges",
                },
                new NumberSetting("Scale", set, "noiseScale", d => CurveOf(d).NoiseScale, run, chosen)
                {
                    Minimum = 1, Maximum = 128, HasSlider = true, Help = "How large the wanderings are, in ogrids",
                },
                new NumberSetting("Seed", set, "noiseSeed", d => CurveOf(d).NoiseSeed, run, chosen) { Help = "Another number gives other wanderings (and variation)", ValueOf = Whole },
            ]), _ => true),
            new CurveSettingGroup(new SettingGroup("Variation along the curve",
            [
                new NumberSetting("Height from", set, "heightVariation", d => Shown(d, true, true), run, chosen)
                {
                    Help = "The lowest height along the curve: absolute while the points share one height, else an offset from each point's",
                    ValueOf = value => Ranged(value, height: true, from: true),
                },
                new NumberSetting("Height to", set, "heightVariation", d => Shown(d, true, false), run, chosen)
                {
                    Help = "The highest height along the curve: absolute while the points share one height, else an offset from each point's",
                    ValueOf = value => Ranged(value, height: true, from: false),
                },
                new NumberSetting("Width from", set, "widthVariation", d => Shown(d, false, true), run, chosen)
                {
                    Help = "The narrowest width along the curve in ogrids: absolute while no point has a width of its own, else an offset",
                    ValueOf = value => Ranged(value, height: false, from: true),
                },
                new NumberSetting("Width to", set, "widthVariation", d => Shown(d, false, false), run, chosen)
                {
                    Help = "The widest width along the curve in ogrids: absolute while no point has a width of its own, else an offset",
                    ValueOf = value => Ranged(value, height: false, from: false),
                },
                new NumberSetting("Scale", set, "variationScale", d => CurveOf(d).VariationScale, run, chosen)
                {
                    Minimum = 1, Maximum = 256, HasSlider = true, Help = "How far along the curve, in ogrids, the height and width change from one to another",
                },
            ]), _ => true),
        ];
        Nodes = new CurveNodesViewModel(run);
        return main;
    }

    /// <summary>
    /// Shows the chosen curve's settings, groups and points.
    /// </summary>
    private void RefreshCurve()
    {
        MapDocument? document = IsCurve ? Document : null;
        HeightCurve? curve = document is not null && HeightLayers.Find(document, Chosen) is { Curve: HeightCurve c } ? c : null;
        Curve?.Refresh(document);
        foreach (CurveSettingGroup group in CurveGroups)
        {
            group.Refresh(document, curve);
        }
        VariationNote = curve is null ? ""
            : (SharedHeight(curve), SharedWidth(curve)) switch
            {
                (float h, float w) => string.Create(CultureInfo.InvariantCulture, $"Heights and widths as they are: the points share the height {Setting.Format(h)} and the width {Setting.Format(w)}."),
                (float h, null) => string.Create(CultureInfo.InvariantCulture, $"Heights as they are (the points share {Setting.Format(h)}); widths as offsets from each point's."),
                (null, float w) => string.Create(CultureInfo.InvariantCulture, $"Heights as offsets from each point's; widths as they are (the points share {Setting.Format(w)})."),
                _ => "Heights and widths as offsets from each point's.",
            };
        Nodes?.Show(document, curve is null ? null : Chosen, curve);
    }
}
