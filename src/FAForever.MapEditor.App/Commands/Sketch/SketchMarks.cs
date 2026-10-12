using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

// The sketch's symbols and labels and the selecting of its items (user story N-4).

/// <summary>
/// Stamps a symbol on a sheet of the sketch (user story N-4), one step of undo: what the Symbol tool does
/// when the mouse is released. The symbol is repeated by the map's symmetry (Mirror), an arrow turned with
/// it. A sketch without sheets gets its first for the symbol, and a hidden sheet is shown.
/// </summary>
/// <param name="X">Where the symbol's centre goes, in ogrids.</param>
/// <param name="Z">Where the symbol's centre goes, in ogrids.</param>
/// <param name="Kind">Spawn, mass, hydrocarbon, arrow, cross or question; left out, the tool's.</param>
/// <param name="Colour">Red, yellow, green, blue, white or black, or 1 to 6; left out, the tool's.</param>
/// <param name="Size">Small, medium or large, or a number of ogrids; left out, the tool's.</param>
/// <param name="Turn">Degrees for an arrow, 0 towards +x, clockwise on the top view; left out, the tool's.</param>
/// <param name="Mirror">Left out, the tool's.</param>
/// <param name="Sheet">The sheet to stamp into; left out, the one chosen in the panel.</param>
/// <returns>How many symbols were made (more than one with the symmetry).</returns>
[UndoStep]
[EditorCommand("sketch.symbol", "Stamp a symbol in the sketch", Story = "N-4")]
public sealed record StampSketchSymbol(
    float X,
    float Z,
    string? Kind = null,
    JsonNode? Colour = null,
    JsonNode? Size = null,
    float? Turn = null,
    bool? Mirror = null,
    string? Sheet = null) : IRequest<int>;

public sealed class StampSketchSymbolHandler(MainViewModel model, CurrentEdit edit) : IRequestHandler<StampSketchSymbol, int>
{
    public Task<int> Handle(StampSketchSymbol r, CancellationToken cancellationToken)
    {
        SymbolTool tool = model.Tool<SymbolTool>();
        SketchSymbolKind kind = r.Kind is { } k ? SketchLook.ParseSymbol(k) : tool.Kind;
        SketchColour colour = r.Colour is { } c ? SketchArguments.Colour(c) : tool.Colour;
        float size = r.Size is { } s ? SketchArguments.Size(s, label: false) : tool.Ogrids;
        float turn = r.Turn is float t && float.IsFinite(t) ? SketchMarkEdits.Normalized(t) : tool.Turn;

        MapDocument document = edit.Transaction.Document;
        IReadOnlyList<SketchSymbol> symbols = tool.Symbols(document, new Vector2(r.X, r.Z), turn, r.Mirror ?? tool.Mirror, kind, colour, size);
        string used = "";
        edit.Transaction.Apply(d =>
        {
            string? sheet = SketchArguments.SheetForNew(model, d, r.Sheet, out d);
            return d.AddSymbols(sheet, symbols, out used).ShowSheet(used, true);
        });
        model.Sketch.ChooseOnNextRefresh(used);
        model.ShowSketch = true;
        model.Status = $"Stamped {(symbols.Count == 1 ? "a" : symbols.Count.ToString())} {kind.ToString().ToLowerInvariant()}{(symbols.Count == 1 ? "" : "s")} in {used}{(symbols.Count > 1 ? ", mirrored by the symmetry" : "")}";
        return Task.FromResult(symbols.Count);
    }
}

/// <summary>
/// Writes a label on a sheet of the sketch (user story N-4), one step of undo: what the Label tool does when
/// Enter keeps the text. A label is written once, never repeated by the symmetry.
/// </summary>
/// <param name="Text">The text, one line of at most 120 characters.</param>
/// <param name="X">Where the label's text is centred, in ogrids.</param>
/// <param name="Z">Where the label's text is centred, in ogrids.</param>
/// <param name="Colour">Red, yellow, green, blue, white or black, or 1 to 6; left out, the tool's.</param>
/// <param name="Size">Small, medium or large, or the height of the letters in ogrids; left out, the tool's.</param>
/// <param name="Sheet">The sheet to write in; left out, the one chosen in the panel.</param>
[UndoStep]
[EditorCommand("sketch.label", "Write a label in the sketch", Story = "N-4")]
public sealed record WriteSketchLabel(string Text, float X, float Z, JsonNode? Colour = null, JsonNode? Size = null, string? Sheet = null) : IRequest;

