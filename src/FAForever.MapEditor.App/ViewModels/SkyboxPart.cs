using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The Lighting panel's Skybox entry in the inspector (user story L-1): the version 60 sky dome the
/// game draws around the map. Its fields (the dome's colours, heights and place, the sky objects'
/// atlases, the cirrus and each cirrus layer) run <c>lighting.setSkybox</c> and
/// <c>lighting.setCirrusLayer</c>; without a skybox it offers to add one made from the lighting or to
/// read a preset. The sky objects are counted, not edited one by one. The sky cube map is made from it.
/// </summary>
public sealed partial class SkyboxPart : ObservableObject
{
    private readonly Func<string, JsonObject, Task> _run;
    private Skybox? _shown;
    private int _version = -1;

    public SkyboxPart(Func<string, JsonObject, Task> run)
    {
        _run = run;
        const string set = "lighting.setSkybox";
        Dome = new SettingGroup("Dome",
        [
            new ColorSetting("Horizon colour", set, "horizonColor", d => Of(d).HorizonColor, run) { Help = "The sky's colour at the horizon" },
            new NumberSetting("Horizon height", set, "horizonHeight", d => Of(d).HorizonHeight, run) { Help = "How far the dome's bottom ring lies above its position; there the horizon's colour starts to give way to the zenith's" },
            new ColorSetting("Zenith colour", set, "zenithColor", d => Of(d).ZenithColor, run) { Help = "The sky's colour overhead" },
            new NumberSetting("Zenith height", set, "zenithHeight", d => Of(d).ZenithHeight, run) { Help = "How far above the horizon height the sky has the zenith's colour" },
            new ColorSetting("Position", set, "position", d => Of(d).Position, run) { ShowsSwatch = false, Help = "x, y and z of the middle of the dome's bottom ring, in the map's units" },
            new NumberSetting("Scale", set, "scale", d => Of(d).Scale, run) { Help = "The radius of the dome's bottom ring, in the map's units" },
            new NumberSetting("Subtract height", set, "subtractHeight", d => Of(d).SubtractHeight, run) { Help = "The angle in radians where the dome starts on its sphere (1.2566, 72°, in the game's maps); larger is flatter" },
            new NumberSetting("Subdivisions around", set, "subdivisionsAxis", d => Of(d).SubdivisionsAxis, run) { Help = "How many pieces the game builds the dome of around its axis" },
            new NumberSetting("Subdivisions up", set, "subdivisionsHeight", d => Of(d).SubdivisionsHeight, run) { Help = "How many rings the game builds the dome of from horizon to top" },
        ]);
        SkyObjects = new SettingGroup("Sky objects",
        [
            new TextureSetting("Atlas", set, "albedo", d => Of(d).Albedo, run) { Help = "The picture the sky objects (planets, stars) are cut from, e.g. /maps/<folder>/env/decals/sky_objects_albedo.dds" },
            new TextureSetting("Glow atlas", set, "glow", d => Of(d).Glow, run) { Help = "Where the sky objects glow, in the same layout as the atlas" },
            new NumberSetting("Glow", set, "glowMultiplier", d => Of(d).DecalGlowMultiplier, run) { Minimum = 0, Maximum = 2, HasSlider = true, Help = "How strongly the glow atlas glows" },
        ]);
        Cirrus = new SettingGroup("Cirrus",
        [
            new TextureSetting("Cirrus texture", set, "cirrusTexture", d => Of(d).CirrusTexture, run) { Help = "The clouds' texture: a layer in each of its channels (red, green, blue, alpha)" },
            new ColorSetting("Cirrus colour", set, "cirrusColor", d => Of(d).CirrusColor, run),
            new NumberSetting("Cirrus strength", set, "cirrusMultiplier", d => Of(d).CirrusMultiplier, run) { Minimum = 0, Maximum = 2, HasSlider = true, Help = "How strongly the cirrus covers the sky; 0 for none" },
        ]);
        CirrusLayers = [.. Enumerable.Range(0, Skyboxes.DefaultCirrusLayers.Length).Select(Layer)];
    }

    private static Skybox Of(MapDocument document) => document.Sky.Skybox ?? Empty;

    // what the fields read while the map has no skybox (they are hidden then)
    private static readonly Skybox Empty = new() { Albedo = "", Glow = "", CirrusTexture = "" };

    /// <summary>
    /// The dome: its colours, heights, place and how the game builds it.
    /// </summary>
    public SettingGroup Dome { get; }

    /// <summary>
    /// The sky objects' atlases and glow.
    /// </summary>
    public SettingGroup SkyObjects { get; }

    /// <summary>
    /// The cirrus texture, colour and strength.
    /// </summary>
    public SettingGroup Cirrus { get; }

