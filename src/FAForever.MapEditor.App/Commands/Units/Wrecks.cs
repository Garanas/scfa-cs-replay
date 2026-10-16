using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Makes the selected units wrecks, or units again (user story U-5), one step of undo: a wreck
/// goes into its army's <c>WRECKAGE</c> group (made when missing), whose units the game turns into
/// wrecks when it starts; a unit made whole again goes into its army's <c>INITIAL</c>.
/// </summary>
/// <param name="Wreck">False makes them units again.</param>
/// <returns>How many units changed.</returns>
[UndoStep]
[EditorCommand("units.asWrecks", "Place as wrecks", Story = "U-5", Needs = Needs.Selection)]
public sealed record PlaceAsWrecks(bool Wreck = true) : IRequest<int>;

public sealed class PlaceAsWrecksHandler(MainViewModel model, SelectionViewModel selection, CurrentEdit edit) : IRequestHandler<PlaceAsWrecks, int>
{
    public Task<int> Handle(PlaceAsWrecks request, CancellationToken cancellationToken)
    {
        string[] names = UnitCommands.SelectedUnits(selection);
        int count = 0;
        UnitCommands.Apply(edit, d => d.SetWrecks(names, request.Wreck, out count));
        string[] playable = [.. edit.Transaction.Document.PlayableArmiesWithWrecks()];
        model.Status = (request.Wreck ? (count == 1 ? "Made a unit a wreck" : $"Made {count} units wrecks") : (count == 1 ? "Made a wreck a unit again" : $"Made {count} wrecks units again"))
            + (request.Wreck && playable.Length > 0 ? $"; {string.Join(", ", playable)} is playable, so its wrecks show only when a player takes it" : "");
        return Task.FromResult(count);
    }
}
