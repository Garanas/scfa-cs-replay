using System.Numerics;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Changes the selected weather markers (user story R-1), one step of undo: what the inspector's
/// weather fields do. The style, types, chances and drift go to the weather definitions, the rest
/// to the weather generators (the fields of <c>lua/weather.lua</c>; a generator's setting left
/// empty in the map is the game's default).
/// </summary>
/// <param name="MapStyle">A style of <c>lua/weatherdefinitions.lua</c>: Desert, Evergreen, Geothermal, Lava, RedRock, Tropical, Tundra.</param>
/// <param name="Type">Which of the definition's four cloud types (1 to 4) <see cref="CloudType"/> and <see cref="Chance"/> set.</param>
/// <param name="CloudType">A cloud type, e.g. <c>SnowClouds</c>, or <c>None</c>.</param>
/// <param name="Chance">How likely that type is, 0 to 1.</param>
/// <param name="DriftX">The direction the clouds drift in, across.</param>
/// <param name="DriftZ">The same, down the map.</param>
/// <param name="ForceType">The cloud type a generator forms, <c>None</c> for the definition's.</param>
/// <returns>How many markers changed.</returns>
[UndoStep]
[EditorCommand("markers.setWeather", "Change the selected weather markers", Story = "R-1", Needs = Needs.Selection)]
public sealed record SetWeather(
    string? MapStyle = null,
    int? Type = null,
    string? CloudType = null,
    double? Chance = null,
    float? DriftX = null,
    float? DriftZ = null,
    string? ForceType = null,
    double? SpawnChance = null,
    double? CloudCount = null,
    double? CloudCountRange = null,
    double? CloudEmitterScale = null,
    double? CloudEmitterScaleRange = null,
    double? CloudHeight = null,
    double? CloudHeightRange = null,
    double? CloudSpread = null) : IRequest<int>;

public sealed class SetWeatherHandler(SelectionViewModel selection, CurrentEdit edit) : IRequestHandler<SetWeather, int>
{
    public Task<int> Handle(SetWeather request, CancellationToken cancellationToken)
    {
        MapDocument document = edit.Transaction.Document;
        Marker[] markers = [.. selection.Items.Where(thing => thing.Kind == ObjectKind.Marker).Select(document.Find).OfType<Marker>()
            .Where(marker => marker is WeatherDefinition or WeatherGenerator)];
        if (markers.Length == 0)
        {
            throw new InvalidOperationException("No weather marker is selected");
        }
        if (request.MapStyle is string style && !WeatherStyles.Styles.ContainsKey(style))
        {
            throw new ArgumentException($"There is no map style '{style}': {string.Join(", ", WeatherStyles.StyleNames)}");
        }
        if ((request.CloudType is not null || request.Chance is not null) && request.Type is not (>= 1 and <= 4))
        {
            throw new ArgumentException("Say which of the four cloud types to set: type 1 to 4");
        }
        if (request.Chance is < 0 or > 1 || request.SpawnChance is < 0 or > 1)
        {
            throw new ArgumentException("A chance is from 0 to 1");
        }
        MapDocument before = document;
        foreach (Marker marker in markers)
        {
            document = document.UpdateMarker(marker.Id, current => current switch
            {
                WeatherDefinition definition => Change(definition, request),
                WeatherGenerator generator => generator with
                {
                    ForceType = request.ForceType ?? generator.ForceType,
                    SpawnChance = request.SpawnChance ?? generator.SpawnChance,
                    CloudCount = request.CloudCount ?? generator.CloudCount,
                    CloudCountRange = request.CloudCountRange ?? generator.CloudCountRange,
                    CloudEmitterScale = request.CloudEmitterScale ?? generator.CloudEmitterScale,
                    CloudEmitterScaleRange = request.CloudEmitterScaleRange ?? generator.CloudEmitterScaleRange,
                    CloudHeight = request.CloudHeight ?? generator.CloudHeight,
                    CloudHeightRange = request.CloudHeightRange ?? generator.CloudHeightRange,
                    CloudSpread = request.CloudSpread ?? generator.CloudSpread,
                },
                _ => current,
            });
        }
        edit.Transaction.Apply(_ => document);
        return Task.FromResult(markers.Count(marker => !Equals(before.FindMarker(marker.Id), document.FindMarker(marker.Id))));
    }

    private static WeatherDefinition Change(WeatherDefinition definition, SetWeather request)
    {
        WeatherDefinition changed = definition with
        {
            MapStyle = request.MapStyle ?? definition.MapStyle,
            DriftDirection = request.DriftX is null && request.DriftZ is null
                ? definition.DriftDirection
                : new Vector3(request.DriftX ?? definition.DriftDirection.X, definition.DriftDirection.Y, request.DriftZ ?? definition.DriftDirection.Z),
        };
        if (request.Type is int index && (request.CloudType is not null || request.Chance is not null))
        {
            List<WeatherChance> types = [.. changed.Types];
            while (types.Count < index)
            {
                types.Add(new WeatherChance { Type = WeatherStyles.None, Chance = 0 });
            }
            WeatherChance type = types[index - 1];
            types[index - 1] = type with { Type = request.CloudType ?? type.Type, Chance = request.Chance ?? type.Chance };
            changed = changed with { Types = [.. types] };
        }
        return changed;
    }
}
