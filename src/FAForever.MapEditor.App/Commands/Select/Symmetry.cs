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
/// <param name="Kind"><c>off</c>, <c>rotational</c>, <c>leftRight</c>, <c>topBottom</c>, <c>diagonal</c> or <c>otherDiagonal</c>.</param>
/// <param name="Count">The players of a rotational symmetry, 2 to 8; 2 when left out.</param>
/// <returns>The symmetry chosen: its kind (null for off), count and title.</returns>
[EditorCommand("select.symmetry", "Symmetry on or off", Gesture = "Ctrl+Shift+Y", Story = "F-12")]
public sealed record ChooseSymmetry(string? Kind = null, int? Count = null) : IRequest<JsonObject>;

public sealed class ChooseSymmetryHandler(MainViewModel model, SymmetryViewModel symmetry) : IRequestHandler<ChooseSymmetry, JsonObject>
{
    public Task<JsonObject> Handle(ChooseSymmetry request, CancellationToken cancellationToken)
    {
        if (request.Kind is string kind)
        {
            symmetry.Set(SymmetryViewModel.Parse(kind), request.Count ?? 2);
        }
        else if (request.Count is not null)
        {
            throw new ArgumentException("Give the kind of symmetry with its count: kind=rotational count=4");
        }
        else
        {
            symmetry.Toggle(model.Map?.Document);
        }
        model.Status = symmetry.IsOn ? $"Symmetry: {symmetry.Chosen.Title}" : "Symmetry off";
        return Task.FromResult(Describe(symmetry));
    }

    public static JsonObject Describe(SymmetryViewModel symmetry) => new JsonObject
    {
        ["kind"] = symmetry.Chosen.Kind?.ToString(),
        ["count"] = symmetry.Chosen.Kind == MapSymmetryKind.Rotational ? symmetry.Chosen.Count : symmetry.IsOn ? 2 : 0,
        ["title"] = symmetry.Chosen.Title,
        ["mirrorsEdits"] = symmetry.MirrorsEdits,
        ["mirroring"] = symmetry.IsMirroring,
    };
}
