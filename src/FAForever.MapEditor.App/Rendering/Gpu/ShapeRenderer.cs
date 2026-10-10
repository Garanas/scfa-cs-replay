using System.Numerics;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.Tools;
using Silk.NET.OpenGL;

namespace FAForever.MapEditor.App.Rendering.Gpu;

/// <summary>
/// Draws what a tool shows at a fixed size on screen (<see cref="IMapTool.Shapes"/>: the handles of
/// the selection, the sun's arrow, the marks of the area tool and the ruler) over everything else
/// in the 3D view: the shapes are in the view's pixels, made triangles on the processor every frame
/// (a few hundred), drawn without the depth test. Lines with a depth (<see cref="ToolMapLine.Depth"/>:
/// the links between path nodes) are drawn first, with the depth test, so hills and models in front
/// of them hide them.
/// </summary>
internal sealed unsafe class ShapeRenderer : IDisposable
{
    // x, y in pixels, the depth (normalized device coordinates); r, g, b, a premultiplied
    private const int Stride = 7 * sizeof(float);

    private readonly GL _gl;
    private readonly TerrainRenderer.Program _program;
    private readonly uint _vao;
    private readonly uint _buffer;
    private readonly List<float> _vertices = [];
    private readonly List<float> _deep = [];
    private List<float> _into = null!;
    private (float From, float To) _depth;

    public ShapeRenderer(GL gl, bool es)
    {
        _gl = gl;
        _program = new TerrainRenderer.Program(gl, Vertex(es), Fragment(es));
        _vao = gl.GenVertexArray();
        _buffer = gl.GenBuffer();
        gl.BindVertexArray(_vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _buffer);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, Stride, null);
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, Stride, (void*)(3 * sizeof(float)));
        gl.BindVertexArray(0);
    }

    /// <summary>
    /// The triangles of the last frame, for automation.
    /// </summary>
    public int Triangles { get; private set; }

    /// <param name="size">The view's size in the pixels the shapes are in.</param>
    /// <param name="ink">The colour of an ink, from the theme, with its opacity.</param>
    public void Draw(IReadOnlyList<ToolShape> shapes, Vector2 size, Func<ToolInk, Vector4> ink)
    {
        Triangles = 0;
        if (shapes.Count == 0)
        {
            return;
        }
        using Performance.Span measure = Performance.Measure("3d.draw-shapes");
        _vertices.Clear();
        _deep.Clear();
        foreach (ToolShape shape in shapes)
        {
            _into = shape is ToolMapLine { Depth: not null } ? _deep : _vertices;
            _depth = shape is ToolMapLine { Depth: (float, float) depth } ? depth : (0, 0);
            switch (shape)
            {
                case ToolLine { Points.Length: >= 2 } line:
                    Vector4 color = ink(line.Ink);
                    int count = line.Closed ? line.Points.Length : line.Points.Length - 1;
                    for (int i = 0; i < count; i++)
                    {
                        Segment(line.Points[i], line.Points[(i + 1) % line.Points.Length], line.Width, color);
                    }
                    break;
                case ToolSquare square:
                    Vector2 c = square.Centre;
                    float h = square.Half;
                    Quad(c + new Vector2(-h, -h), c + new Vector2(h, -h), c + new Vector2(h, h), c + new Vector2(-h, h), ink(square.Fill));
                    Vector2[] corners = [c + new Vector2(-h, -h), c + new Vector2(h, -h), c + new Vector2(h, h), c + new Vector2(-h, h)];
                    Vector4 stroke = ink(square.Stroke);
                    for (int i = 0; i < 4; i++)
                    {
                        Segment(corners[i], corners[(i + 1) % 4], 1.5f, stroke);
                    }
                    break;
                case ToolMapLine mapLine:
                    Segment(mapLine.From, mapLine.To, mapLine.Width, mapLine.Color, deep: mapLine.Depth is not null);
                    break;
                case ToolTriangle triangle:
                    Vector4 fill = ink(triangle.Ink);
                    Add(triangle.A, fill);
                    Add(triangle.B, fill);
                    Add(triangle.C, fill);
                    break;
            }
        }
        Triangles = (_vertices.Count + _deep.Count) / 21;
        if (Triangles == 0)
        {
            return;
        }
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
        _program.Use();
        _program.Set("uViewSize", size.X, size.Y);
        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _buffer);
        // the lines in the scene: tested against what was drawn, without writing their own depth
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Lequal);
        _gl.DepthMask(false);
        DrawTriangles(_deep);
        _gl.DepthMask(true);
        // the rest over everything
        _gl.Disable(EnableCap.DepthTest);
        DrawTriangles(_vertices);
        _gl.BindVertexArray(0);
        _gl.Disable(EnableCap.Blend);
        _gl.Enable(EnableCap.DepthTest);
    }

    private void DrawTriangles(List<float> vertices)
    {
        if (vertices.Count == 0)
        {
            return;
        }
        ReadOnlySpan<float> data = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(vertices);
        _gl.BufferData<float>(BufferTargetARB.ArrayBuffer, data, BufferUsageARB.StreamDraw);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)(data.Length / 7));
    }

    private void Segment(Vector2 a, Vector2 b, float width, Vector4 color, bool deep = false)
    {
        Vector2 d = b - a;
        if (d.LengthSquared() < 1e-6f)
        {
            return;
        }
        Vector2 along = Vector2.Normalize(d);
        Vector2 across = new Vector2(-along.Y, along.X) * (width / 2);
        // a little past each end, so joined segments close their corners
        Vector2 extend = along * (width / 2);
        if (deep)
        {
            // each end at its own depth; not past the ends, which would stick out of the scene
            (float from, float to) = _depth;
            Add(a + across, color, from);
            Add(b + across, color, to);
            Add(b - across, color, to);
            Add(a + across, color, from);
            Add(b - across, color, to);
            Add(a - across, color, from);
            return;
        }
        Quad(a - extend + across, b + extend + across, b + extend - across, a - extend - across, color);
    }

    private void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Vector4 color)
    {
        Add(a, color);
        Add(b, color);
        Add(c, color);
        Add(a, color);
        Add(c, color);
        Add(d, color);
    }

    private void Add(Vector2 p, Vector4 color, float depth = 0)
    {
        _into.Add(p.X);
        _into.Add(p.Y);
        _into.Add(depth);
        _into.Add(color.X * color.W);
        _into.Add(color.Y * color.W);
        _into.Add(color.Z * color.W);
        _into.Add(color.W);
    }

    private static string Vertex(bool es) => TerrainShaders.Header(es) + """
        layout(location = 0) in vec3 aPixel;
        layout(location = 1) in vec4 aColor;
        uniform vec2 uViewSize;
        out vec4 vColor;

        void main()
        {
            vColor = aColor;
            gl_Position = vec4(aPixel.x / uViewSize.x * 2.0 - 1.0, 1.0 - aPixel.y / uViewSize.y * 2.0, aPixel.z, 1.0);
        }
        """;

    private static string Fragment(bool es) => TerrainShaders.Header(es) + """
        in vec4 vColor;
        out vec4 outColor;

        void main()
        {
            outColor = vColor;
        }
        """;

    public void Dispose()
    {
        _program.Dispose();
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteBuffer(_buffer);
    }
}
