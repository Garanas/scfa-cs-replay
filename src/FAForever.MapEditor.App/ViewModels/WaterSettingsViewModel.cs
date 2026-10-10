using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// A wave generator in the Water inspector's list.
/// </summary>
/// <param name="Index">Its place in the map's list, from 0.</param>
public sealed record WaveGeneratorItem(int Index, string Title);

/// <summary>
/// The water (user story W-1): on or off, the surface, deep and abyss elevations and the colours in
/// the Water panel; the sun on the water, reflection, refraction, its textures, the four wave
/// textures and the wave generators in the inspector. Every field changes the map through a
/// command (<c>water.set</c>, <c>water.setWaveTexture</c>, <c>water.setWaveGenerator</c>), one
/// step of undo per field; the 3D view draws the water at its elevations at once.
/// </summary>
public sealed partial class WaterSettingsViewModel : ObservableObject
{
    private readonly Func<string, JsonObject, Task> _run;
    private Water? _shown;
    private MapDocument? _document;

    public WaterSettingsViewModel(Func<string, JsonObject, Task> run)
    {
        _run = run;
        const string set = "water.set";
        Basic = new SettingGroup("Water",
        [
            new BoolSetting("Water", set, "hasWater", d => d.Water.HasWater, run) { Help = "Whether the map has water; the elevations are kept either way" },
            new NumberSetting("Surface", set, "elevation", d => d.Water.Elevation, run) { Minimum = 0, Maximum = 256, HasSlider = true, Help = "The height of the water's surface, in game units" },
            new NumberSetting("Deep", set, "elevationDeep", d => d.Water.ElevationDeep, run) { Minimum = 0, Maximum = 256, HasSlider = true, Help = "Below this height the water is deep: naval units only" },
            new NumberSetting("Abyss", set, "elevationAbyss", d => d.Water.ElevationAbyss, run) { Minimum = 0, Maximum = 256, HasSlider = true, Help = "Below this height the water is at its darkest" },
            new ColorSetting("Surface colour", set, "surfaceColor", d => d.Water.SurfaceColor, run),
            new NumberSetting("Colour near", set, "colorLerpMin", d => d.Water.ColorLerp.X, run) { Help = "How much of the surface colour shows in shallow water" },
            new NumberSetting("Colour far", set, "colorLerpMax", d => d.Water.ColorLerp.Y, run) { Help = "How much of the surface colour shows in deep water" },
        ]);
        Surface = new SettingGroup("Sun and reflection",
        [
            new ColorSetting("Sun colour", set, "sunColor", d => d.Water.SunColor, run),
            new ColorSetting("Sun direction", set, "sunDirection", d => d.Water.SunDirection, run) { ShowsSwatch = false, Help = "x, y, z of the sun the water reflects" },
            new NumberSetting("Sun shininess", set, "sunShininess", d => d.Water.SunShininess, run) { Minimum = 0, Maximum = 200, HasSlider = true },
            new NumberSetting("Sun strength", set, "sunStrength", d => d.Water.SunStrength, run) { Minimum = 0, Maximum = 20, HasSlider = true },
            new NumberSetting("Sun reflection", set, "sunReflection", d => d.Water.SunReflection, run) { Minimum = 0, Maximum = 10, HasSlider = true },
            new NumberSetting("Sun glow", set, "sunGlow", d => d.Water.SunGlow, run) { Minimum = 0, Maximum = 2, HasSlider = true },
            new NumberSetting("Unit reflection", set, "unitReflection", d => d.Water.UnitReflection, run) { Minimum = 0, Maximum = 2, HasSlider = true },
            new NumberSetting("Sky reflection", set, "skyReflection", d => d.Water.SkyReflection, run) { Minimum = 0, Maximum = 2, HasSlider = true },
            new NumberSetting("Refraction", set, "refractionScale", d => d.Water.RefractionScale, run) { Minimum = 0, Maximum = 1, HasSlider = true },
            new NumberSetting("Fresnel bias", set, "fresnelBias", d => d.Water.FresnelBias, run) { Minimum = 0, Maximum = 1, HasSlider = true },
            new NumberSetting("Fresnel power", set, "fresnelPower", d => d.Water.FresnelPower, run) { Minimum = 0, Maximum = 10, HasSlider = true },
        ]);
        Textures = new SettingGroup("Textures",
        [
            new TextureSetting("Cubemap", set, "cubemap", d => d.Water.Cubemap, run, null,
                () => run("lighting.chooseCubemap", new JsonObject { ["target"] = "water" }))
            {
                Help = "What the water reflects, e.g. /textures/environment/skycube_evergreen01a.dds; Choose opens the picker of the workspace's cube maps",
            },
            new TextureSetting("Ramp", set, "ramp", d => d.Water.Ramp, run) { Help = "The colour of the water by depth, e.g. /textures/engine/waterramp.dds" },
        ]);
        WaveTextures = [.. Enumerable.Range(0, 4).Select(index => WaveTexture(index, run))];
        Generator = GeneratorFields(run, () => SelectedGenerator?.Index ?? 0);
    }

