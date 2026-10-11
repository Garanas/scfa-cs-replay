using System.Numerics;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Changes the map's skybox (user story L-1), the version 60 sky dome the game draws: its colours
/// and heights, where it stands, the sky objects' atlases and the cirrus. One step of undo; the steps
/// that change the same fields one after the other are one step. What the Lighting inspector's
/// Skybox fields do. The sky objects themselves are not edited one by one.
/// </summary>
/// <param name="HorizonColor">Red, green and blue of the sky at the horizon.</param>
/// <param name="ZenithColor">Red, green and blue of the sky overhead.</param>
/// <param name="MidColor">Red, green and blue (0 to 1) between them; black for none.</param>
/// <param name="HorizonHeight">The height on the dome where the horizon's colour starts to give way.</param>
/// <param name="ZenithHeight">The height on the dome where the zenith's colour is reached.</param>
/// <param name="Position">x, y and z of the dome's middle, in the map's units.</param>
/// <param name="Scale">The dome's radius.</param>
/// <param name="Albedo">The game path of the sky objects' atlas.</param>
/// <param name="Glow">The game path of their glow atlas.</param>
/// <param name="GlowMultiplier">How strongly the glow atlas glows.</param>
/// <param name="CirrusTexture">The game path of the cirrus texture (a layer per channel).</param>
/// <param name="CirrusColor">Red, green and blue of the cirrus.</param>
/// <param name="CirrusMultiplier">How strongly the cirrus covers the sky; 0 for none.</param>
[EditorCommand("lighting.setSkybox", "Change the skybox", Story = "L-1", Needs = Needs.Map | Needs.NoStroke)]
public sealed record SetSkybox(
    float[]? HorizonColor = null,
    float[]? ZenithColor = null,
    float[]? MidColor = null,
    float? HorizonHeight = null,
    float? ZenithHeight = null,
    float[]? Position = null,
    float? Scale = null,
    float? SubtractHeight = null,
    int? SubdivisionsAxis = null,
    int? SubdivisionsHeight = null,
    string? Albedo = null,
    string? Glow = null,
    float? GlowMultiplier = null,
    string? CirrusTexture = null,
    float[]? CirrusColor = null,
    float? CirrusMultiplier = null) : IRequest;

public sealed class SetSkyboxHandler(MainViewModel model) : IRequestHandler<SetSkybox>
{
    public Task Handle(SetSkybox r, CancellationToken cancellationToken)
    {
        string given = WaterArguments.Given(("horizonColor", r.HorizonColor), ("zenithColor", r.ZenithColor), ("midColor", r.MidColor),
            ("horizonHeight", r.HorizonHeight), ("zenithHeight", r.ZenithHeight), ("position", r.Position), ("scale", r.Scale),
            ("subtractHeight", r.SubtractHeight), ("subdivisionsAxis", r.SubdivisionsAxis), ("subdivisionsHeight", r.SubdivisionsHeight),
            ("albedo", r.Albedo), ("glow", r.Glow), ("glowMultiplier", r.GlowMultiplier),
            ("cirrusTexture", r.CirrusTexture), ("cirrusColor", r.CirrusColor), ("cirrusMultiplier", r.CirrusMultiplier));
        if (given.Length == 0)
        {
            model.InspectPart(LightingSettingsViewModel.SkyboxPart);
            model.Status = "The skybox's settings are in the Lighting mode's inspector";
            return Task.CompletedTask;
        }
        SkyboxCommands.Require(model);
        if (r.Scale is <= 0)
        {
            throw new ArgumentException("The dome's scale is more than 0");
        }
        if (r.SubdivisionsAxis is < 3 || r.SubdivisionsHeight is < 2)
        {
            throw new ArgumentException("The dome has at least 3 subdivisions around and 2 up");
        }
        Vector3? horizon = WaterArguments.Vector(r.HorizonColor, "horizonColor");
        Vector3? zenith = WaterArguments.Vector(r.ZenithColor, "zenithColor");
        Vector3? mid = WaterArguments.Vector(r.MidColor, "midColor");
        Vector3? position = WaterArguments.Vector(r.Position, "position");
        Vector3? cirrus = WaterArguments.Vector(r.CirrusColor, "cirrusColor");
        model.EditMap($"Skybox: {given.Replace(",", ", ")}", document => SkyboxCommands.With(document, s => s with
        {
            HorizonColor = horizon ?? s.HorizonColor,
            ZenithColor = zenith ?? s.ZenithColor,
            MidColor = mid is { } m ? SkyboxCommands.ToColor32(m, s.MidColor.Alpha) : s.MidColor,
            HorizonHeight = r.HorizonHeight ?? s.HorizonHeight,
            ZenithHeight = r.ZenithHeight ?? s.ZenithHeight,
            Position = position ?? s.Position,
            Scale = r.Scale ?? s.Scale,
            SubtractHeight = r.SubtractHeight ?? s.SubtractHeight,
            SubdivisionsAxis = r.SubdivisionsAxis ?? s.SubdivisionsAxis,
            SubdivisionsHeight = r.SubdivisionsHeight ?? s.SubdivisionsHeight,
            Albedo = r.Albedo?.Trim() ?? s.Albedo,
            Glow = r.Glow?.Trim() ?? s.Glow,
            DecalGlowMultiplier = r.GlowMultiplier ?? s.DecalGlowMultiplier,
            CirrusTexture = r.CirrusTexture?.Trim() ?? s.CirrusTexture,
            CirrusColor = cirrus ?? s.CirrusColor,
            CirrusMultiplier = r.CirrusMultiplier ?? s.CirrusMultiplier,
        }), $"lighting.setSkybox:{given}");
        return Task.CompletedTask;
    }
}

