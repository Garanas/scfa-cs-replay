using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Grows or crops the map to another of the game's sizes (user story M-3), one step of undo:
/// every grid and thing follows the old map to its place in the new one, and what lies outside is
/// removed (spawns move to the edge, areas are cut). Without a size it chooses the size tool, whose
/// options are this command's arguments and whose rectangle shows the new map in the views.
/// </summary>
/// <param name="Width">The new width in ogrids: 256, 512, 1024, 2048 or 4096.</param>
/// <param name="Height">The new height in ogrids; left out, the width (or the map's height when
/// neither is given with an offset).</param>
/// <param name="Anchor">Where the old map lies in the new one: <c>Centre</c> (left out),
/// <c>TopLeft</c>, <c>Top</c>, <c>TopRight</c>, <c>Left</c>, <c>Right</c>, <c>BottomLeft</c>,
/// <c>Bottom</c>, <c>BottomRight</c>.</param>
/// <param name="OffsetX">Where the old map's corner lands in the new one, in ogrids; given, it wins over the anchor.</param>
/// <param name="FillHeight">The height of the terrain the map gains; left out, the edges' heights continue.</param>
/// <param name="FitPlayableArea">Make the playable area the whole new map.</param>
/// <returns>What lay outside and was removed, as text.</returns>
[EditorCommand("map.resize", "Size and playable area...", Story = "M-3", Needs = Needs.Map | Needs.Idle | Needs.NoStroke)]
public sealed record ResizeMap(
    int? Width = null,
    int? Height = null,
    string? Anchor = null,
    int? OffsetX = null,
    int? OffsetZ = null,
    float? FillHeight = null,
    bool FitPlayableArea = false) : IRequest<string?>;

public sealed class ResizeMapHandler(MainViewModel model) : IRequestHandler<ResizeMap, string?>
{
    public async Task<string?> Handle(ResizeMap request, CancellationToken cancellationToken)
    {
        MapDocument document = model.ShownDocument ?? throw new InvalidOperationException("No map is open");
        if (request.Width is null && request.Height is null)
        {
            model.SelectTool("map.size");
            model.Status = "Choose the new size and where the old map lies in it, then Apply";
            return null;
        }
        MapAnchor anchor = MapAnchor.Centre;
        if (request.Anchor is string named && !Enum.TryParse(named, ignoreCase: true, out anchor))
        {
            throw new ArgumentException($"An anchor is one of {string.Join(", ", Enum.GetNames<MapAnchor>())}, not {named}");
        }
        int width = request.Width ?? document.Width;
        int height = request.Height ?? request.Width ?? document.Height;
        MapResizeSettings settings = new MapResizeSettings
        {
            Width = width,
            Height = height,
            Anchor = anchor,
            Offset = request.OffsetX is not null || request.OffsetZ is not null ? (request.OffsetX ?? 0, request.OffsetZ ?? 0) : null,
            FillHeight = request.FillHeight,
            FitPlayableArea = request.FitPlayableArea,
        };
        if (width == document.Width && height == document.Height && !request.FitPlayableArea
            && MapResize.OffsetOf(document.Width, document.Height, settings) == (0, 0))
        {
            throw new ArgumentException($"The map is {document.Width} by {document.Height} ogrids already and nothing moves: choose another size, move the old map or fit the playable area");
        }
        MapResizeResult result = await model.ResizeMapAsync(settings);
        return result.Removed.ToString();
    }
}

/// <summary>
/// Makes an area the playable area at the start (user story M-3), one step of undo; left out, the
/// first area is it again (the convention). The export makes the map's script set it.
/// </summary>
/// <param name="Name">The area's name.</param>
[EditorCommand("map.setPlayableArea", "Set the playable area", Story = "M-3", Needs = Needs.Map | Needs.NoStroke)]
public sealed record SetPlayableArea(string? Name = null) : IRequest;

public sealed class SetPlayableAreaHandler(MainViewModel model) : IRequestHandler<SetPlayableArea>
{
    public Task Handle(SetPlayableArea request, CancellationToken cancellationToken)
    {
        MapDocument document = model.ShownDocument ?? throw new InvalidOperationException("No map is open");
        string? name = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim();
        if (name is not null && !document.Markers.Areas.Any(area => area.Name == name))
        {
            throw new ArgumentException($"The map has no area {name}");
        }
        model.EditMap(name is null ? "Make the first area the playable area" : $"Make {name} the playable area", d => d.SetPlayableArea(name));
        model.Status = model.ShownDocument?.PlayableArea is Area area
            ? $"The playable area is {area.Name}: {area.X0:0.#}, {area.Z0:0.#} to {area.X1:0.#}, {area.Z1:0.#}"
            : "The map has no area: all of it is playable";
        return Task.CompletedTask;
    }
}
