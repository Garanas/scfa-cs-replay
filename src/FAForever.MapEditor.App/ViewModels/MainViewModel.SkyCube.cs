using System.Text.Json;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Rendering;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The sky cube map's side of the core (user story L-1): its generator, its entry in the Procedural
/// mode, choosing it for the sky, the units and the water, and reading a skybox preset.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// The sky cube map made from the map: settings, preview, how it stands.
    /// </summary>
    public SkyCubeViewModel SkyCube { get; private set; } = null!;

    /// <summary>
    /// The generator of the sky cube map; the 3D view gives it its renderer (<see cref="SkyCubeGenerator.Faces"/>).
    /// </summary>
    public SkyCubeGenerator SkyCubeGenerator { get; private set; } = null!;

    private void InitializeSkyCube()
    {
        SkyCubeGenerator = new SkyCubeGenerator(() => Setup.Workspace) { Log = Logger("Editor") };
        Generation.Register(SkyCubeGenerator, "generate.skyCube");
        SkyCube = new SkyCubeViewModel(RunWithAsync, Generation, () => Map?.Document);
        DocumentChanged += (_, _) => SkyCube.Refresh();
        MapChanged += (_, _) => SkyCube.Refresh();
        Generation.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(GenerationViewModel.States) or nameof(GenerationViewModel.Rows))
            {
                SkyCube.Refresh();
            }
        };
        SkyCube.Refresh();
    }

    /// <summary>
    /// Changes how the sky cube map is generated, one step of undo (steps of the same fields one
    /// after the other are one); the cube map is then out of date.
    /// </summary>
    /// <exception cref="ArgumentException">A value is out of its range.</exception>
    public void SetSkyCube(int? resolution, float? eyeHeight, bool? terrain, bool? sunDisc)
    {
        if (resolution is < SkyCubeSettings.SmallestResolution or > SkyCubeSettings.LargestResolution)
        {
            throw new ArgumentException($"A face of the sky cube map is {SkyCubeSettings.SmallestResolution} to {SkyCubeSettings.LargestResolution} pixels, not {resolution}");
        }
        if (eyeHeight is < 0 or > 10000 || eyeHeight is float h && !float.IsFinite(h))
        {
            throw new ArgumentException($"The eye is 0 to 10000 ogrids above the map, not {eyeHeight}");
        }
        string given = string.Join(",", new[] { ("resolution", resolution is not null), ("eyeHeight", eyeHeight is not null), ("terrain", terrain is not null), ("sunDisc", sunDisc is not null) }
            .Where(field => field.Item2).Select(field => field.Item1));
        if (given.Length == 0)
        {
            return;
        }
        EditMap($"Sky cube map: {given.Replace(",", ", ")}", document =>
        {
            SkyCubeSettings settings = document.Procedural.SkyCube;
            settings = settings with
            {
                Resolution = resolution is int r ? SkyCubeSettings.ValidResolution(r) : settings.Resolution,
                EyeHeight = eyeHeight ?? settings.EyeHeight,
                IncludeTerrain = terrain ?? settings.IncludeTerrain,
                SunDisc = sunDisc ?? settings.SunDisc,
            };
            return document with { Procedural = document.Procedural with { SkyCube = settings } };
        }, $"generate.setSkyCube:{given}");
    }

    /// <summary>
    /// Makes the generated sky cube map the sky's, the units' (the <c>&lt;default&gt;</c> environment
    /// cube map) and the water's, as asked, one step of undo.
    /// </summary>
    /// <returns>What it was chosen for, e.g. <c>the sky, the units and the water</c>.</returns>
    /// <exception cref="InvalidOperationException">The map holds no generated cube map.</exception>
    public string UseSkyCube(bool sky, bool environment, bool water)
    {
        EditedMap map = RequireMap();
        if (!map.Document.Files.ContainsKey(SkyCubeMaps.FilePath))
        {
            throw new InvalidOperationException("Generate the sky cube map first: the map holds none");
        }
        string path = SkyCubeMaps.GamePath(map.Document);
        string[] targets = [.. new[] { (sky, "the sky"), (environment, "the units"), (water, "the water") }.Where(use => use.Item1).Select(use => use.Item2)];
        if (targets.Length == 0)
        {
            throw new ArgumentException("Choose the sky cube map for the sky, the units or the water");
        }
        string what = targets.Length == 1 ? targets[0] : $"{string.Join(", ", targets[..^1])} and {targets[^1]}";
        EditMap($"Use the sky cube map for {what}", document =>
        {
            Sky changed = SkyFields.With(document.Sky, sky ? path : null, environment ? path : null, null);
            return document with
            {
                Sky = changed,
                Water = water ? document.Water with { Cubemap = path } : document.Water,
            };
        });
        return what;
    }

    /// <summary>
    /// Puts a skybox read from a preset of the FAF map editor (JSON) into the map's sky, one step of
    /// undo; its textures in another map's folder are taken from this map's folder when it has them.
    /// </summary>
    /// <exception cref="IOException">The file cannot be read.</exception>
    /// <exception cref="JsonException">It is no skybox.</exception>
    public Skybox ImportSkybox(string file)
    {
        EditedMap map = RequireMap();
        string text = File.ReadAllText(file);
        MapDocument document = map.Document;
        Skybox skybox = Skyboxes.ReadJson(text, document.Folder.ToString(), path => document.Files.Keys.Any(key => string.Equals(key, path, StringComparison.OrdinalIgnoreCase)));
        EditMap($"Read the skybox {Path.GetFileName(file)}", d => d with { Sky = d.Sky with { Skybox = skybox } });
        return skybox;
    }
}