    /// <summary>
    /// The Water panel's fields.
    /// </summary>
    public SettingGroup Basic { get; }

    public SettingGroup Surface { get; }

    public SettingGroup Textures { get; }

    /// <summary>
    /// The four normal maps of the surface, each with its repeat and movement.
    /// </summary>
    public IReadOnlyList<SettingGroup> WaveTextures { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GeneratorsSummary))]
    public partial IReadOnlyList<WaveGeneratorItem> Generators { get; private set; } = [];

    /// <summary>
    /// The generator whose fields <see cref="Generator"/> shows.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedGenerator))]
    public partial WaveGeneratorItem? SelectedGenerator { get; set; }

    public bool HasSelectedGenerator => SelectedGenerator is not null;

    /// <summary>
    /// The fields of the selected wave generator.
    /// </summary>
    public SettingGroup Generator { get; }

    public string GeneratorsSummary => Generators.Count switch
    {
        0 => "None: waves roll onto no shore",
        1 => "1 wave generator",
        int count => string.Create(CultureInfo.InvariantCulture, $"{count} wave generators"),
    };

    partial void OnSelectedGeneratorChanged(WaveGeneratorItem? value) => Generator.Refresh(value is null ? null : _document);

    [RelayCommand]
    private Task AddGenerator() => _run("water.addWaveGenerator", []);

    [RelayCommand]
    private Task RemoveGenerator() => SelectedGenerator is { } generator
        ? _run("water.removeWaveGenerator", new JsonObject { ["index"] = generator.Index })
        : Task.CompletedTask;

    public void Refresh(MapDocument? document)
    {
        _document = document;
        if (document is null)
        {
            _shown = null;
            Basic.Refresh(null);
            Surface.Refresh(null);
            Textures.Refresh(null);
            foreach (SettingGroup group in WaveTextures)
            {
                group.Refresh(null);
            }
            Generators = [];
            SelectedGenerator = null;
            return;
        }
        if (ReferenceEquals(document.Water, _shown))
        {
            return;
        }
        ImmutableArray<WaveGenerator> before = _shown?.WaveGenerators ?? [];
        _shown = document.Water;
        Basic.Refresh(document);
        Surface.Refresh(document);
        Textures.Refresh(document);
        foreach (SettingGroup group in WaveTextures)
        {
            group.Refresh(document);
        }
        if (before != document.Water.WaveGenerators)
        {
            int? selected = SelectedGenerator?.Index;
            Generators = [.. document.Water.WaveGenerators.Select((generator, index) => new WaveGeneratorItem(index, string.Create(CultureInfo.InvariantCulture,
                $"{index + 1}: {Path.GetFileNameWithoutExtension(generator.Texture)} at {generator.Position.X:0.#}, {generator.Position.Z:0.#}")))];
            SelectedGenerator = selected is int index && index < Generators.Count ? Generators[index] : Generators.LastOrDefault();
        }
        if (SelectedGenerator is not null)
        {
            Generator.Refresh(document);
        }
    }

    private static SettingGroup WaveTexture(int index, Func<string, JsonObject, Task> run)
    {
        const string command = "water.setWaveTexture";
        JsonObject Fixed() => new JsonObject { ["index"] = index };
        WaveTexture? Of(MapDocument d) => index < d.Water.WaveTextures.Length ? d.Water.WaveTextures[index] : null;
        return new SettingGroup(string.Create(CultureInfo.InvariantCulture, $"Wave texture {index + 1}"),
        [
            new TextureSetting("Texture", command, "texture", d => Of(d)?.Texture ?? "", run, Fixed),
            new NumberSetting("Repeat", command, "normalRepeat", d => Of(d)?.NormalRepeat ?? 0, run, Fixed) { Help = "How often the normal map repeats across the surface" },
            new NumberSetting("Movement x", command, "movementX", d => Of(d)?.NormalMovement.X ?? 0, run, Fixed),
            new NumberSetting("Movement z", command, "movementZ", d => Of(d)?.NormalMovement.Y ?? 0, run, Fixed),
        ]);
    }

    private static SettingGroup GeneratorFields(Func<string, JsonObject, Task> run, Func<int> selected)
    {
        const string command = "water.setWaveGenerator";
        JsonObject Fixed() => new JsonObject { ["index"] = selected() };
        WaveGenerator Of(MapDocument d) => selected() < d.Water.WaveGenerators.Length ? d.Water.WaveGenerators[selected()] : new WaveGenerator { Texture = "", Ramp = "" };
        return new SettingGroup("Wave generator",
        [
            new TextureSetting("Texture", command, "texture", d => Of(d).Texture, run, Fixed),
            new TextureSetting("Ramp", command, "ramp", d => Of(d).Ramp, run, Fixed),
            new NumberSetting("X", command, "x", d => Of(d).Position.X, run, Fixed),
            new NumberSetting("Z", command, "z", d => Of(d).Position.Z, run, Fixed),
            new NumberSetting("Height", command, "y", d => Of(d).Position.Y, run, Fixed),
            new NumberSetting("Rotation", command, "rotation", d => Of(d).Rotation, run, Fixed) { Help = "In radians" },
            new ColorSetting("Velocity", command, "velocity", d => Of(d).Velocity, run, Fixed) { ShowsSwatch = false, Help = "x, y, z: how fast the waves move" },
            new NumberSetting("Lifetime from", command, "lifetimeFirst", d => Of(d).LifetimeFirst, run, Fixed),
            new NumberSetting("Lifetime to", command, "lifetimeSecond", d => Of(d).LifetimeSecond, run, Fixed),
            new NumberSetting("Period from", command, "periodFirst", d => Of(d).PeriodFirst, run, Fixed),
            new NumberSetting("Period to", command, "periodSecond", d => Of(d).PeriodSecond, run, Fixed),
            new NumberSetting("Scale from", command, "scaleFirst", d => Of(d).ScaleFirst, run, Fixed),
            new NumberSetting("Scale to", command, "scaleSecond", d => Of(d).ScaleSecond, run, Fixed),
            new NumberSetting("Frames", command, "frameCount", d => Of(d).FrameCount, run, Fixed),
            new NumberSetting("Frame rate from", command, "frameRateFirst", d => Of(d).FrameRateFirst, run, Fixed),
            new NumberSetting("Frame rate to", command, "frameRateSecond", d => Of(d).FrameRateSecond, run, Fixed),
            new NumberSetting("Strips", command, "stripCount", d => Of(d).StripCount, run, Fixed),
        ]);
    }
}
