using System.Collections.Immutable;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The Sketch mode's sheets (user story N-1): each with its eye and its count of strokes; add,
/// duplicate and delete; the one chosen is the one the pencil and the eraser work in.
/// </summary>
public sealed record SketchSheetsSection() : PanelSection("Sheets");

/// <summary>
/// The chosen sheet of the sketch (user story N-1), the Sketch mode's inspector: its name, its
/// opacity outside the mode, its legend, its counts, Duplicate and Clear.
/// </summary>
public sealed record SketchSheetSection() : PanelSection("Sheet");

/// <summary>
/// A sheet of the sketch as the Sketch panel lists it (user story N-1): its eye, its name and how
/// many strokes it holds.
/// </summary>
public sealed partial class SketchSheetItem(string name, Func<string, JsonObject, Task> run) : ObservableObject
{
    public string Name { get; } = name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EyeLabel), nameof(EyeText))]
    public partial bool Shown { get; internal set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText), nameof(Label), nameof(Summary))]
    public partial int Strokes { get; internal set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText), nameof(Label), nameof(Summary))]
    public partial int Symbols { get; internal set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText), nameof(Label), nameof(Summary))]
    public partial int Labels { get; internal set; }

    [ObservableProperty]
    public partial bool IsChosen { get; internal set; }

    /// <summary>
    /// <c>34</c>, <c>1</c>: how many strokes, as a number for the panel's row.
    /// </summary>
    public string CountText => (Strokes + Symbols + Labels).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// <c>34 strokes · 3 symbols · 5 labels</c>: what the sheet holds, the kinds it has none of left out.
    /// </summary>
    public string Summary => Describe(Strokes, Symbols, Labels, " · ");

    /// <summary>
    /// What a screen reader says of its check box.
    /// </summary>
    public string EyeLabel => $"{(Shown ? "Hide" : "Show")} the sheet {Name}";

    /// <summary>
    /// What its eye says: <c>Shown</c> or <c>Hidden</c>.
    /// </summary>
    public string EyeText => Shown ? "Shown" : "Hidden";

    /// <summary>
    /// What a screen reader says of the row.
    /// </summary>
    public string Label => $"{Name}: {Describe(Strokes, Symbols, Labels, ", ")}{(Shown ? "" : ", hidden")}";

    /// <summary>
    /// How many strokes, symbols and labels, as a phrase; a sheet without anything holds <c>0 strokes</c>.
    /// </summary>
    public static string Describe(int strokes, int symbols, int labels, string separator)
    {
        List<string> parts = [];
        if (strokes > 0 || (symbols == 0 && labels == 0))
        {
            parts.Add($"{strokes} {(strokes == 1 ? "stroke" : "strokes")}");
        }
        if (symbols > 0)
        {
            parts.Add($"{symbols} {(symbols == 1 ? "symbol" : "symbols")}");
        }
        if (labels > 0)
        {
            parts.Add($"{labels} {(labels == 1 ? "label" : "labels")}");
        }
        return string.Join(separator, parts);
    }

    /// <summary>
    /// The check box of its eye: shows the sheet or hides it (<c>sketch.showSheet</c>).
    /// </summary>
    [RelayCommand]
    private Task ToggleShown() => run("sketch.showSheet", new JsonObject { ["sheet"] = Name, ["on"] = !Shown });

    public override string ToString() => Name;
}

/// <summary>
/// One of the six pencils in a row of choices: its colour, its key and whether it is the one in
/// hand (user story N-1).
/// </summary>
public sealed partial class SketchColourChoice(SketchColour colour, Func<SketchColour, Task> choose, string tool = "Pencil") : ObservableObject
{
    public SketchColour Colour => colour;

    /// <summary>
    /// As the model has it, <c>0xAARRGGBB</c>: the swatch.
    /// </summary>
    public uint Argb => SketchLook.Argb(colour);

    /// <summary>
    /// The key that picks it, <c>1</c> to <c>6</c>.
    /// </summary>
    public string Key => ((int)colour + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

    public string Title => colour.ToString();

    [ObservableProperty]
    public partial bool IsOn { get; internal set; }

    public string Label => $"{tool} {Title.ToLowerInvariant()}";

    public string Help => $"{Title}: key {Key}";

    [RelayCommand]
    private Task Choose() => choose(colour);
}

/// <summary>
/// One of the three widths in a row of choices (user story N-1).
/// </summary>
public sealed partial class SketchWidthChoice(SketchWidth width, Func<SketchWidth, Task> choose) : ObservableObject
{
    public SketchWidth Width => width;

    public string Title => width.ToString();

    [ObservableProperty]
    public partial bool IsOn { get; internal set; }

    public string Label => $"{Title} strokes";

    [RelayCommand]
    private Task Choose() => choose(width);
}

/// <summary>
/// The text of one colour of the chosen sheet's legend, as the inspector's field shows it
/// (user story N-1): what the colour means on this sheet.
/// </summary>
public sealed partial class SketchLegendItem(SketchColour colour, Func<SketchColour, string, Task> set) : ObservableObject
{
    public SketchColour Colour => colour;

