using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Rendering;

/// <summary>
/// A grid of the document that the top view lays over the terrain (<see cref="TerrainPicture"/>):
/// a mask in the overlay's orange, as strong as the mask is full, or a grid of codes with a colour
/// per code (the terrain's types).
/// </summary>
/// <param name="Key">Names what is shown: another key colours the whole picture again.</param>
/// <param name="Select">The grid in a version of the document; null when it has none.</param>
/// <param name="Palette">A colour per code (<c>0xAARRGGBB</c>, a transparent one shows nothing);
/// null for a mask.</param>
public sealed record GridOverlay(string Key, Func<MapDocument, TiledBuffer<byte>?> Select, IReadOnlyList<uint>? Palette = null)
{
    /// <summary>
    /// The grid a brush paints, as a mask.
    /// </summary>
    public static GridOverlay Mask(PaintTarget target) => new GridOverlay(target.ToString(), document =>
    {
        try
        {
            return target.Read(document);
        }
        catch (ArgumentException)
        {
            // the mask is gone (renamed, removed, undone): nothing to show
            return null;
        }
    });

    /// <summary>
    /// The terrain's types, each in its colour (<see cref="TerrainTypeColours"/>).
    /// </summary>
    public static GridOverlay TerrainTypes { get; } = new GridOverlay("terrain types", document => document.Terrain.Types, TerrainTypeColours.Palette);
}

/// <summary>
/// A colour per terrain type of the game (<see cref="FileFormats.Map.TerrainTypes"/>), for the
/// top view and the legend of the terrain types tool (user story T-6). The game gives its types
/// no colours: these follow what a type is (dirt brown, vegetation green, rock grey, water blue,
/// lava red), the types of a kind in steps from dark to light.
/// </summary>
public static class TerrainTypeColours
{
    /// <summary>
    /// The colour of every code, <c>0xAARRGGBB</c>; a code the game does not know is magenta.
    /// </summary>
    public static IReadOnlyList<uint> Palette { get; } = MakePalette();

    /// <summary>
    /// The colour of a type, <c>0xAARRGGBB</c>.
    /// </summary>
    public static uint Of(int typeCode) => Palette[typeCode & 0xff];

    private static uint[] MakePalette()
    {
        uint[] palette = new uint[256];
        Array.Fill(palette, Argb(255, 0, 255));
        foreach (IGrouping<string, TerrainType> kind in TerrainTypes.All.GroupBy(Kind))
        {
            TerrainType[] types = [.. kind];
            (int r, int g, int b) = Base(kind.Key);
            for (int i = 0; i < types.Length; i++)
            {
                // from a darker to a lighter shade of the kind's colour
                float shade = types.Length == 1 ? 1 : 0.7f + 0.55f * i / (types.Length - 1);
                palette[types[i].TypeCode] = Argb(Scale(r, shade), Scale(g, shade), Scale(b, shade));
            }
        }
        return palette;
    }

    /// <summary>
    /// What a type is, from its name without the number: <c>Dirt</c>, <c>Rocky</c>, ...
    /// </summary>
    private static string Kind(TerrainType type) => type.Name switch
    {
        "Concrete01" => "Concrete",
        "Rocky14" => "Crystal",
        "Rocky15" => "Cyber",
        _ => type.Name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9'),
    };

    private static (int R, int G, int B) Base(string kind) => kind switch
    {
        "Default" => (170, 160, 140),
        "Dirt" => (150, 105, 65),
        "Sand" => (220, 195, 130),
        "Vegetation" => (75, 150, 60),
        "Rocky" => (125, 125, 125),
        "Concrete" => (190, 190, 180),
        "Crystal" => (80, 190, 230),
        "Cyber" => (170, 90, 210),
        "TarmacUEF" => (70, 80, 100),
        "TarmacAeon" => (80, 100, 70),
        "TarmacCybran" => (100, 65, 65),
        "Snowy" => (200, 215, 235),
        "Water" => (45, 105, 190),
        "Lava" => (235, 75, 25),
        _ => (255, 0, 255),
    };

    private static int Scale(int channel, float shade) => Math.Clamp((int)MathF.Round(channel * shade), 0, 255);

    private static uint Argb(int r, int g, int b) => 0xFF000000u | (uint)r << 16 | (uint)g << 8 | (uint)b;
}
