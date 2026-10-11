using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Checks;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.Tools;

/// <summary>
/// A group of an army the place tool places into, as its combo box shows it.
/// </summary>
/// <param name="Path"><c>INITIAL/Wrecks</c>; empty for the army's root group.</param>
public sealed record GroupChoice(string Path, string Title)
{
    public override string ToString() => Title;
}

/// <summary>
/// One way to place in the options row's segmented control (user story U-7): a chip that is on
/// while it is the tool's <see cref="UnitPlaceTool.Mode"/>.
/// </summary>
public sealed partial class PlaceModeChoice(UnitPlaceTool tool, PlaceMode mode) : ObservableObject
{
    public PlaceMode Mode => mode;

    public string Title => mode.ToString();

    /// <summary>
    /// For screen readers and automation: <c>Place in a line</c>.
    /// </summary>
    public string Label => mode switch
    {
        PlaceMode.Single => "Place one unit at a time",
        PlaceMode.Line => "Place in a line",
        PlaceMode.Rectangle => "Place in a rectangle",
        PlaceMode.Ring => "Place a ring",
        PlaceMode.Scatter => "Scatter with a brush",
        _ => $"Place: {mode}",
    };

    public string Help => UnitPlaceTool.HelpOf(mode);

    public bool IsOn => tool.Mode == mode;

    [RelayCommand]
    private void Choose() => tool.Mode = mode;

    internal void Refresh() => OnPropertyChanged(nameof(IsOn));
}

/// <summary>
/// Places units for an army (user story U-2), or a units preset (U-4): a click places the chosen
/// unit at the mouse (one step of undo, through <c>units.add</c>), into the chosen army's chosen
/// group, named with a free <c>UNIT_n</c>. A structure turns in quarter turns and snaps to the
/// build grid; a mobile unit turns freely. The unit comes from the unit picker (Choose...), a tile
/// chosen on the shelf, or a tile dropped on a view, which places it where it is dropped. Q and E
/// turn what is placed next: a structure or a preset a quarter turn, a mobile unit 15 degrees
/// (with shift a quarter turn). A preset's units show under the mouse before the click, and so
/// does a single unit's footprint.
/// </summary>
/// <remarks>
/// <para>
/// Many at once (user story U-7, <see cref="Mode"/>): <see cref="PlaceMode.Line"/> and
/// <see cref="PlaceMode.Rectangle"/> are dragged out (the footprints of every unit the drag will
/// place show under the mouse, the status line counts them; shift keeps a line to eighth turns;
/// Escape cancels), and the release runs one <c>units.addMany</c>, one step of undo.
/// <see cref="PlaceMode.Ring"/> places around the selected units' footprints, or around the structure
/// under the mouse, a <see cref="Distance"/> away, at a click. <see cref="PlaceMode.Scatter"/> is a
/// brush (<see cref="BrushStroke"/>; <c>[</c> and <c>]</c> its radius): a click scatters
/// <see cref="Count"/> units at random headings in its circle, none closer than
/// <see cref="LeastDistance"/>, and a drag goes on at that density; the stroke is one step. Every
/// mode places into the options row's army and group, as wrecks when asked, gives the symmetry's
/// images and selects what it placed. The maths is the library's <see cref="UnitLayouts"/>.
/// </para>
/// </remarks>
public sealed partial class UnitPlaceTool : ObservableObject, IMapTool, IAssetDropTarget
{
    /// <summary>
    /// How far, in pixels, the mouse moves before a press becomes a drag.
    /// </summary>
    private const float DragPixels = 3;

    private readonly MainViewModel model;

    public UnitPlaceTool(MainViewModel model)
    {
        this.model = model;
        Modes = [.. Enum.GetValues<PlaceMode>().Select(mode => new PlaceModeChoice(this, mode))];
    }

    /// <summary>
    /// The armies of the map, for the combo box.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<string> Armies { get; private set; } = [];

    /// <summary>
    /// The army it places for.
    /// </summary>
    [ObservableProperty]
    public partial string? Army { get; set; }

    /// <summary>
    /// The groups of <see cref="Army"/>, for the combo box.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<GroupChoice> Groups { get; private set; } = [];

    /// <summary>
    /// The group it places into; <c>INITIAL</c>, whose units the game creates at the start, unless
    /// another is chosen.
    /// </summary>
    [ObservableProperty]
    public partial GroupChoice? ChosenGroup { get; set; }

    /// <summary>
    /// The group's path (<see cref="ChosenGroup"/>), <c>INITIAL</c> when none is chosen.
    /// </summary>
    public string Group => ChosenGroup?.Path ?? UnitGroupEdits.InitialGroup;

