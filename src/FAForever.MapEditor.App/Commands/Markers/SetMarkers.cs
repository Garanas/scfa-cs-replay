using System.Numerics;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Changes the selected markers (user stories R-2, R-5), one step of undo: what the inspector's
/// marker fields do. Each argument changes the selected markers it applies to and leaves the
/// others: a resource kind the deposits, a type the AI markers, a layer the path nodes (their
/// links to the old layer go), a no-rush offset the spawns, the army the deposits of an adaptive
/// map wait for. A deposit or AI marker named after its kind or type is named after the new one.
/// </summary>
/// <param name="Name">The one selected marker's new name; a spawn is named after its army and keeps it.</param>
/// <param name="Resource"><c>Mass</c> or <c>Hydrocarbon</c>.</param>
/// <param name="Type">An AI marker's type, e.g. <c>Rally Point</c>.</param>
/// <param name="Layer"><c>Land</c>, <c>Water</c>, <c>Amphibious</c> or <c>Air</c>.</param>
/// <param name="NoRushX">Where a spawn's no-rush circle lies from it, across, in game units (the other part stays, 0 when there was none).</param>
/// <param name="NoRushZ">The same, down the map.</param>
/// <param name="ClearNoRush">The no-rush circle lies around the spawn again.</param>
/// <param name="SpawnWithArmy">The army a deposit waits for on an adaptive map; empty for none (always there),
/// <c>nearest</c> for the army of the spawn nearest to each deposit.</param>
/// <returns>How many markers changed.</returns>
[UndoStep]
[EditorCommand("markers.set", "Change the selected markers", Story = "R-2", Needs = Needs.Selection)]
public sealed record SetMarkers(
    string? Name = null,
    string? Resource = null,
    string? Type = null,
    string? Layer = null,
    float? NoRushX = null,
    float? NoRushZ = null,
    bool ClearNoRush = false,
    string? SpawnWithArmy = null) : IRequest<int>;

public sealed class SetMarkersHandler(SelectionViewModel selection, CurrentEdit edit) : IRequestHandler<SetMarkers, int>
{
    public Task<int> Handle(SetMarkers request, CancellationToken cancellationToken)
    {
        MapDocument document = edit.Transaction.Document;
        Marker[] markers = [.. selection.Items.Where(thing => thing.Kind == ObjectKind.Marker).Select(document.Find).OfType<Marker>()];
        if (markers.Length == 0)
        {
            throw new InvalidOperationException("No marker is selected");
        }
        MapDocument before = document;

        if (request.Name is string wanted)
        {
            document = Rename(document, markers, wanted.Trim());
        }
        if (request.Resource is string resourceText)
        {
            ResourceKind kind = Enum.TryParse(resourceText, ignoreCase: true, out ResourceKind parsed) && Enum.IsDefined(parsed)
                ? parsed : throw new ArgumentException($"There is no resource '{resourceText}': Mass or Hydrocarbon");
            foreach (ResourceMarker resource in markers.OfType<ResourceMarker>().Where(r => r.Kind != kind))
            {
                string name = Renamed(document, resource.Name, resource.Kind.ToString(), kind.ToString());
                document = document.UpdateMarker(resource.Id, _ => resource with { Kind = kind, Name = name });
            }
        }
        if (request.Type is string type)
        {
            if (type.Trim().Length == 0)
            {
                throw new ArgumentException("An AI marker needs a type");
            }
            foreach (AiMarker ai in markers.OfType<AiMarker>().Where(m => !m.IsBlank && m.Type != type))
            {
                string name = Renamed(document, ai.Name, ai.Type, type);
                document = document.UpdateMarker(ai.Id, _ => ai with { Type = type, Name = name });
            }
        }
        if (request.Layer is string layerText)
        {
            PathLayer layer = Enum.TryParse(layerText, ignoreCase: true, out PathLayer parsed) && Enum.IsDefined(parsed)
                ? parsed : throw new ArgumentException($"There is no layer '{layerText}': Land, Water, Amphibious or Air");
            foreach (PathNode node in markers.OfType<PathNode>())
            {
                document = document.SetPathLayer(node.Id, layer);
            }
        }
        if (request.ClearNoRush || request.NoRushX is not null || request.NoRushZ is not null)
        {
            foreach (Spawn spawn in markers.OfType<Spawn>())
            {
                Vector2? offset = request.ClearNoRush ? null : new Vector2(
                    request.NoRushX ?? spawn.NoRushOffset?.X ?? 0,
                    request.NoRushZ ?? spawn.NoRushOffset?.Y ?? 0);
                document = document.UpdateMarker(spawn.Id, marker => ((Spawn)marker) with { NoRushOffset = offset });
            }
        }
        if (request.SpawnWithArmy is string army)
        {
            document = WaitFor(document, [.. markers.OfType<ResourceMarker>()], army.Trim());
        }

        edit.Transaction.Apply(_ => document);
        int changed = markers.Count(marker => !Equals(before.FindMarker(marker.Id), document.FindMarker(marker.Id)));
        return Task.FromResult(changed);
    }

