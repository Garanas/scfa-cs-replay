using FAForever.MapEditor.Project.Editing;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The status line says what the last operation did: a step of undo made by the mouse or a key (a
/// stroke, a drag, a tool's key) that says nothing itself gets <c>Done: ...</c>, as a command with
/// <c>[UndoStep]</c> does, and the texts of placing and counting read alike everywhere.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// Whether a command with <c>[UndoStep]</c> is running: it says what it did itself, or gets
    /// <c>Done: </c> its title when its transaction ends (<c>UndoStepBehavior</c>).
    /// </summary>
    internal bool InCommandStep { get; set; }

    /// <summary>
    /// A new step of undo outside a command (a stroke, a drag, an edit of a tool or panel): the
    /// status line says it was done, unless what made it says more right after.
    /// </summary>
    private void SayDone(DocumentChange change)
    {
        if (change.Reason != ChangeReason.Edit || InCommandStep || Map?.Session is not { InTransaction: false, InGroup: false })
        {
            return;
        }
        Status = Selection.HasAny ? $"Done: {change.Description} ({Selection.Summary} selected)" : $"Done: {change.Description}";
    }

    /// <summary>
    /// What follows the name of something placed with the symmetry: <c> and 1 mirrored</c>, or
    /// nothing when it was placed alone.
    /// </summary>
    /// <param name="placed">How many were placed, itself and its images.</param>
    public static string Mirrored(int placed) => placed > 1 ? $" and {placed - 1} mirrored" : "";

    /// <summary>
    /// <c>1 unit</c>, <c>3 units</c>.
    /// </summary>
    public static string Units(int count) => count == 1 ? "1 unit" : $"{count} units";

    /// <summary>
    /// What is selected now, for the status line of a command that changes the selection.
    /// </summary>
    public string SelectedText => Selection.HasAny ? $"Selected {Selection.Summary}" : "Selected nothing";
}
