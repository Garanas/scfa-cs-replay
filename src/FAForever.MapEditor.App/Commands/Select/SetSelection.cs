using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Selects things by reference (user story F-11), for automation and plugins: what a click selects
/// in the views, of any kind, whether shown or not.
/// </summary>
/// <param name="Refs">What to select: <c>marker:12</c>, <c>prop:5</c>, <c>decal:3</c>, <c>unit:UNIT_5</c>, <c>area:AREA_1</c>.</param>
/// <param name="Change">How: <c>Replace</c> (left out), <c>Add</c> or <c>Toggle</c>.</param>
/// <returns>What is selected afterwards.</returns>
[EditorCommand("select.set", "Select things", Story = "F-11", Needs = Needs.Map)]
public sealed record SetSelection(string[] Refs, SelectionChange Change = SelectionChange.Replace) : IRequest<string[]>;

public sealed class SetSelectionHandler(MainViewModel model, SelectionViewModel selection) : IRequestHandler<SetSelection, string[]>
{
    public Task<string[]> Handle(SetSelection request, CancellationToken cancellationToken)
    {
        MapDocument document = model.ShownDocument ?? throw new InvalidOperationException("No map is open");
        ObjectRef[] things = [.. request.Refs.Select(ObjectRef.Parse)];
        foreach (ObjectRef thing in things)
        {
            if (!document.Exists(thing))
            {
                throw new ArgumentException($"The map has no {thing}");
            }
        }
        selection.Apply(things, request.Change);
        return Task.FromResult(selection.Items.Select(thing => thing.ToString()).ToArray());
    }
}
