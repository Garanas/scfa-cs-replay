using FAForever.MapEditor.App.ViewModels;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Turns on or off whether props follow the terrain (user story P-1): after a stroke of a height
/// brush, the props on the terrain it raised or lowered are set back on it, in the same step of
/// undo as the stroke. On unless turned off.
/// </summary>
/// <param name="On">Left out, it turns the other way.</param>
/// <returns>Whether props follow the terrain now.</returns>
[EditorCommand("props.followTerrain", "Props follow the terrain", Story = "P-1")]
public sealed record FollowTerrain(bool? On = null) : IRequest<bool>;

public sealed class FollowTerrainHandler(MainViewModel model) : IRequestHandler<FollowTerrain, bool>
{
    public Task<bool> Handle(FollowTerrain request, CancellationToken cancellationToken)
    {
        model.Props.FollowTerrain = request.On ?? !model.Props.FollowTerrain;
        model.Status = model.Props.FollowTerrain ? "Props follow the terrain when it is sculpted" : "Props stay where they are when the terrain is sculpted";
        return Task.FromResult(model.Props.FollowTerrain);
    }
}
