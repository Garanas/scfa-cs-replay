using System.Collections.Immutable;
using System.Numerics;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using Silk.NET.OpenGL;

namespace FAForever.MapEditor.App.Rendering.Gpu;

/// <summary>
/// What the 3D view drew of the sketch in a frame (<see cref="SketchRenderer.Drawn"/>), for automation.
/// </summary>
/// <param name="Sheets">Sheets drawn.</param>
/// <param name="Lines">Lines (a stroke is one) of those sheets.</param>
/// <param name="Segments">Pieces the lines were cut into (at most an ogrid long, so that they follow the ground).</param>
/// <param name="Pending">Lines being drawn with the mouse, with their images by the symmetry.</param>
/// <param name="Built">Segments made on the processor this frame: 0 while the sketch did not change, only the
/// added ones when strokes were added.</param>
internal readonly record struct SketchDrawn(int Sheets, int Lines, int Segments, int Pending, int Built);

/// <summary>
/// The sketch's lines over the terrain (user story N-3), at the same width on screen at every
/// distance, with round ends and joins, in the sketch's colours.
/// </summary>
/// <remarks>
/// A line is cut into pieces of at most an ogrid in the map's own places (x, z) once, kept on the card per
/// sheet; the vertex shader reads the height of each end from the heightmap's texture, so the lines follow
/// the ground when it is edited without being made again, lifts them a little towards the eye (so that the
/// ground's own triangles do not hide them) and spreads each piece into a quad of pixels, a capsule of the
/// line's width that the fragment shader rounds and smooths. A stroke added to a sheet adds only its own
/// pieces to the sheet's buffer, written from its end backwards, so that the newest lie first: a sheet is
/// drawn once with a stencil that lets a pixel be written once, so that overlaps do not darken at the
/// sheet's opacity and the newest line lies on top, as in the top view.
/// </remarks>
internal sealed unsafe class SketchRenderer : IDisposable
{
    // two places (x, z) a piece, its colour as four bytes, its width in pixels
    private const int Stride = 6 * sizeof(float);

    // the longest a piece is, in ogrids: the heightmap has a sample per ogrid
    private const float Piece = 1;

    private readonly GL _gl;
    private readonly TerrainRenderer.Program _program;
    private readonly Dictionary<string, Batch> _sheets = [];
    private readonly Batch _pending;
    private bool? _stencilOf;
    private int _built;

    public SketchRenderer(GL gl, bool es)
    {
        _gl = gl;
        _program = new TerrainRenderer.Program(gl, Vertex(es), Fragment(es));
        _pending = new Batch(gl);
    }

    /// <summary>
    /// What the last frame drew.
    /// </summary>
    public SketchDrawn Drawn { get; private set; }

    /// <summary>
    /// Whether overlaps in a sheet are kept from darkening at its opacity (the frame buffer has a stencil).
    /// </summary>
    public bool HasStencil => _stencil;

    // asked of the frame buffer being drawn into, the first time
    private bool _stencil
    {
        get
        {
            if (_stencilOf is null)
            {
                _gl.GetFramebufferAttachmentParameter(FramebufferTarget.Framebuffer, FramebufferAttachment.StencilAttachment, FramebufferAttachmentParameterName.StencilSize, out int bits);
                _stencilOf = _gl.GetError() == GLEnum.NoError && bits > 0;
            }
            return _stencilOf.Value;
        }
    }

