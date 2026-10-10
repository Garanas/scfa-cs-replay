using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Makes the map adaptive or not (user story R-5), one step of undo: an adaptive map leaves out
/// the deposits whose army no player takes. Which army a deposit waits for is the inspector's
/// (<c>markers.set spawnWithArmy=...</c>).
/// </summary>
/// <param name="On">Whether it adapts; left out, it turns over.</param>
[UndoStep]
[EditorCommand("markers.adaptive", "Adaptive map", Story = "R-5", Needs = Needs.Map)]
public sealed record EditAdaptiveMap(bool? On = null) : IRequest;

public sealed class EditAdaptiveMapHandler(CurrentEdit edit) : IRequestHandler<EditAdaptiveMap>
{
    public Task Handle(EditAdaptiveMap request, CancellationToken cancellationToken)
    {
        edit.Transaction.Apply(document => document with
        {
            Scenario = document.Scenario with { AdaptiveMap = request.On ?? !document.Scenario.AdaptiveMap },
        });
        return Task.CompletedTask;
    }
}