/// <summary>
/// Gives a map without a skybox one made from its lighting (user story L-1): a plain dome around
/// the map's middle, the horizon in the fog's colour warmed by the sun, a deeper blue overhead, no
/// sky objects and no cirrus; one step of undo. The skybox the sky cube map is made from without one.
/// </summary>
[EditorCommand("lighting.addSkybox", "Add a skybox", Story = "L-1", Needs = Needs.Map | Needs.NoStroke)]
public sealed record AddSkybox : IRequest;

public sealed class AddSkyboxHandler(MainViewModel model) : IRequestHandler<AddSkybox>
{
    public Task Handle(AddSkybox request, CancellationToken cancellationToken)
    {
        MapDocument document = model.ShownDocument ?? throw new InvalidOperationException("No map is open");
        if (document.Sky.Skybox is not null)
        {
            throw new InvalidOperationException("The map has a skybox already");
        }
        model.EditMap("Add a skybox", d => d with { Sky = d.Sky with { Skybox = Skyboxes.FromLighting(d) } });
        model.InspectPart(LightingSettingsViewModel.SkyboxPart);
        model.Status = "Added a skybox made from the lighting";
        return Task.CompletedTask;
    }
}

/// <summary>
/// Takes the map's skybox away (user story L-1), one step of undo; the sky cube map is then made
/// from the lighting.
/// </summary>
[EditorCommand("lighting.removeSkybox", "Remove the skybox", Story = "L-1", Needs = Needs.Map | Needs.NoStroke)]
public sealed record RemoveSkybox : IRequest;

public sealed class RemoveSkyboxHandler(MainViewModel model) : IRequestHandler<RemoveSkybox>
{
    public Task Handle(RemoveSkybox request, CancellationToken cancellationToken)
    {
        SkyboxCommands.Require(model);
        model.EditMap("Remove the skybox", d => d with { Sky = d.Sky with { Skybox = null } });
        model.Status = "Removed the skybox";
        return Task.CompletedTask;
    }
}

/// <summary>
/// Changes a cirrus layer of the skybox (user story L-1): how often its channel of the cirrus
/// texture repeats over the dome, its speed and the direction it drifts; one step of undo.
/// </summary>
/// <param name="Index">The layer, from 0 (the texture's red channel; green, blue and alpha follow).</param>
[EditorCommand("lighting.setCirrusLayer", "Change a cirrus layer", Story = "L-1", Needs = Needs.Map | Needs.NoStroke)]
public sealed record SetCirrusLayer(int Index, float? FrequencyX = null, float? FrequencyY = null, float? Speed = null, float? DirectionX = null, float? DirectionY = null) : IRequest;

public sealed class SetCirrusLayerHandler(MainViewModel model) : IRequestHandler<SetCirrusLayer>
{
    public Task Handle(SetCirrusLayer r, CancellationToken cancellationToken)
    {
        string given = WaterArguments.Given(("frequencyX", r.FrequencyX), ("frequencyY", r.FrequencyY), ("speed", r.Speed),
            ("directionX", r.DirectionX), ("directionY", r.DirectionY));
        if (given.Length == 0)
        {
            throw new ArgumentException("Give frequencyX, frequencyY, speed, directionX or directionY");
        }
        Skybox skybox = SkyboxCommands.Require(model);
        if (r.Index < 0 || r.Index >= skybox.CirrusLayers.Length)
        {
            throw new ArgumentException($"The skybox has {skybox.CirrusLayers.Length} cirrus layers, numbered from 0");
        }
        model.EditMap($"Cirrus layer {r.Index + 1}: {given.Replace(",", ", ")}", document => SkyboxCommands.With(document, s =>
        {
            CirrusLayer layer = s.CirrusLayers[r.Index];
            return s with
            {
                CirrusLayers = s.CirrusLayers.SetItem(r.Index, layer with
                {
                    Frequency = new Vector2(r.FrequencyX ?? layer.Frequency.X, r.FrequencyY ?? layer.Frequency.Y),
                    Speed = r.Speed ?? layer.Speed,
                    Direction = new Vector2(r.DirectionX ?? layer.Direction.X, r.DirectionY ?? layer.Direction.Y),
                }),
            };
        }), $"lighting.setCirrusLayer:{r.Index}:{given}");
        return Task.CompletedTask;
    }
}

