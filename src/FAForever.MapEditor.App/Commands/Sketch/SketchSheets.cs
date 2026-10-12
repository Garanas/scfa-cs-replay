using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

// The sheets of the sketch (user story N-1): the commands of the Sketch panel's list and the inspector's fields.

/// <summary>
/// Adds a sheet to the sketch, one step of undo, and chooses it: the pencil draws into it.
/// </summary>
/// <param name="Name">Left out, a free one (<c>Sheet 1</c>).</param>
/// <returns>The new sheet's name.</returns>
[UndoStep]
[EditorCommand("sketch.addSheet", "Add a sheet", Story = "N-1")]
public sealed record AddSketchSheet(string? Name = null) : IRequest<string>;

public sealed class AddSketchSheetHandler(MainViewModel model, CurrentEdit edit) : IRequestHandler<AddSketchSheet, string>
{
    public Task<string> Handle(AddSketchSheet r, CancellationToken cancellationToken)
    {
        string added = "";
        edit.Transaction.Apply(d => d.AddSheet(r.Name, out added));
        model.Sketch.ChooseOnNextRefresh(added);
        model.Status = $"Added the sheet {added}";
        return Task.FromResult(added);
    }
}

/// <summary>
/// Gives a sheet another name, one step of undo.
/// </summary>
/// <param name="Name">The new name: not empty, not another sheet's (without regard to case).</param>
/// <param name="Sheet">The sheet; left out, the one chosen in the panel.</param>
[UndoStep]
[EditorCommand("sketch.renameSheet", "Rename the sheet", Story = "N-1")]
public sealed record RenameSketchSheet(string Name, string? Sheet = null) : IRequest;

public sealed class RenameSketchSheetHandler(MainViewModel model, CurrentEdit edit) : IRequestHandler<RenameSketchSheet>
{
    public Task Handle(RenameSketchSheet r, CancellationToken cancellationToken)
    {
        string sheet = SketchArguments.Sheet(model, edit.Transaction.Document, r.Sheet);
        edit.Transaction.Apply(d => d.RenameSheet(sheet, r.Name));
        string name = r.Name.Trim();
        if (model.Sketch.Chosen == sheet)
        {
            model.Sketch.ChooseOnNextRefresh(name);
        }
        model.Status = $"Renamed the sheet {sheet} to {name}";
        return Task.CompletedTask;
    }
}

/// <summary>
/// Adds a copy of a sheet (its strokes, legend and opacity) right after it, one step of undo, and chooses it.
/// </summary>
/// <param name="Name">The copy's name; left out, the original's with <c>copy</c> after it.</param>
/// <param name="Sheet">The sheet; left out, the one chosen in the panel.</param>
/// <returns>The copy's name.</returns>
[UndoStep]
[EditorCommand("sketch.duplicateSheet", "Duplicate the sheet", Story = "N-1")]
public sealed record DuplicateSketchSheet(string? Name = null, string? Sheet = null) : IRequest<string>;

public sealed class DuplicateSketchSheetHandler(MainViewModel model, CurrentEdit edit) : IRequestHandler<DuplicateSketchSheet, string>
{
    public Task<string> Handle(DuplicateSketchSheet r, CancellationToken cancellationToken)
    {
        string sheet = SketchArguments.Sheet(model, edit.Transaction.Document, r.Sheet);
        string added = "";
        edit.Transaction.Apply(d => d.DuplicateSheet(sheet, r.Name, out added));
        model.Sketch.ChooseOnNextRefresh(added);
        model.Status = $"Duplicated the sheet {sheet} as {added}";
        return Task.FromResult(added);
    }
}

/// <summary>
/// Deletes a sheet and its strokes, one step of undo.
/// </summary>
/// <param name="Sheet">The sheet; left out, the one chosen in the panel.</param>
[UndoStep]
[EditorCommand("sketch.deleteSheet", "Delete the sheet", Story = "N-1")]
public sealed record DeleteSketchSheet(string? Sheet = null) : IRequest;

public sealed class DeleteSketchSheetHandler(MainViewModel model, CurrentEdit edit) : IRequestHandler<DeleteSketchSheet>
{
    public Task Handle(DeleteSketchSheet r, CancellationToken cancellationToken)
    {
        string sheet = SketchArguments.Sheet(model, edit.Transaction.Document, r.Sheet);
        edit.Transaction.Apply(d => d.DeleteSheet(sheet));
        model.Status = $"Deleted the sheet {sheet}";
        return Task.CompletedTask;
    }
}

/// <summary>
/// Takes every stroke off a sheet, one step of undo; its legend stays.
/// </summary>
/// <param name="Sheet">The sheet; left out, the one chosen in the panel.</param>
[UndoStep]
[EditorCommand("sketch.clearSheet", "Clear the sheet", Story = "N-1")]
public sealed record ClearSketchSheet(string? Sheet = null) : IRequest;

