using System.Numerics;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The sun's direction as two angles, the way a person turns it: the azimuth, clockwise from north
/// (up in the top view, towards -z) in degrees, and the elevation above the horizon. The map keeps
/// the direction towards the sun as a vector (<see cref="Lighting.SunDirection"/>, y up).
/// </summary>
public static class SunAngles
{
    /// <summary>
    /// The azimuth (0 to 360) and elevation (-90 to 90) of a direction towards the sun.
    /// </summary>
    public static (double Azimuth, double Elevation) Of(Vector3 direction)
    {
        if (direction.LengthSquared() == 0)
        {
            return (0, 90);
        }
        Vector3 d = Vector3.Normalize(direction);
        double elevation = Math.Asin(Math.Clamp(d.Y, -1, 1)) * 180 / Math.PI;
        double azimuth = Math.Atan2(d.X, -d.Z) * 180 / Math.PI;
        return (azimuth < 0 ? azimuth + 360 : azimuth, elevation);
    }

    /// <summary>
    /// The direction towards the sun at an azimuth and elevation, in degrees.
    /// </summary>
    public static Vector3 Direction(double azimuth, double elevation)
    {
        double a = azimuth * Math.PI / 180;
        double e = elevation * Math.PI / 180;
        return new Vector3((float)(Math.Cos(e) * Math.Sin(a)), (float)Math.Sin(e), (float)(-Math.Cos(e) * Math.Cos(a)));
    }
}

/// <summary>
/// The lighting (user story L-1): the sun's direction as azimuth and elevation, its colour, the
/// ambience, shadow fill, multiplier, specular, bloom and fog, each a field of the Lighting panel
/// that changes the map through <c>lighting.set</c>, one step of undo per field. The 3D view's
/// terrain and objects are lit by it at once; the sun tool turns the sun with the mouse. The sky's
/// cube maps and background are fields of the inspector (<c>lighting.setSky</c>, chosen in the picker
/// with <c>lighting.chooseCubemap</c>); the 3D view shows the sky's cube map around the map.
/// </summary>
public sealed partial class LightingSettingsViewModel : ObservableObject
{
    private Lighting? _shown;
    private Sky? _shownSky;

    /// <summary>
    /// The backgrounds the game has in <c>/textures/environment</c>, as the maps name them, with
    /// what the question of Choose... calls them.
    /// </summary>
    public static IReadOnlyList<(string Title, string Path)> Backgrounds { get; } =
    [
        ("Default", "/textures/environment/defaultbackground.dds"),
        ("Black", "/textures/environment/blackbackground.dds"),
        ("Seraphim", "/textures/environment/seraphim iibackground3.dds"),
    ];

