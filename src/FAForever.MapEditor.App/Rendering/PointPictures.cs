using System.Numerics;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Generation;

namespace FAForever.MapEditor.App.Rendering;

/// <summary>
/// Things at places on the map drawn as a small picture over a dark ground: the nodes and links of
/// path networks, the props rules placed. The map's size gives the picture its proportions.
/// </summary>
public abstract record PointPicture(int MapWidth, int MapHeight) : PreviewPicture
{
    /// <summary>
    /// The ground under the points: near black, as a grid's empty cells are.
    /// </summary>
    protected const uint Ground = 0xFF181818u;

    public override (int Width, int Height)? GridSize => (MapWidth, MapHeight);

    public override (uint[] Pixels, int Width, int Height)? Render(int size)
    {
        (int width, int height) = Fit(MapWidth, MapHeight, size);
        uint[] pixels = new uint[width * height];
        Array.Fill(pixels, Ground);
        Draw(new Canvas(pixels, width, height, (float)width / Math.Max(1, MapWidth), (float)height / Math.Max(1, MapHeight)));
        return (pixels, width, height);
    }

    /// <summary>
    /// Draws the points onto the picture.
    /// </summary>
    protected abstract void Draw(Canvas canvas);

    /// <summary>
    /// The picture's pixels and how a place on the map in ogrids becomes a pixel.
    /// </summary>
    protected readonly record struct Canvas(uint[] Pixels, int Width, int Height, float ScaleX, float ScaleZ)
    {
        public void Dot(float x, float z, uint colour)
        {
            int px = (int)(x * ScaleX);
            int py = (int)(z * ScaleZ);
            if (px >= 0 && py >= 0 && px < Width && py < Height)
            {
                Pixels[py * Width + px] = colour;
            }
        }

        public void Line(Vector2 from, Vector2 to, uint colour)
        {
            float length = Vector2.Distance(new Vector2(from.X * ScaleX, from.Y * ScaleZ), new Vector2(to.X * ScaleX, to.Y * ScaleZ));
            int steps = Math.Max(1, (int)MathF.Ceiling(length));
            for (int i = 0; i <= steps; i++)
            {
                Vector2 at = Vector2.Lerp(from, to, (float)i / steps);
                Dot(at.X, at.Y, colour);
            }
        }
    }

    /// <summary>
    /// A grey of the given brightness, opaque.
    /// </summary>
    protected static uint Grey(uint value) => 0xFF000000u | value << 16 | value << 8 | value;
}

/// <summary>
/// The path networks: each layer's links and nodes, a grey of its own per layer (land the
/// brightest, air the darkest), so the Procedural list shows what was generated.
/// </summary>
public sealed record PathNetworkPicture(PathNetwork Network, int MapWidth, int MapHeight) : PointPicture(MapWidth, MapHeight)
{
    public override object Key => Network;

    protected override void Draw(Canvas canvas)
    {
        // the air's grid covers the map: drawn first, below the others
        foreach (PathLayerNetwork layer in Network.Layers.OrderBy(layer => layer.Layer == PathLayer.Air ? 0 : 1))
        {
            uint links = Grey(BrightnessOf(layer.Layer) * 2 / 3);
            uint nodes = Grey(BrightnessOf(layer.Layer));
            foreach ((int a, int b) in layer.Links)
            {
                canvas.Line(layer.Nodes[a].Place, layer.Nodes[b].Place, links);
            }
            foreach (GeneratedPathNode node in layer.Nodes)
            {
                canvas.Dot(node.X, node.Z, nodes);
            }
        }
    }

    private static uint BrightnessOf(PathLayer layer) => layer switch
    {
        PathLayer.Land => 250,
        PathLayer.Amphibious => 210,
        PathLayer.Water => 170,
        _ => 120,
    };
}

/// <summary>
/// The props the prop rules placed, a light dot each.
/// </summary>
/// <param name="Version">The object the picture follows: one per version of the map's props.</param>
public sealed record RulePropsPicture(object Version, IReadOnlyList<Vector2> Places, int MapWidth, int MapHeight) : PointPicture(MapWidth, MapHeight)
{
    public override object Key => Version;

    protected override void Draw(Canvas canvas)
    {
        uint colour = Grey(230);
        foreach (Vector2 place in Places)
        {
            canvas.Dot(place.X, place.Y, colour);
        }
    }
}
