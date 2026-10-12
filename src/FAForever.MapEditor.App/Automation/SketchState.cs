using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Controls;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Automation;

/// <summary>
/// The state's <c>sketch</c> (user story N-1): the sheets of the map's sketch with their strokes and
/// legends, the sheet drawn into, the pencil's and eraser's settings, the line being drawn, and what
/// the top view drew last (<c>drawn</c>).
/// </summary>
internal static class SketchState
{
    /// <summary>
    /// Strokes of the chosen sheet listed with their places' count and ends; the rest is counted.
    /// </summary>
    private const int ListedStrokes = 200;

    public static JsonObject Describe(MainViewModel model, HeightmapView top)
    {
        PencilTool pencil = model.Tool<PencilTool>();
        EraserTool eraser = model.Tool<EraserTool>();
        MapDocument? document = model.ShownDocument;
        Sketch sketch = document?.Sketch ?? Sketch.Empty;
        SketchSheet? chosen = model.Sketch.Chosen is string name ? sketch.Find(name) : null;
        return new JsonObject
        {
            ["show"] = model.ShowSketch,
            ["inMode"] = model.InSketchMode,
            ["chosen"] = model.Sketch.Chosen,
            ["strokes"] = sketch.StrokeCount,
            ["sheets"] = new JsonArray([.. sketch.Sheets.Select(sheet => (JsonNode)new JsonObject
            {
                ["name"] = sheet.Name,
                ["shown"] = sheet.Shown,
                ["opacity"] = sheet.Opacity,
                ["strokes"] = sheet.Strokes.Length,
                ["places"] = sheet.Strokes.Sum(stroke => stroke.Points.Length),
                ["legend"] = new JsonObject([.. Enum.GetValues<SketchColour>().Select(colour => KeyValuePair.Create(colour.ToString().ToLowerInvariant(), (JsonNode?)JsonValue.Create(sheet.LegendOf(colour))))]),
            })]),
            // the chosen sheet's strokes: colour, width, how many places, the first and the last
            ["list"] = chosen is null ? new JsonArray() : new JsonArray([.. chosen.Strokes.Take(ListedStrokes).Select(stroke => (JsonNode)new JsonObject
            {
                ["colour"] = stroke.Colour.ToString().ToLowerInvariant(),
                ["width"] = stroke.Width.ToString().ToLowerInvariant(),
                ["places"] = stroke.Points.Length,
                ["from"] = new JsonArray(Math.Round(stroke.Points[0].X, 2), Math.Round(stroke.Points[0].Y, 2)),
                ["to"] = new JsonArray(Math.Round(stroke.Points[^1].X, 2), Math.Round(stroke.Points[^1].Y, 2)),
            })]),
            ["pencil"] = new JsonObject
            {
                ["colour"] = pencil.Colour.ToString().ToLowerInvariant(),
                ["width"] = pencil.Width.ToString().ToLowerInvariant(),
                ["mirror"] = pencil.Mirror,
                ["smooth"] = pencil.Smooth,
                ["drawing"] = pencil.IsDrawing,
            },
            ["eraser"] = new JsonObject
            {
                ["radius"] = eraser.Radius,
                ["mirror"] = eraser.Mirror,
                ["whole"] = eraser.Whole,
            },
            ["pending"] = model.Sketch.Pending.Count,
            ["key"] = document is null ? new JsonArray() : new JsonArray([.. model.Sketch.Keys(document).Select(key => (JsonNode)new JsonObject
            {
                ["sheet"] = key.Sheet,
                ["entries"] = new JsonArray([.. key.Entries.Select(entry => (JsonNode)$"{entry.Colour.ToString().ToLowerInvariant()}: {entry.Text}")]),
            })]),
            ["drawn"] = new JsonObject
            {
                ["sheets"] = top.SketchDrawn.Sheets,
                ["strokes"] = top.SketchDrawn.Strokes,
                ["pending"] = top.SketchDrawn.Pending,
            },
        };
    }
}
