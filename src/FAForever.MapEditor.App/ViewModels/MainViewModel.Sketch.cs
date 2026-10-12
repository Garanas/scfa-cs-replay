using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.App.Tools;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The Sketch mode (user story N-1): the map maker's drafts, shown over the terrain in every mode, the
/// chosen sheet and the strokes being drawn (<see cref="Sketch"/>). The sketch itself is the document's.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// The Sketch mode's sheets and the line being drawn.
    /// </summary>
    public SketchViewModel Sketch { get; private set; } = null!;

    /// <summary>
    /// Whether the views draw the sketch's shown sheets (the Sketch chip, <c>Ctrl+Shift+K</c>): in
    /// every mode, at the sheet's own opacity outside the Sketch mode.
    /// </summary>
    [ObservableProperty]
    public partial bool ShowSketch { get; set; } = true;

    partial void OnShowSketchChanged(bool value)
    {
        RefreshOverlays();
        RefreshSketchNote();
    }

    /// <summary>
    /// What the inspector of the other modes says of the sketch while it has strokes: which sheets show
    /// over the terrain at what opacity and how to hide them, or that it is hidden and how to show it;
    /// empty in the Sketch mode, whose inspector holds the sheet itself, and for a map without strokes.
    /// </summary>
    public string SketchNote { get; private set; } = "";

    public bool HasSketchNote => SketchNote.Length > 0;

    private void RefreshSketchNote()
    {
        string note = SketchNoteOf(ShownDocument?.Sketch);
        if (note != SketchNote)
        {
            SketchNote = note;
            OnPropertyChanged(nameof(SketchNote));
            OnPropertyChanged(nameof(HasSketchNote));
        }
    }

    // where the sketch is turned on and off: the chip is the third of the views' bar, so a narrow window has it behind More
    private const string SketchSwitches = "the Sketch chip in the views' bar (behind More when the window is narrow), the View menu or Ctrl+Shift+K";

    /// <summary>
    /// Whether the sketch is hidden (the Sketch chip is off); then says so in the status, as the Image tool does for the images:
    /// the Eraser and the Select tool leave what cannot be seen alone, while the tools that add (pencil, symbol, label) show the
    /// sketch again.
    /// </summary>
    public bool SketchIsHidden()
    {
        if (ShowSketch)
        {
            return false;
        }
        Status = $"The sketch is hidden: show it with {SketchSwitches}";
        return true;
    }

    private string SketchNoteOf(Project.Files.InMemory.Sketch? sketch)
    {
        if (InSketchMode || sketch is null || sketch.ItemCount == 0)
        {
            return "";
        }
        if (!ShowSketch)
        {
            return $"The sketch is hidden. Show it with {SketchSwitches}.";
        }
        string[] shown = [.. sketch.Sheets.Where(sheet => sheet.Shown && !sheet.IsBlank && sheet.Opacity > 0)
            .Select(sheet => $"{sheet.Name} at {Math.Round(sheet.Opacity * 100):0}%")];
        return shown.Length == 0
            ? "No sheet of the sketch shows in the other modes: each sheet is hidden or fully transparent here."
            : $"{string.Join(", ", shown)} over the terrain. Hide it with {SketchSwitches}.";
    }

    /// <summary>
    /// Whether the Sketch mode is the one chosen: its chosen sheet is then drawn fully and the others at
    /// their opacity for the other modes, as every sheet is in the other modes.
    /// </summary>
    public bool InSketchMode => SelectedMode?.Definition.Id == "sketch";

    /// <summary>
    /// The sheet the views draw fully: the chosen one while the Sketch mode is the one chosen, so that it can be
    /// told from the other shown sheets, which are drawn at their own opacity; null in the other modes, where
    /// every sheet is drawn at its own.
    /// </summary>
    public string? SketchDrawnFully => InSketchMode ? Sketch.Chosen : null;

    // whether the Sketch mode's Select tool holds a selection: what Delete acts on in that mode
    private bool SketchHasSelection => _tools.Values.OfType<Tools.SketchSelectTool>().FirstOrDefault()?.HasSelection == true;

    private void InitializeSketch()
    {
        Sketch = new SketchViewModel(RunWithAsync);
        // another map has its own sheets: nothing of the last one's stays chosen or half drawn
        AnotherMap += (_, _) => Sketch.Forget();
        DocumentChanged += (_, _) =>
        {
            Sketch.Refresh(ShownDocument);
            RefreshSketchNote();
        };
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SelectedMode))
            {
                OnPropertyChanged(nameof(InSketchMode));
                RefreshSketchNote();
            }
            else if (e.PropertyName == nameof(ActiveTool) && ActiveTool?.Tool is not (LabelTool or SketchSelectTool))
            {
                // a label being written belongs to the tools that write it
                Sketch.Drop();
            }
        };
    }
}