public sealed class ClearSketchSheetHandler(MainViewModel model, CurrentEdit edit) : IRequestHandler<ClearSketchSheet>
{
    public Task Handle(ClearSketchSheet r, CancellationToken cancellationToken)
    {
        string sheet = SketchArguments.Sheet(model, edit.Transaction.Document, r.Sheet);
        edit.Transaction.Apply(d => d.ClearSheet(sheet));
        model.Status = $"Cleared the sheet {sheet}";
        return Task.CompletedTask;
    }
}

/// <summary>
/// Shows a sheet in the views or hides it, one step of undo (the eye of the panel's list).
/// </summary>
/// <param name="Sheet">The sheet; left out, the one chosen in the panel.</param>
/// <param name="On">Shown or hidden; left out, the other of what it is.</param>
[UndoStep]
[EditorCommand("sketch.showSheet", "Show or hide the sheet", Story = "N-1")]
public sealed record ShowSketchSheet(string? Sheet = null, bool? On = null) : IRequest;

public sealed class ShowSketchSheetHandler(MainViewModel model, CurrentEdit edit) : IRequestHandler<ShowSketchSheet>
{
    public Task Handle(ShowSketchSheet r, CancellationToken cancellationToken)
    {
        MapDocument document = edit.Transaction.Document;
        string sheet = SketchArguments.Sheet(model, document, r.Sheet);
        bool on = r.On ?? !document.SheetNamed(sheet).Shown;
        edit.Transaction.Apply(d => d.ShowSheet(sheet, on));
        model.Status = on ? $"Showing the sheet {sheet}" : $"Hid the sheet {sheet}";
        return Task.CompletedTask;
    }
}

/// <summary>
/// Chooses the sheet the pencil and the eraser work in, which the inspector shows. Not an edit of the map.
/// </summary>
[EditorCommand("sketch.chooseSheet", "Choose a sheet", Story = "N-1", Needs = Needs.Map)]
public sealed record ChooseSketchSheet(string Sheet) : IRequest;

public sealed class ChooseSketchSheetHandler(MainViewModel model) : IRequestHandler<ChooseSketchSheet>
{
    public Task Handle(ChooseSketchSheet r, CancellationToken cancellationToken)
    {
        model.Sketch.Refresh(model.ShownDocument);
        model.Sketch.Choose(r.Sheet);
        model.Status = $"Drawing into the sheet {model.Sketch.Chosen}";
        return Task.CompletedTask;
    }
}

/// <summary>
/// Sets what a colour means on a sheet, one step of undo: the legend, shown as a key in a corner of the top view.
/// </summary>
/// <param name="Colour">Red, yellow, green, blue, white or black, or 1 to 6.</param>
/// <param name="Text">What it means, at most 80 characters; empty for nothing.</param>
/// <param name="Sheet">The sheet; left out, the one chosen in the panel.</param>
[UndoStep]
[EditorCommand("sketch.setLegend", "Set the legend of a colour", Story = "N-1")]
public sealed record SetSketchLegend(System.Text.Json.Nodes.JsonNode Colour, string Text, string? Sheet = null) : IRequest;

public sealed class SetSketchLegendHandler(MainViewModel model, CurrentEdit edit) : IRequestHandler<SetSketchLegend>
{
    public Task Handle(SetSketchLegend r, CancellationToken cancellationToken)
    {
        SketchColour colour = SketchArguments.Colour(r.Colour);
        string sheet = SketchArguments.Sheet(model, edit.Transaction.Document, r.Sheet);
        edit.Transaction.Apply(d => d.SetLegend(sheet, colour, r.Text));
        model.Status = $"{sheet}: {colour.ToString().ToLowerInvariant()} means '{r.Text.Trim()}'";
        return Task.CompletedTask;
    }
}

/// <summary>
/// Sets how opaque a sheet is outside the Sketch mode, from 0 to 1; the steps that follow each
/// other are one step of undo (a slider dragged).
/// </summary>
/// <param name="Opacity">0 (invisible) to 1.</param>
/// <param name="Sheet">The sheet; left out, the one chosen in the panel.</param>
[EditorCommand("sketch.setOpacity", "Set the opacity of the sheet outside the Sketch mode", Story = "N-1", Needs = Needs.Map | Needs.NoStroke)]
public sealed record SetSketchOpacity(float Opacity, string? Sheet = null) : IRequest;

public sealed class SetSketchOpacityHandler(MainViewModel model) : IRequestHandler<SetSketchOpacity>
{
    public Task Handle(SetSketchOpacity r, CancellationToken cancellationToken)
    {
        MapDocument document = model.ShownDocument ?? throw new InvalidOperationException("No map is open");
        string sheet = SketchArguments.Sheet(model, document, r.Sheet);
        model.EditMap($"Set the opacity of the sheet {sheet}", d => d.SetSheetOpacity(sheet, r.Opacity), mergeKey: $"sketch.opacity:{sheet}");
        model.Status = $"{sheet}: {Math.Round(r.Opacity * 100)}% outside the Sketch mode";
        return Task.CompletedTask;
    }
}