    public uint Argb => SketchLook.Argb(colour);

    public string Label => $"Legend of {colour.ToString().ToLowerInvariant()}";

    private string _text = "";

    /// <summary>
    /// The text; typing it and leaving the field sets it (<c>sketch.setLegend</c>), one step of undo.
    /// </summary>
    public string Text
    {
        get => _text;
        set
        {
            if (value != _text)
            {
                _ = Edit(value);
            }
        }
    }

    internal void Show(string text)
    {
        if (text != _text)
        {
            _text = text;
            OnPropertyChanged(nameof(Text));
        }
    }

    private async Task Edit(string value)
    {
        await set(colour, value);
        // a text the command refused stays in the field until the sheet's own text is shown again
        OnPropertyChanged(nameof(Text));
    }
}

/// <summary>
/// The Sketch mode's state (user story N-1): the sheets the panel lists, the one chosen to draw into
/// and edit in the inspector, the legend of the chosen sheet, and the strokes being drawn that the
/// views show before they become part of the sketch. The sketch itself is the document's
/// (<see cref="MapDocument.Sketch"/>); every operation is a command (<c>sketch.*</c>).
/// </summary>
public sealed partial class SketchViewModel : ObservableObject
{
    private readonly Func<string, JsonObject, Task> _run;
    private Sketch? _shown;

    public SketchViewModel(Func<string, JsonObject, Task> run)
    {
        _run = run;
        Legend = [.. Enum.GetValues<SketchColour>().Select(colour => new SketchLegendItem(colour, SetLegend))];
    }

    /// <summary>
    /// The sheets, in the sketch's order.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<SketchSheetItem> Items { get; private set; } = [];

    /// <summary>
    /// The name of the sheet drawn into and shown in the inspector; null while the sketch has no sheet
    /// (the first stroke makes one).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChosenItem), nameof(HasChosen), nameof(ChosenName), nameof(ChosenOpacity), nameof(ChosenCount))]
    public partial string? Chosen { get; private set; }

    public bool HasChosen => Chosen is not null;

    public bool IsEmpty => Items.Count == 0;

    /// <summary>
    /// The chosen sheet as the list shows it, for the list's selection.
    /// </summary>
    public SketchSheetItem? ChosenItem
    {
        get => Items.FirstOrDefault(item => item.Name == Chosen);
        set
        {
            if (value is not null && value.Name != Chosen)
            {
                _ = _run("sketch.chooseSheet", new JsonObject { ["sheet"] = value.Name });
            }
        }
    }

    /// <summary>
    /// The panel's buttons: each runs the command of its name on the chosen sheet.
    /// </summary>
    [RelayCommand]
    private Task Add() => _run("sketch.addSheet", []);

    [RelayCommand]
    private Task Duplicate() => _run("sketch.duplicateSheet", []);

    [RelayCommand]
    private Task Delete() => _run("sketch.deleteSheet", []);

    [RelayCommand]
    private Task Clear() => _run("sketch.clearSheet", []);

    /// <summary>
    /// The chosen sheet's name; typing another and leaving the field renames it (<c>sketch.renameSheet</c>).
    /// </summary>
    public string ChosenName
    {
        get => Chosen ?? "";
        set
        {
            if (Chosen is string sheet && value != sheet)
            {
                _ = Rename(sheet, value);
            }
        }
    }

    private async Task Rename(string sheet, string name)
    {
        await _run("sketch.renameSheet", new JsonObject { ["sheet"] = sheet, ["name"] = name });
        // a name the command refused (taken, empty) goes back to the sheet's own
        OnPropertyChanged(nameof(ChosenName));
    }

    /// <summary>
    /// The chosen sheet's opacity outside the Sketch mode in percent, 0 to 100; dragging the slider
    /// sets it (<c>sketch.setOpacity</c>), one step of undo for the whole drag.
    /// </summary>
    public double ChosenOpacity
    {
        get => Math.Round(_chosenOpacity * 100);
        set
        {
            if (Chosen is string sheet && Math.Abs(value - ChosenOpacity) >= 0.5)
            {
                _chosenOpacity = (float)(value / 100);
                OnPropertyChanged();
                _ = _run("sketch.setOpacity", new JsonObject { ["sheet"] = sheet, ["opacity"] = Math.Round(value / 100, 2) });
            }
        }
    }

    private float _chosenOpacity = SketchSheet.DefaultOpacity;

    /// <summary>
    /// What the inspector says of the chosen sheet: <c>34 strokes</c>.
    /// </summary>
    public string ChosenCount => ChosenItem?.Summary ?? "";

    /// <summary>
    /// The six texts of the chosen sheet's legend, in the order of the colours.
    /// </summary>
    public IReadOnlyList<SketchLegendItem> Legend { get; }

