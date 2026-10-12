using System.Numerics;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Files.InMemory;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// What the water commands share: the names of the arguments given, for a step's merge key, and
/// three numbers as a vector.
/// </summary>
internal static class WaterArguments
{
    public static string Given(params (string Name, object? Value)[] arguments) =>
        string.Join(",", arguments.Where(argument => argument.Value is not null).Select(argument => argument.Name));

    /// <exception cref="ArgumentException">Not three numbers.</exception>
    public static Vector3? Vector(float[]? values, string name) => values switch
    {
        null => null,
        { Length: 3 } => new Vector3(values[0], values[1], values[2]),
        _ => throw new ArgumentException($"{name} is three numbers: [x, y, z] or [red, green, blue]"),
    };
}

/// <summary>
/// Changes the water's settings (user story W-1), one step of undo; the steps that change the same
/// fields one after the other are one step (a slider dragged). What the Water panel's and
/// inspector's fields do; the 3D view draws the water at its elevations at once.
/// </summary>
/// <param name="HasWater">Whether the map has water; the elevations stay either way.</param>
/// <param name="Elevation">The surface's height in game units; deep and abyss stay below it.</param>
/// <param name="SurfaceColor">Red, green and blue.</param>
/// <param name="ColorLerpMin">How much of the surface colour shows in shallow water.</param>
/// <param name="SunDirection">x, y and z of the sun the water reflects.</param>
/// <param name="Cubemap">The game path of what the water reflects.</param>
[EditorCommand("water.set", "Change the water", Story = "W-1", Needs = Needs.Map | Needs.NoStroke)]
public sealed record SetWater(
    bool? HasWater = null,
    float? Elevation = null,
    float? ElevationDeep = null,
    float? ElevationAbyss = null,
    float[]? SurfaceColor = null,
    float? ColorLerpMin = null,
    float? ColorLerpMax = null,
    float? RefractionScale = null,
    float? FresnelBias = null,
    float? FresnelPower = null,
    float? UnitReflection = null,
    float? SkyReflection = null,
    float? SunShininess = null,
    float? SunStrength = null,
    float[]? SunDirection = null,
    float[]? SunColor = null,
    float? SunReflection = null,
    float? SunGlow = null,
    string? Cubemap = null,
    string? Ramp = null) : IRequest;

