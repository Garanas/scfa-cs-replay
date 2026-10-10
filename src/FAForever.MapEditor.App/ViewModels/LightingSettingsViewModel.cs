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
/// terrain and objects are lit by it at once; the sun tool turns the sun with the mouse.
/// </summary>
public sealed partial class LightingSettingsViewModel : ObservableObject
{
    private Lighting? _shown;

    public LightingSettingsViewModel(Func<string, JsonObject, Task> run)
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
            new ColorSetting("Ambience", set, "sunAmbience", d => d.Lighting.SunAmbience, run) { Help = "The light that comes from everywhere" },
            new ColorSetting("Shadow fill", set, "shadowFillColor", d => d.Lighting.ShadowFillColor, run) { Help = "The colour of what lies in shadow" },
            new NumberSetting("Multiplier", set, "multiplier", d => d.Lighting.Multiplier, run) { Minimum = 0, Maximum = 4, HasSlider = true, Help = "How strongly the sun lights" },
            new ColorSetting("Specular", set, "specularColor", d => new Vector3(d.Lighting.SpecularColor.X, d.Lighting.SpecularColor.Y, d.Lighting.SpecularColor.Z), run),
            new NumberSetting("Specular strength", set, "specularAlpha", d => d.Lighting.SpecularColor.W, run) { Minimum = 0, Maximum = 1, HasSlider = true },
            new NumberSetting("Bloom", set, "bloom", d => d.Lighting.Bloom, run) { Minimum = 0, Maximum = 0.5, HasSlider = true },
        ]);
        Fog = new SettingGroup("Fog",
        [
            new ColorSetting("Fog colour", set, "fogColor", d => d.Lighting.FogColor, run),
            new NumberSetting("Fog start", set, "fogStart", d => d.Lighting.FogStart, run) { Minimum = 0, Maximum = 2000, HasSlider = true },
            new NumberSetting("Fog end", set, "fogEnd", d => d.Lighting.FogEnd, run) { Minimum = 0, Maximum = 4000, HasSlider = true },
        ]);
    }

    public SettingGroup Sun { get; }

    public SettingGroup Fog { get; }

    public void Refresh(MapDocument? document)
    {
        if (document is not null && ReferenceEquals(document.Lighting, _shown))
        {
            return;
        }
        _shown = document?.Lighting;
        Sun.Refresh(document);
        Fog.Refresh(document);
    }
}