    /// <summary>
    /// The unit it places, from the picker or the shelf; null until one is chosen.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnitTitle), nameof(UnitButtonText), nameof(HasUnit), nameof(UsesSpacing))]
    public partial AssetEntry? Unit { get; set; }

    public bool HasUnit => Unit is not null;

    /// <summary>
    /// The units preset it places instead of a unit; null for none.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnitTitle), nameof(UnitButtonText), nameof(HasPreset))]
    public partial string? Preset { get; set; }

    public bool HasPreset => Preset is not null;

    private IReadOnlyList<PreviewOutline> _outlines = [];

    /// <summary>
    /// The army role of the preset it places (<see cref="UnitPresetSettings.Role"/>), which picks the
    /// army its units go to in place of <see cref="Army"/>; null for a preset without one.
    /// </summary>
    public string? PresetRole { get; private set; }

    /// <summary>
    /// Makes a units preset what a click places, with its units' footprints under the mouse.
    /// </summary>
    /// <param name="role">Its army role, if any.</param>
    public void UsePreset(string name, UnitGroup units, string? role = null)
    {
        _outlines = PresetPreview.OfUnits(units, model.Assets.Catalog);
        PresetRole = role;
        Preset = name;
    }

    /// <summary>
    /// What the unit button of the options row reads: the preset or unit's name.
    /// </summary>
    public string UnitButtonText => Preset is string preset ? preset : Unit is AssetEntry unit ? unit.Name : "Choose a unit";

    /// <summary>
    /// What a click places, as the options bar says it.
    /// </summary>
    public string UnitTitle => Preset is string preset ? $"Preset {preset}"
        : Unit is AssetEntry unit ? $"{unit.Name} ({unit.Key})"
        : "No unit chosen";

    /// <summary>
    /// Whether what it places are wrecks (user story U-5): they go into the army's <c>WRECKAGE</c>
    /// group, whose units the game turns into wrecks at the start, in place of the chosen group.
    /// </summary>
    [ObservableProperty]
    public partial bool AsWrecks { get; set; }

    /// <summary>
    /// Which way what it places next faces, in degrees clockwise from +x in the top view.
    /// </summary>
    [ObservableProperty]
    public partial double Heading { get; set; }

    #region Many at once (U-7)

    /// <summary>
    /// How a click or drag places: one unit, a line, a rectangle, a ring or a scatter.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLine), nameof(IsRectangle), nameof(IsRing), nameof(IsScatter), nameof(UsesSpacing), nameof(Help), nameof(CursorRadius))]
    public partial PlaceMode Mode { get; set; } = PlaceMode.Single;

    /// <summary>
    /// The ways to place, for the options row's segmented control.
    /// </summary>
    public IReadOnlyList<PlaceModeChoice> Modes { get; }

    /// <summary>
    /// The way chosen, for the options row's combo box.
    /// </summary>
    public PlaceModeChoice ModeChoice
    {
        get => Modes.First(choice => choice.Mode == Mode);
        set
        {
            if (value is not null)
            {
                Mode = value.Mode;
            }
        }
    }

    public bool IsLine => Mode == PlaceMode.Line;

    public bool IsRectangle => Mode == PlaceMode.Rectangle;

    public bool IsRing => Mode == PlaceMode.Ring;

    public bool IsScatter => Mode == PlaceMode.Scatter;

    /// <summary>
    /// Whether the spacing matters: a line, rectangle or ring of mobile units (structures butt
    /// together at their footprint).
    /// </summary>
    public bool UsesSpacing => Mode is PlaceMode.Line or PlaceMode.Rectangle or PlaceMode.Ring && Unit?.IsStructure != true;

    /// <summary>
    /// Between the centres of mobile units in a line, rectangle or ring, in ogrids.
    /// </summary>
    [ObservableProperty]
    public partial double Spacing { get; set; } = 2;

    /// <summary>
    /// Whether the units of a line face along it; else they face <see cref="Heading"/>.
    /// </summary>
    [ObservableProperty]
    public partial bool FaceAlong { get; set; } = true;

    /// <summary>
    /// Whether a rectangle is only its border (a wall around a yard).
    /// </summary>
    [ObservableProperty]
    public partial bool Hollow { get; set; }

    /// <summary>
    /// Between a ring and the footprints it goes around, in ogrids.
    /// </summary>
    [ObservableProperty]
    public partial double Distance { get; set; } = 1;

    /// <summary>
    /// How many units a click of the scatter brush puts in its circle (a drag goes on at that density).
    /// </summary>
    [ObservableProperty]
    public partial int Count { get; set; } = 6;