    /// <param name="ask">Asks a question over the window with these buttons (the last cancels) and
    /// answers the index of the one pressed: Choose... of the background.</param>
    public LightingSettingsViewModel(Func<string, JsonObject, Task> run, Func<string, IReadOnlyList<string>, Task<int>>? ask = null)
    {
        const string set = "lighting.set";
        Sun = new SettingGroup("Sun",
        [
            new NumberSetting("Azimuth", set, "azimuth", d => Math.Round(SunAngles.Of(d.Lighting.SunDirection).Azimuth, 1), run)
            {
                Minimum = 0, Maximum = 360, HasSlider = true, Help = "Where the sun stands, in degrees clockwise from north (up in the top view)",
            },
            new NumberSetting("Elevation", set, "elevation", d => Math.Round(SunAngles.Of(d.Lighting.SunDirection).Elevation, 1), run)
            {
                Minimum = 0, Maximum = 90, HasSlider = true, Help = "How high the sun stands above the horizon, in degrees",
            },
            new ColorSetting("Sun colour", set, "sunColor", d => d.Lighting.SunColor, run),
            new NumberSetting("Multiplier", set, "multiplier", d => d.Lighting.Multiplier, run) { Minimum = 0, Maximum = 4, HasSlider = true, Help = "How strongly the sun lights" },
        ]);
        Colours = new SettingGroup("Colours and ambience",
        [
            new ColorSetting("Ambience", set, "sunAmbience", d => d.Lighting.SunAmbience, run) { Help = "The light that comes from everywhere" },
            new ColorSetting("Shadow fill", set, "shadowFillColor", d => d.Lighting.ShadowFillColor, run) { Help = "The colour of what lies in shadow" },
            new ColorSetting("Specular", set, "specularColor", d => new Vector3(d.Lighting.SpecularColor.X, d.Lighting.SpecularColor.Y, d.Lighting.SpecularColor.Z), run),
            new NumberSetting("Specular strength", set, "specularAlpha", d => d.Lighting.SpecularColor.W, run) { Minimum = 0, Maximum = 1, HasSlider = true },
        ]);
        Bloom = new SettingGroup("Bloom",
        [
            new NumberSetting("Bloom", set, "bloom", d => d.Lighting.Bloom, run) { Minimum = 0, Maximum = 0.5, HasSlider = true, Help = "How much bright parts glow beyond their edges" },
        ]);
        const string sky = "lighting.setSky";
        Sky = new SettingGroup("Sky",
        [
            new TextureSetting("Sky cube map", sky, "cubemap", d => d.Sky.Cubemap, run, null,
                () => run("lighting.chooseCubemap", new JsonObject { ["target"] = "sky" }))
            {
                Help = "What the 3D view shows around the map, and the water reflects there; Choose opens the picker of the workspace's cube maps",
            },
            new TextureSetting("Reflected by units", sky, "environmentCubemap", d => Commands.SkyFields.EnvironmentOf(d.Sky), run, null,
                () => run("lighting.chooseCubemap", new JsonObject { ["target"] = "environment" }))
            {
                Help = "The cube map units and props reflect (the map's <default> environment cube map)",
            },
            new TextureSetting("Background", sky, "background", d => d.Sky.Background, run, null, ask is null ? null : async () =>
            {
                int chosen = await ask("The picture far behind the sky: one of the game's", [.. Backgrounds.Select(background => background.Title), "Cancel"]);
                if (chosen >= 0 && chosen < Backgrounds.Count)
                {
                    await run(sky, new JsonObject { ["background"] = Backgrounds[chosen].Path });
                }
            })
            {
                Help = "The picture far behind the sky, e.g. /textures/environment/defaultbackground.dds; Choose offers the game's",
            },
        ]);
        Fog = new SettingGroup("Fog",
        [
            new ColorSetting("Fog colour", set, "fogColor", d => d.Lighting.FogColor, run),
            new NumberSetting("Fog start", set, "fogStart", d => d.Lighting.FogStart, run) { Minimum = 0, Maximum = 2000, HasSlider = true },
            new NumberSetting("Fog end", set, "fogEnd", d => d.Lighting.FogEnd, run) { Minimum = 0, Maximum = 4000, HasSlider = true },
        ]);
        Parts = new PanelParts(
        [
            new PanelPart(SunPart, "Sun", "Settings", new SettingsPart([Sun], "The Sun tool turns the sun: drag in a view to where it is to stand."), "Where the sun stands, its colour and strength"),
            new PanelPart(ColoursPart, "Colours and ambience", "Settings", new SettingsPart([Colours]), "The light from everywhere, the shadows' colour and the shine"),
            new PanelPart(FogPart, "Fog", "Settings", new SettingsPart([Fog]), "The haze far from the camera"),
            new PanelPart(BloomPart, "Bloom", "Settings", new SettingsPart([Bloom]), "How bright parts glow"),
            new PanelPart(SkyPart, "Sky and cube maps", "Sky", new SettingsPart([Sky], "The 3D view shows the sky's cube map around the map, and the water reflects it."), "The sky around the map, what units reflect, the background"),
            new PanelPart(ShadowDecalPart, "Shadow decal", "Map-wide decals",
                new PlannedPart("L-1", "A decal over the whole map that darkens the crevices: generated from the final heightmap (ambient occlusion, and the sun's shadows as an option) or a file of your own, generated again from here."),
                "The map-wide shadow decal"),
        ]);
    }

    // the ids of the Lighting panel's entries (view.inspectPart)
    public const string SunPart = "lighting.sun";
    public const string ColoursPart = "lighting.colours";
    public const string FogPart = "lighting.fog";
    public const string BloomPart = "lighting.bloom";
    public const string SkyPart = "lighting.sky";
    public const string ShadowDecalPart = "lighting.shadowDecal";

    /// <summary>
    /// What the Lighting panel lists: the sun, the colours, fog, bloom, the sky and the map-wide
    /// shadow decal; the inspector edits the chosen one.
    /// </summary>
    public PanelParts Parts { get; }

    /// <summary>
    /// The sun's place, colour and multiplier.
    /// </summary>
    public SettingGroup Sun { get; }

    /// <summary>
    /// The ambience, shadow fill and specular colour.
    /// </summary>
    public SettingGroup Colours { get; }

    public SettingGroup Bloom { get; }

    public SettingGroup Fog { get; }

    /// <summary>
    /// The sky's cube maps and background (user story L-1), in the Lighting mode's inspector.
    /// </summary>
    public SettingGroup Sky { get; }

    public void Refresh(MapDocument? document)
    {
        if (document is null || !ReferenceEquals(document.Sky, _shownSky))
        {
            _shownSky = document?.Sky;
            Sky.Refresh(document);
            Parts.Find(SkyPart)!.Detail = document is null ? "" : Path.GetFileNameWithoutExtension(document.Sky.Cubemap);
        }
        if (document is not null && ReferenceEquals(document.Lighting, _shown))
        {
            return;
        }
        _shown = document?.Lighting;
        Sun.Refresh(document);
        Colours.Refresh(document);
        Bloom.Refresh(document);
        Fog.Refresh(document);
        RefreshDetails(document?.Lighting);
    }

    /// <summary>
    /// The lines beside the Lighting panel's entries: where the sun stands, how far the fog lies, the bloom.
    /// </summary>
    private void RefreshDetails(Lighting? lighting)
    {
        if (lighting is null)
        {
            foreach (PanelPart part in Parts.Items)
            {
                part.Detail = "";
            }
            return;
        }
        (double azimuth, double elevation) = SunAngles.Of(lighting.SunDirection);
        Parts.Find(SunPart)!.Detail = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{azimuth:0}° · {elevation:0}° up");
        Parts.Find(FogPart)!.Detail = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{lighting.FogStart:0} to {lighting.FogEnd:0}");
        Parts.Find(BloomPart)!.Detail = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{lighting.Bloom:0.###}");
    }
}
