using System.Text.Json.Nodes;
using FAForever.MapEditor.App.ViewModels;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Sets how the handles of the selection work (user story F-11): whether the views draw them, what
/// turns and scales go about, and the grid a move snaps to with shift. Without arguments it shows
/// or hides the handles. <c>keepOnGrid</c> is whether a move keeps structures on the build grid (U-11).
/// </summary>
/// <param name="Show">Whether the handles show.</param>
/// <param name="Pivot"><c>selection</c> (turn and scale about the selection's centre) or <c>each</c>
/// (each thing about its own).</param>
/// <param name="Snap">The grid of a snapped move in ogrids: 0.5, 1 or 4.</param>
/// <param name="KeepOnGrid">Whether a move of structures is rounded onto the build grid.</param>
/// <returns>The settings: show, pivot, snap, keepOnGrid.</returns>
[EditorCommand("select.handles", "Handles on or off", Story = "F-11")]
public sealed record SetHandles(bool? Show = null, string? Pivot = null, float? Snap = null, bool? KeepOnGrid = null) : IRequest<JsonObject>;

public sealed class SetHandlesHandler(MainViewModel model) : IRequestHandler<SetHandles, JsonObject>
{
    public Task<JsonObject> Handle(SetHandles request, CancellationToken cancellationToken)
    {
        HandleSettings handles = model.Handles;
        if (request.Show is null && request.Pivot is null && request.Snap is null && request.KeepOnGrid is null)
        {
            handles.Show = !handles.Show;
        }
        if (request.Show is bool show)
        {
            handles.Show = show;
        }
        if (request.Pivot is string pivot)
        {
            handles.Pivot = ParsePivot(pivot);
        }
        if (request.Snap is float snap)
        {
            handles.Snap = handles.SnapOf(snap);
        }
        if (request.KeepOnGrid is bool keep)
        {
            handles.KeepOnGrid = keep;
        }
        model.Status = handles.Show ? $"Handles shown; turn and scale about {Describe(handles.Pivot)}, snap to {handles.Snap.Title}" : "Handles hidden";
        return Task.FromResult(Describe(handles));
    }

    public static HandlePivot ParsePivot(string pivot) => pivot.ToLowerInvariant() switch
    {
        "selection" => HandlePivot.Selection,
        "each" => HandlePivot.Each,
        _ => throw new ArgumentException($"A pivot is selection or each, not {pivot}"),
    };

    public static string Describe(HandlePivot pivot) => pivot == HandlePivot.Each ? "each thing's own centre" : "the selection's centre";

    public static JsonObject Describe(HandleSettings handles) => new JsonObject
    {
        ["show"] = handles.Show,
        ["pivot"] = handles.Pivot == HandlePivot.Each ? "each" : "selection",
        ["snap"] = handles.Snap.Step,
        ["keepOnGrid"] = handles.KeepOnGrid,
    };
}
