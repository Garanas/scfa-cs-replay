using CommunityToolkit.Mvvm.ComponentModel;

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

    private string SketchNoteOf(Project.Files.InMemory.Sketch? sketch)
    {
        if (InSketchMode || sketch is null || sketch.StrokeCount == 0)
        {
            return "";
        }
        if (!ShowSketch)
        {
            return "The sketch is hidden. The Sketch chip in the views' bar shows it, or Ctrl+Shift+K.";
        }
        string[] shown = [.. sketch.Sheets.Where(sheet => sheet.Shown && !sheet.Strokes.IsEmpty && sheet.Opacity > 0)
            .Select(sheet => $"{sheet.Name} at {Math.Round(sheet.Opacity * 100):0}%")];
        return shown.Length == 0
            ? "No sheet of the sketch shows in the other modes: each sheet is hidden or fully transparent here."
            : $"{string.Join(", ", shown)} over the terrain. The Sketch chip in the views' bar hides it, or Ctrl+Shift+K.";
    }

    /// <summary>
    /// Whether the Sketch mode is the one chosen: its sheets are then drawn fully, else at their
    /// opacity for the other modes.
    /// </summary>
    public bool InSketchMode => SelectedMode?.Definition.Id == "sketch";

    private void InitializeSketch()
    {
        Sketch = new SketchViewModel(RunWithAsync);
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
        };
    }
}
