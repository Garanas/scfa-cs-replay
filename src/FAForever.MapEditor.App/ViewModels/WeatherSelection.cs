using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The inspector's fields of the selected weather markers (user story R-1), part of
/// <see cref="MarkerSelection"/>: a definition's style, its four cloud types with their chances and
/// the drift of the clouds; a generator's forced type, chance, count, size, height and spread. They
/// show the first selected definition's and generator's values and give a value to every selected
/// one through <c>markers.setWeather</c>.
/// </summary>
public sealed partial class WeatherSelection : ObservableObject
{
    private Marker[] _selected = [];

    public WeatherSelection(Func<string, JsonObject, Task> run)
    {
        const string command = "markers.setWeather";
        Definition = new SettingGroup("Weather",
        [
            new ChoiceSetting("Map style", command, "mapStyle", WeatherStyles.StyleNames, document => FirstDefinition(document)?.MapStyle ?? "", run)
            {
                Help = "The style of the game's weather (lua/weatherdefinitions.lua), which decides the clouds it has",
            },
            .. Enumerable.Range(1, 4).SelectMany(index => new Setting[]
            {
                new ChoiceSetting($"Clouds {index}", command, "cloudType", WeatherStyles.AllTypes, document => TypeOf(document, index)?.Type ?? WeatherStyles.None, run, () => new JsonObject { ["type"] = index })
                {
                    Help = "A cloud type the weather may have; a type its style lacks shows none",
                },
                new NumberSetting($"Chance {index}", command, "chance", document => TypeOf(document, index)?.Chance ?? 0, run, () => new JsonObject { ["type"] = index })
                {
                    Help = "How likely that cloud type is, 0 to 1",
                },
            }),
            new NumberSetting("Drift X", command, "driftX", document => FirstDefinition(document)?.DriftDirection.X ?? 0, run) { Help = "The direction the clouds drift in, across the map" },
            new NumberSetting("Drift Z", command, "driftZ", document => FirstDefinition(document)?.DriftDirection.Z ?? 0, run) { Help = "The direction the clouds drift in, down the map" },
        ]);
        Generator = new SettingGroup("Clouds",
        [
            new ChoiceSetting("Force type", command, "forceType", WeatherStyles.AllTypes, document => FirstGenerator(document)?.ForceType ?? WeatherStyles.None, run)
            {
                Help = "The cloud type it forms; None forms the weather definition's",
            },
            Number("Spawn chance", "spawnChance", generator => generator.SpawnChance, "How likely it forms clouds at all, 0 to 1 (the game: 1)"),
            Number("Cloud count", "cloudCount", generator => generator.CloudCount, "How many clouds (the game: 10)"),
            Number("Count range", "cloudCountRange", generator => generator.CloudCountRange, "How much the count varies (the game: 0)"),
            Number("Emitter scale", "cloudEmitterScale", generator => generator.CloudEmitterScale, "How large the clouds are (the game: 1)"),
            Number("Scale range", "cloudEmitterScaleRange", generator => generator.CloudEmitterScaleRange, "How much their size varies (the game: 0)"),
            Number("Cloud height", "cloudHeight", generator => generator.CloudHeight, "How high above the map they float (the game: 180)"),
            Number("Height range", "cloudHeightRange", generator => generator.CloudHeightRange, "How much their height varies (the game: 10)"),
            Number("Cloud spread", "cloudSpread", generator => generator.CloudSpread, "How far around the marker they form (the game: 15 % of the map's size)"),
        ]);

        NumberSetting Number(string label, string argument, Func<WeatherGenerator, double?> read, string help) =>
            new NumberSetting(label, command, argument, document => FirstGenerator(document) is WeatherGenerator generator ? read(generator) ?? 0 : 0, run) { Help = help };
    }

    public SettingGroup Definition { get; }

    public SettingGroup Generator { get; }

    [ObservableProperty]
    public partial bool HasDefinitions { get; private set; }

    [ObservableProperty]
    public partial bool HasGenerators { get; private set; }

    /// <summary>
    /// Shows the selected weather markers as they are in a version of the document.
    /// </summary>
    public void Refresh(MapDocument document, IReadOnlyList<Marker> markers)
    {
        _selected = [.. markers.Where(marker => marker is WeatherDefinition or WeatherGenerator)];
        HasDefinitions = _selected.Any(marker => marker is WeatherDefinition);
        HasGenerators = _selected.Any(marker => marker is WeatherGenerator);
        if (HasDefinitions)
        {
            Definition.Refresh(document);
        }
        if (HasGenerators)
        {
            Generator.Refresh(document);
        }
    }

    private WeatherDefinition? FirstDefinition(MapDocument document) =>
        _selected.OfType<WeatherDefinition>().Select(marker => document.FindMarker(marker.Id)).OfType<WeatherDefinition>().FirstOrDefault();

    private WeatherGenerator? FirstGenerator(MapDocument document) =>
        _selected.OfType<WeatherGenerator>().Select(marker => document.FindMarker(marker.Id)).OfType<WeatherGenerator>().FirstOrDefault();

    private WeatherChance? TypeOf(MapDocument document, int index) =>
        FirstDefinition(document) is WeatherDefinition definition && definition.Types.Length >= index ? definition.Types[index - 1] : null;
}