    /// <summary>
    /// How close scattered units may stand to each other, in ogrids.
    /// </summary>
    [ObservableProperty]
    public partial double LeastDistance { get; set; } = 2;

    /// <summary>
    /// The scatter brush's radius, in ogrids.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CursorRadius))]
    public partial double Radius { get; set; } = 8;

    /// <summary>
    /// The layout settings as the library takes them.
    /// </summary>
    public UnitLayoutSettings LayoutSettings => new UnitLayoutSettings((float)Spacing, FaceAlong, Hollow, (float)Distance, (float)(Heading * Math.PI / 180));

    partial void OnModeChanged(PlaceMode value)
    {
        CancelDrag();
        OnPropertyChanged(nameof(ModeChoice));
        foreach (PlaceModeChoice choice in Modes)
        {
            choice.Refresh();
        }
    }

    /// <summary>
    /// What a mode does, for the options row and the status line.
    /// </summary>
    public static string HelpOf(PlaceMode mode) => mode switch
    {
        PlaceMode.Line => "Drag a line; Shift keeps it to 45°; Escape cancels; Q and E turn",
        PlaceMode.Rectangle => "Drag a rectangle (Hollow: only its border); Escape cancels; Q and E turn",
        PlaceMode.Ring => "Click to place a ring around the selected units, or around the structure under the mouse",
        PlaceMode.Scatter => "Click or drag to scatter units at random; [ and ] the radius, Shift with them the count",
        _ => "Click to place; Q and E turn; drop a unit from the shelf to place it there",
    };

    // the drag of a line or rectangle
    private Vector2? _from;
    private Vector2 _to;
    private bool _dragging;
    private ToolInput _input = ToolInput.Default;
    private ImmutableArray<UnitPlace> _planned = [];

    // the scatter brush's stroke
    private BrushStroke? _stroke;
    private List<Vector2> _scattered = [];
    private int _seed;
    private int _dabs;
    private readonly List<ObjectRef> _strokePlaced = [];
    private int _strokeImages;

    // the ring's preview, made again only when what it depends on changes
    private (MapDocument Document, ImmutableArray<ObjectRef> Selection, Vector2? Pointer, string? Unit, UnitLayoutSettings Settings, ImmutableArray<UnitPlace> Places)? _ring;

    /// <summary>
    /// Whether a line or rectangle is being dragged out.
    /// </summary>
    public bool IsDragging => _dragging;

    /// <summary>
    /// How many units the drag going on will place.
    /// </summary>
    public int PlannedCount => _planned.Length;

    #endregion

    public double CursorRadius => Mode == PlaceMode.Scatter ? Radius : 0;

    public string Help => HelpOf(Mode);

    /// <summary>
    /// Its settings for automation's <c>state</c>.
    /// </summary>
    public JsonObject Describe() => Describe(CurrentVerdict());

    private JsonObject Describe(PlaceVerdict? verdict) => new JsonObject
    {
        ["unit"] = Unit?.Key,
        ["preset"] = Preset,
        ["presetRole"] = Preset is null ? null : PresetRole,
        ["army"] = Army,
        ["group"] = Group,
        ["heading"] = Heading,
        ["asWrecks"] = AsWrecks,
        ["previewOutlines"] = Preset is not null ? _outlines.Count : _dragging ? _planned.Length : Unit is null ? 0 : Mode == PlaceMode.Ring ? _ring?.Places.Length ?? 0 : Mode == PlaceMode.Scatter ? 0 : 1,
        ["structure"] = Unit?.IsStructure,
        ["footprint"] = Unit?.Footprint is (double x, double z) ? new JsonArray(x, z) : null,
        ["mode"] = Mode.ToString(),
        ["spacing"] = Spacing,
        ["faceAlong"] = FaceAlong,
        ["hollow"] = Hollow,
        ["distance"] = Distance,
        ["count"] = Count,
        ["leastDistance"] = LeastDistance,
        ["radius"] = Radius,
        ["dragging"] = _dragging,
        ["planned"] = _planned.Length,
        ["blocked"] = verdict is { Count: > 0 },
        ["blockedCount"] = verdict?.Count ?? 0,
        ["outside"] = verdict?.Outside ?? 0,
        ["why"] = verdict?.Why,
    };

    partial void OnUnitChanged(AssetEntry? value)
    {
        if (value is not null)
        {
            Preset = null;
        }
    }

