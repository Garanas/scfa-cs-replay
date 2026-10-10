using System.Text.Json.Nodes;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Adds the selected things' mirror partners to the selection (user story F-12): in every other part
/// of the map by the chosen symmetry, the thing of the same kind and blueprint (texture, resource,
/// type, layer) within an ogrid of the image of its place.
/// </summary>
/// <returns>How many partners were found and how many things are selected now.</returns>
[EditorCommand("select.symmetryPartners", "Select the mirror partners", Story = "F-12", Needs = Needs.Map | Needs.Selection)]
public sealed record SelectSymmetryPartners : IRequest<JsonObject>;

public sealed class SelectSymmetryPartnersHandler(MainViewModel model, SymmetryViewModel symmetry, SelectionViewModel selection) : IRequestHandler<SelectSymmetryPartners, JsonObject>
{
    public Task<JsonObject> Handle(SelectSymmetryPartners request, CancellationToken cancellationToken)
    {
        if (!symmetry.IsOn || model.ShownDocument is not { } document)
        {
            throw new InvalidOperationException("Choose a symmetry first (Select, Symmetry on or off)");
        }
        SymmetryPartner[] partners = [.. document.Partners(selection.Items, symmetry.Transforms(document))];
        selection.Apply(partners.Select(partner => partner.Partner), SelectionChange.Add);
        model.Status = partners.Length == 0 ? "The selection has no mirror partners" : $"Selected {partners.Length} mirror partners";
        return Task.FromResult(new JsonObject { ["partners"] = partners.Length, ["selected"] = selection.Count });
    }
}

/// <summary>
/// Turns the mirroring of edits by the chosen symmetry on or off (user stories F-12, S-1): off, the
/// symmetry only shows its guides and checks the map; brush strokes, placing, moving, turning and
/// deleting work in one part.
/// </summary>
/// <param name="On">Left out, the other way.</param>
/// <returns>Whether edits are mirrored now.</returns>
[EditorCommand("select.mirrorEdits", "Mirror edits by the symmetry", Story = "F-12")]
public sealed record MirrorEdits(bool? On = null) : IRequest<bool>;

public sealed class MirrorEditsHandler(MainViewModel model, SymmetryViewModel symmetry) : IRequestHandler<MirrorEdits, bool>
{
    public Task<bool> Handle(MirrorEdits request, CancellationToken cancellationToken)
    {
        symmetry.MirrorsEdits = request.On ?? !symmetry.MirrorsEdits;
        model.Status = symmetry.MirrorsEdits ? "Edits are mirrored by the symmetry" : "Edits are not mirrored: the symmetry shows its guides only";
        return Task.FromResult(symmetry.MirrorsEdits);
    }
}