public sealed class SetWaterHandler(MainViewModel model) : IRequestHandler<SetWater>
{
    public Task Handle(SetWater r, CancellationToken cancellationToken)
    {
        string given = WaterArguments.Given(("hasWater", r.HasWater), ("elevation", r.Elevation), ("elevationDeep", r.ElevationDeep), ("elevationAbyss", r.ElevationAbyss),
            ("surfaceColor", r.SurfaceColor), ("colorLerpMin", r.ColorLerpMin), ("colorLerpMax", r.ColorLerpMax), ("refractionScale", r.RefractionScale),
            ("fresnelBias", r.FresnelBias), ("fresnelPower", r.FresnelPower), ("unitReflection", r.UnitReflection), ("skyReflection", r.SkyReflection),
            ("sunShininess", r.SunShininess), ("sunStrength", r.SunStrength), ("sunDirection", r.SunDirection), ("sunColor", r.SunColor),
            ("sunReflection", r.SunReflection), ("sunGlow", r.SunGlow), ("cubemap", r.Cubemap), ("ramp", r.Ramp));
        if (given.Length == 0)
        {
            model.SelectMode("water");
            model.Status = "The water's settings are in the Water panel and the inspector";
            return Task.CompletedTask;
        }
        if (r.Elevation is < 0 || r.ElevationDeep is < 0 || r.ElevationAbyss is < 0)
        {
            throw new ArgumentException("The water's elevations are 0 or more");
        }
        Vector3? surfaceColor = WaterArguments.Vector(r.SurfaceColor, "surfaceColor");
        Vector3? sunDirection = WaterArguments.Vector(r.SunDirection, "sunDirection");
        Vector3? sunColor = WaterArguments.Vector(r.SunColor, "sunColor");
        string description = r.HasWater is bool on && given == "hasWater" ? on ? "Water on" : "Water off" : $"Water: {given.Replace(",", ", ")}";
        model.EditMap(description, document =>
        {
            Water w = document.Water;
            float elevation = r.Elevation ?? w.Elevation;
            // deep lies below the surface, and the abyss below deep: the one given wins
            float deep = r.ElevationDeep ?? w.ElevationDeep;
            float abyss = r.ElevationAbyss ?? w.ElevationAbyss;
            if (r.ElevationDeep is null)
            {
                deep = Math.Min(deep, elevation);
            }
            else
            {
                elevation = Math.Max(elevation, deep);
            }
            if (r.ElevationAbyss is null)
            {
                abyss = Math.Min(abyss, deep);
            }
            else
            {
                deep = Math.Max(deep, abyss);
                elevation = Math.Max(elevation, deep);
            }
            return document with
            {
                Water = w with
                {
                    HasWater = r.HasWater ?? w.HasWater,
                    Elevation = elevation,
                    ElevationDeep = deep,
                    ElevationAbyss = abyss,
                    SurfaceColor = surfaceColor ?? w.SurfaceColor,
                    ColorLerp = new Vector2(r.ColorLerpMin ?? w.ColorLerp.X, r.ColorLerpMax ?? w.ColorLerp.Y),
                    RefractionScale = r.RefractionScale ?? w.RefractionScale,
                    FresnelBias = r.FresnelBias ?? w.FresnelBias,
                    FresnelPower = r.FresnelPower ?? w.FresnelPower,
                    UnitReflection = r.UnitReflection ?? w.UnitReflection,
                    SkyReflection = r.SkyReflection ?? w.SkyReflection,
                    SunShininess = r.SunShininess ?? w.SunShininess,
                    SunStrength = r.SunStrength ?? w.SunStrength,
                    SunDirection = sunDirection ?? w.SunDirection,
                    SunColor = sunColor ?? w.SunColor,
                    SunReflection = r.SunReflection ?? w.SunReflection,
                    SunGlow = r.SunGlow ?? w.SunGlow,
                    Cubemap = r.Cubemap?.Trim() ?? w.Cubemap,
                    Ramp = r.Ramp?.Trim() ?? w.Ramp,
                },
            };
        }, $"water.set:{given}");
        return Task.CompletedTask;
    }
}

/// <summary>
/// Changes one of the four normal maps of the water's surface (user story W-1), one step of undo.
/// </summary>
/// <param name="Index">0 to 3.</param>
/// <param name="NormalRepeat">How often it repeats across the surface.</param>
/// <param name="MovementX">How fast it moves along x.</param>
[EditorCommand("water.setWaveTexture", "Change a wave texture", Story = "W-1", Needs = Needs.Map | Needs.NoStroke)]
public sealed record SetWaveTexture(int Index, string? Texture = null, float? NormalRepeat = null, float? MovementX = null, float? MovementZ = null) : IRequest;

public sealed class SetWaveTextureHandler(MainViewModel model) : IRequestHandler<SetWaveTexture>
{
    public Task Handle(SetWaveTexture r, CancellationToken cancellationToken)
    {
        int count = model.ShownDocument?.Water.WaveTextures.Length ?? throw new InvalidOperationException("No map is open");
        if (r.Index < 0 || r.Index >= count)
        {
            throw new ArgumentException($"The water has wave textures 0 to {count - 1}");
        }
        string given = WaterArguments.Given(("texture", r.Texture), ("normalRepeat", r.NormalRepeat), ("movementX", r.MovementX), ("movementZ", r.MovementZ));
        model.EditMap($"Wave texture {r.Index + 1}: {given.Replace(",", ", ")}", document =>
        {
            WaveTexture t = document.Water.WaveTextures[r.Index];
            WaveTexture changed = t with
            {
                Texture = r.Texture?.Trim() ?? t.Texture,
                NormalRepeat = r.NormalRepeat ?? t.NormalRepeat,
                NormalMovement = new Vector2(r.MovementX ?? t.NormalMovement.X, r.MovementZ ?? t.NormalMovement.Y),
            };
            return document with { Water = document.Water with { WaveTextures = document.Water.WaveTextures.SetItem(r.Index, changed) } };
        }, $"water.setWaveTexture:{r.Index}:{given}");
        return Task.CompletedTask;
    }
}