    partial void OnArmyChanged(string? value)
    {
        ArmyChosen |= !_defaulting;
        RefreshGroups(model.ShownDocument);
        // another army chosen in the row: the tree shows it too
        if (!_defaulting && !_following && value is not null)
        {
            GroupPicked?.Invoke(value, Group);
        }
    }

    private bool _defaulting;

    /// <summary>
    /// Whether the army was chosen (in the row, in the tree or by a command) rather than defaulted;
    /// a chosen army stays until the map has no such army.
    /// </summary>
    public bool ArmyChosen { get; private set; }

    /// <summary>
    /// The view's middle in ogrids, for the army it places for by default; null (the map's middle)
    /// until the window sets it.
    /// </summary>
    public Func<Vector2?>? ViewMiddle { get; set; }

    /// <summary>
    /// The army a new place tool places for: the playable army whose spawn lies nearest the view's
    /// middle, else the civilians (<c>NEUTRAL_CIVILIAN</c>), the first extra army, the first army.
    /// </summary>
    public string? DefaultArmy(MapDocument document)
    {
        Vector2 middle = ViewMiddle?.Invoke() ?? new Vector2(document.Width / 2f, document.Height / 2f);
        string? nearest = document.Markers.Spawns
            .Where(spawn => document.Scenario.PlayableArmies.Contains(spawn.Army) && document.FindArmy(spawn.Army) is not null)
            .OrderBy(spawn => Vector2.DistanceSquared(new Vector2(spawn.Position.X, spawn.Position.Z), middle))
            .FirstOrDefault()?.Army;
        return nearest
            ?? document.FindArmy("NEUTRAL_CIVILIAN")?.Name
            ?? document.Scenario.ExtraArmies.FirstOrDefault(name => document.FindArmy(name) is not null)
            ?? document.Armies.FirstOrDefault()?.Name;
    }

    private bool _following;

    /// <summary>
    /// Raised with the army and group's path when the user chooses a group in the row's combo box,
    /// so the Units mode's tree shows the same group (the tree's choice reaches <see cref="PlaceInto"/>).
    /// </summary>
    public event Action<string, string>? GroupPicked;

    partial void OnChosenGroupChanged(GroupChoice? value)
    {
        if (!_following && value is not null && Army is string army)
        {
            GroupPicked?.Invoke(army, value.Path);
        }
    }

    public void Press(float x, float z) => Press(x, z, ToolInput.Default);

    public void Drag(float x, float z) => Drag(x, z, _input);

    public void Press(float x, float z, ToolInput input)
    {
        _input = input;
        if (model.ShownDocument is null)
        {
            return;
        }
        if (Preset is string preset)
        {
            _ = model.RunWithAsync("units.placePreset", new JsonObject { ["name"] = preset, ["x"] = x, ["z"] = z });
            return;
        }
        if (Unit is null)
        {
            model.Status = "Choose a unit to place: Units, Choose a unit, or a unit on the asset shelf";
            return;
        }
        switch (Mode)
        {
            case PlaceMode.Line:
            case PlaceMode.Rectangle:
                _from = new Vector2(x, z);
                _to = _from.Value;
                _dragging = true;
                Plan();
                break;
            case PlaceMode.Ring:
                _ = model.RunWithAsync("units.addMany", new JsonObject { ["mode"] = "ring", ["x"] = x, ["z"] = z });
                break;
            case PlaceMode.Scatter:
                StartScatter(x, z);
                break;
            default:
                _ = model.RunWithAsync("units.add", new JsonObject { ["x"] = x, ["z"] = z });
                break;
        }
    }

    public void Drag(float x, float z, ToolInput input)
    {
        _input = input;
        if (_stroke is not null)
        {
            _stroke.MoveTo(x, z);
            return;
        }
        if (_from is not Vector2 from || !_dragging)
        {
            return;
        }
        Vector2 to = new Vector2(x, z);
        if (Mode == PlaceMode.Line && input.Modifiers.HasFlag(ToolModifiers.Shift))
        {
            to = UnitLayouts.Constrain(from, to, UnitLayouts.EighthTurn);
        }
        _to = to;
        Plan();
    }

    public void Release()
    {
        if (_stroke is BrushStroke stroke)
        {
            EndScatter(stroke);
            return;
        }
        if (_from is not Vector2 from || !_dragging)
        {
            return;
        }
        Vector2 to = _to;
        CancelDrag();
        _ = model.RunWithAsync("units.addMany", new JsonObject
        {
            ["mode"] = Mode.ToString(),
            ["x0"] = from.X,
            ["z0"] = from.Y,
            ["x1"] = to.X,
            ["z1"] = to.Y,
        });
    }

