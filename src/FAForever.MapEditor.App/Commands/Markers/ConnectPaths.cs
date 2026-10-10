using System.Text.Json.Nodes;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Generation;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Generates the AI's path networks from walkability (user stories R-3, F-7), on a thread of the
/// pool, and puts them into the map as one step of undo: per layer nodes about a spacing apart on
/// the ground its units can walk, linked where they can walk straight to each other, one network per
/// region (the library's <c>PathNetworks</c>). Without <c>replace</c> a layer's own nodes stay and
/// new ones are only added where none are, linked to them; with it they go first. The result goes
/// out of date when walkability's inputs change, and <c>generate.stale</c> makes it again with the
/// same settings. What is left out is the Markers panel's.
/// </summary>
/// <param name="Layers"><c>Land</c>, <c>Water</c>, <c>Amphibious</c>, <c>Air</c>; left out, the panel's.</param>
/// <param name="Spacing">About how far apart the nodes lie, in ogrids; left out, the panel's (empty there: a sixteenth of the map, 16 to 64).</param>
/// <param name="AirSpacing">How far apart the air nodes lie; left out, twice the spacing.</param>
/// <param name="Replace">Remove the layers' own nodes first.</param>
/// <returns>Per layer the nodes, how many are new, and the links.</returns>
[EditorCommand("markers.connectPaths", "Generate path networks", Story = "R-3", Needs = Needs.Map | Needs.NoStroke)]
public sealed record ConnectPathNodes(string[]? Layers = null, float? Spacing = null, float? AirSpacing = null, bool? Replace = null) : IRequest<JsonObject>;

public sealed class ConnectPathNodesHandler(MainViewModel model, MarkersViewModel markers, GenerationViewModel generation) : IRequestHandler<ConnectPathNodes, JsonObject>
{
    public async Task<JsonObject> Handle(ConnectPathNodes request, CancellationToken cancellationToken)
    {
        PathNetworkGenerator generator = (PathNetworkGenerator)generation.Results.Find(PathNetworkGenerator.GeneratorId)!;
        PathNetworkPanel panel = markers.Paths;
        PathLayer[] layers = request.Layers is string[] names
            ? [.. names.Select(name => Enum.TryParse(name, ignoreCase: true, out PathLayer layer) && Enum.IsDefined(layer)
                ? layer : throw new ArgumentException($"There is no layer '{name}': Land, Water, Amphibious or Air"))]
            : [.. panel.Layers];
        if (layers.Length == 0)
        {
            throw new ArgumentException("Choose a layer to generate: Land, Water, Amphibious or Air");
        }
        float? spacing = request.Spacing ?? panel.SpacingValue;
        if (spacing is < 4 or > 512)
        {
            throw new ArgumentException("The spacing is from 4 to 512 ogrids");
        }
        generator.Settings = new PathNetworkSettings
        {
            Layers = [.. layers],
            Spacing = spacing,
            AirSpacing = request.AirSpacing,
            Replace = request.Replace ?? panel.Replace,
        };
        generator.Walkability = generation.Results.ResultOf(WalkabilityGenerator.GeneratorId);

        GeneratedResult result = await model.GenerateAsync(PathNetworkGenerator.GeneratorId);
        PathNetwork network = (PathNetwork)result.Value;
        model.ShowPathNodes = true;
        foreach (PathLayer layer in layers)
        {
            markers.Show(MarkerEdits.KindOf(layer), true);
        }
        string summary = string.Join(", ", network.Layers.Select(layer => $"{layer.Layer.ToString().ToLowerInvariant()} {layer.Nodes.Length} nodes ({layer.NewNodes} new), {layer.Links.Length} links"));
        panel.Result = $"Generated: {summary}";
        model.Status = $"Generated path networks: {summary}";
        return new JsonObject
        {
            ["layers"] = new JsonArray([.. network.Layers.Select(layer => (JsonNode)new JsonObject
            {
                ["layer"] = layer.Layer.ToString(),
                ["spacing"] = layer.Spacing,
                ["nodes"] = layer.Nodes.Length,
                ["new"] = layer.NewNodes,
                ["links"] = layer.Links.Length,
                ["replaced"] = layer.Replace,
            })]),
        };
    }
}
