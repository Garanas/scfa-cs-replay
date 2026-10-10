using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.Rendering;

/// <summary>
/// The map's preview image (user stories S-4, M-8), the one the map ships with in its
/// <c>.scmap</c> (<see cref="Terrain.Preview"/>, 256 by 256 pixels): the terrain from above drawn
/// with the strata's textures, blended as the map's terrain shader blends them and lit by its sun
/// (<see cref="TexturedPreview"/>), the textures found as the 3D view finds them (the map's own
/// files, else the workspace's assets). Without a workspace it is the top view's picture
/// (<see cref="TerrainPicture"/>: shaded, coloured by height, blue under water) made smaller.
/// </summary>
/// <param name="workspace">The workspace whose assets hold the textures; null for none.</param>
public sealed class MapPreview(Func<Workspace?>? workspace = null) : IDocumentGenerator
{
    /// <summary>
    /// The map's terrain shader, which decides how the layers blend.
    /// </summary>
    private static readonly GeneratedInput TerrainShader = new GeneratedInput("terrain shader", document => document.Terrain.Shader);

    public const string GeneratorId = "preview";

    /// <summary>
    /// The size of the game's preview, square.
    /// </summary>
    public const int Size = 256;

    public string Id => GeneratorId;

    public string Title => "Preview";

    public string Story => "S-4";

    public IReadOnlyList<GeneratedInput> Inputs { get; } =
        [GeneratedInput.Heightmap, GeneratedInput.WaterLevel, GeneratedInput.Water, GeneratedInput.StratumMasks, GeneratedInput.TerrainLayers, GeneratedInput.Lighting, TerrainShader];

    public object Generate(MapDocument document)
    {
        if (workspace?.Invoke() is Workspace found)
        {
            return TexturedPreview.Render(document, path => GameTextures.Find(path, document, found)?.Read(), Size);
        }
        TerrainPicture picture = new TerrainPicture();
        picture.Update(document, 0);
        return Reduce(picture.Pixels, picture.Width, picture.Height, Size, Size);
    }

    public MapDocument Apply(MapDocument document, object value) =>
        document with { Terrain = document.Terrain with { Preview = (TiledBuffer<Color32>)value } };

    /// <summary>
    /// A picture of opaque BGRA pixels (<see cref="TerrainPicture.Pixels"/>) made smaller or larger:
    /// each pixel the average of the pixels it covers, or the nearest one when it covers less than one.
    /// </summary>
    public static TiledBuffer<Color32> Reduce(uint[] pixels, int width, int height, int toWidth, int toHeight)
    {
        Color32[] reduced = new Color32[toWidth * toHeight];
        Parallel.For(0, toHeight, row =>
        {
            int y0 = (int)((long)row * height / toHeight);
            int y1 = Math.Max(y0 + 1, (int)((long)(row + 1) * height / toHeight));
            for (int column = 0; column < toWidth; column++)
            {
                int x0 = (int)((long)column * width / toWidth);
                int x1 = Math.Max(x0 + 1, (int)((long)(column + 1) * width / toWidth));
                long r = 0, g = 0, b = 0;
                for (int y = y0; y < y1; y++)
                {
                    for (int x = x0; x < x1; x++)
                    {
                        uint pixel = pixels[y * width + x];
                        r += (pixel >> 16) & 0xFF;
                        g += (pixel >> 8) & 0xFF;
                        b += pixel & 0xFF;
                    }
                }
                long count = (long)(y1 - y0) * (x1 - x0);
                reduced[row * toWidth + column] = new Color32(255, (byte)(r / count), (byte)(g / count), (byte)(b / count));
            }
        });
        return TiledBuffer<Color32>.FromArray(toWidth, toHeight, reduced);
    }
}