    public bool Key(string key, ToolModifiers modifiers)
    {
        if (key == "Escape" && _dragging)
        {
            CancelDrag();
            model.Status = $"Cancelled the {Mode.ToString().ToLowerInvariant()}: nothing placed";
            return true;
        }
        if (modifiers.HasFlag(ToolModifiers.Control) || modifiers.HasFlag(ToolModifiers.Alt))
        {
            return false;
        }
        if (PaletteKey(key, modifiers))
        {
            return true;
        }
        if (key is not ("Q" or "E"))
        {
            return false;
        }
        double step = Preset is not null || Unit?.IsStructure == true || modifiers.HasFlag(ToolModifiers.Shift) ? 90 : 15;
        double heading = (Heading + (key == "E" ? step : -step)) % 360;
        Heading = heading < 0 ? heading + 360 : heading;
        model.Status = $"Places facing {Heading:0}°";
        if (_dragging)
        {
            Plan();
        }
        return true;
    }

    /// <summary>
    /// What the views draw under the mouse: a preset's units, the footprints a drag will fill, the
    /// ring a click will place, or the one unit a click will place (span <c>tool.place-preview</c>).
    /// </summary>
    public IReadOnlyList<ToolShape> Shapes(ToolView view)
    {
        if (Preset is not null)
        {
            return PresetPreview.Shapes(model, view, _outlines, BuildGrid.SnapHeading((float)(Heading * Math.PI / 180)));
        }
        if (Unit is not AssetEntry unit || model.ShownDocument is not MapDocument document)
        {
            return [];
        }
        ImmutableArray<UnitPlace> places = PreviewPlaces(document, unit);
        if (places.IsEmpty)
        {
            return [];
        }
        return PresetPreview.ShapesAt(model, view, Outlines(unit, places), "tool.place-preview", VerdictOf(document, unit, places).Blocked);
    }

    private string? _toldWhy;

    /// <summary>
    /// The mouse moved over a view: the status line says why the preview is red when that changed
    /// (not while drawing, which must not change what the window shows).
    /// </summary>
    public void PointerMoved()
    {
        if (_dragging || CurrentVerdict() is not PlaceVerdict verdict || verdict.Why == _toldWhy)
        {
            return;
        }
        _toldWhy = verdict.Why;
        if (verdict.Why is not null)
        {
            model.Status = verdict.Why;
        }
    }

    /// <summary>
    /// The places a click or release would put the unit at now: the drag's, the ring's, or the one
    /// under the mouse; none for the scatter.
    /// </summary>
    private ImmutableArray<UnitPlace> PreviewPlaces(MapDocument document, AssetEntry unit)
    {
        if (_dragging)
        {
            return _planned;
        }
        switch (Mode)
        {
            case PlaceMode.Scatter:
                return [];
            case PlaceMode.Ring:
                return RingPlan(document, unit);
            default:
                return model.Pointer is (float px, float pz) ? [UnitPlans.Single(unit, px, pz, (float)(Heading * Math.PI / 180))] : [];
        }
    }

    /// <summary>
    /// Which of the places the unit would break a rule at (user story U-12): overlap an existing
    /// structure or another planned one, or stand on ground its blueprint rules out
    /// (<see cref="PlacementChecks"/>, the Problems tab's functions), and which lie outside the map
    /// (left out when placed). Placing is allowed anyway. Wrecks and mobile units break no rule.
    /// </summary>
    /// <param name="Blocked">One flag per place, as many as outlines are drawn: a place that breaks a rule or lies outside the map.</param>
    /// <param name="Why">What is wrong at the first place that breaks a rule, e.g. <c>Overlaps the Land Factory UNIT_12</c>.</param>
    /// <param name="Outside">How many of the places lie outside the map.</param>
    /// <param name="Breaking">How many of the places inside the map break a rule.</param>
    public sealed record PlaceVerdict(bool[] Blocked, string? Why, int Outside = 0, int Breaking = 0)
    {
        /// <summary>
        /// How many places break a rule.
        /// </summary>
        public int Count => Breaking;
    }