public sealed class WriteSketchLabelHandler(MainViewModel model, CurrentEdit edit) : IRequestHandler<WriteSketchLabel>
{
    public Task Handle(WriteSketchLabel r, CancellationToken cancellationToken)
    {
        LabelTool tool = model.Tool<LabelTool>();
        SketchColour colour = r.Colour is { } c ? SketchArguments.Colour(c) : tool.Colour;
        float size = r.Size is { } s ? SketchArguments.Size(s, label: true) : tool.Ogrids;
        string used = "";
        edit.Transaction.Apply(d =>
        {
            string? sheet = SketchArguments.SheetForNew(model, d, r.Sheet, out d);
            return d.AddLabel(sheet, new SketchLabel(r.Text, new Vector2(r.X, r.Z), colour, size), out used).ShowSheet(used, true);
        });
        model.Sketch.ChooseOnNextRefresh(used);
        model.ShowSketch = true;
        model.Status = $"Wrote the label {r.Text.Trim()} in {used}";
        return Task.CompletedTask;
    }
}

/// <summary>
/// Changes the text of a label of the sketch (user story N-4), one step of undo: what a double click on
/// a label with the Select tool does when Enter keeps the text.
/// </summary>
/// <param name="Text">The new text.</param>
/// <param name="Label">The label's place in its sheet's list; left out, the one label that is selected.</param>
/// <param name="Sheet">The sheet; left out, the one chosen in the panel.</param>
[UndoStep]
[EditorCommand("sketch.editLabel", "Change the text of a label", Story = "N-4")]
public sealed record EditSketchLabel(string Text, int? Label = null, string? Sheet = null) : IRequest;

public sealed class EditSketchLabelHandler(MainViewModel model, CurrentEdit edit) : IRequestHandler<EditSketchLabel>
{
    public Task Handle(EditSketchLabel r, CancellationToken cancellationToken)
    {
        MapDocument document = edit.Transaction.Document;
        string sheet = SketchArguments.Sheet(model, document, r.Sheet);
        int label = r.Label ?? SketchArguments.OnlyLabel(model, sheet);
        edit.Transaction.Apply(d => d.SetLabelText(sheet, label, r.Text));
        model.Status = $"Changed the label to {r.Text.Trim()}";
        return Task.CompletedTask;
    }
}

/// <summary>
/// Sets up the Symbol tool (user story N-4): the symbol, its colour, its size, the turn of an arrow and
/// Mirror, and chooses the tool. What the options row's choices do.
/// </summary>
/// <param name="Kind">Spawn, mass, hydrocarbon, arrow, cross or question.</param>
/// <param name="Colour">Red, yellow, green, blue, white or black, or 1 to 6.</param>
/// <param name="Size">Small, medium or large, or a number of ogrids from 1 to 200.</param>
/// <param name="Turn">Degrees for an arrow.</param>
/// <param name="Mirror">Whether a symbol is repeated by the map's symmetry.</param>
[EditorCommand("sketch.setSymbol", "Set up the Symbol tool", Story = "N-4", Needs = Needs.Map)]
public sealed record SetSketchSymbol(string? Kind = null, JsonNode? Colour = null, JsonNode? Size = null, float? Turn = null, bool? Mirror = null) : IRequest;

