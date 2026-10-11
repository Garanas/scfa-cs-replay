using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The chosen curve's points as the inspector's table (user story T-4): a row per point with its
/// place, height and the values it has of its own (an empty field is the curve's value, shown as
/// a hint), the grade between each two rows, the chosen points (the views highlight them, the
/// curve tool chooses the point it presses), and the buttons that work on points: reverse, even
/// the grade, snap to the ground. Every change is a command, one step of undo.
/// </summary>
public sealed partial class CurveNodesViewModel(Func<string, JsonObject, Task> run) : ObservableObject
{
    private int[] _chosen = [];

    /// <summary>
    /// A row per point, in the curve's order; kept and changed in place while the count stays.
    /// </summary>
    public ObservableCollection<CurveNodeRow> Rows { get; } = [];

    /// <summary>
    /// The curve layer shown; null for none.
    /// </summary>
    public string? Layer { get; private set; }

    /// <summary>
    /// The curve shown; null for none.
    /// </summary>
    public HeightCurve? Curve { get; private set; }

    /// <summary>
    /// The chosen points, lowest first; empty for none.
    /// </summary>
    public IReadOnlyList<int> Chosen => _chosen;

    /// <summary>
    /// The point chosen last (the keys and the tool's height drag change it), -1 for none.
    /// </summary>
    public int Current { get; private set; } = -1;

    /// <summary>
    /// The grade above which a stretch between two points shows in the warning colour, in percent:
    /// a hint of what units climb.
    /// </summary>
    [ObservableProperty]
    public partial double GradeLimit { get; set; } = 30;

    /// <summary>
    /// The chosen points changed: the views draw them again, the table follows.
    /// </summary>
    public event EventHandler? ChosenChanged;

    /// <summary>
    /// Whether the curve fills its inside: one height for all of it, so the points' heights are greyed.
    /// </summary>
    public bool Fills => Curve?.Fills == true;

    /// <summary>
    /// Whether the snap to the ground works on the curve: a road's or river's points hold heights.
    /// </summary>
    public bool CanSnap => Curve is HeightCurve curve && HeightCurves.IsAbsolute(curve.Profile) && curve.Points.Length > 0;

    /// <summary>
    /// Why the snap is off, or empty while it works.
    /// </summary>
    public string SnapReason => Curve is null ? "" : CanSnap ? "" : "Roads and rivers only: a ridge's, valley's or cliff's heights are a rise above the ground, not a place on it";

    /// <summary>
    /// What Even the grade evens: between the first and last chosen point, else the curve's ends.
    /// </summary>
    public (int From, int To)? EvenRange => Curve is not HeightCurve curve || curve.Points.Length < 3 ? null
        : _chosen.Length >= 2 ? _chosen[^1] - _chosen[0] >= 2 ? (_chosen[0], _chosen[^1]) : null
        : (0, curve.Points.Length - 1);

    /// <summary>
    /// The button's text: <c>Even the grade (points 2 to 7)</c>.
    /// </summary>
    public string EvenTitle => EvenRange is (int from, int to) ? $"Even the grade (points {from} to {to})" : "Even the grade";

    /// <summary>
    /// What the chosen points are, for the table's caption.
    /// </summary>
    public string ChosenText => _chosen.Length switch
    {
        0 => "No point chosen: click a row or a point with the curve tool; Ctrl or Shift and a click choose more.",
        1 => $"Point {_chosen[0]} chosen.",
        _ => $"Points {string.Join(", ", _chosen)} chosen.",
    };

