using System.Numerics;
using System.Text.Json.Nodes;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// What the sketch's commands share (user story N-1): places, colours, widths and the sheet an
/// argument leaves out.
/// </summary>
internal static class SketchArguments
{
    /// <summary>
    /// A line's places as <c>[[x, z], ...]</c>, in ogrids.
    /// </summary>
    /// <exception cref="ArgumentException">No places, or a place that is not two numbers.</exception>
    public static Vector2[] Points(float[][]? points, string name = "points")
    {
        if (points is null || points.Length == 0)
        {
            throw new ArgumentException($"Give {name} as [[x, z], ...] in ogrids: at least one place");
        }
        return [.. points.Select(point => point is { Length: 2 } && float.IsFinite(point[0]) && float.IsFinite(point[1])
            ? new Vector2(point[0], point[1])
            : throw new ArgumentException($"A place of {name} is two numbers [x, z], not [{string.Join(", ", point ?? [])}]"))];
    }

    /// <summary>
    /// A colour by its name (<c>red</c>) or its number from 1 to 6, as a JSON string or number.
    /// </summary>
    public static SketchColour Colour(JsonNode node) => SketchLook.ParseColour(Text(node));

    /// <summary>
    /// A width by its name (<c>thick</c>).
    /// </summary>
    public static SketchWidth Width(JsonNode node) => SketchLook.ParseWidth(Text(node));

    private static string Text(JsonNode node) => node is JsonValue value
        ? value.TryGetValue(out string? text) ? text ?? "" : value.ToString()
        : throw new ArgumentException("Give a name or a number");

    /// <summary>
    /// The sheet an argument names, else the one chosen in the panel, else the first.
    /// </summary>
    /// <exception cref="ArgumentException">The sketch has no sheet, or no such sheet.</exception>
    public static string Sheet(MainViewModel model, MapDocument document, string? name)
    {
        if (name is not null)
        {
            return document.SheetNamed(name).Name;
        }
        if (document.Sketch.IsEmpty)
        {
            throw new ArgumentException("The sketch has no sheet yet: add one, or draw a stroke");
        }
        return model.Sketch.Chosen is string chosen && document.Sketch.Find(chosen) is { } held ? held.Name : document.Sketch.Sheets[0].Name;
    }

    /// <summary>
    /// A size by its name (<c>large</c>) or a number of ogrids, as a JSON string or number.
    /// </summary>
    /// <param name="choice">The named size; null when a number was given.</param>
    /// <param name="ogrids">The number given, or the named size's for a symbol or a label.</param>
    /// <exception cref="ArgumentException">Not a size, or a number outside 1 to 200.</exception>
    public static void SizeChoice(JsonNode node, bool label, out SketchSize? choice, out float ogrids)
    {
        string text = Text(node).Trim();
        if (float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float number))
        {
            if (!(number >= SketchLook.MinimumOgrids && number <= SketchLook.MaximumOgrids))
            {
                throw new ArgumentException($"A size lies between {SketchLook.MinimumOgrids} and {SketchLook.MaximumOgrids} ogrids, not {text}");
            }
            choice = null;
            ogrids = number;
            return;
        }
        SketchSize size = SketchLook.ParseSize(text);
        choice = size;
        ogrids = label ? SketchLook.LabelOgrids(size) : SketchLook.SymbolOgrids(size);
    }

    /// <summary>
    /// A size in ogrids: a number, or the named size's for a symbol or a label.
    /// </summary>
    public static float Size(JsonNode node, bool label)
    {
        SizeChoice(node, label, out _, out float ogrids);
        return ogrids;
    }

    /// <summary>
    /// The sheet a symbol or a label goes into: the one named, else the one chosen; null for a sketch without
    /// sheets (the first one is made for it), and a sheet that does not exist yet is added to the document.
    /// </summary>
    public static string? SheetForNew(MainViewModel model, MapDocument document, string? name, out MapDocument result)
    {
        string? sheet = name ?? (document.Sketch.IsEmpty ? null : Sheet(model, document, null));
        result = document;
        if (sheet is not null && !document.Sketch.IsEmpty && document.Sketch.Find(sheet) is null)
        {
            result = document.AddSheet(sheet, out sheet);
        }
        return sheet;
    }

    /// <summary>
    /// What the Select tool has selected, and in which sheet.
    /// </summary>
    /// <exception cref="ArgumentException">Nothing is selected.</exception>
    public static (string Sheet, System.Collections.Immutable.ImmutableArray<SketchItem> Items) Selected(MainViewModel model)
    {
        Tools.SketchSelectTool tool = model.Tool<Tools.SketchSelectTool>();
        System.Collections.Immutable.ImmutableArray<SketchItem> items = tool.Selected;
        if (items.IsEmpty)
        {
            throw new ArgumentException("Nothing of the sketch is selected: select items first (the Select tool, or sketch.select)");
        }
        return (tool.Sheet, items);
    }

    /// <summary>
    /// The one label that is selected, as its place in the sheet.
    /// </summary>
    /// <exception cref="ArgumentException">No label is selected, or several.</exception>
    public static int OnlyLabel(MainViewModel model, string sheet)
    {
        Tools.SketchSelectTool tool = model.Tool<Tools.SketchSelectTool>();
        SketchItem[] labels = [.. tool.Selected.Where(item => item.Kind == SketchItemKind.Label)];
        return tool.Sheet == sheet && labels.Length == 1
            ? labels[0].Index
            : throw new ArgumentException("Give the label: its place in the sheet's list, or select just one label");
    }

    /// <summary>
    /// How many strokes, as a phrase: <c>2 strokes</c>.
    /// </summary>
    public static string Strokes(int count) => count == 1 ? "1 stroke" : $"{count} strokes";
}