    /// <param name="heights">The heightmap's texture, bound to unit 0 by the caller.</param>
    /// <param name="layers">The sheets to draw, each with its lines and opacity.</param>
    /// <param name="alive">The names of every sheet of the sketch: the buffers of the others are let go.</param>
    /// <param name="pending">The lines being drawn.</param>
    /// <param name="size">The view's size in pixels.</param>
    public void Draw(Matrix4x4 viewProjection, Vector3 eye, Vector2 size, float heightScale, IReadOnlyList<SketchLayer> layers, IReadOnlyCollection<string> alive, IReadOnlyList<SketchStroke> pending)
    {
        using Performance.Span measure = Performance.Measure("3d.draw-sketch");
        _built = 0;
        foreach (string gone in _sheets.Keys.Where(name => !alive.Contains(name)).ToArray())
        {
            _sheets[gone].Dispose();
            _sheets.Remove(gone);
        }
        int lines = 0;
        int segments = 0;
        foreach (SketchLayer layer in layers)
        {
            if (!_sheets.TryGetValue(layer.Sheet.Name, out Batch? batch))
            {
                batch = new Batch(_gl);
                _sheets[layer.Sheet.Name] = batch;
            }
            using (Performance.Measure("3d.sketch-build"))
            {
                _built += batch.Update(layer.Lines);
            }
            lines += layer.Lines.Length;
            segments += batch.Count;
        }

        if (layers.Count == 0 && pending.Count == 0)
        {
            Drawn = new SketchDrawn(0, 0, 0, 0, _built);
            return;
        }
        _program.Use();
        _program.Set("uViewProjection", viewProjection);
        _program.Set("uViewSize", size.X, size.Y);
        _program.Set("uEye", eye.X, eye.Y, eye.Z);
        _program.Set("uHeights", 0);
        _program.Set("uHeightScale", heightScale);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Lequal);
        _gl.DepthMask(false);
        if (_stencil)
        {
            _gl.Enable(EnableCap.StencilTest);
            _gl.StencilMask(0xFF);
            _gl.StencilFunc(StencilFunction.Notequal, 1, 0xFF);
            _gl.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Replace);
        }
        foreach (SketchLayer layer in layers)
        {
            DrawBatch(_sheets[layer.Sheet.Name], layer.Opacity);
        }
        int pendingCount = pending.Count;
        if (pendingCount > 0)
        {
            // the line the mouse draws and its images by the symmetry, which are fainter
            _pending.Reset();
            for (int i = 0; i < pendingCount; i++)
            {
                _pending.Add(pending[i].Colour, pending[i].Width, pending[i].Points);
            }
            _pending.Flush();
            DrawBatch(_pending, 1);
        }
        if (_stencil)
        {
            _gl.Disable(EnableCap.StencilTest);
        }
        _gl.BindVertexArray(0);
        _gl.DepthMask(false);
        Drawn = new SketchDrawn(layers.Count, lines, segments, pendingCount, _built);
    }

    private void DrawBatch(Batch batch, float opacity)
    {
        if (batch.Count == 0)
        {
            return;
        }
        if (_stencil)
        {
            _gl.Clear(ClearBufferMask.StencilBufferBit);
        }
        _program.Set("uAlpha", opacity);
        batch.Draw();
    }

    public void Dispose()
    {
        foreach (Batch batch in _sheets.Values)
        {
            batch.Dispose();
        }
        _sheets.Clear();
        _pending.Dispose();
        _program.Dispose();
    }

    /// <summary>
    /// The pieces of some lines on the card.
    /// </summary>
    private sealed class Batch : IDisposable
    {
        private readonly GL _gl;
        private readonly uint _vao;
        private readonly uint _buffer;
        private int _capacity;
        private int _first;
        private float[] _cpu = [];
        private ImmutableArray<SketchLine> _lines = [];

        // the pieces of the lines added since the last Flush, a line each
        private readonly List<float[]> _added = [];

        public Batch(GL gl)
        {
            _gl = gl;
            _vao = gl.GenVertexArray();
            _buffer = gl.GenBuffer();
        }

        /// <summary>
        /// How many pieces the batch holds.
        /// </summary>
        public int Count => _capacity - _first;

        /// <summary>
        /// Makes the batch hold <paramref name="lines"/>: only the lines added to its end when it holds
        /// the lines before them, else all of them.
        /// </summary>
        /// <returns>How many pieces were made.</returns>
        public int Update(ImmutableArray<SketchLine> lines)
        {
            if (lines == _lines)
            {
                return 0;
            }
            int from = 0;
            if (SketchLines.Extends(_lines, lines))
            {
                from = _lines.Length;
            }
            else
            {
                Reset();
            }
            _lines = lines;
            int made = 0;
            for (int i = from; i < lines.Length; i++)
            {
                made += Add(lines[i].Colour, lines[i].Width, lines[i].Points);
            }
            Flush();
            return made;
        }

        /// <summary>
        /// Empties the batch, to fill it again with <see cref="Add"/> and <see cref="Flush"/>.
        /// </summary>
        public void Reset()
        {
            _capacity = 0;
            _first = 0;
            _cpu = [];
            _lines = [];
            _added.Clear();
        }

        /// <summary>
        /// Cuts a line into pieces, to be put on the card by <see cref="Flush"/>.
        /// </summary>
        /// <returns>How many pieces.</returns>
        public int Add(SketchColour colour, SketchWidth width, ImmutableArray<Vector2> points)
        {
            if (points.IsDefaultOrEmpty)
            {
                return 0;
            }
            uint packed = Pack(colour);
            float pixels = SketchLook.Pixels(width);
            List<float> data = [];
            int count = 0;
            if (points.Length == 1)
            {
                AddPiece(data, points[0], points[0], packed, pixels);
                count = 1;
            }
            for (int i = 1; i < points.Length; i++)
            {
                Vector2 a = points[i - 1];
                Vector2 b = points[i];
                int pieces = Math.Clamp((int)MathF.Ceiling(Vector2.Distance(a, b) / Piece), 1, 4096);
                for (int p = 0; p < pieces; p++)
                {
                    AddPiece(data, Vector2.Lerp(a, b, (float)p / pieces), Vector2.Lerp(a, b, (float)(p + 1) / pieces), packed, pixels);
                    count++;
                }
            }
            _added.Add([.. data]);
            return count;
        }

        private static void AddPiece(List<float> data, Vector2 a, Vector2 b, uint colour, float width)
        {
            data.Add(a.X);
            data.Add(a.Y);
            data.Add(b.X);
            data.Add(b.Y);
            data.Add(BitConverter.UInt32BitsToSingle(colour));
            data.Add(width);
        }

        /// <summary>
        /// Puts the lines added on the card, each in front of the ones before it.
        /// </summary>
        public void Flush()
        {
            if (_added.Count == 0)
            {
                return;
            }
            int floats = _added.Sum(line => line.Length);
            int needed = floats / 6;
            bool regrown = false;
            if (needed > _first)
            {
                // no room in front: a larger buffer, the pieces held at its end, all sent again
                int old = Count;
                int capacity = Math.Max(256, (old + needed) * 2);
                float[] larger = new float[capacity * 6];
                Array.Copy(_cpu, _first * 6, larger, (capacity - old) * 6, old * 6);
                _cpu = larger;
                _capacity = capacity;
                _first = capacity - old;
                regrown = true;
            }
            // the newest lowest: the lines in the order they were added, each in front of the one before
            float[] block = new float[floats];
            int at = floats;
            foreach (float[] line in _added)
            {
                at -= line.Length;
                Array.Copy(line, 0, block, at, line.Length);
            }
            _added.Clear();
            _first -= needed;
            Array.Copy(block, 0, _cpu, _first * 6, floats);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _buffer);
            if (regrown)
            {
                fixed (float* source = _cpu)
                {
                    _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)((nint)_capacity * Stride), source, BufferUsageARB.DynamicDraw);
                }
                return;
            }
            fixed (float* source = block)
            {
                _gl.BufferSubData(BufferTargetARB.ArrayBuffer, (nint)_first * Stride, (nuint)((nint)needed * Stride), source);
            }
        }

        public void Draw()
        {
            _gl.BindVertexArray(_vao);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _buffer);
            nint offset = (nint)_first * Stride;
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(0, 4, VertexAttribPointerType.Float, false, Stride, (void*)offset);
            _gl.VertexAttribDivisor(0, 1);
            _gl.EnableVertexAttribArray(1);
            _gl.VertexAttribPointer(1, 4, VertexAttribPointerType.UnsignedByte, true, Stride, (void*)(offset + 4 * sizeof(float)));
            _gl.VertexAttribDivisor(1, 1);
            _gl.EnableVertexAttribArray(2);
            _gl.VertexAttribPointer(2, 1, VertexAttribPointerType.Float, false, Stride, (void*)(offset + 5 * sizeof(float)));
            _gl.VertexAttribDivisor(2, 1);
            _gl.DrawArraysInstanced(PrimitiveType.TriangleStrip, 0, 4, (uint)Count);
        }

        public void Dispose()
        {
            _gl.DeleteVertexArray(_vao);
            _gl.DeleteBuffer(_buffer);
        }
    }

    // the bytes red, green, blue and alpha in memory order
    private static uint Pack(SketchColour colour)
    {
        uint argb = SketchLook.Argb(colour);
        uint r = (argb >> 16) & 0xFF;
        uint g = (argb >> 8) & 0xFF;
        uint b = argb & 0xFF;
        return r | (g << 8) | (b << 16) | (0xFFu << 24);
    }

    private static string Vertex(bool es) => TerrainShaders.Header(es) + TerrainShaders.Heights + """
        layout(location = 0) in vec4 aPlaces;
        layout(location = 1) in vec4 aColour;
        layout(location = 2) in float aWidth;
        uniform mat4 uViewProjection;
        uniform vec2 uViewSize;
        uniform vec3 uEye;
        flat out vec2 vA;
        flat out vec2 vB;
        flat out vec4 vColour;
        flat out float vHalf;
        out vec2 vPixel;

        // a place on the ground, lifted towards the eye by a little more the further away it is
        vec3 onGround(vec2 p)
        {
            vec3 place = vec3(p.x, heightAt(p), p.y);
            return place + vec3(0.0, 0.2 + 0.0025 * distance(uEye, place), 0.0);
        }

        void main()
        {
            vec4 a = uViewProjection * vec4(onGround(aPlaces.xy), 1.0);
            vec4 b = uViewProjection * vec4(onGround(aPlaces.zw), 1.0);
            // the part behind the camera is cut off
            const float near = 0.05;
            if (a.w < near && b.w < near)
            {
                gl_Position = vec4(2.0, 2.0, 2.0, 1.0);
                return;
            }
            if (a.w < near)
            {
                a = mix(a, b, (near - a.w) / (b.w - a.w));
            }
            else if (b.w < near)
            {
                b = mix(b, a, (near - b.w) / (a.w - b.w));
            }
            vec3 na = a.xyz / a.w;
            vec3 nb = b.xyz / b.w;
            vec2 pa = vec2(na.x * 0.5 + 0.5, 0.5 - na.y * 0.5) * uViewSize;
            vec2 pb = vec2(nb.x * 0.5 + 0.5, 0.5 - nb.y * 0.5) * uViewSize;
            vec2 along = pb - pa;
            float length2 = length(along);
            vec2 dir = length2 > 1e-4 ? along / length2 : vec2(1.0, 0.0);
            vec2 side = vec2(-dir.y, dir.x);
            float halfWidth = aWidth * 0.5;
            float reach = halfWidth + 1.0;
            float end = float(gl_VertexID & 1);
            float across = float(gl_VertexID >> 1) * 2.0 - 1.0;
            vec2 pixel = mix(pa, pb, end) + dir * (end * 2.0 - 1.0) * reach + side * across * reach;
            float t = length2 > 1e-4 ? clamp(dot(pixel - pa, dir) / length2, 0.0, 1.0) : 0.0;
            vA = pa;
            vB = pb;
            vColour = aColour;
            vHalf = halfWidth;
            vPixel = pixel;
            gl_Position = vec4(pixel.x / uViewSize.x * 2.0 - 1.0, 1.0 - pixel.y / uViewSize.y * 2.0, mix(na.z, nb.z, t), 1.0);
        }
        """;

    private static string Fragment(bool es) => TerrainShaders.Header(es) + """
        flat in vec2 vA;
        flat in vec2 vB;
        flat in vec4 vColour;
        flat in float vHalf;
        in vec2 vPixel;
        uniform float uAlpha;
        out vec4 outColor;

        void main()
        {
            vec2 ab = vB - vA;
            float length2 = dot(ab, ab);
            float t = length2 > 1e-8 ? clamp(dot(vPixel - vA, ab) / length2, 0.0, 1.0) : 0.0;
            float distanceToLine = distance(vPixel, vA + ab * t);
            // a pixel wide edge, smooth
            float cover = clamp(vHalf - distanceToLine + 0.5, 0.0, 1.0);
            if (cover < 0.02)
            {
                discard;
            }
            float alpha = cover * uAlpha;
            outColor = vec4(vColour.rgb * alpha, alpha);
        }
        """;
}

/// <summary>
/// What the 3D view hands the renderer to draw of the sketch in a frame.
/// </summary>
/// <param name="Layers">The sheets to draw (the shown ones, with their opacity in this mode).</param>
/// <param name="Alive">The names of all the sketch's sheets.</param>
/// <param name="Pending">The line being drawn and its images.</param>
/// <param name="Size">The view's size in pixels.</param>
internal sealed record SketchFrame(IReadOnlyList<SketchLayer> Layers, IReadOnlyCollection<string> Alive, IReadOnlyList<SketchStroke> Pending, Vector2 Size);
