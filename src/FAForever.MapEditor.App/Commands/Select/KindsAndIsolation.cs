using System.Text.Json.Nodes;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Locks a kind of thing, or unlocks it (user story F-11): a locked kind shows but no click, box
/// or Select all picks it, so one can work among the props without taking the decals under them.
/// The select tools' options show the same choice as check boxes (what they select). Locking
/// takes the kind's things out of the selection.
/// </summary>
/// <param name="Kind"><c>markers</c>, <c>pathNodes</c>, <c>props</c>, <c>decals</c>, <c>units</c> or <c>areas</c>.</param>
/// <param name="Locked">Left out, it turns the lock over.</param>
/// <returns>The locked kinds.</returns>
[EditorCommand("select.lockKind", "Lock a kind", Story = "F-11")]
public sealed record LockKind(string Kind, bool? Locked = null) : IRequest<JsonArray>;

public sealed class LockKindHandler(MainViewModel model) : IRequestHandler<LockKind, JsonArray>
{
    public Task<JsonArray> Handle(LockKind request, CancellationToken cancellationToken)
    {
        ObjectKinds kind = SelectionKinds.Parse(request.Kind);
        bool locked = request.Locked ?? !model.LockedKinds.HasFlag(kind);
        model.LockedKinds = locked ? model.LockedKinds | kind : model.LockedKinds & ~kind;
        model.Status = locked ? $"{SelectionKinds.Title(kind)} are locked: clicks and boxes leave them" : $"{SelectionKinds.Title(kind)} can be selected again";
        return Task.FromResult(SelectionKinds.Describe(model.LockedKinds));
    }
}

/// <summary>
/// Hides a kind of thing in the views, or shows it again (user story F-11): what is hidden is not
/// picked either. Markers, path nodes, props, units and areas are the overlays of the same names;
/// decals are hidden in the 3D view.
/// </summary>
/// <param name="Kind"><c>markers</c>, <c>pathNodes</c>, <c>props</c>, <c>decals</c>, <c>units</c> or <c>areas</c>.</param>
/// <param name="Hidden">Left out, it turns it over.</param>
/// <returns>Whether the kind is hidden now.</returns>
[EditorCommand("select.hideKind", "Hide a kind", Story = "F-11")]
public sealed record HideKind(string Kind, bool? Hidden = null) : IRequest<bool>;

public sealed class HideKindHandler(MainViewModel model, SelectionViewModel selection) : IRequestHandler<HideKind, bool>
{
    public Task<bool> Handle(HideKind request, CancellationToken cancellationToken)
    {
        ObjectKinds kind = SelectionKinds.Parse(request.Kind);
        bool hidden = request.Hidden ?? model.ShownKinds.HasFlag(kind);
        switch (kind)
        {
            case ObjectKinds.Markers:
                model.ShowMarkers = !hidden;
                break;
            case ObjectKinds.PathNodes:
                model.ShowPathNodes = !hidden;
                break;
            case ObjectKinds.Props:
                model.ShowProps = !hidden;
                break;
            case ObjectKinds.Decals:
                model.ShowDecals = !hidden;
                break;
            case ObjectKinds.Units:
                model.ShowUnits = !hidden;
                break;
            case ObjectKinds.Areas:
                model.ShowAreas = !hidden;
                break;
        }
        // what is hidden is not picked: it leaves the selection too
        if (hidden && model.ShownDocument is { } document)
        {
            selection.Set([.. selection.Items.Where(thing => (MainViewModel.KindsOf(document, thing) & kind) == 0)]);
        }
        model.Status = hidden ? $"{SelectionKinds.Title(kind)} hidden" : $"{SelectionKinds.Title(kind)} shown";
        return Task.FromResult(hidden);
    }
}

/// <summary>
/// Isolates the selection (user story F-11): only the selected things show in both views and can
/// be picked, until it is run again (or another map opens). The terrain and its decals' textures
/// stay; other decals are left out of the 3D view.
/// </summary>
/// <param name="On">Left out, it turns isolation on (with a selection) or off.</param>
/// <returns>How many things are isolated; 0 when it ended.</returns>
[EditorCommand("select.isolate", "Isolate the selection", Gesture = "Ctrl+Shift+H", Story = "F-11", Needs = Needs.Map)]
public sealed record IsolateSelection(bool? On = null) : IRequest<int>;

public sealed class IsolateSelectionHandler(MainViewModel model) : IRequestHandler<IsolateSelection, int>
{
    public Task<int> Handle(IsolateSelection request, CancellationToken cancellationToken)
    {
        bool on = request.On ?? model.Isolated is null;
        if (on)
        {
            model.IsolateSelection();
            model.Status = $"Isolated {model.Isolated!.Count} thing{(model.Isolated.Count == 1 ? "" : "s")}: only they show and can be picked; Ctrl+Shift+H shows everything again";
            return Task.FromResult(model.Isolated.Count);
        }
        model.EndIsolation();
        model.Status = "Everything shows again";
        return Task.FromResult(0);
    }
}

/// <summary>
/// The kinds of things as the selection commands name them.
/// </summary>
public static class SelectionKinds
{
    /// <summary>
    /// The kinds one can lock or hide, in the order the options show them.
    /// </summary>
    public static IReadOnlyList<ObjectKinds> All { get; } = [ObjectKinds.Markers, ObjectKinds.PathNodes, ObjectKinds.Props, ObjectKinds.Decals, ObjectKinds.Units, ObjectKinds.Areas];

    /// <exception cref="ArgumentException">No such kind.</exception>
    public static ObjectKinds Parse(string name) => name.Trim().ToLowerInvariant() switch
    {
        "markers" or "marker" => ObjectKinds.Markers,
        "pathnodes" or "pathnode" or "path nodes" => ObjectKinds.PathNodes,
        "props" or "prop" => ObjectKinds.Props,
        "decals" or "decal" => ObjectKinds.Decals,
        "units" or "unit" => ObjectKinds.Units,
        "areas" or "area" => ObjectKinds.Areas,
        _ => throw new ArgumentException($"'{name}' is no kind of thing: markers, pathNodes, props, decals, units or areas"),
    };

    public static string Title(ObjectKinds kind) => kind switch
    {
        ObjectKinds.Markers => "Markers",
        ObjectKinds.PathNodes => "Path nodes",
        ObjectKinds.Props => "Props",
        ObjectKinds.Decals => "Decals",
        ObjectKinds.Units => "Units",
        ObjectKinds.Areas => "Areas",
        _ => kind.ToString(),
    };

    public static string Name(ObjectKinds kind) => kind switch
    {
        ObjectKinds.PathNodes => "pathNodes",
        _ => kind.ToString().ToLowerInvariant(),
    };

    public static JsonArray Describe(ObjectKinds kinds) => [.. All.Where(kind => kinds.HasFlag(kind)).Select(kind => (JsonNode)JsonValue.Create(Name(kind))!)];
}