    /// <summary>
    /// A group of fields per cirrus layer: four, as the game always has.
    /// </summary>
    public IReadOnlyList<SettingGroup> CirrusLayers { get; }

    /// <summary>
    /// Whether the map has a skybox: its fields show, else Add and Read a preset.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoSkybox))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    public partial bool HasSkybox { get; private set; }

    public bool HasNoSkybox => !HasSkybox;

    /// <summary>
    /// What the skybox holds, or what a map without one shows instead.
    /// </summary>
    [ObservableProperty]
    public partial string Summary { get; private set; } = "";

    /// <summary>
    /// Whether the map is written in a version that stores no skybox (53, 56): the game then draws its default dome.
    /// </summary>
    [ObservableProperty]
    public partial string VersionNote { get; private set; } = "";

    /// <summary>
    /// Whether the map can be written as version 60 to use the skybox (a map of 56; 53 keeps its version).
    /// </summary>
    [ObservableProperty]
    public partial bool CanWriteAsVersion60 { get; private set; }

    /// <summary>
    /// The line beside the panel's entry.
    /// </summary>
    public string Detail { get; private set; } = "";

    public void Refresh(MapDocument? document)
    {
        Skybox? skybox = document?.Sky.Skybox;
        int version = document?.Terrain.ScmapVersion ?? -1;
        if (document is not null && ReferenceEquals(skybox, _shown) && version == _version)
        {
            return;
        }
        _shown = skybox;
        _version = version;
        HasSkybox = skybox is not null;
        MapDocument? shown = skybox is null ? null : document;
        Dome.Refresh(shown);
        SkyObjects.Refresh(shown);
        Cirrus.Refresh(shown);
        foreach (SettingGroup layer in CirrusLayers)
        {
            layer.Refresh(shown);
        }
        Summary = skybox is null
            ? "The map has no skybox: the sky cube map is made from the lighting. Add one made from the lighting, or read a preset of the FAF map editor."
            : $"{Count(skybox.Planets.Length, "sky object")} cut from the atlas, {Count(skybox.CirrusLayers.Length, "cirrus layer")}. The editor shows the skybox through the sky cube map (Procedural mode), made from it.";
        VersionNote = skybox is not null && version is > 0 and < 60
            ? $"The map is a version {version} map, which stores no skybox: the game draws its own default dome (made from the map's size), and the export leaves this one out. It stays in the project and the sky cube map is made from it."
            : "";
        CanWriteAsVersion60 = skybox is not null && version is >= NewMap.TenLayersFrom and < 60;
        Detail = document is null ? "" : skybox is null ? "none" : Count(skybox.Planets.Length, "object");
    }

    private SettingGroup Layer(int index)
    {
        const string set = "lighting.setCirrusLayer";
        Func<JsonObject> layer = () => new JsonObject { ["index"] = index };
        CirrusLayer LayerOf(MapDocument d) => d.Sky.Skybox is { } s && index < s.CirrusLayers.Length ? s.CirrusLayers[index] : new CirrusLayer();
        string channel = index < Channels.Length ? Channels[index] : "none";
        return new SettingGroup($"Cirrus layer {index + 1} ({channel})",
        [
            new NumberSetting($"Layer {index + 1} frequency x", set, "frequencyX", d => LayerOf(d).Frequency.X, _run, layer) { Help = "How often the layer repeats across the dome" },
            new NumberSetting($"Layer {index + 1} frequency y", set, "frequencyY", d => LayerOf(d).Frequency.Y, _run, layer),
            new NumberSetting($"Layer {index + 1} speed", set, "speed", d => LayerOf(d).Speed, _run, layer) { Help = "How fast it drifts in the game (the sky cube map shows it at rest)" },
            new NumberSetting($"Layer {index + 1} direction x", set, "directionX", d => LayerOf(d).Direction.X, _run, layer) { Help = "Which way it drifts and turns" },
            new NumberSetting($"Layer {index + 1} direction y", set, "directionY", d => LayerOf(d).Direction.Y, _run, layer),
        ]);
    }

    // the cirrus texture's channel each layer reads, in order
    private static readonly string[] Channels = ["red", "green", "blue", "alpha"];

    private static string Count(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    [RelayCommand]
    private Task Add() => _run("lighting.addSkybox", []);

    [RelayCommand]
    private Task Import() => _run("lighting.importSkybox", []);

    [RelayCommand(CanExecute = nameof(HasSkybox))]
    private Task Remove() => _run("lighting.removeSkybox", []);

    /// <summary>
    /// Writes the map as version 60, so the game draws this skybox (<c>map.setScmapVersion</c>).
    /// </summary>
    [RelayCommand]
    private Task WriteAsVersion60() => _run("map.setScmapVersion", new JsonObject { ["version"] = 60 });

}
