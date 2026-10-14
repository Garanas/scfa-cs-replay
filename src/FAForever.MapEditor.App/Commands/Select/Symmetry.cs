using System.Text.Json.Nodes;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.App.ViewModels;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Chooses the symmetry edits are mirrored by (user story F-12), whose guides the top view draws.
/// Without a kind it turns the symmetry on or off: on, the one used last, else the map's own (from
/// its spawns and deposits), else rotational for two players.
/// </summary>
/// <param name="Kind"><c>off</c>, <c>point</c>, <c>rotational</c>, <c>leftRight</c>, <c>topBottom</c>, <c>diagonal</c>,
/// <c>otherDiagonal</c>, <c>bothWays</c>, <c>bothDiagonals</c>, <c>kaleidoscope</c> (turned and mirrored) or <c>eightWay</c>.</param>
/// <param name="Count">The players of a rotational symmetry, 2 to 8, or the turns of a kaleidoscope, 3 to 8; 2 when left out
/// (4 for <c>eightWay</c>).</param>
/// <param name="Around"><c>map</c>: around the whole map; <c>playableArea</c>: around the playable area as it is now. Left
/// out, what the symmetry was around stays.</param>
/// <returns>The symmetry chosen: its kind (null for off), count, title, parts and what it is around.</returns>
[EditorCommand("select.symmetry", "Symmetry on or off", Gesture = "Ctrl+Shift+Y", Story = "F-12")]
public sealed record ChooseSymmetry(string? Kind = null, int? Count = null, string? Around = null) : IRequest<JsonObject>;

public sealed class ChooseSymmetryHandler(MainViewModel model, SymmetryViewModel symmetry) : IRequestHandler<ChooseSymmetry, JsonObject>
{
    public Task<JsonObject> Handle(ChooseSymmetry request, CancellationToken cancellationToken)
    {
        if (request.Around is string around)
        {
            switch (around.ToLowerInvariant())
            {
                case "map" or "whole" or "wholemap":
                    symmetry.Around = null;
                    break;
                case "playablearea" or "playable":
                    symmetry.Around = symmetry.PlayableArea
                        ?? throw new ArgumentException("The playable area is the whole map: the symmetry is around it already");
                    break;
                default:
                    throw new ArgumentException($"A symmetry is around 'map' or 'playableArea', not '{around}'");
            }
        }
        if (request.Kind is string kind)
        {
            symmetry.Set(SymmetryViewModel.Parse(kind), SymmetryViewModel.CountOf(kind) ?? request.Count ?? 2);
        }
        else if (request.Around is not null)
        {
            if (!symmetry.IsOn)
            {
                symmetry.Toggle(model.Map?.Document);
            }
        }
        else if (request.Count is not null)
        {
            throw new ArgumentException("Give the kind of symmetry with its count: kind=rotational count=4");
        }
        else
        {
            symmetry.Toggle(model.Map?.Document);
        }
        model.Status = symmetry.IsOn ? $"Symmetry: {symmetry.Chosen.Title}{(symmetry.Around is null ? "" : ", around the playable area")}" : "Symmetry off";
        return Task.FromResult(Describe(symmetry));
    }

    public static JsonObject Describe(SymmetryViewModel symmetry) => new JsonObject
    {
        ["kind"] = symmetry.Chosen.Kind?.ToString(),
        ["count"] = symmetry.Chosen.Kind is MapSymmetryKind kind && MapSymmetry.HasCount(kind) ? symmetry.Chosen.Count : symmetry.IsOn ? 2 : 0,
        ["title"] = symmetry.Chosen.Title,
        ["parts"] = symmetry.IsOn ? symmetry.Chosen.Parts : 0,
        ["around"] = symmetry.Around is MapSymmetryArea area ? new JsonArray(area.X0, area.Z0, area.X1, area.Z1) : null,
        ["aroundText"] = symmetry.AroundText,
        ["playableArea"] = symmetry.PlayableArea is MapSymmetryArea playable ? new JsonArray(playable.X0, playable.Z0, playable.X1, playable.Z1) : null,
        ["mirrorsEdits"] = symmetry.MirrorsEdits,
        ["mirroring"] = symmetry.IsMirroring,
    };
}
