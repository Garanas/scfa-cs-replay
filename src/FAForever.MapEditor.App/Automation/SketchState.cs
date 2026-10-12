using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Controls;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Automation;

/// <summary>
/// The state's <c>sketch</c> (user story N-1): the sheets of the map's sketch with their strokes and
/// legends, the sheet drawn into, the tools' settings, the line being drawn, and what
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
        SymbolTool symbol = model.Tool<SymbolTool>();
        LabelTool labelTool = model.Tool<LabelTool>();
        SketchSelectTool select = model.Tool<SketchSelectTool>();
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
                ["symbols"] = sheet.Symbols.Length,
                ["labels"] = sheet.Labels.Length,
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
            // the chosen sheet's symbols and labels (N-4): what they are, where, in which colour and how large
            ["symbols"] = chosen is null ? new JsonArray() : new JsonArray([.. chosen.Symbols.Take(ListedStrokes).Select(symbol => (JsonNode)new JsonObject
            {
                ["kind"] = symbol.Kind.ToString().ToLowerInvariant(),
                ["at"] = new JsonArray(Math.Round(symbol.Place.X, 2), Math.Round(symbol.Place.Y, 2)),
                ["colour"] = symbol.Colour.ToString().ToLowerInvariant(),
                ["size"] = Math.Round(symbol.Size, 2),
                ["turn"] = Math.Round(symbol.Turn, 1),
            })]),
            ["labels"] = chosen is null ? new JsonArray() : new JsonArray([.. chosen.Labels.Take(ListedStrokes).Select(label => (JsonNode)new JsonObject
            {
                ["text"] = label.Text,
                ["at"] = new JsonArray(Math.Round(label.Place.X, 2), Math.Round(label.Place.Y, 2)),
                ["colour"] = label.Colour.ToString().ToLowerInvariant(),
                ["size"] = Math.Round(label.Size, 2),
            })]),
            ["symbol"] = new JsonObject
            {
                ["kind"] = symbol.Kind.ToString().ToLowerInvariant(),
                ["colour"] = symbol.Colour.ToString().ToLowerInvariant(),
                ["size"] = symbol.Ogrids,
                ["turn"] = symbol.Turn,
                ["mirror"] = symbol.Mirror,
                ["state"] = symbol.Describe(),
            },
            ["label"] = new JsonObject
            {
                ["colour"] = labelTool.Colour.ToString().ToLowerInvariant(),
                ["size"] = labelTool.Ogrids,
                ["writing"] = model.Sketch.Draft is { } draft ? new JsonObject
                {
                    ["at"] = new JsonArray(Math.Round(draft.Place.X, 2), Math.Round(draft.Place.Y, 2)),
                    ["screen"] = new JsonArray(Math.Round(draft.Screen.X, 1), Math.Round(draft.Screen.Y, 1)),
                    ["text"] = model.Sketch.LabelText,
                    ["editing"] = draft.Index,
                } : null,
            },
            ["select"] = select.Describe(),
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
            ["note"] = model.SketchNote,
            ["key"] = document is null ? new JsonArray() : new JsonArray([.. model.Sketch.Keys(document).Select(key => (JsonNode)new JsonObject
            {
                ["sheet"] = key.Sheet,
                ["entries"] = new JsonArray([.. key.Entries.Select(entry => (JsonNode)$"{entry.Colour.ToString().ToLowerInvariant()}: {entry.Text}")]),
            })]),
            ["drawn"] = new JsonObject
            {
                ["sheets"] = top.SketchDrawn.Sheets,
                ["strokes"] = top.SketchDrawn.Strokes,
                ["culled"] = top.SketchDrawn.Culled,
                ["pending"] = top.SketchDrawn.Pending,
                ["symbols"] = top.SketchDrawn.Symbols,
                ["labels"] = top.SketchDrawn.Labels,
            },
        };
    }
}
