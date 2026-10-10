using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Adds an area (user story M-5), one step of undo, selects it and chooses the areas tool, whose
/// handles resize it. Its corners in ogrids, in any order; left out, a square in the middle of the
/// map, a quarter of its size. The first area is the playable area unless another is chosen
/// (<c>map.setPlayableArea</c>).
/// </summary>
/// <param name="Name">Left out, <c>AREA_</c> with the next free number.</param>
/// <returns>The area's name.</returns>
[EditorCommand("map.addArea", "Add an area", Story = "M-5", Needs = Needs.Map | Needs.NoStroke)]
public sealed record AddArea(string? Name = null, double? X0 = null, double? Z0 = null, double? X1 = null, double? Z1 = null) : IRequest<string>;

public sealed class AddAreaHandler(MainViewModel model, SelectionViewModel selection) : IRequestHandler<AddArea, string>
{
    public Task<string> Handle(AddArea request, CancellationToken cancellationToken)
    {
        MapDocument document = model.ShownDocument ?? throw new InvalidOperationException("No map is open");
        double quarter = Math.Min(document.Width, document.Height) / 8.0;
        double x0 = request.X0 ?? Math.Round(document.Width / 2.0 - quarter);
        double z0 = request.Z0 ?? Math.Round(document.Height / 2.0 - quarter);
        double x1 = request.X1 ?? Math.Round(document.Width / 2.0 + quarter);
        double z1 = request.Z1 ?? Math.Round(document.Height / 2.0 + quarter);
        Area area = AreaEdits.Normalized(new Area { Name = request.Name?.Trim() ?? "AREA_1", X0 = x0, Z0 = z0, X1 = x1, Z1 = z1 });
        if (area.X1 - area.X0 <= 0 || area.Z1 - area.Z0 <= 0)
        {
            throw new ArgumentException("An area is a rectangle of some size");
        }
        Area added = area;
        model.EditMap($"Add the area {area.Name}", d => d.AddArea(area, out added));
        selection.Set([ObjectRef.Area(added.Name)]);
        if (model.ActiveTool?.Definition.Id != "map.areas")
        {
            model.SelectTool("map.areas");
        }
        model.Status = $"Added the area {added.Name}: drag its corners or edges to resize it";
        return Task.FromResult(added.Name);
    }
}

/// <summary>
/// Renames an area, or moves its corners (user story M-5), one step of undo; the steps that change
/// the same area's same fields one after the other are one step. What the Map panel's list and
/// the inspector's fields do.
/// </summary>
/// <param name="Name">The area's name now.</param>
/// <param name="Rename">Its new name, which no other area may have.</param>
[EditorCommand("map.setArea", "Change an area", Story = "M-5", Needs = Needs.Map | Needs.NoStroke)]
public sealed record SetArea(string Name, string? Rename = null, double? X0 = null, double? Z0 = null, double? X1 = null, double? Z1 = null) : IRequest;

public sealed class SetAreaHandler(MainViewModel model, SelectionViewModel selection) : IRequestHandler<SetArea>
{
    public Task Handle(SetArea request, CancellationToken cancellationToken)
    {
        MapDocument document = model.ShownDocument ?? throw new InvalidOperationException("No map is open");
        if (!document.Markers.Areas.Any(area => area.Name == request.Name))
        {
            throw new ArgumentException($"The map has no area {request.Name}");
        }
        string? rename = request.Rename?.Trim();
        if (rename is { Length: 0 })
        {
            throw new ArgumentException("An area needs a name");
        }
        if (rename is not null && rename != request.Name && document.Markers.Areas.Any(area => area.Name == rename))
        {
            throw new ArgumentException($"The map has an area {rename} already");
        }
        bool wasSelected = selection.Contains(ObjectRef.Area(request.Name));
        string fields = string.Join(",", new (string, object?)[] { ("name", rename), ("x0", request.X0), ("z0", request.Z0), ("x1", request.X1), ("z1", request.Z1) }
            .Where(field => field.Item2 is not null).Select(field => field.Item1));
        model.EditMap(rename is null ? $"Change the area {request.Name}" : $"Rename the area {request.Name} to {rename}", d => d.UpdateArea(request.Name, area => AreaEdits.Normalized(area with
        {
            Name = rename ?? area.Name,
            X0 = request.X0 ?? area.X0,
            Z0 = request.Z0 ?? area.Z0,
            X1 = request.X1 ?? area.X1,
            Z1 = request.Z1 ?? area.Z1,
        })), $"map.setArea:{request.Name}:{fields}");
        if (rename is not null && wasSelected)
        {
            // the selection names things: the renamed area stays selected under its new name
            selection.Apply([ObjectRef.Area(rename)], SelectionChange.Add);
        }
        return Task.CompletedTask;
    }
}

/// <summary>
/// Removes an area by name (user story M-5), one step of undo: the Map panel's cross. A selected
/// area is removed with Delete too (<c>edit.delete</c>).
/// </summary>
[EditorCommand("map.removeArea", "Remove an area", Story = "M-5", Needs = Needs.Map | Needs.NoStroke)]
public sealed record RemoveArea(string Name) : IRequest;

public sealed class RemoveAreaHandler(MainViewModel model) : IRequestHandler<RemoveArea>
{
    public Task Handle(RemoveArea request, CancellationToken cancellationToken)
    {
        MapDocument document = model.ShownDocument ?? throw new InvalidOperationException("No map is open");
        if (!document.Markers.Areas.Any(area => area.Name == request.Name))
        {
            throw new ArgumentException($"The map has no area {request.Name}");
        }
        model.EditMap($"Remove the area {request.Name}", d => d.RemoveArea(request.Name));
        model.Status = document.PlayableArea?.Name == request.Name
            ? $"Removed the area {request.Name}, the playable area: {(model.ShownDocument?.PlayableArea is Area next ? $"the first area, {next.Name}, is the playable one now" : "the whole map is playable now")}"
            : $"Removed the area {request.Name}";
        return Task.CompletedTask;
    }
}
