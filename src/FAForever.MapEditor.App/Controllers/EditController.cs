using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.ViewModels;

namespace FAForever.MapEditor.App.Controllers;

/// <summary>
/// The Edit and Select menus: undo, the selection, symmetry and presets.
/// </summary>
public sealed class EditController(MainViewModel model)
{
    public bool CanUndo => model.CanUndo;

    public bool CanRedo => model.CanRedo;

    [EditorCommand("edit.undo", "Undo", Menu = "Edit", Gesture = "Ctrl+Z", Story = "F-3", CanRun = nameof(CanUndo))]
    public void Undo() => model.Undo();

    [EditorCommand("edit.redo", "Redo", Menu = "Edit", Gesture = "Ctrl+Y", Story = "F-3", CanRun = nameof(CanRedo))]
    public void Redo() => model.Redo();

    [Incomplete("F-11", "Copy the selected markers, props, decals or units")]
    [EditorCommand("edit.copy", "Copy", Menu = "Edit", Gesture = "Ctrl+C", Story = "F-11", SeparatorBefore = true)]
    public void Copy()
    {
    }

    [Incomplete("F-11", "Paste what was copied, where the mouse is")]
    [EditorCommand("edit.paste", "Paste", Menu = "Edit", Gesture = "Ctrl+V", Story = "F-11")]
    public void Paste()
    {
    }

    [Incomplete("F-11", "Duplicate the selection")]
    [EditorCommand("edit.duplicate", "Duplicate", Menu = "Edit", Gesture = "Ctrl+D", Story = "F-11")]
    public void Duplicate()
    {
    }

    [Incomplete("F-11", "Delete the selection")]
    [EditorCommand("edit.delete", "Delete", Menu = "Edit", Story = "F-11")]
    public void Delete()
    {
    }

    [Incomplete("F-10", "Save the selection as a preset in the workspace")]
    [EditorCommand("edit.saveAsPreset", "Save the selection as a preset...", Menu = "Edit", Story = "F-10", SeparatorBefore = true)]
    public void SaveAsPreset()
    {
    }

    [Incomplete("F-11", "Select everything of the active mode")]
    [EditorCommand("select.all", "Select all", Menu = "Select", Gesture = "Ctrl+A", Story = "F-11")]
    public void SelectAll()
    {
    }

    [Incomplete("F-11", "Select nothing")]
    [EditorCommand("select.none", "Select none", Menu = "Select", Story = "F-11")]
    public void SelectNone()
    {
    }

    [Incomplete("F-12", "Mirror every edit: horizontally, vertically, diagonally, or around the middle for 2 to 8 players")]
    [EditorCommand("select.symmetry", "Symmetry...", Menu = "Select", Story = "F-12", SeparatorBefore = true)]
    public void Symmetry()
    {
    }
}