    private Task SetLegend(SketchColour colour, string text) =>
        Chosen is string sheet
            ? _run("sketch.setLegend", new JsonObject { ["sheet"] = sheet, ["colour"] = colour.ToString(), ["text"] = text })
            : Task.CompletedTask;

    /// <summary>
    /// The sheets that are shown and have a legend text: what the key in a corner of the top view lists.
    /// </summary>
    public IReadOnlyList<(string Sheet, IReadOnlyList<(SketchColour Colour, string Text)> Entries)> Keys(MapDocument document) =>
        [.. document.Sketch.Sheets
            .Where(sheet => sheet.Shown && sheet.HasLegend)
            .Select(sheet => (sheet.Name, (IReadOnlyList<(SketchColour, string)>)[.. Enum.GetValues<SketchColour>().Where(colour => sheet.LegendOf(colour).Length > 0).Select(colour => (colour, sheet.LegendOf(colour)))]))];

    /// <summary>
    /// The strokes being drawn, which the views show over the sketch before the stroke is part of it
    /// (the pencil's line while the mouse is down, with its images); empty for none.
    /// </summary>
    public IReadOnlyList<SketchStroke> Pending { get; private set; } = [];

    /// <summary>
    /// Raised when <see cref="Pending"/> changes: the views draw again.
    /// </summary>
    public event EventHandler? PendingChanged;

    public void SetPending(IReadOnlyList<SketchStroke> strokes)
    {
        Pending = strokes;
        PendingChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Raised when another sheet is chosen.
    /// </summary>
    public event EventHandler? ChosenChanged;

    /// <summary>
    /// Chooses the sheet drawn into.
    /// </summary>
    /// <exception cref="ArgumentException">The sketch has no such sheet.</exception>
    public void Choose(string name)
    {
        string? found = Items.FirstOrDefault(item => item.Name == name)?.Name
            ?? Items.FirstOrDefault(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Name;
        Chosen = found ?? throw new ArgumentException(Items.Count == 0
            ? $"The sketch has no sheet '{name}': it has none yet"
            : $"The sketch has no sheet '{name}': {string.Join(", ", Items.Select(item => item.Name))}");
        Show(_shown);
    }

    partial void OnChosenChanged(string? value) => ChosenChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Shows the sketch of a document: the sheets are made again when their names change, else they
    /// follow their counts and eyes. The chosen sheet stays while the sketch has it, else the first
    /// is chosen; a sheet just made is chosen when <paramref name="choose"/> names it.
    /// </summary>
    public void Refresh(MapDocument? document, string? choose = null)
    {
        Sketch? sketch = document?.Sketch;
        if (ReferenceEquals(sketch, _shown) && choose is null && _wanted is null)
        {
            return;
        }
        Show(sketch, choose);
    }

    private string? _wanted;

    /// <summary>
    /// Chooses a sheet the next time the sketch is shown, when the command that makes it has
    /// finished (a sheet added, renamed or duplicated, which the pencil then draws into).
    /// </summary>
    public void ChooseOnNextRefresh(string name) => _wanted = name;

    private void Show(Sketch? sketch, string? choose = null)
    {
        _shown = sketch;
        if (sketch is null)
        {
            // no map: no label is being written
            Drop();
        }
        choose ??= _wanted;
        _wanted = null;
        ImmutableArray<SketchSheet> sheets = sketch is null ? [] : sketch.Sheets;
        bool sameNames = Items.Count == sheets.Length && Items.Zip(sheets).All(pair => pair.First.Name == pair.Second.Name);
        if (!sameNames)
        {
            Items = [.. sheets.Select(sheet => new SketchSheetItem(sheet.Name, _run))];
            OnPropertyChanged(nameof(IsEmpty));
        }
        string? chosen = choose is not null && sheets.Any(sheet => sheet.Name == choose) ? choose
            : Chosen is string held && sheets.Any(sheet => sheet.Name == held) ? held
            : sheets.FirstOrDefault()?.Name;
        for (int i = 0; i < sheets.Length; i++)
        {
            SketchSheetItem item = Items[i];
            item.Shown = sheets[i].Shown;
            item.Strokes = sheets[i].Strokes.Length;
            item.Symbols = sheets[i].Symbols.Length;
            item.Labels = sheets[i].Labels.Length;
            item.IsChosen = sheets[i].Name == chosen;
        }
        SketchSheet? shownSheet = sheets.FirstOrDefault(sheet => sheet.Name == chosen);
        _chosenOpacity = shownSheet?.Opacity ?? SketchSheet.DefaultOpacity;
        foreach (SketchLegendItem item in Legend)
        {
            item.Show(shownSheet?.LegendOf(item.Colour) ?? "");
        }
        if (chosen == Chosen)
        {
            // the same sheet: its fields follow what the document says now
            OnPropertyChanged(nameof(ChosenItem));
            OnPropertyChanged(nameof(ChosenName));
            OnPropertyChanged(nameof(ChosenOpacity));
            OnPropertyChanged(nameof(ChosenCount));
        }
        Chosen = chosen;
    }
}