/// <summary>
/// Adds a cirrus layer to the skybox (user story L-1), up to four (one per channel of the cirrus
/// texture); one step of undo.
/// </summary>
[EditorCommand("lighting.addCirrusLayer", "Add a cirrus layer", Story = "L-1", Needs = Needs.Map | Needs.NoStroke)]
public sealed record AddCirrusLayer : IRequest;

public sealed class AddCirrusLayerHandler(MainViewModel model) : IRequestHandler<AddCirrusLayer>
{
    public Task Handle(AddCirrusLayer request, CancellationToken cancellationToken)
    {
        Skybox skybox = SkyboxCommands.Require(model);
        if (skybox.CirrusLayers.Length >= SkyboxCommands.MaxCirrusLayers)
        {
            throw new InvalidOperationException($"The skybox has {SkyboxCommands.MaxCirrusLayers} cirrus layers, one per channel of the cirrus texture");
        }
        model.EditMap("Add a cirrus layer", document => SkyboxCommands.With(document, s => s with
        {
            CirrusLayers = s.CirrusLayers.Add(new CirrusLayer { Frequency = Vector2.One, Speed = 0, Direction = new Vector2(1, 0) }),
        }));
        model.Status = $"Added cirrus layer {skybox.CirrusLayers.Length + 1}";
        return Task.CompletedTask;
    }
}

/// <summary>
/// Takes a cirrus layer out of the skybox (user story L-1), one step of undo; the layers after it
/// move up a channel.
/// </summary>
/// <param name="Index">The layer, from 0; left out, the last.</param>
[EditorCommand("lighting.removeCirrusLayer", "Remove a cirrus layer", Story = "L-1", Needs = Needs.Map | Needs.NoStroke)]
public sealed record RemoveCirrusLayer(int? Index = null) : IRequest;

public sealed class RemoveCirrusLayerHandler(MainViewModel model) : IRequestHandler<RemoveCirrusLayer>
{
    public Task Handle(RemoveCirrusLayer request, CancellationToken cancellationToken)
    {
        Skybox skybox = SkyboxCommands.Require(model);
        int index = request.Index ?? skybox.CirrusLayers.Length - 1;
        if (index < 0 || index >= skybox.CirrusLayers.Length)
        {
            throw new ArgumentException(skybox.CirrusLayers.IsEmpty ? "The skybox has no cirrus layers" : $"The skybox has {skybox.CirrusLayers.Length} cirrus layers, numbered from 0");
        }
        model.EditMap($"Remove cirrus layer {index + 1}", document => SkyboxCommands.With(document, s => s with { CirrusLayers = s.CirrusLayers.RemoveAt(index) }));
        model.Status = $"Removed cirrus layer {index + 1}";
        return Task.CompletedTask;
    }
}

/// <summary>
/// What the skybox's commands share.
/// </summary>
internal static class SkyboxCommands
{
    /// <summary>
    /// The cirrus texture's channels, one per layer (<c>effects/sky.fx</c>, <c>CirrusPS</c>).
    /// </summary>
    public const int MaxCirrusLayers = 4;

    /// <exception cref="InvalidOperationException">No map is open, or it has no skybox.</exception>
    public static Skybox Require(MainViewModel model) =>
        (model.ShownDocument ?? throw new InvalidOperationException("No map is open")).Sky.Skybox
        ?? throw new InvalidOperationException("The map has no skybox: add one (lighting.addSkybox) or read a preset (lighting.importSkybox)");

    /// <summary>
    /// The document with its skybox changed; a document without one stays as it is.
    /// </summary>
    public static MapDocument With(MapDocument document, Func<Skybox, Skybox> change) =>
        document.Sky.Skybox is Skybox skybox ? document with { Sky = document.Sky with { Skybox = change(skybox) } } : document;

    /// <summary>
    /// A colour of 0 to 1 as the skybox keeps its middle colour, with the alpha it had (its use is unknown).
    /// </summary>
    public static Color32 ToColor32(Vector3 color, byte alpha)
    {
        static byte Byte(float value) => (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);
        return new Color32(alpha, Byte(color.X), Byte(color.Y), Byte(color.Z));
    }

    /// <summary>
    /// The skybox's middle colour as 0 to 1.
    /// </summary>
    public static Vector3 FromColor32(Color32 color) => new Vector3(color.Red, color.Green, color.Blue) / 255f;
}
