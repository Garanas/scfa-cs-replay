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

    partial void OnShowSketchChanged(bool value) => RefreshOverlays();

    /// <summary>
    /// Whether the Sketch mode is the one chosen: its sheets are then drawn fully, else at their
    /// opacity for the other modes.
    /// </summary>
    public bool InSketchMode => SelectedMode?.Definition.Id == "sketch";

    private void InitializeSketch()
    {
        Sketch = new SketchViewModel(RunWithAsync);
        DocumentChanged += (_, _) => Sketch.Refresh(ShownDocument);
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SelectedMode))
            {
                OnPropertyChanged(nameof(InSketchMode));
            }
        };
    }
}
