using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Turns the map's water on or off (user story W-1), one step of undo.
/// </summary>
/// <param name="On">Whether the map has water; left out, it turns over.</param>
[UndoStep]
[EditorCommand("water.toggle", "Water on or off", Story = "W-1")]
public sealed record ToggleWater(bool? On = null) : IRequest;

public sealed class ToggleWaterHandler(CurrentEdit edit) : IRequestHandler<ToggleWater>
{
    public Task Handle(ToggleWater request, CancellationToken cancellationToken)
    {
        edit.Transaction.Apply(document => document with
        {
            Water = document.Water with { HasWater = request.On ?? !document.Water.HasWater },
        });
        return Task.CompletedTask;
    }
}
