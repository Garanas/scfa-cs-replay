using System.Text.Json.Nodes;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Selects units by what they are (user story U-10): of an army, a faction (the blueprint's, in the
/// workspace's assets; the army's when the assets lack the blueprint), a kind, a tech level, a
/// blueprint, and wrecks or not, within the map, the selection or an area. Units only; every part
/// left out asks nothing.
/// </summary>
/// <param name="Army">An army's name.</param>
/// <param name="Faction">One or more of <c>uef</c>, <c>aeon</c>, <c>cybran</c>, <c>seraphim</c>, <c>other</c>
/// (civilian units): a text with commas (<c>uef,aeon</c>) or a list.</param>
/// <param name="Kind">One or more of <c>structure</c>, <c>land</c>, <c>air</c>, <c>naval</c>, <c>other</c>, as for <paramref name="Faction"/>.</param>
/// <param name="Tech">One or more of 1 to 4 (4 is an experimental): <c>2</c>, <c>"1,2"</c> or <c>[1, 2]</c>.</param>
/// <param name="Blueprint">A blueprint id (<c>ueb1101</c>) or part of an id or of the unit's name (<c>factory</c>).</param>
/// <param name="Wrecks"><c>leave</c> (left out, the units that are not wrecks), <c>only</c> or <c>include</c>.</param>
/// <param name="Within"><c>map</c> (left out), <c>selection</c> (the units selected now) or <c>area:NAME</c>.</param>
/// <param name="Add">Keeps the current selection and adds to it.</param>
/// <returns>How many units are selected.</returns>
[EditorCommand("select.where", "Select units by...", Story = "U-10", Needs = Needs.Map)]
public sealed record SelectWhere(string? Army = null, JsonNode? Faction = null, JsonNode? Kind = null, JsonNode? Tech = null, string? Blueprint = null,
    string? Wrecks = null, string? Within = null, bool Add = false) : IRequest<int>;

public sealed class SelectWhereHandler(MainViewModel model) : IRequestHandler<SelectWhere, int>
{
    public Task<int> Handle(SelectWhere request, CancellationToken cancellationToken)
    {
        MapDocument document = model.ShownDocument ?? throw new InvalidOperationException("No map is open");
        if (request.Army is not null && document.FindArmy(request.Army) is null)
        {
            throw new ArgumentException($"The map has no army '{request.Army}': {string.Join(", ", document.Armies.Select(army => army.Name))}");
        }
        UnitQuery query = new UnitQuery
        {
            Army = request.Army,
            Factions = Words(request.Faction, "faction", UnitQuery.FactionNames),
            Layers = Words(request.Kind, "kind", UnitQuery.Kinds),
            Tech = [.. Words(request.Tech, "tech", ["1", "2", "3", "4"]).Select(int.Parse)],
            Blueprint = request.Blueprint,
            Wrecks = request.Wrecks?.Trim().ToLowerInvariant() switch
            {
                null or "" or "leave" => WreckChoice.Leave,
                "only" => WreckChoice.Only,
                "include" => WreckChoice.Include,
                var other => throw new ArgumentException($"'{other}' is no choice for wrecks: leave, only or include"),
            },
        };
        int count = model.SelectWhere(query, request.Within ?? "map", request.Add);
        model.Status = $"Selected {MainViewModel.Units(count)}{(request.Add ? " (added)" : "")}";
        return Task.FromResult(count);
    }

    /// <summary>
    /// The words of a text with commas or a list, each one of the choices, in the choices' order.
    /// </summary>
    private static System.Collections.Immutable.ImmutableArray<string> Words(JsonNode? node, string what, IReadOnlyList<string> choices)
    {
        IEnumerable<string> given = node switch
        {
            null => [],
            JsonArray list => list.Select(item => item?.ToString() ?? ""),
            _ => (node.ToString()).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        };
        HashSet<string> wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string word in given)
        {
            if (!choices.Contains(word, StringComparer.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"'{word}' is no {what}: {string.Join(", ", choices)}");
            }
            wanted.Add(word);
        }
        return [.. choices.Where(wanted.Contains)];
    }
}

/// <summary>
/// Opens or closes the Select by popover over the views (user story U-10), the button of the select tool's options.
/// </summary>
/// <param name="Open">Opens it (true), closes it (false); left out, turns it over.</param>
[EditorCommand("select.by", "Select by...", Story = "U-10", Needs = Needs.Map)]
public sealed record SelectBy(bool? Open = null) : IRequest;

public sealed class SelectByHandler(MainViewModel model) : IRequestHandler<SelectBy>
{
    public Task Handle(SelectBy request, CancellationToken cancellationToken)
    {
        model.SelectBy.IsOpen = request.Open ?? !model.SelectBy.IsOpen;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Stores the selection under a digit (user story U-10), as Alt and the digit do in the select tool: it
/// holds the things until stored over, with the project (units and areas; a marker, prop or decal
/// only while the editor runs). An empty selection empties the digit.
/// </summary>
/// <param name="Slot">1 to 9.</param>
/// <returns>How many things are stored.</returns>
[EditorCommand("select.store", "Store the selection", Story = "U-10", Needs = Needs.Map)]
public sealed record StoreSelection(int Slot) : IRequest<int>;

public sealed class StoreSelectionHandler(MainViewModel model) : IRequestHandler<StoreSelection, int>
{
    public Task<int> Handle(StoreSelection request, CancellationToken cancellationToken)
    {
        int count = model.StoredSelections.Store(request.Slot);
        model.Status = count == 0 ? $"Emptied the selection stored as {request.Slot}" : $"Stored {count} thing{(count == 1 ? "" : "s")} as {request.Slot}";
        return Task.FromResult(count);
    }
}

/// <summary>
/// Selects what is stored under a digit (user story U-10), as the digit does in the select tool; what
/// no longer exists is left out.
/// </summary>
/// <param name="Slot">1 to 9.</param>
/// <param name="Add">Adds to the selection in place of replacing it (shift with the digit).</param>
/// <returns>How many things are selected.</returns>
[EditorCommand("select.recall", "Recall a stored selection", Story = "U-10", Needs = Needs.Map)]
public sealed record RecallSelection(int Slot, bool Add = false) : IRequest<int>;

public sealed class RecallSelectionHandler(MainViewModel model) : IRequestHandler<RecallSelection, int>
{
    public Task<int> Handle(RecallSelection request, CancellationToken cancellationToken)
    {
        int count = model.StoredSelections.Recall(request.Slot, request.Add);
        model.Status = $"Recalled {request.Slot}: {model.SelectedText.ToLowerInvariant()}";
        return Task.FromResult(count);
    }
}