    /// <summary>
    /// Shows a curve's points (null: none), keeping the chosen points that it still has.
    /// </summary>
    public void Show(MapDocument? document, string? layer, HeightCurve? curve)
    {
        if (ReferenceEquals(curve, Curve) && layer == Layer)
        {
            return;
        }
        using Performance.Span span = Performance.Measure("inspector.curve-nodes");
        bool otherLayer = layer != Layer;
        Layer = layer;
        Curve = curve;
        int count = curve?.Points.Length ?? 0;
        while (Rows.Count > count)
        {
            Rows.RemoveAt(Rows.Count - 1);
        }
        while (Rows.Count < count)
        {
            Rows.Add(new CurveNodeRow(this, Rows.Count));
        }
        if (curve is not null)
        {
            IReadOnlyList<CurveGrade> grades = count >= 2 ? HeightCurves.Grades(curve) : [];
            for (int i = 0; i < count; i++)
            {
                Rows[i].Show(curve, i, grades.FirstOrDefault(grade => grade.From == i));
            }
            MarkSteep();
        }
        int[] kept = otherLayer ? [] : [.. _chosen.Where(index => index < count)];
        if (!kept.SequenceEqual(_chosen))
        {
            SetChosen(kept, kept.Contains(Current) ? Current : kept.LastOrDefault(-1));
        }
        OnPropertyChanged(nameof(Fills));
        OnPropertyChanged(nameof(CanSnap));
        OnPropertyChanged(nameof(SnapReason));
        OnPropertyChanged(nameof(EvenTitle));
        ReverseCommand.NotifyCanExecuteChanged();
        EvenGradeCommand.NotifyCanExecuteChanged();
        SnapEndsCommand.NotifyCanExecuteChanged();
        SnapChosenCommand.NotifyCanExecuteChanged();
        SnapAllCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Chooses points (out of the curve's left out); <paramref name="current"/> is the one the keys
    /// change, the last of them when left out.
    /// </summary>
    public void Choose(IEnumerable<int> indices, int? current = null)
    {
        int count = Curve?.Points.Length ?? 0;
        int[] chosen = [.. indices.Where(index => index >= 0 && index < count).Distinct().Order()];
        int now = current is int c && chosen.Contains(c) ? c : chosen.Length == 0 ? -1 : chosen.Contains(Current) && current is null ? Current : chosen[^1];
        if (chosen.SequenceEqual(_chosen) && now == Current)
        {
            return;
        }
        SetChosen(chosen, now);
    }

    private void SetChosen(int[] chosen, int current)
    {
        _chosen = chosen;
        Current = current;
        for (int i = 0; i < Rows.Count; i++)
        {
            Rows[i].IsChosen = Array.BinarySearch(_chosen, i) >= 0;
        }
        OnPropertyChanged(nameof(Chosen));
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(ChosenText));
        OnPropertyChanged(nameof(EvenTitle));
        EvenGradeCommand.NotifyCanExecuteChanged();
        SnapChosenCommand.NotifyCanExecuteChanged();
        ChosenChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnGradeLimitChanged(double value) => MarkSteep();

    private void MarkSteep()
    {
        foreach (CurveNodeRow row in Rows)
        {
            row.IsSteep = row.SteepestPercent is float percent && MathF.Abs(percent) > GradeLimit;
        }
    }

    /// <summary>
    /// A field of a row given: a number sets the point's value, an empty field gives it back to the
    /// curve; anything else shows the value again.
    /// </summary>
    internal void Give(int index, string argument, string text, bool clearable)
    {
        if (Layer is null)
        {
            return;
        }
        JsonObject arguments = new JsonObject { ["layer"] = Layer, ["index"] = index };
        if (string.IsNullOrWhiteSpace(text) && clearable)
        {
            arguments["clear"] = argument;
        }
        else if (Setting.TryParse(text, out double value))
        {
            arguments[argument] = Math.Round(value, 4);
        }
        else
        {
            Rows[index].ShowAgain();
            return;
        }
        _ = run("terrain.setCurvePoint", arguments);
    }

    internal void GiveSharp(int index, bool sharp)
    {
        if (Layer is not null)
        {
            _ = run("terrain.setCurvePoint", new JsonObject { ["layer"] = Layer, ["index"] = index, ["sharp"] = sharp });
        }
    }

    private bool HasCurve() => Curve is { Points.Length: >= 2 };

    [RelayCommand(CanExecute = nameof(HasCurve))]
    private Task Reverse() => run("terrain.reverseCurve", new JsonObject { ["layer"] = Layer });

    private bool CanEven() => EvenRange is not null;

    [RelayCommand(CanExecute = nameof(CanEven))]
    private Task EvenGrade() => EvenRange is (int from, int to)
        ? run("terrain.evenGrade", new JsonObject { ["layer"] = Layer, ["from"] = from, ["to"] = to })
        : Task.CompletedTask;

    private bool CanSnapChosen() => CanSnap && _chosen.Length > 0;

    [RelayCommand(CanExecute = nameof(CanSnap))]
    private Task SnapEnds() => run("terrain.snapCurve", new JsonObject { ["layer"] = Layer, ["points"] = "ends" });

    [RelayCommand(CanExecute = nameof(CanSnapChosen))]
    private Task SnapChosen() => run("terrain.snapCurve", new JsonObject { ["layer"] = Layer, ["points"] = new JsonArray([.. _chosen.Select(index => (JsonNode)index)]) });

    [RelayCommand(CanExecute = nameof(CanSnap))]
    private Task SnapAll() => run("terrain.snapCurve", new JsonObject { ["layer"] = Layer, ["points"] = "all" });
}

/// <summary>
/// One point of the curve table (<see cref="CurveNodesViewModel"/>): its fields show its values,
/// empty where it takes the curve's (shown as the hint); a value typed runs
/// <c>terrain.setCurvePoint</c>.
/// </summary>
public sealed partial class CurveNodeRow(CurveNodesViewModel owner, int index) : ObservableObject
{
    private readonly string[] _texts = ["", "", "", "", "", "", ""];
    private bool _sharp;

    private static readonly (string Argument, bool Clearable)[] Fields =
    [
        ("x", false), ("z", false), ("height", false), ("width", true), ("falloffLeft", true), ("falloffRight", true), ("strength", true),
    ];

    public int Index => index;

    public string Number => index.ToString(CultureInfo.InvariantCulture);

    public string X { get => _texts[0]; set => Set(0, value); }

    public string Z { get => _texts[1]; set => Set(1, value); }

    public string Height { get => _texts[2]; set => Set(2, value); }

    public string Width { get => _texts[3]; set => Set(3, value); }

    public string FalloffLeft { get => _texts[4]; set => Set(4, value); }

    public string FalloffRight { get => _texts[5]; set => Set(5, value); }

    public string Strength { get => _texts[6]; set => Set(6, value); }

    public bool Sharp
    {
        get => _sharp;
        set
        {
            if (value != _sharp)
            {
                owner.GiveSharp(index, value);
            }
        }
    }

    [ObservableProperty]
    public partial string WidthHint { get; private set; } = "";

    [ObservableProperty]
    public partial string FalloffLeftHint { get; private set; } = "";

    [ObservableProperty]
    public partial string FalloffRightHint { get; private set; } = "";

    /// <summary>
    /// Whether the point's height is its own: not on a fill, which has one height.
    /// </summary>
    [ObservableProperty]
    public partial bool HeightEditable { get; private set; } = true;

    /// <summary>
    /// The grade to the next point, <c>12.5 % (7.1°)</c>; empty for the last of an open curve.
    /// </summary>
    [ObservableProperty]
    public partial string Grade { get; private set; } = "";

    /// <summary>
    /// The steepest grade to the next point, in percent; what the limit is compared with.
    /// </summary>
    public float? SteepestPercent { get; private set; }

    /// <summary>
    /// Whether the grade to the next point is somewhere above the table's limit.
    /// </summary>
    [ObservableProperty]
    public partial bool IsSteep { get; set; }

    [ObservableProperty]
    public partial bool IsChosen { get; set; }

    private void Set(int field, string value)
    {
        if (value != _texts[field])
        {
            owner.Give(index, Fields[field].Argument, value, Fields[field].Clearable);
        }
    }

    /// <summary>
    /// The fields show the values again (a text that is no number was typed).
    /// </summary>
    internal void ShowAgain()
    {
        foreach (string name in new[] { nameof(X), nameof(Z), nameof(Height), nameof(Width), nameof(FalloffLeft), nameof(FalloffRight), nameof(Strength) })
        {
            OnPropertyChanged(name);
        }
    }

    internal void Show(HeightCurve curve, int point, CurveGrade? grade)
    {
        CurvePoint p = curve.Points[point];
        // two decimals: the fields are narrow, and a point's place is hardly typed finer
        static string F(float? value) => value is float v ? Setting.Format(Math.Round(v, 2)) : "";
        string[] texts = [F(p.X), F(p.Z), F(p.Height), F(p.Width), F(p.FalloffLeft), F(p.FalloffRight), F(p.Strength)];
        string[] names = [nameof(X), nameof(Z), nameof(Height), nameof(Width), nameof(FalloffLeft), nameof(FalloffRight), nameof(Strength)];
        for (int i = 0; i < texts.Length; i++)
        {
            if (texts[i] != _texts[i])
            {
                _texts[i] = texts[i];
                OnPropertyChanged(names[i]);
            }
        }
        if (p.Sharp != _sharp)
        {
            _sharp = p.Sharp;
            OnPropertyChanged(nameof(Sharp));
        }
        WidthHint = Setting.Format(curve.Width);
        FalloffLeftHint = Setting.Format(curve.Falloff);
        FalloffRightHint = Setting.Format(curve.FalloffRight ?? curve.Falloff);
        HeightEditable = !curve.Fills;
        SteepestPercent = grade?.SteepestPercent;
        Grade = grade is null ? "" : string.Create(CultureInfo.InvariantCulture, $"{grade.Percent:0.#} % ({grade.Degrees:0.#}°) to point {grade.To}, steepest {grade.SteepestPercent:0.#} %");
    }
}