/// <summary>
/// Adds a wave generator (user story W-1), one step of undo: waves that roll onto a shore. At a
/// place in ogrids, on the water's surface; left out, the middle of the map.
/// </summary>
/// <returns>Its index.</returns>
[EditorCommand("water.addWaveGenerator", "Add a wave generator", Story = "W-1", Needs = Needs.Map | Needs.NoStroke)]
public sealed record AddWaveGenerator(float? X = null, float? Z = null) : IRequest<int>;

public sealed class AddWaveGeneratorHandler(MainViewModel model) : IRequestHandler<AddWaveGenerator, int>
{
    /// <summary>
    /// A new generator: the shore texture and ramp most maps of the vault use, with the timings of
    /// one of them (abhor.v0004).
    /// </summary>
    public static WaveGenerator Default(Vector3 position) => new WaveGenerator
    {
        Texture = "/env/common/decals/shoreline/wavetest.dds",
        Ramp = "/env/common/decals/shoreline/waveramptest.dds",
        Position = position,
        LifetimeFirst = 32,
        LifetimeSecond = 34,
        PeriodFirst = 3.2f,
        PeriodSecond = 3.4f,
        ScaleFirst = 0.5f,
        ScaleSecond = 1.2f,
        FrameCount = 1,
        FrameRateFirst = 1,
        FrameRateSecond = 1,
        StripCount = 1,
    };

    public Task<int> Handle(AddWaveGenerator r, CancellationToken cancellationToken)
    {
        MapDocument document = model.ShownDocument ?? throw new InvalidOperationException("No map is open");
        Vector3 position = new Vector3(r.X ?? document.Width / 2f, document.Water.Elevation, r.Z ?? document.Height / 2f);
        model.EditMap("Add a wave generator", d => d with { Water = d.Water with { WaveGenerators = d.Water.WaveGenerators.Add(Default(position)) } });
        int index = model.ShownDocument!.Water.WaveGenerators.Length - 1;
        // the one added is the one chosen, as when the Waves tool places one
        if (index < model.WaterSettings.Generators.Count)
        {
            model.WaterSettings.SelectedGenerator = model.WaterSettings.Generators[index];
        }
        model.Status = $"Added wave generator {index + 1} at {position.X:0.#}, {position.Z:0.#}";
        return Task.FromResult(index);
    }
}

/// <summary>
/// Removes a wave generator (user story W-1), one step of undo.
/// </summary>
/// <param name="Index">From 0.</param>
[EditorCommand("water.removeWaveGenerator", "Remove a wave generator", Story = "W-1", Needs = Needs.Map | Needs.NoStroke)]
public sealed record RemoveWaveGenerator(int Index) : IRequest;

public sealed class RemoveWaveGeneratorHandler(MainViewModel model) : IRequestHandler<RemoveWaveGenerator>
{
    public Task Handle(RemoveWaveGenerator r, CancellationToken cancellationToken)
    {
        int count = model.ShownDocument?.Water.WaveGenerators.Length ?? throw new InvalidOperationException("No map is open");
        if (r.Index < 0 || r.Index >= count)
        {
            throw new ArgumentException(count == 0 ? "The water has no wave generators" : $"The water has wave generators 0 to {count - 1}");
        }
        model.EditMap($"Remove wave generator {r.Index + 1}", d => d with { Water = d.Water with { WaveGenerators = d.Water.WaveGenerators.RemoveAt(r.Index) } });
        return Task.CompletedTask;
    }
}

