using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Changes the sky (user story L-1), one step of undo; the steps that change the same fields one
/// after the other are one step. The sky's cube map is what the 3D view shows around the map and
/// the water reflects there; the cube map units reflect is the map's first environment cube map
/// (<c>&lt;default&gt;</c>, added when the map has none); the background is the picture far behind.
/// What the Lighting inspector's Sky fields do.
/// </summary>
/// <param name="Cubemap">The game path of the sky's cube map, e.g. <c>/textures/environment/skycube_evergreen01a.dds</c>.</param>
/// <param name="EnvironmentCubemap">The game path of the cube map units reflect, e.g. <c>/textures/environment/envcube_evergreen01a.dds</c>.</param>
/// <param name="Background">The game path of the background, e.g. <c>/textures/environment/defaultbackground.dds</c>.</param>
[EditorCommand("lighting.setSky", "Change the sky", Story = "L-1", Needs = Needs.Map | Needs.NoStroke)]
public sealed record SetSky(string? Cubemap = null, string? EnvironmentCubemap = null, string? Background = null) : IRequest;

public sealed class SetSkyHandler(MainViewModel model) : IRequestHandler<SetSky>
{
    public Task Handle(SetSky r, CancellationToken cancellationToken)
    {
        string given = WaterArguments.Given(("cubemap", r.Cubemap), ("environmentCubemap", r.EnvironmentCubemap), ("background", r.Background));
        if (given.Length == 0)
        {
            model.SelectMode("lighting");
            model.Status = "The sky's cube maps are in the Lighting mode's inspector";
            return Task.CompletedTask;
        }
        model.EditMap($"Sky: {given.Replace(",", ", ")}", document => document with { Sky = SkyFields.With(document.Sky, r.Cubemap, r.EnvironmentCubemap, r.Background) }, $"lighting.setSky:{given}");
        return Task.CompletedTask;
    }
}

/// <summary>
/// Chooses a cube map from the workspace's assets in the picker (user story L-1): the sky's, the
/// one units reflect, or the one the water reflects; one step of undo.
/// </summary>
/// <param name="Target"><c>sky</c>, <c>environment</c> or <c>water</c>.</param>
/// <param name="Key">The cube map's game path; left out, the picker asks for it.</param>
[EditorCommand("lighting.chooseCubemap", "Choose a cube map...", Story = "L-1", Needs = Needs.Map | Needs.NoStroke)]
public sealed record ChooseCubemap(string Target = "sky", string? Key = null) : IRequest<string?>;

public sealed class ChooseCubemapHandler(MainViewModel model, AssetsViewModel assets, IEditorDialogs dialogs) : IRequestHandler<ChooseCubemap, string?>
{
    public async Task<string?> Handle(ChooseCubemap request, CancellationToken cancellationToken)
    {
        MapDocument document = model.ShownDocument ?? throw new InvalidOperationException("No map is open");
        string target = request.Target.Trim().ToLowerInvariant();
        (string title, string current) = target switch
        {
            "sky" => ("Choose the sky's cube map", document.Sky.Cubemap),
            "environment" => ("Choose the cube map units reflect", SkyFields.EnvironmentOf(document.Sky)),
            "water" => ("Choose the cube map the water reflects", document.Water.Cubemap),
            _ => throw new ArgumentException($"A cube map is the sky's, the environment's or the water's, not '{request.Target}'"),
        };
        string? key = request.Key ?? await dialogs.PickAssetAsync(AssetKind.Cubemap, title, assets.Catalog.Find(current)?.Key);
        if (key is null)
        {
            model.Status = "Chose nothing";
            return null;
        }
        AssetEntry entry = assets.Catalog.Find(key) is { Kind: AssetKind.Cubemap } found
            ? found
            : throw new ArgumentException($"The workspace has no cube map '{key}'");
        string path = "/" + entry.Key;
        model.EditMap($"{title[7..]}: {entry.Name}", d => target switch
        {
            "sky" => d with { Sky = SkyFields.With(d.Sky, path, null, null) },
            "environment" => d with { Sky = SkyFields.With(d.Sky, null, path, null) },
            _ => d with { Water = d.Water with { Cubemap = path } },
        });
        model.Status = $"{char.ToUpperInvariant(title[7])}{title[8..]} is {entry.Name}";
        return entry.Key;
    }
}

/// <summary>
/// The sky's fields as the commands change them.
/// </summary>
internal static class SkyFields
{
    /// <summary>
    /// The name of the environment cube map units reflect unless a mesh names another.
    /// </summary>
    public const string DefaultEnvironment = "<default>";

    /// <summary>
    /// The texture of the cube map units reflect: the <c>&lt;default&gt;</c> one, else the first; empty for none.
    /// </summary>
    public static string EnvironmentOf(Sky sky) =>
        (sky.EnvironmentCubemaps.FirstOrDefault(cubemap => cubemap.Name == DefaultEnvironment) ?? sky.EnvironmentCubemaps.FirstOrDefault())?.Texture ?? "";

    /// <summary>
    /// The sky with the fields given changed, the others kept.
    /// </summary>
    public static Sky With(Sky sky, string? cubemap, string? environment, string? background)
    {
        var result = sky with
        {
            Cubemap = cubemap?.Trim() ?? sky.Cubemap,
            Background = background?.Trim() ?? sky.Background,
        };
        if (environment?.Trim() is string texture)
        {
            int index = sky.EnvironmentCubemaps.IndexOf(sky.EnvironmentCubemaps.FirstOrDefault(cubemap => cubemap.Name == DefaultEnvironment)!);
            index = index < 0 && sky.EnvironmentCubemaps.Length > 0 ? 0 : index;
            result = result with
            {
                EnvironmentCubemaps = index < 0
                    ? sky.EnvironmentCubemaps.Add(new Cubemap { Name = DefaultEnvironment, Texture = texture })
                    : sky.EnvironmentCubemaps.SetItem(index, sky.EnvironmentCubemaps[index] with { Texture = texture }),
            };
        }
        return result;
    }
}