    /// <summary>
    /// The verdict for places of the unit now.
    /// </summary>
    public PlaceVerdict VerdictOf(MapDocument document, AssetEntry unit, ImmutableArray<UnitPlace> places)
    {
        bool[] blocked = new bool[Math.Min(places.Length, PresetPreview.MaxOutlines)];
        bool[] outside = new bool[blocked.Length];
        int outsideCount = 0;
        for (int i = 0; i < blocked.Length; i++)
        {
            UnitPlace place = places[i];
            if (place.X < 0 || place.Z < 0 || place.X > document.Width || place.Z > document.Height)
            {
                blocked[i] = outside[i] = true;
                outsideCount++;
            }
        }
        // the ones past the outlines drawn are counted too, for the status line
        for (int i = blocked.Length; i < places.Length; i++)
        {
            if (places[i].X < 0 || places[i].Z < 0 || places[i].X > document.Width || places[i].Z > document.Height)
            {
                outsideCount++;
            }
        }
        string? outsideWhy = outsideCount > 0 ? $"{MainViewModel.Units(outsideCount)} lie{(outsideCount == 1 ? "s" : "")} beyond the map's edge and are left out" : null;
        if (!unit.IsStructure || AsWrecks || model.Assets.Catalog is not AssetCatalog catalog)
        {
            return new PlaceVerdict(blocked, outsideWhy, outsideCount);
        }
        using Performance.Span measure = Performance.Measure("tool.place-checks");
        PlacementIndex index = PlacementIndex.Of(document, catalog);
        Heightmap heights = document.Terrain.Heightmap;
        float? water = document.Water.HasWater ? document.Water.Elevation : null;
        PlacementRect[] rects = new PlacementRect[blocked.Length];
        for (int i = 0; i < rects.Length; i++)
        {
            rects[i] = PlacementChecks.SkirtOf(unit, places[i].X, places[i].Z, places[i].Heading);
        }
        bool[] among = PlacementChecks.OverlapAmong(rects);
        string? why = null;
        int breaking = 0;
        for (int i = 0; i < blocked.Length; i++)
        {
            if (outside[i])
            {
                continue;
            }
            UnitPlace place = places[i];
            string? reason = PlacementChecks.OverlapAt(index, unit, place.X, place.Z, place.Heading) is PlacedStructure other
                ? $"Overlaps the {other.Title}"
                : among[i] ? "Overlaps another of the units to place"
                : PlacementChecks.GroundIssue(heights, water, unit, place.X, place.Z, place.Heading) is string ground ? $"{unit.Name}: {ground}" : null;
            if (reason is not null)
            {
                blocked[i] = true;
                breaking++;
                why ??= reason;
            }
        }
        return new PlaceVerdict(blocked, why ?? outsideWhy, outsideCount, breaking);
    }

    /// <summary>
    /// The verdict for what a click or release would place now, for automation.
    /// </summary>
    private PlaceVerdict? CurrentVerdict() =>
        Unit is AssetEntry unit && Preset is null && model.ShownDocument is MapDocument document ? VerdictOf(document, unit, PreviewPlaces(document, unit)) : null;

    /// <summary>
    /// The palette's keys (user story U-8): 1 to 9 and 0 pick a slot of the chosen palette (its
    /// unit or preset is what a click places), Tab goes to the next palette and Shift+Tab to the
    /// one before. A digit with shift is left alone.
    /// </summary>
    /// <param name="key">The key's name: <c>D1</c>, <c>NumPad0</c>, <c>Tab</c>.</param>
    private bool PaletteKey(string key, ToolModifiers modifiers)
    {
        UnitPalettesViewModel palettes = model.UnitPalettes;
        if (key == "Tab")
        {
            model.Status = palettes.Next(modifiers.HasFlag(ToolModifiers.Shift) ? -1 : 1) is string palette
                ? $"Palette {palette}: 1 to 9 and 0 pick a slot"
                : "The workspace has no palettes yet: Units, New palette makes one";
            return true;
        }
        string digit = key.Length == 2 && key[0] == 'D' ? key[1..] : key.StartsWith("NumPad", StringComparison.Ordinal) && key.Length == 7 ? key[6..] : "";
        if (modifiers.HasFlag(ToolModifiers.Shift) || UnitPalettes.SlotOfKey(digit) is not int index)
        {
            return false;
        }
        _ = model.RunWithAsync("units.slot", new JsonObject { ["n"] = index + 1 });
        return true;
    }

    public bool Accepts(AssetEntry entry) => entry.Kind == AssetKind.Unit;

    public void Drop(AssetEntry entry, float x, float z)
    {
        Unit = entry;
        _ = model.RunWithAsync("units.add", new JsonObject { ["blueprint"] = entry.Key, ["x"] = x, ["z"] = z });
    }

    /// <summary>
    /// Shows the armies and groups of a version of the document; an army it no longer has makes
    /// way for the civilians (<c>NEUTRAL_CIVILIAN</c>), the first extra army, or the first army.
    /// </summary>
    public void Refresh(MapDocument? document)
    {
        ImmutableArray<Army> armies = document?.Armies ?? [];
        if (!Armies.SequenceEqual(armies.Select(army => army.Name)))
        {
            Armies = [.. armies.Select(army => army.Name)];
        }
        if (document is not null && (Army is null || document.FindArmy(Army) is null))
        {
            _defaulting = true;
            try
            {
                Army = DefaultArmy(document);
            }
            finally
            {
                _defaulting = false;
            }
        }
        RefreshGroups(document);
    }

