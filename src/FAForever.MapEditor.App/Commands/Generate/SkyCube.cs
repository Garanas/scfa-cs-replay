using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Rendering;
using FAForever.MapEditor.App.ViewModels;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Generates the sky cube map from the map (user story L-1): six faces seen from above the map's
/// middle, the map's sky above the horizon (one made from the lighting when it has no skybox) and
/// the map with its water below, drawn by the 3D view's renderer when it is shown (else on the
/// processor), on a thread of the pool; written into the project's <c>env/</c> as a DDS cube map,
/// one step of undo. Its settings are <c>generate.setSkyCube</c>'s.
/// </summary>
/// <param name="Use">Also make it the sky's, the units' and the water's cube map (<c>generate.useSkyCube</c>).</param>
/// <returns>The cube map as <c>state.skyCube</c> describes it.</returns>
[EditorCommand("generate.skyCube", "Sky cube map", Story = "L-1", Needs = Needs.Map)]
public sealed record GenerateSkyCube(bool? Use = null) : IRequest<JsonObject>;

public sealed class GenerateSkyCubeHandler(MainViewModel model) : IRequestHandler<GenerateSkyCube, JsonObject>
{
    public async Task<JsonObject> Handle(GenerateSkyCube request, CancellationToken cancellationToken)
    {
        Project.Generation.GeneratedResult result = await model.GenerateAsync(SkyCubeGenerator.GeneratorId);
        SkyCubeResult made = (SkyCubeResult)result.Value;
        string status = $"Generated the sky cube map: {made.Size} pixels a face, the map drawn with {made.DrawnWith}";
        if (request.Use == true)
        {
            status += $"; used for {model.UseSkyCube(true, true, true)}";
        }
        model.Status = status;
        model.SkyCube.Refresh();
        return model.SkyCube.Describe();
    }
}

/// <summary>
/// Changes how the sky cube map is generated (user story L-1), kept with the map, one step of undo;
/// what is left out stays. The cube map is then out of date.
/// </summary>
/// <param name="Resolution">The size of a face in pixels: 512, 1024 or 2048 (another number becomes the nearest).</param>
/// <param name="EyeHeight">How high above the terrain or water at the map's middle the faces are seen from, in ogrids.</param>
/// <param name="Terrain">Whether the map and its water show below the horizon.</param>
/// <param name="SunDisc">Whether the sun shows as a disc.</param>
[EditorCommand("generate.setSkyCube", "Sky cube map settings", Story = "L-1", Needs = Needs.Map | Needs.NoStroke)]
public sealed record SetSkyCube(int? Resolution = null, float? EyeHeight = null, bool? Terrain = null, bool? SunDisc = null) : IRequest;

public sealed class SetSkyCubeHandler(MainViewModel model) : IRequestHandler<SetSkyCube>
{
    public Task Handle(SetSkyCube request, CancellationToken cancellationToken)
    {
        if (request is { Resolution: null, EyeHeight: null, Terrain: null, SunDisc: null })
        {
            model.Status = "The sky cube map's settings are in the Procedural mode's inspector";
            return Task.CompletedTask;
        }
        model.SetSkyCube(request.Resolution, request.EyeHeight, request.Terrain, request.SunDisc);
        model.Status = "Changed how the sky cube map is generated: Generate makes it again";
        return Task.CompletedTask;
    }
}

/// <summary>
/// Makes the generated sky cube map the sky's, the units' (the <c>&lt;default&gt;</c> environment
/// cube map) and the water's cube map (user story L-1), one step of undo: the sky around the map and
/// the reflections then show this map's own colours. With none of the three given, all three.
/// </summary>
[EditorCommand("generate.useSkyCube", "Use the sky cube map", Story = "L-1", Needs = Needs.Map | Needs.NoStroke)]
public sealed record UseSkyCube(bool? Sky = null, bool? Environment = null, bool? Water = null) : IRequest<string>;

public sealed class UseSkyCubeHandler(MainViewModel model) : IRequestHandler<UseSkyCube, string>
{
    public Task<string> Handle(UseSkyCube request, CancellationToken cancellationToken)
    {
        bool all = request is { Sky: null, Environment: null, Water: null };
        string what = model.UseSkyCube(all || request.Sky == true, all || request.Environment == true, all || request.Water == true);
        model.Status = $"The sky cube map is now the cube map of {what}";
        return Task.FromResult(what);
    }
}
