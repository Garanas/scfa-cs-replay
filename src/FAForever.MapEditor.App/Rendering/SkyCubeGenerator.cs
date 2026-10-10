using System.Diagnostics;
using System.Numerics;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;
using FAForever.MapEditor.Project.Workspaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FAForever.MapEditor.App.Rendering;

/// <summary>
/// What draws the map below the horizon into the faces of a sky cube map on the graphics card: the
/// 3D view's renderer (<c>Controls/TerrainViewGpu.SkyCube.cs</c>), the terrain with its textures,
/// decals, props and units and the water reflecting the sky, as the 3D view draws them.
/// </summary>
public interface ISkyCubeFaces
{
    /// <summary>
    /// Whether it can draw now: the 3D view is shown and its renderer runs.
    /// </summary>
    bool CanRender { get; }

    /// <summary>
    /// The six faces seen from <paramref name="eye"/>, with <paramref name="sky"/> (a strip of six
    /// faces, <see cref="CubeMaps"/>) behind the map, as a strip of the same size.
    /// </summary>
    Task<DecodedTexture> RenderAsync(MapDocument document, Vector3 eye, DecodedTexture sky, CancellationToken cancel);
}

/// <summary>
/// The generated sky cube map: its DDS file, a small strip of its faces for the panels, how large a
/// face is and what drew the map below the horizon.
/// </summary>
/// <param name="DrawnWith">E.g. <c>the 3D renderer</c>, <c>the processor</c>, <c>the sky alone</c>.</param>
public sealed record SkyCubeResult(byte[] Dds, DecodedTexture Preview, int Size, string DrawnWith, string Sky);

/// <summary>
/// The sky cube map made from the map (the Procedural mode, user story L-1;
/// <see cref="SkyCubeMaps"/>): the map's sky drawn on the processor, the map below the horizon drawn
/// by the 3D view's renderer when it can (<see cref="Faces"/>), else on the processor, written as a
/// DDS cube map among the map's own files (<see cref="SkyCubeMaps.FilePath"/>), one step of undo.
/// </summary>
/// <param name="workspace">The workspace whose assets hold the textures; null for none.</param>
public sealed class SkyCubeGenerator(Func<Workspace?>? workspace = null) : IDocumentGenerator
{
    public const string GeneratorId = "skyCube";

    // the thread the editor made it on, its UI thread
    private readonly int _uiThread = Environment.CurrentManagedThreadId;

    /// <summary>
    /// The size of a face of the panels' small strip.
    /// </summary>
    public const int PreviewFace = 128;

    public static GeneratedInput Settings { get; } = new GeneratedInput("sky cube settings", document => document.Procedural.SkyCube);

    /// <summary>
    /// The sky dome (the lighting makes one for a map without it).
    /// </summary>
    public static GeneratedInput Skybox { get; } = new GeneratedInput("sky", document => document.Sky.Skybox);

    /// <summary>
    /// The water as it looks, without the cube map it reflects: choosing the generated cube map for
    /// the water leaves the cube map current.
    /// </summary>
    public static GeneratedInput WaterLook { get; } = new GeneratedInput("water", document => document.Water with { Cubemap = "" });

    private static GeneratedInput WhenTerrain(GeneratedInput input) =>
        new GeneratedInput(input.Name, document => document.Procedural.SkyCube.IncludeTerrain ? input.Read(document) : null);

    public string Id => GeneratorId;

    public string Title => "Sky cube map";

    public string Story => "L-1";

    public IReadOnlyList<GeneratedInput> Inputs { get; } =
    [
        Settings, Skybox, GeneratedInput.Lighting, WhenTerrain(WaterLook), WhenTerrain(GeneratedInput.Heightmap),
        WhenTerrain(GeneratedInput.StratumMasks), WhenTerrain(GeneratedInput.TerrainLayers), WhenTerrain(GeneratedInput.Decals),
        WhenTerrain(GeneratedInput.Props),
    ];

    /// <summary>
    /// The 3D view's renderer, which draws the map below the horizon; null (or unable to draw) draws
    /// it on the processor.
    /// </summary>
    public ISkyCubeFaces? Faces { get; set; }

