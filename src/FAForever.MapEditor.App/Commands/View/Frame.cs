using System.Text.Json.Nodes;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Shows things in both views (user story V-1): the top view zooms to the rectangle around them,
/// the 3D camera glides to see it.
/// </summary>
/// <param name="Refs">The things (<c>prop:5</c>, <c>unit:UNIT_1</c>, ...); left out, the selection.</param>
/// <returns>The rectangle framed, <c>[x0, z0, x1, z1]</c> in ogrids.</returns>
[EditorCommand("view.frame", "Frame the selection", Gesture = "Ctrl+Shift+F", Story = "V-1", Needs = Needs.Map)]
public sealed record FrameThings(string[]? Refs = null) : IRequest<JsonArray>;

public sealed class FrameThingsHandler(MainViewModel model) : IRequestHandler<FrameThings, JsonArray>
{
    public Task<JsonArray> Handle(FrameThings request, CancellationToken cancellationToken)
    {
        IReadOnlyList<ObjectRef> things = request.Refs is { } refs ? [.. refs.Select(ObjectRef.Parse)] : [.. model.Selection.Items];
        if (things.Count == 0)
        {
            throw new InvalidOperationException("Nothing to frame: select something, or give refs");
        }
        (float x0, float z0, float x1, float z1) = model.Frame(things)
            ?? throw new ArgumentException($"The map has none of {string.Join(", ", things)}");
        return Task.FromResult(new JsonArray(Math.Round(x0, 2), Math.Round(z0, 2), Math.Round(x1, 2), Math.Round(z1, 2)));
    }
}