    private static MapDocument Rename(MapDocument document, Marker[] markers, string name)
    {
        if (markers.Length != 1)
        {
            throw new InvalidOperationException("Only one marker at a time is renamed: select one");
        }
        Marker marker = markers[0];
        if (marker.Name == name)
        {
            return document;
        }
        if (marker is Spawn)
        {
            throw new InvalidOperationException($"A spawn is named after its army, {marker.Name}: the spawns are numbered in order");
        }
        if (name.Length == 0)
        {
            throw new ArgumentException("A marker needs a name");
        }
        if (document.Markers.All.Any(other => other.Name == name))
        {
            throw new InvalidOperationException($"Another marker is called {name}");
        }
        return document.UpdateMarker(marker.Id, m => m with { Name = name });
    }

    /// <summary>
    /// A marker's name after its kind or type changed: named after the new one when it was named
    /// after the old one (<c>Mass 05</c> becomes <c>Hydrocarbon 03</c>), else as it was.
    /// </summary>
    private static string Renamed(MapDocument document, string name, string from, string to) =>
        name.StartsWith(from, StringComparison.Ordinal)
            ? DocumentEdits.FreeName(document.Markers.All.Select(marker => marker.Name), $"{to} 01")
            : name;

    private static MapDocument WaitFor(MapDocument document, ResourceMarker[] deposits, string army)
    {
        if (deposits.Length == 0)
        {
            throw new InvalidOperationException("No deposit is selected: only deposits wait for an army");
        }
        if (army.Length > 0 && !string.Equals(army, "nearest", StringComparison.OrdinalIgnoreCase)
            && !document.Scenario.PlayableArmies.Contains(army) && !document.Armies.Any(a => a.Name == army))
        {
            throw new ArgumentException($"The map has no army {army}: {string.Join(", ", document.Scenario.PlayableArmies)}");
        }
        foreach (ResourceMarker deposit in deposits)
        {
            string? waitFor = army.Length == 0 ? null
                : string.Equals(army, "nearest", StringComparison.OrdinalIgnoreCase) ? NearestArmy(document, deposit.Position)
                : army;
            document = document.UpdateMarker(deposit.Id, marker => ((ResourceMarker)marker) with { SpawnWithArmy = waitFor });
        }
        return document;
    }

    /// <summary>
    /// The army of the spawn nearest to a place, or null when the map has no spawn.
    /// </summary>
    public static string? NearestArmy(MapDocument document, Vector3 place) => document.Markers.Spawns
        .OrderBy(spawn => Vector2.DistanceSquared(new Vector2(spawn.Position.X, spawn.Position.Z), new Vector2(place.X, place.Z)))
        .FirstOrDefault()?.Army;
}
