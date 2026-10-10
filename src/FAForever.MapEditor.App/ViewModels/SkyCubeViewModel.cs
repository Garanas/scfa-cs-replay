using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Rendering;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The sky cube map made from the map (user story L-1): six faces seen from above the map's middle,
/// the map's sky above the horizon and the map with its water below, written into the project's
/// <c>env/</c> (<see cref="SkyCubeMaps.FilePath"/>) and chosen with one click as the sky's, the
/// units' and the water's cube map; the export copies it with the map's files. Its settings are the
/// map's (<see cref="SkyCubeSettings"/>): resolution, eye height, the terrain, the sun's disc.
/// </summary>
public sealed partial class SkyCubeViewModel : ObservableObject
{
    private readonly Func<string, JsonObject, Task> _run;
    private readonly GenerationViewModel _generation;
    private readonly Func<MapDocument?> _document;

    public SkyCubeViewModel(Func<string, JsonObject, Task> run, GenerationViewModel generation, Func<MapDocument?> document)
    {
        _run = run;
        _generation = generation;
        _document = document;
        const string set = "generate.setSkyCube";
        Settings = new SettingGroup("Sky cube map",
        [
            new ChoiceSetting("Resolution", set, "resolution", ["512", "1024", "2048"], d => d.Procedural.SkyCube.Resolution.ToString(System.Globalization.CultureInfo.InvariantCulture), run)
            {
                Help = "The size of each of the six faces in pixels",
            },
            new NumberSetting("Eye height", set, "eyeHeight", d => d.Procedural.SkyCube.EyeHeight, run)
            {
                Minimum = 0, Maximum = 400, HasSlider = true,
                Help = "How high above the terrain or water at the map's middle the faces are seen from, in ogrids",
            },
            new BoolSetting("Include the terrain", set, "terrain", d => d.Procedural.SkyCube.IncludeTerrain, run)
            {
                Help = "Draw the map and its water below the horizon; off, the horizon's colour fills the lower half",
            },
            new BoolSetting("Sun disc", set, "sunDisc", d => d.Procedural.SkyCube.SunDisc, run)
            {
                Help = "Draw the sun as a disc where the lighting says it is",
            },
        ]);
    }

    public string Help => "Six faces seen from above the map's middle: the map's sky above the horizon, the map and its water below. Use it as the sky's, the units' and the water's cube map, so reflections show this map's colours";

    /// <summary>
    /// The resolution, eye height, terrain and sun disc.
    /// </summary>
    public SettingGroup Settings { get; }

    /// <summary>
    /// The generated faces, small, as a strip of six; null before it is generated in this session.
    /// </summary>
    [ObservableProperty]
    public partial PreviewPicture? Preview { get; private set; }

    /// <summary>
    /// How it stands: <c>Up to date, 1024 pixels a face, drawn with the 3D renderer</c>.
    /// </summary>
    [ObservableProperty]
    public partial string Status { get; private set; } = "";

    /// <summary>
    /// The sky it shows: the map's skybox, or one made from the lighting.
    /// </summary>
    [ObservableProperty]
    public partial string SkyLine { get; private set; } = "";

    /// <summary>
    /// What uses the generated cube map: the sky, the units, the water.
    /// </summary>
    [ObservableProperty]
    public partial string UseLine { get; private set; } = "";

    /// <summary>
    /// Whether the map holds a generated cube map (in this session or saved before).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUse))]
    public partial bool HasFile { get; private set; }

    /// <summary>
    /// Whether the sky, the units and the water all use it already.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUse))]
    public partial bool IsUsed { get; private set; }

    [ObservableProperty]
    public partial bool IsGenerating { get; private set; }

    public bool CanUse => HasFile && !IsUsed;

    [RelayCommand]
    private Task UseEverywhere() => _run("generate.useSkyCube", []);

    /// <summary>
    /// What uses the generated cube map in a version of the map.
    /// </summary>
    public static (bool Sky, bool Environment, bool Water) UsesOf(MapDocument document)
    {
        string path = SkyCubeMaps.GamePath(document);
        bool Same(string named) => string.Equals(named.Replace('\\', '/'), path, StringComparison.OrdinalIgnoreCase);
        return (Same(document.Sky.Cubemap), Same(SkyFields.EnvironmentOf(document.Sky)), Same(document.Water.Cubemap));
    }

