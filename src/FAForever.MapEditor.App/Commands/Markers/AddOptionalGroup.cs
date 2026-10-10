using System.Collections.Immutable;
using System.Text.Json.Nodes;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Editing;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Adds a group of units that an adaptive map's script creates when a lobby option asks for it
/// (user story R-5), one step of undo: <c>Optional_&lt;option&gt;_&lt;level&gt;</c> directly under the
/// civilian army's units, beside <c>INITIAL</c>, as FAF's adaptive maps name them (the library's
/// <c>AdaptiveGroups</c>). The selected units can go into it at once. The map's script and
/// <c>_options.lua</c> say which value of the option creates it; the editor writes neither.
/// </summary>
/// <param name="Option">E.g. <c>Civilian_Defenses</c> (the option <c>optional_civilian_defenses</c>).</param>
/// <param name="Level">The option's value from which on the script creates it, 2 or more; left out, a name without one.</param>
/// <param name="Army">Left out, the civilians: <c>ARMY_17</c>, else <c>NEUTRAL_CIVILIAN</c>, else the first extra army.</param>
/// <param name="FromSelection">Move the selected units into it.</param>
/// <returns>The army, the group and how many units moved into it.</returns>
[UndoStep]
[EditorCommand("markers.addOptionalGroup", "Add an optional group of units", Story = "R-5", Needs = Needs.Map)]
public sealed record AddOptionalGroup(string Option, int? Level = null, string? Army = null, bool FromSelection = false) : IRequest<JsonObject>;

public sealed class AddOptionalGroupHandler(MainViewModel model, SelectionViewModel selection, CurrentEdit edit) : IRequestHandler<AddOptionalGroup, JsonObject>
{
    public Task<JsonObject> Handle(AddOptionalGroup request, CancellationToken cancellationToken)
    {
        MapDocument document = edit.Transaction.Document;
        string army = request.Army ?? AdaptiveGroups.CivilianArmy(document)
            ?? throw new InvalidOperationException("The map has no extra army for civilians: add one in the Map mode");
        if (document.Armies.All(a => a.Name != army))
        {
            throw new ArgumentException($"The map has no army {army}");
        }
        document = document.AddOptionalGroup(army, request.Option, request.Level, out string name);
        int moved = 0;
        if (request.FromSelection)
        {
            string[] units = [.. selection.Items.Where(thing => thing.Kind == ObjectKind.Unit).Select(thing => thing.Name!)];
            document = document.MoveUnits(units, army, ImmutableArray.Create(name), out moved);
        }
        edit.Transaction.Apply(_ => document);
        model.Status = moved > 0 ? $"Added {name} to {army} with {MainViewModel.Units(moved)}" : $"Added {name} to {army}";
        return Task.FromResult(new JsonObject { ["army"] = army, ["group"] = name, ["moved"] = moved });
    }
}