    /// <summary>
    /// Chooses where it places: an army and one of its groups.
    /// </summary>
    public void PlaceInto(string army, string group)
    {
        _following = true;
        try
        {
            Army = army;
            if (Groups.All(choice => choice.Path != group))
            {
                RefreshGroups(model.ShownDocument);
            }
            ChosenGroup = Groups.FirstOrDefault(choice => choice.Path == group) ?? ChosenGroup;
        }
        finally
        {
            _following = false;
        }
    }

    private void RefreshGroups(MapDocument? document)
    {
        bool following = _following;
        _following = true;
        try
        {
            RefreshGroupsCore(document);
        }
        finally
        {
            _following = following;
        }
    }

    private void RefreshGroupsCore(MapDocument? document)
    {
        UnitGroup? root = Army is null ? null : document?.FindArmy(Army)?.Units;
        List<GroupChoice> choices = [];
        void Add(UnitGroup group, ImmutableArray<string> path)
        {
            string text = UnitGroupEdits.PathText(path);
            choices.Add(new GroupChoice(text, path.IsEmpty ? $"{group.Name} (the root group)" : text));
            foreach (UnitGroup child in group.Groups)
            {
                Add(child, path.Add(child.Name));
            }
        }
        if (root is not null)
        {
            Add(root, []);
        }
        if (!choices.Contains(new GroupChoice(UnitGroupEdits.InitialGroup, UnitGroupEdits.InitialGroup)))
        {
            // placing makes it
            choices.Insert(Math.Min(1, choices.Count), new GroupChoice(UnitGroupEdits.InitialGroup, UnitGroupEdits.InitialGroup));
        }
        if (!choices.SequenceEqual(Groups))
        {
            string group = Group;
            Groups = choices;
            ChosenGroup = choices.FirstOrDefault(choice => choice.Path == group) ?? choices.First(choice => choice.Path == UnitGroupEdits.InitialGroup);
        }
        else if (ChosenGroup is null)
        {
            ChosenGroup = choices.First(choice => choice.Path == UnitGroupEdits.InitialGroup);
        }
    }

    #region The drag, the ring and the scatter

    /// <summary>
    /// The places the drag going on will fill, and the status line's count and length.
    /// </summary>
    private void Plan()
    {
        if (_from is not Vector2 from || Unit is not AssetEntry unit)
        {
            return;
        }
        using Performance.Span measure = Performance.Measure("tool.place-plan");
        _planned = UnitPlans.Of(Mode, unit, from, _to, null, LayoutSettings);
        OnPropertyChanged(nameof(PlannedCount));
        float length = Vector2.Distance(from, _to);
        string size = Mode == PlaceMode.Line
            ? $"{length:0.#} ogrids"
            : $"{MathF.Abs(_to.X - from.X):0.#} × {MathF.Abs(_to.Y - from.Y):0.#} ogrids";
        PlaceVerdict? verdict = model.ShownDocument is MapDocument shown ? VerdictOf(shown, unit, _planned) : null;
        string breaks = verdict is { Count: > 0 } ? $"; {verdict.Count} break{(verdict.Count == 1 ? "s" : "")} a rule: {verdict.Why}" : "";
        string left = verdict is { Outside: > 0 } ? $"; {verdict.Outside} beyond the map's edge {(verdict.Outside == 1 ? "is" : "are")} left out" : "";
        model.Status = $"{_planned.Length} × {unit.Name}, {size}{breaks}{left}; release to place, Escape to cancel";
    }

    private void CancelDrag()
    {
        _from = null;
        _dragging = false;
        _planned = [];
        OnPropertyChanged(nameof(PlannedCount));
    }

    /// <summary>
    /// The ring a click places now: around the selected units, else around the structure under the mouse.
    /// </summary>
    private ImmutableArray<UnitPlace> RingPlan(MapDocument document, AssetEntry unit)
    {
        Vector2? pointer = model.Pointer is (float px, float pz) ? new Vector2(px, pz) : null;
        ImmutableArray<ObjectRef> selection = model.Selection.Items;
        UnitLayoutSettings settings = LayoutSettings;
        if (_ring is { } ring && ReferenceEquals(ring.Document, document) && ring.Selection == selection && ring.Pointer == pointer && ring.Unit == unit.Key && ring.Settings == settings)
        {
            return ring.Places;
        }
        using Performance.Span measure = Performance.Measure("tool.place-plan");
        (float X0, float Z0, float X1, float Z1)? bounds = UnitPlans.RingBounds(document, model.Assets.Catalog, selection, pointer);
        ImmutableArray<UnitPlace> places = bounds is null ? [] : UnitPlans.Of(PlaceMode.Ring, unit, default, default, bounds, settings);
        _ring = (document, selection, pointer, unit.Key, settings, places);
        return places;
    }