    /// <summary>
    /// Shows the cube map as the open map and the last result have it: after every step and generation.
    /// </summary>
    public void Refresh()
    {
        MapDocument? document = _document();
        Settings.Refresh(document);
        IsGenerating = _generation.IsRunning(SkyCubeGenerator.GeneratorId);
        if (document is null)
        {
            (Status, SkyLine, UseLine, HasFile, IsUsed, Preview) = ("", "", "", false, false, null);
            return;
        }
        HasFile = document.Files.ContainsKey(SkyCubeMaps.FilePath);
        SkyCubeResult? result = _generation.Results.ResultOf(SkyCubeGenerator.GeneratorId)?.Value as SkyCubeResult;
        if (result is not null && (Preview is not TexturePicture shown || shown.Texture != result.Preview))
        {
            Preview = new TexturePicture(result.Preview, TexturePicture.Colour);
        }
        GeneratedState? state = _generation.States.FirstOrDefault(s => s.Generator.Id == SkyCubeGenerator.GeneratorId);
        Status = IsGenerating ? "Generating..."
            : state?.Status switch
            {
                GeneratedStatus.Current when result is not null => $"Up to date: {result.Size} pixels a face, the map drawn with {result.DrawnWith}",
                GeneratedStatus.Stale => $"Out of date: {string.Join(", ", state.ChangedInputs)} changed",
                _ when HasFile => "Kept with the map; Generate makes it again as the map is now",
                _ => "Not generated yet",
            };
        Skybox? skybox = document.Sky.Skybox;
        SkyLine = skybox is null
            ? "Sky: made from the lighting (the map has no skybox): the horizon from the fog and sun"
            : $"Sky: the map's skybox, {Count(skybox.Planets.Length, "sky object")}, {Count(skybox.CirrusLayers.Length, "cirrus layer")}";
        (bool sky, bool environment, bool water) = UsesOf(document);
        IsUsed = sky && environment && water;
        string[] users = [.. new[] { (sky, "the sky"), (environment, "the units"), (water, "the water") }.Where(use => use.Item1).Select(use => use.Item2)];
        UseLine = !HasFile ? "Generate it, then use it for the sky, the units and the water"
            : users.Length == 0 ? "Not used yet: Use makes it the sky's, the units' and the water's cube map"
            : $"Used by {string.Join(", ", users)}; the export copies it with the map";
    }

    private static string Count(int count, string what) => count == 1 ? $"1 {what}" : $"{count} {what}s";

    /// <summary>
    /// The sky cube map as automation reads it (<c>state.skyCube</c>).
    /// </summary>
    public JsonObject Describe()
    {
        MapDocument? document = _document();
        SkyCubeResult? result = _generation.Results.ResultOf(SkyCubeGenerator.GeneratorId)?.Value as SkyCubeResult;
        (bool sky, bool environment, bool water) = document is null ? default : UsesOf(document);
        return new JsonObject
        {
            ["settings"] = document is null ? null : new JsonObject
            {
                ["resolution"] = document.Procedural.SkyCube.Resolution,
                ["eyeHeight"] = document.Procedural.SkyCube.EyeHeight,
                ["terrain"] = document.Procedural.SkyCube.IncludeTerrain,
                ["sunDisc"] = document.Procedural.SkyCube.SunDisc,
            },
            ["status"] = Status,
            ["sky"] = SkyLine,
            ["use"] = UseLine,
            ["generating"] = IsGenerating,
            ["file"] = document is not null && HasFile ? SkyCubeMaps.GamePath(document) : null,
            ["bytes"] = document is not null && document.Files.TryGetValue(SkyCubeMaps.FilePath, out var bytes) ? bytes.Length : null,
            ["size"] = result?.Size,
            ["drawnWith"] = result?.DrawnWith,
            ["usedBy"] = new JsonObject { ["sky"] = sky, ["environment"] = environment, ["water"] = water },
        };
    }
}
