using System.Runtime.CompilerServices;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// Undo and redo bring back the selection (user story F-3): each version of the document
/// remembers what was selected while it was the current one, and going back to a version selects
/// that again, as far as those things exist in it (undoing a delete selects what came back). The
/// status line says what was undone or redone.
/// </summary>
public sealed partial class MainViewModel
{
    // the selection of each version of the document, while the session's history holds it
    private readonly ConditionalWeakTable<MapDocument, ObjectRef[]> _selectionOf = new ConditionalWeakTable<MapDocument, ObjectRef[]>();

    private bool _restoringSelection;

    private void InitializeUndoSelection()
    {
        Selection.Changed += (_, _) => RememberSelection();
        Events.DocumentChanged += OnDocumentChangedForSelection;
    }

    /// <summary>
    /// Notes the selection as the current version's: not while an edit is going on (a command that
    /// deletes clears the selection inside its step, and the version before it keeps what was selected).
    /// </summary>
    private void RememberSelection()
    {
        if (_restoringSelection || Map is not EditedMap map || map.Session.InTransaction || map.Session.InGroup)
        {
            return;
        }
        _selectionOf.AddOrUpdate(map.Session.Document, [.. Selection.Items]);
    }

    private void OnDocumentChangedForSelection(DocumentChange change)
    {
        switch (change.Reason)
        {
            case ChangeReason.Edit:
                // the step's own selection: what its command left selected
                _selectionOf.AddOrUpdate(change.Current, [.. Selection.Items]);
                break;
            case ChangeReason.Derive:
                // the same version with a generated result in it: the same selection
                if (_selectionOf.TryGetValue(change.Previous, out ObjectRef[]? kept))
                {
                    _selectionOf.AddOrUpdate(change.Current, kept);
                }
                break;
            case ChangeReason.Undo or ChangeReason.Redo:
            {
                _selectionOf.AddOrUpdate(change.Previous, [.. Selection.Items]);
                Status = $"{(change.Reason == ChangeReason.Undo ? "Undone" : "Redone")}: {change.Description}";
                if (_selectionOf.TryGetValue(change.Current, out ObjectRef[]? selected))
                {
                    _restoringSelection = true;
                    try
                    {
                        Selection.Set(selected.Where(change.Current.Exists));
                    }
                    finally
                    {
                        _restoringSelection = false;
                    }
                }
                break;
            }
        }
    }
}
