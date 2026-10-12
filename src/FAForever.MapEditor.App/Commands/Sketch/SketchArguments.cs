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
    /// How many strokes, as a phrase: <c>2 strokes</c>.
    /// </summary>
    public static string Strokes(int count) => count == 1 ? "1 stroke" : $"{count} strokes";
}