public sealed class SetSketchSymbolHandler(MainViewModel model) : IRequestHandler<SetSketchSymbol>
{
    public Task Handle(SetSketchSymbol r, CancellationToken cancellationToken)
    {
        SymbolTool tool = model.Tool<SymbolTool>();
        if (r.Kind is { } kind)
        {
            tool.Kind = SketchLook.ParseSymbol(kind);
        }
        if (r.Colour is { } colour)
        {
            tool.Colour = SketchArguments.Colour(colour);
        }
        if (r.Size is { } size)
        {
            SketchArguments.SizeChoice(size, label: false, out SketchSize? choice, out float ogrids);
            if (choice is SketchSize named)
            {
                tool.Size = named;
            }
            tool.Custom = choice is null ? ogrids : null;
        }
        if (r.Turn is float turn && float.IsFinite(turn))
        {
            tool.Turn = SketchMarkEdits.Normalized(turn);
        }
        if (r.Mirror is bool mirror)
        {
            tool.Mirror = mirror;
        }
        if (model.ActiveTool?.Definition.Id != "sketch.symbol")
        {
            model.SelectTool("sketch.symbol");
        }
        model.Status = $"Symbol: {tool.Kind.ToString().ToLowerInvariant()}, {tool.Colour.ToString().ToLowerInvariant()}, {tool.Ogrids:0.#} ogrids{(tool.Kind == SketchSymbolKind.Arrow ? $", turned {tool.Turn:0.#} degrees" : "")}{(tool.Mirror ? ", mirrored" : "")}";
        return Task.CompletedTask;
    }
}

/// <summary>
/// Sets up the Label tool (user story N-4): the colour and size of the letters, and chooses the tool.
/// </summary>
/// <param name="Colour">Red, yellow, green, blue, white or black, or 1 to 6.</param>
/// <param name="Size">Small, medium or large, or the height of the letters in ogrids from 1 to 200.</param>
[EditorCommand("sketch.setLabel", "Set up the Label tool", Story = "N-4", Needs = Needs.Map)]
public sealed record SetSketchLabel(JsonNode? Colour = null, JsonNode? Size = null) : IRequest;

public sealed class SetSketchLabelHandler(MainViewModel model) : IRequestHandler<SetSketchLabel>
{
    public Task Handle(SetSketchLabel r, CancellationToken cancellationToken)
    {
        LabelTool tool = model.Tool<LabelTool>();
        if (r.Colour is { } colour)
        {
            tool.Colour = SketchArguments.Colour(colour);
        }
        if (r.Size is { } size)
        {
            SketchArguments.SizeChoice(size, label: true, out SketchSize? choice, out float ogrids);
            if (choice is SketchSize named)
            {
                tool.Size = named;
            }
            tool.Custom = choice is null ? ogrids : null;
        }
        if (model.ActiveTool?.Definition.Id != "sketch.label")
        {
            model.SelectTool("sketch.label");
        }
        model.Status = $"Label: {tool.Colour.ToString().ToLowerInvariant()}, letters {tool.Ogrids:0.#} ogrids high";
        return Task.CompletedTask;
    }
}

/// <summary>
/// Selects strokes, symbols and labels of a sheet for the Select tool (user story N-4), and chooses the
/// tool. Without arguments it selects nothing. The selection is the tool's, not an edit of the map.
/// </summary>
/// <param name="At">A place <c>[x, z]</c> in ogrids: the topmost item within <paramref name="Reach"/> of it.</param>
/// <param name="Box">A box <c>[x0, z0, x1, z1]</c> in ogrids: what it crosses.</param>
/// <param name="All">Every item of the sheet.</param>
/// <param name="Add">Keep what is selected and add to it (Shift).</param>
/// <param name="Reach">How near an item <paramref name="At"/> must lie, in ogrids; left out, 2.</param>
/// <param name="Sheet">The sheet; left out, the one chosen in the panel.</param>
/// <returns>How many items are selected now.</returns>
[EditorCommand("sketch.select", "Select in the sketch", Story = "N-4", Needs = Needs.Map)]
public sealed record SelectSketchItems(float[]? At = null, float[]? Box = null, bool? All = null, bool? Add = null, float? Reach = null, string? Sheet = null) : IRequest<int>;

public sealed class SelectSketchItemsHandler(MainViewModel model) : IRequestHandler<SelectSketchItems, int>
{
    public Task<int> Handle(SelectSketchItems r, CancellationToken cancellationToken)
    {
        SketchSelectTool tool = model.Tool<SketchSelectTool>();
        MapDocument document = model.ShownDocument ?? throw new InvalidOperationException("No map is open");
        if (model.ActiveTool?.Definition.Id != "sketch.select")
        {
            model.SelectTool("sketch.select");
        }
        if (r.At is null && r.Box is null && r.All != true)
        {
            tool.Clear();
            model.Status = "Selected nothing in the sketch";
            return Task.FromResult(0);
        }
        string name = SketchArguments.Sheet(model, document, r.Sheet);
        SketchSheet sheet = document.SheetNamed(name);
        ImmutableArray<SketchItem> items;
        if (r.All == true)
        {
            items = [.. Enumerable.Range(0, sheet.Strokes.Length).Select(i => new SketchItem(SketchItemKind.Stroke, i)),
                .. Enumerable.Range(0, sheet.Symbols.Length).Select(i => new SketchItem(SketchItemKind.Symbol, i)),
                .. Enumerable.Range(0, sheet.Labels.Length).Select(i => new SketchItem(SketchItemKind.Label, i))];
        }
        else if (r.Box is { } box)
        {
            if (box.Length != 4 || box.Any(v => !float.IsFinite(v)))
            {
                throw new ArgumentException("box is [x0, z0, x1, z1] in ogrids");
            }
            items = SketchPicking.InBox(sheet, new Vector2(box[0], box[1]), new Vector2(box[2], box[3]));
        }
        else
        {
            float[] at = r.At!;
            if (at.Length != 2 || at.Any(v => !float.IsFinite(v)))
            {
                throw new ArgumentException("at is [x, z] in ogrids");
            }
            items = SketchPicking.Pick(sheet, new Vector2(at[0], at[1]), r.Reach ?? 2) is { } hit ? [hit] : [];
        }
        tool.Select(name, items, r.Add == true);
        int count = tool.Selected.Length;
        model.Status = $"Selected {count} {(count == 1 ? "item" : "items")} of {name}";
        return Task.FromResult(count);
    }
}

/// <summary>
/// Moves the selected items of the sketch (user story N-4), one step of undo: what dragging them does.
/// </summary>
/// <param name="Dx">How far, in ogrids; towards +x.</param>
/// <param name="Dz">How far, in ogrids; towards +z.</param>
[UndoStep]
[EditorCommand("sketch.move", "Move the selected items of the sketch", Story = "N-4")]
public sealed record MoveSketchSelection(float Dx, float Dz) : IRequest<int>;

public sealed class MoveSketchSelectionHandler(MainViewModel model, CurrentEdit edit) : IRequestHandler<MoveSketchSelection, int>
{
    public Task<int> Handle(MoveSketchSelection r, CancellationToken cancellationToken)
    {
        (string sheet, ImmutableArray<SketchItem> items) = SketchArguments.Selected(model);
        edit.Transaction.Apply(d => d.MoveItems(sheet, items, r.Dx, r.Dz));
        model.Status = $"Moved {items.Length} {(items.Length == 1 ? "item" : "items")} of {sheet} by {r.Dx:0.#}, {r.Dz:0.#} ogrids";
        return Task.FromResult(items.Length);
    }
}

/// <summary>
/// Gives the selected items of the sketch another colour (user story N-4), one step of undo.
/// </summary>
/// <param name="Colour">Red, yellow, green, blue, white or black, or 1 to 6.</param>
[UndoStep]
[EditorCommand("sketch.recolour", "Recolour the selected items of the sketch", Story = "N-4")]
public sealed record RecolourSketchSelection(JsonNode Colour) : IRequest<int>;

public sealed class RecolourSketchSelectionHandler(MainViewModel model, CurrentEdit edit) : IRequestHandler<RecolourSketchSelection, int>
{
    public Task<int> Handle(RecolourSketchSelection r, CancellationToken cancellationToken)
    {
        (string sheet, ImmutableArray<SketchItem> items) = SketchArguments.Selected(model);
        SketchColour colour = SketchArguments.Colour(r.Colour);
        edit.Transaction.Apply(d => d.RecolourItems(sheet, items, colour));
        model.Status = $"Recoloured {items.Length} {(items.Length == 1 ? "item" : "items")} of {sheet} {colour.ToString().ToLowerInvariant()}";
        return Task.FromResult(items.Length);
    }
}

/// <summary>
/// Sizes the selected symbols and labels of the sketch (user story N-4), one step of undo; strokes have
/// no size and stay as they are.
/// </summary>
/// <param name="Size">Small, medium or large, or a number of ogrids from 1 to 200 (the symbol's width, the label's letters' height).</param>
[UndoStep]
[EditorCommand("sketch.resize", "Resize the selected symbols and labels of the sketch", Story = "N-4")]
public sealed record ResizeSketchSelection(JsonNode Size) : IRequest<int>;

public sealed class ResizeSketchSelectionHandler(MainViewModel model, CurrentEdit edit) : IRequestHandler<ResizeSketchSelection, int>
{
    public Task<int> Handle(ResizeSketchSelection r, CancellationToken cancellationToken)
    {
        (string sheet, ImmutableArray<SketchItem> items) = SketchArguments.Selected(model);
        SketchArguments.SizeChoice(r.Size, label: false, out SketchSize? choice, out float ogrids);
        MapDocument document = edit.Transaction.Document;
        SketchSheet held = document.SheetNamed(sheet);
        // a named size is the symbol's width for a symbol and the letters' height for a label
        foreach (SketchItem item in items.Where(item => item.Kind != SketchItemKind.Stroke))
        {
            float size = choice is SketchSize named ? (item.Kind == SketchItemKind.Symbol ? SketchLook.SymbolOgrids(named) : SketchLook.LabelOgrids(named)) : ogrids;
            edit.Transaction.Apply(d => d.ResizeItems(sheet, [item], size));
        }
        int count = items.Count(item => item.Kind != SketchItemKind.Stroke);
        model.Status = $"Sized {count} {(count == 1 ? "item" : "items")} of {held.Name}";
        return Task.FromResult(count);
    }
}

/// <summary>
/// Removes the selected items of the sketch (user story N-4), one step of undo, and selects nothing.
/// </summary>
[UndoStep]
[EditorCommand("sketch.deleteSelected", "Delete the selected items of the sketch", Story = "N-4")]
public sealed record DeleteSketchSelection : IRequest<int>;

public sealed class DeleteSketchSelectionHandler(MainViewModel model, CurrentEdit edit) : IRequestHandler<DeleteSketchSelection, int>
{
    public Task<int> Handle(DeleteSketchSelection r, CancellationToken cancellationToken)
    {
        (string sheet, ImmutableArray<SketchItem> items) = SketchArguments.Selected(model);
        edit.Transaction.Apply(d => d.DeleteItems(sheet, items));
        model.Tool<SketchSelectTool>().Clear();
        model.Status = $"Deleted {items.Length} {(items.Length == 1 ? "item" : "items")} of {sheet}";
        return Task.FromResult(items.Length);
    }
}