    /// <summary>
    /// How long the 3D view may take before the processor draws the map instead.
    /// </summary>
    public TimeSpan FacesTimeout { get; set; } = TimeSpan.FromSeconds(60);

    public ILogger Log { get; set; } = NullLogger.Instance;

    public object Generate(MapDocument document) => Generate(document, CancellationToken.None);

    public object Generate(MapDocument document, CancellationToken cancel)
    {
        SkyCubeSettings settings = document.Procedural.SkyCube;
        Workspace? found = workspace?.Invoke();
        DecodedTexture? Read(string path) => GameTextures.Find(path, document, found)?.Read();
        long start = Stopwatch.GetTimestamp();
        DecodedTexture sky;
        using (Performance.Measure("model.sky-cube.sky"))
        {
            sky = SkyCubeMaps.RenderSky(document, settings, Read, cancel);
        }
        DecodedTexture strip = sky;
        string drawnWith = "the sky alone";
        if (settings.IncludeTerrain)
        {
            strip = DrawMap(document, settings, sky, Read, cancel, out drawnWith);
        }
        byte[] dds;
        using (Performance.Measure("model.sky-cube.dds"))
        {
            dds = SkyCubeMaps.Dds(strip);
        }
        Log.LogInformation("Generated the sky cube map of {Size} pixels a face with {DrawnWith} in {Milliseconds} ms", strip.Height, drawnWith, Math.Round(Stopwatch.GetElapsedTime(start).TotalMilliseconds));
        return new SkyCubeResult(dds, Small(strip, PreviewFace), strip.Height, drawnWith, document.Sky.Skybox is null ? "made from the lighting" : "the map's skybox");
    }

    private DecodedTexture DrawMap(MapDocument document, SkyCubeSettings settings, DecodedTexture sky, Func<string, DecodedTexture?> read, CancellationToken cancel, out string drawnWith)
    {
        // the 3D view draws on the UI thread: never wait for it there (generating everything out of date runs there)
        if (Faces is { CanRender: true } faces && Environment.CurrentManagedThreadId != _uiThread)
        {
            try
            {
                using Performance.Span measure = Performance.Measure("model.sky-cube.gpu");
                Task<DecodedTexture> task = faces.RenderAsync(document, SkyCubeMaps.Eye(document, settings), sky, cancel);
                if (task.Wait(FacesTimeout, cancel))
                {
                    drawnWith = "the 3D renderer";
                    return task.Result;
                }
                Log.LogWarning("The 3D view did not draw the sky cube map within {Seconds} s; the processor draws it", FacesTimeout.TotalSeconds);
            }
            catch (AggregateException exception) when (exception.InnerException is not OperationCanceledException)
            {
                Log.LogWarning(exception.InnerException, "The 3D view could not draw the sky cube map; the processor draws it: {Reason}", exception.InnerException?.Message);
            }
        }
        using (Performance.Measure("model.sky-cube.terrain"))
        {
            drawnWith = "the processor";
            return SkyCubeMaps.DrawTerrain(document, settings, sky, read, cancel);
        }
    }

    public MapDocument Apply(MapDocument document, object value) => document with
    {
        Files = document.Files.SetItem(SkyCubeMaps.FilePath, System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsImmutableArray(((SkyCubeResult)value).Dds)),
    };

    /// <summary>
    /// A strip of faces made smaller: each face <paramref name="face"/> pixels, each pixel the
    /// nearest of the larger one.
    /// </summary>
    internal static DecodedTexture Small(DecodedTexture strip, int face)
    {
        int size = strip.Height;
        if (size <= face)
        {
            return strip;
        }
        byte[] rgba = new byte[6 * face * face * 4];
        for (int y = 0; y < face; y++)
        {
            int row = (int)((y + 0.5) * size / face);
            for (int x = 0; x < 6 * face; x++)
            {
                int column = x / face * size + (int)((x % face + 0.5) * size / face);
                Array.Copy(strip.Rgba, (row * strip.Width + column) * 4, rgba, (y * 6 * face + x) * 4, 4);
            }
        }
        return new DecodedTexture(6 * face, face, rgba);
    }
}
