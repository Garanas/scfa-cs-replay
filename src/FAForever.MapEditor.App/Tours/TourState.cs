using System.Text.Json;
using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Automation;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;

namespace FAForever.MapEditor.App.Tours;

/// <summary>
/// The part of the editor's state a tour can wait on (<c>until</c>), read cheaply from the model
/// without automation: the paths and values are those of automation's <c>state</c>
/// (<c>TourTest</c> checks that they agree), so a step is tried with <c>wait path=... value=...</c>.
/// </summary>
public static class TourState
{
    /// <summary>
    /// The paths a step may wait on, as written in a tour file.
    /// </summary>
    public static readonly IReadOnlyList<string> Paths =
    [
        "tab", "mode", "tool", "dock", "palette", "selection.count", "show.<overlay>", "generated.<result>", "units.place.unit", "layout.modePanel", "layout.inspector", "layout.dock", "undo",
    ];

    /// <summary>
    /// Whether a path is one of <see cref="Paths"/>.
    /// </summary>
    public static bool Known(string path) => path.StartsWith("show.", StringComparison.Ordinal) && path.Length > 5
        || path.StartsWith("generated.", StringComparison.Ordinal) && path.Length > 10
        || Paths.Contains(path, StringComparer.Ordinal);

    /// <summary>
    /// The view's name in the state's <c>tab</c>.
    /// </summary>
    public static string TabName(MapViewTab view) => view switch
    {
        MapViewTab.Top => "top",
        MapViewTab.ThreeD => "3d",
        _ => "side",
    };

    /// <summary>
    /// The value at a path, or null.
    /// </summary>
    public static JsonNode? Read(MainViewModel model, string path) => AutomationServer.At(Describe(model), path);

    /// <summary>
    /// Whether the value at a path is the one a step waits for: numbers by value, the rest by their text.
    /// </summary>
    public static bool Holds(MainViewModel model, TourCondition condition) =>
        AutomationServer.Matches(Read(model, condition.Path), JsonNode.Parse(condition.Value.GetRawText()));

    /// <summary>
    /// The state's part that tours read.
    /// </summary>
    public static JsonObject Describe(MainViewModel model) => new JsonObject
    {
        ["tab"] = TabName(model.View),
        ["mode"] = model.SelectedMode?.Definition.Id,
        ["tool"] = model.ActiveTool?.Definition.Id,
        ["dock"] = model.Dock.ToString(),
        ["palette"] = model.Palette.IsOpen,
        ["selection"] = new JsonObject { ["count"] = model.Selection.Count },
        ["show"] = new JsonObject([.. model.Overlays.Select(overlay => KeyValuePair.Create(overlay.Id, (JsonNode?)JsonValue.Create(overlay.IsOn)))]),
        // how each generated result stands (Missing, OutOfDate, Current, ...): the state's procedural.rows[].state by the row's id
        ["generated"] = new JsonObject([.. model.Procedural.Rows.Select(row => KeyValuePair.Create(row.Id, (JsonNode?)JsonValue.Create(row.Row.State.ToString())))]),
        ["layout"] = new JsonObject
        {
            ["modePanel"] = model.Layout.ShowModePanel,
            ["inspector"] = model.Layout.ShowInspector,
            ["dock"] = model.Layout.ShowDock,
        },
        ["units"] = new JsonObject { ["place"] = new JsonObject { ["unit"] = model.Tool<UnitPlaceTool>().Unit?.Key } },
        ["undo"] = model.Map?.Session.UndoDescription,
    };
}