    private static IReadOnlyList<PreviewOutline> Outlines(AssetEntry unit, ImmutableArray<UnitPlace> places)
    {
        PreviewOutline[] outlines = new PreviewOutline[Math.Min(places.Length, PresetPreview.MaxOutlines)];
        for (int i = 0; i < outlines.Length; i++)
        {
            outlines[i] = PresetPreview.Footprint(unit.Footprint, places[i].X, places[i].Z, places[i].Heading);
        }
        return outlines;
    }

    /// <summary>
    /// A stroke of the scatter brush: the first dab fills its circle with <see cref="Count"/> units,
    /// each next one the strip it moves on at the same density; each dab's units are added with
    /// their images and selected when the stroke ends. A unit the workspace lacks is placed 1 by 1.
    /// </summary>
    private void StartScatter(float x, float z)
    {
        if (model.StrokeInProgress is not null || Unit is not AssetEntry unit)
        {
            return;
        }
        string army = Army ?? "";
        if (model.ShownDocument?.FindArmy(army) is null)
        {
            model.Status = "The map has no army to place units for: add one in the Map mode";
            return;
        }
        _scattered = [];
        _seed = Random.Shared.Next();
        _dabs = 0;
        _strokePlaced.Clear();
        _strokeImages = 0;
        float radius = (float)Radius;
        float circle = MathF.PI * radius * radius;
        // the first dab covers its circle; each next one the strip it moves on, a quarter of the radius
        float strip = 2 * radius * Math.Max(0.5f, radius / 4);
        _stroke = new BrushStroke(model, $"Scatter {unit.Name}", Radius, x, z, (edit, dab) =>
        {
            int count = _dabs == 0 ? Count : (int)MathF.Ceiling(Count * strip / circle);
            ImmutableArray<UnitPlace> places = UnitLayouts.Scatter(new Vector2(dab.X, dab.Z), radius, count, (float)LeastDistance, _seed + _dabs++, _scattered);
            MapDocument document = edit.Document;
            List<Project.Files.InMemory.Unit> units = [];
            foreach (UnitPlace place in places)
            {
                if (place.X < 0 || place.Z < 0 || place.X > document.Width || place.Z > document.Height)
                {
                    continue;
                }
                _scattered.Add(new Vector2(place.X, place.Z));
                units.Add(Commands.UnitCommands.Make(document, unit.Key, unit, place));
            }
            if (units.Count == 0)
            {
                return;
            }
            ImmutableArray<string> group = AsWrecks ? document.WreckagePath(army) : UnitGroupEdits.ParsePath(Group);
            edit.Apply(current =>
            {
                current = current.AddUnits(army, group, units, out ImmutableArray<string> names);
                current = model.MirrorPlaced(current, [.. names.Select(ObjectRef.Unit)], out ImmutableArray<ObjectRef> all);
                _strokePlaced.AddRange(all);
                _strokeImages += all.Length - names.Length;
                return current;
            });
        }, mirror: false);
    }

    private void EndScatter(BrushStroke stroke)
    {
        stroke.End();
        _stroke = null;
        int placed = _strokePlaced.Count - _strokeImages;
        if (placed > 0 && model.ShownDocument is MapDocument document)
        {
            model.Selection.Set(_strokePlaced.Where(document.Exists));
            if (Unit is AssetEntry scattered)
            {
                model.UnitPalettes.NotePlaced(LastPlacedItem.UnitKey(scattered.Key));
            }
            model.Status = $"Scattered {MainViewModel.Units(placed)} ({Unit?.Name}) for {Army}{(AsWrecks ? " as wrecks" : "")}{(_strokeImages > 0 ? $" and {_strokeImages} mirrored" : "")}";
        }
        else
        {
            model.Status = "Scattered nothing: no place in the circle keeps the least distance, or it lies outside the map";
            model.Logger("Editor").LogInformation("A scatter stroke placed no unit (radius {Radius}, count {Count}, least distance {LeastDistance})", Radius, Count, LeastDistance);
        }
        _strokePlaced.Clear();
    }

    #endregion
}
