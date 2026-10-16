using System.Numerics;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Changes the lighting (user story L-1), one step of undo; the steps that change the same fields
/// one after the other are one step (a slider dragged, the sun turned with the sun tool). What
/// the Lighting panel's fields do; the 3D view is lit by it at once.
/// </summary>
/// <param name="Azimuth">Where the sun stands, in degrees clockwise from north (up in the top view).</param>
/// <param name="Elevation">How high it stands above the horizon, in degrees.</param>
/// <param name="SunDirection">The direction towards the sun as x, y, z, instead of the angles.</param>
/// <param name="SunColor">Red, green and blue; past 1 is brighter.</param>
/// <param name="SpecularColor">Red, green and blue of the specular colour; its fourth number is <c>specularAlpha</c>.</param>
[EditorCommand("lighting.set", "Change the lighting", Story = "L-1", Needs = Needs.Map | Needs.NoStroke)]
public sealed record SetLighting(
    double? Azimuth = null,
    double? Elevation = null,
    float[]? SunDirection = null,
    float[]? SunColor = null,
    float[]? SunAmbience = null,
    float[]? ShadowFillColor = null,
    float? Multiplier = null,
    float[]? SpecularColor = null,
    float? SpecularAlpha = null,
    float? Bloom = null,
    float[]? FogColor = null,
    float? FogStart = null,
    float? FogEnd = null) : IRequest;

public sealed class SetLightingHandler(MainViewModel model) : IRequestHandler<SetLighting>
{
    public Task Handle(SetLighting r, CancellationToken cancellationToken)
    {
        string given = WaterArguments.Given(("azimuth", r.Azimuth), ("elevation", r.Elevation), ("sunDirection", r.SunDirection), ("sunColor", r.SunColor),
            ("sunAmbience", r.SunAmbience), ("shadowFillColor", r.ShadowFillColor), ("multiplier", r.Multiplier), ("specularColor", r.SpecularColor),
            ("specularAlpha", r.SpecularAlpha), ("bloom", r.Bloom), ("fogColor", r.FogColor), ("fogStart", r.FogStart), ("fogEnd", r.FogEnd));
        if (given.Length == 0)
        {
            model.SelectMode("lighting");
            model.Status = "The lighting's settings are in the Lighting panel";
            return Task.CompletedTask;
        }
        if (r.Elevation is < -90 or > 90)
        {
            throw new ArgumentException("The sun's elevation lies between -90 and 90 degrees");
        }
        Vector3? direction = WaterArguments.Vector(r.SunDirection, "sunDirection");
        if (direction is { } d && d.LengthSquared() == 0)
        {
            throw new ArgumentException("The sun's direction is not 0, 0, 0");
        }
        Vector3? sunColor = WaterArguments.Vector(r.SunColor, "sunColor");
        Vector3? ambience = WaterArguments.Vector(r.SunAmbience, "sunAmbience");
        Vector3? shadowFill = WaterArguments.Vector(r.ShadowFillColor, "shadowFillColor");
        Vector3? specular = WaterArguments.Vector(r.SpecularColor, "specularColor");
        Vector3? fog = WaterArguments.Vector(r.FogColor, "fogColor");
        model.EditMap($"Lighting: {given.Replace(",", ", ")}", document =>
        {
            Lighting l = document.Lighting;
            Vector3 sun = direction is { } chosen ? Vector3.Normalize(chosen) : l.SunDirection;
            if (r.Azimuth is not null || r.Elevation is not null)
            {
                (double azimuth, double elevation) = SunAngles.Of(l.SunDirection);
                sun = SunAngles.Direction(r.Azimuth ?? azimuth, r.Elevation ?? elevation);
            }
            return document with
            {
                Lighting = l with
                {
                    SunDirection = sun,
                    SunColor = sunColor ?? l.SunColor,
                    SunAmbience = ambience ?? l.SunAmbience,
                    ShadowFillColor = shadowFill ?? l.ShadowFillColor,
                    Multiplier = r.Multiplier ?? l.Multiplier,
                    SpecularColor = new Vector4(specular ?? new Vector3(l.SpecularColor.X, l.SpecularColor.Y, l.SpecularColor.Z), r.SpecularAlpha ?? l.SpecularColor.W),
                    Bloom = r.Bloom ?? l.Bloom,
                    FogColor = fog ?? l.FogColor,
                    FogStart = r.FogStart ?? l.FogStart,
                    FogEnd = r.FogEnd ?? l.FogEnd,
                },
            };
        }, $"lighting.set:{given}");
        return Task.CompletedTask;
    }
}

/// <summary>
/// Applies a lighting preset of the workspace (user story L-2), one step of undo: the lighting of
/// a biome, saved from another map (the Lighting panel's presets, <c>edit.saveAsPreset</c>).
/// Without a name it shows the Lighting panel, which lists them.
/// </summary>
/// <param name="Name">The preset's name.</param>
[EditorCommand("lighting.preset", "Apply a lighting preset...", Story = "L-2", Needs = Needs.Map | Needs.NoStroke)]
public sealed record ApplyLightingPreset(string? Name = null) : IRequest;

public sealed class ApplyLightingPresetHandler(MainViewModel model, PresetsViewModel presets) : IRequestHandler<ApplyLightingPreset>
{
    public Task Handle(ApplyLightingPreset request, CancellationToken cancellationToken)
    {
        if (request.Name is null)
        {
            model.SelectMode("lighting");
            string[] names = presets.Library is PresetLibrary library ? [.. library.List(AtmospherePresets.LightingKind).Select(preset => preset.Name)] : [];
            model.Status = names.Length == 0
                ? "No lighting presets yet: save one in the Lighting panel"
                : $"Lighting presets, in the Lighting panel: {string.Join(", ", names)}";
            return Task.CompletedTask;
        }
        Preset preset = presets.Apply(AtmospherePresets.LightingKind, request.Name);
        model.Status = $"Applied the lighting {preset.Name}";
        return Task.CompletedTask;
    }
}