/// <summary>
/// Changes a wave generator (user story W-1), one step of undo; the steps that change the same
/// fields of the same generator one after the other are one step.
/// </summary>
/// <param name="Index">From 0.</param>
/// <param name="X">Its place in ogrids; <c>y</c> is its height.</param>
/// <param name="Rotation">In radians.</param>
/// <param name="Velocity">x, y and z.</param>
[EditorCommand("water.setWaveGenerator", "Change a wave generator", Story = "W-1", Needs = Needs.Map | Needs.NoStroke)]
public sealed record SetWaveGenerator(
    int Index,
    string? Texture = null,
    string? Ramp = null,
    float? X = null,
    float? Y = null,
    float? Z = null,
    float? Rotation = null,
    float[]? Velocity = null,
    float? LifetimeFirst = null,
    float? LifetimeSecond = null,
    float? PeriodFirst = null,
    float? PeriodSecond = null,
    float? ScaleFirst = null,
    float? ScaleSecond = null,
    float? FrameCount = null,
    float? FrameRateFirst = null,
    float? FrameRateSecond = null,
    float? StripCount = null) : IRequest;

public sealed class SetWaveGeneratorHandler(MainViewModel model) : IRequestHandler<SetWaveGenerator>
{
    public Task Handle(SetWaveGenerator r, CancellationToken cancellationToken)
    {
        int count = model.ShownDocument?.Water.WaveGenerators.Length ?? throw new InvalidOperationException("No map is open");
        if (r.Index < 0 || r.Index >= count)
        {
            throw new ArgumentException(count == 0 ? "The water has no wave generators" : $"The water has wave generators 0 to {count - 1}");
        }
        Vector3? velocity = WaterArguments.Vector(r.Velocity, "velocity");
        string given = WaterArguments.Given(("texture", r.Texture), ("ramp", r.Ramp), ("x", r.X), ("y", r.Y), ("z", r.Z), ("rotation", r.Rotation), ("velocity", r.Velocity),
            ("lifetimeFirst", r.LifetimeFirst), ("lifetimeSecond", r.LifetimeSecond), ("periodFirst", r.PeriodFirst), ("periodSecond", r.PeriodSecond),
            ("scaleFirst", r.ScaleFirst), ("scaleSecond", r.ScaleSecond), ("frameCount", r.FrameCount), ("frameRateFirst", r.FrameRateFirst),
            ("frameRateSecond", r.FrameRateSecond), ("stripCount", r.StripCount));
        model.EditMap($"Wave generator {r.Index + 1}: {given.Replace(",", ", ")}", document =>
        {
            WaveGenerator g = document.Water.WaveGenerators[r.Index];
            WaveGenerator changed = g with
            {
                Texture = r.Texture?.Trim() ?? g.Texture,
                Ramp = r.Ramp?.Trim() ?? g.Ramp,
                Position = new Vector3(r.X ?? g.Position.X, r.Y ?? g.Position.Y, r.Z ?? g.Position.Z),
                Rotation = r.Rotation ?? g.Rotation,
                Velocity = velocity ?? g.Velocity,
                LifetimeFirst = r.LifetimeFirst ?? g.LifetimeFirst,
                LifetimeSecond = r.LifetimeSecond ?? g.LifetimeSecond,
                PeriodFirst = r.PeriodFirst ?? g.PeriodFirst,
                PeriodSecond = r.PeriodSecond ?? g.PeriodSecond,
                ScaleFirst = r.ScaleFirst ?? g.ScaleFirst,
                ScaleSecond = r.ScaleSecond ?? g.ScaleSecond,
                FrameCount = r.FrameCount ?? g.FrameCount,
                FrameRateFirst = r.FrameRateFirst ?? g.FrameRateFirst,
                FrameRateSecond = r.FrameRateSecond ?? g.FrameRateSecond,
                StripCount = r.StripCount ?? g.StripCount,
            };
            return document with { Water = document.Water with { WaveGenerators = document.Water.WaveGenerators.SetItem(r.Index, changed) } };
        }, $"water.setWaveGenerator:{r.Index}:{given}");
        return Task.CompletedTask;
    }
}
