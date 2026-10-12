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
/// <param name="Built">Pieces made on the processor this frame: 0 while the sketch did not change, only the
/// added ones when strokes were added.</param>
/// <param name="Layers">Sheets drawn into a layer of their own and laid over the scene at their opacity.</param>
/// <param name="Labels">Labels drawn as text over the terrain.</param>
internal readonly record struct SketchDrawn(int Sheets, int Lines, int Segments, int Pending, int Built, int Layers, int Labels = 0);

/// <summary>
/// What the 3D view hands the renderer to draw of the sketch in a frame.
/// </summary>
/// <param name="Layers">The sheets to draw (the shown ones, with their opacity in this mode).</param>
/// <param name="Alive">The names of all the sketch's sheets.</param>
/// <param name="Pending">The line being drawn and its images.</param>
/// <param name="Size">The view's size, in the units the lines' widths are in (the view's own pixels).</param>
/// <param name="Labels">The labels' pictures; null draws no labels.</param>
/// <param name="Scale">How many of the card's pixels a unit of <paramref name="Size"/> is (the screen's scaling).</param>
internal sealed record SketchFrame(IReadOnlyList<SketchLayer> Layers, IReadOnlyCollection<string> Alive, IReadOnlyList<SketchStroke> Pending, Vector2 Size,
    LabelBitmaps? Labels = null, float Scale = 1);

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
/// pieces to the sheet's buffer. A sheet at full opacity is drawn into the scene itself, the lines in the
/// order they were drawn; a sheet at less is drawn at full opacity into a layer of its own (a texture
/// the size of the view, tested against the scene's own depth buffer, which the layer's frame buffer shares)
/// and laid over the scene at the sheet's opacity, so that lines that overlap do not darken, as in the top view.
/// </remarks>
internal sealed unsafe class SketchRenderer : IDisposable
{
    // two places (x, z) a piece, its colour as four bytes, its width in pixels
    private const int Stride = 6 * sizeof(float);

    // the longest a piece is, in ogrids: the heightmap has a sample per ogrid
    private const float Piece = 1;

    private readonly GL _gl;
    private readonly TerrainRenderer.Program _program;
    private readonly TerrainRenderer.Program _lay;
    private readonly TerrainRenderer.Program _label;
    private readonly Dictionary<LabelBitmap, uint> _labelTextures = [];
    private int _labelGeneration;
    private readonly Dictionary<string, Batch> _sheets = [];
    private readonly Batch _pending;
    private readonly uint _layerVao;
    private uint _layerFrame;
    private uint _layerTexture;
    private (int Width, int Height) _layerSize;
    private uint _layerDepth;
    private int _built;

    public SketchRenderer(GL gl, bool es)
    {
        _gl = gl;
        _program = new TerrainRenderer.Program(gl, Vertex(es), Fragment(es));
        _lay = new TerrainRenderer.Program(gl, LayVertex(es), LayFragment(es));
        _label = new TerrainRenderer.Program(gl, LabelVertex(es), LabelFragment(es));
        _pending = new Batch(gl);
        _layerVao = gl.GenVertexArray();
    }

    /// <summary>
    /// What the last frame drew.
    /// </summary>
    public SketchDrawn Drawn { get; private set; }

    /// <summary>
    /// Whether a sheet at less than full opacity is drawn in a layer of its own (else its lines are
    /// blended one by one, which darkens where they overlap): false once the card would not make the
    /// layer's frame buffer.
    /// </summary>
    public bool LayersWork { get; private set; } = true;

    /// <param name="layers">The sheets to draw, each with its lines and opacity.</param>
    /// <param name="alive">The names of every sheet of the sketch: the buffers of the others are let go.</param>
    /// <param name="pending">The lines being drawn.</param>
    /// <param name="size">The view's size, in the units the lines' widths are in.</param>
    /// <param name="heights">The terrain's heights, to size the labels by their distance.</param>
    /// <param name="labels">The labels' pictures; null draws none.</param>
    /// <param name="scale">How many of the card's pixels a unit of <paramref name="size"/> is.</param>
    public void Draw(Matrix4x4 viewProjection, Vector3 eye, Vector2 size, float heightScale, IReadOnlyList<SketchLayer> layers, IReadOnlyCollection<string> alive, IReadOnlyList<SketchStroke> pending,
        Heightmap? heights = null, LabelBitmaps? labels = null, float scale = 1)
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

        int layered = 0;
        if (layers.Count > 0 || pending.Count > 0)
        {
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
            // the sheets at full opacity first, straight into the scene
            foreach (SketchLayer layer in layers)
            {
                if (layer.Opacity >= 1 || !LayersWork)
                {
                    DrawWithHalos(_sheets[layer.Sheet.Name], layer.Opacity);
                }
            }
            foreach (SketchLayer layer in layers)
            {
                if (layer.Opacity < 1 && LayersWork)
                {
                    if (DrawLayer(_sheets[layer.Sheet.Name], layer.Opacity))
                    {
                        layered++;
                    }
                    else
                    {
                        // the card cannot make a layer: this sheet is blended line by line, and the next ones
                        _program.Use();
                        DrawWithHalos(_sheets[layer.Sheet.Name], layer.Opacity);
                    }
                }
            }
            int pendingCount = pending.Count;
            if (pendingCount > 0)
            {
                // the line the mouse draws and its images by the symmetry
                _pending.Reset();
                for (int i = 0; i < pendingCount; i++)
                {
                    _pending.Add(pending[i].Colour, pending[i].Width, pending[i].Points);
                }
                _pending.Flush();
                _program.Use();
                DrawWithHalos(_pending, 1f);
            }
            _gl.BindVertexArray(0);
        }
        int labelsDrawn = 0;
        if (labels is not null && heights is not null)
        {
            using (Performance.Measure("3d.draw-sketch-labels"))
            {
                foreach (SketchLayer layer in layers)
                {
                    if (!layer.Sheet.Labels.IsEmpty)
                    {
                        labelsDrawn += DrawLabels(layer.Sheet, layer.Opacity, viewProjection, eye, size, heightScale, heights, labels, scale);
                    }
                }
            }
        }
        Drawn = new SketchDrawn(layers.Count, lines, segments, pending.Count, _built, layered, labelsDrawn);
    }

    // a label is as large on screen as its size on the map makes it, within these limits (in the view's units), so it can be read near
    // and far without covering the map up
    private const float SmallestLabel = 11, LargestLabel = 44;

    // the sizes a label's picture is made at, in the card's pixels: a picture for every size asked for would be made for every step of the camera
    private static readonly int[] LabelSizes = [10, 12, 14, 17, 20, 24, 28, 34, 40, 48, 58, 70, 84];

    /// <summary>
    /// Draws a sheet's labels as text, a picture each laid on a quad that faces the screen, centred over its place a little above the ground;
    /// the ground and what stands on it hide it as they hide the lines.
    /// </summary>
    /// <returns>How many labels were drawn (the pictures not made yet are left out and the view draws again when they are).</returns>
    private int DrawLabels(SketchSheet sheet, float opacity, Matrix4x4 viewProjection, Vector3 eye, Vector2 size, float heightScale, Heightmap heights, LabelBitmaps labels, float scale)
    {
        if (_labelGeneration != labels.Generation)
        {
            // the pictures the textures were made of are gone
            foreach (uint texture in _labelTextures.Values)
            {
                _gl.DeleteTexture(texture);
            }
            _labelTextures.Clear();
            _labelGeneration = labels.Generation;
        }
        int drawn = 0;
        bool prepared = false;
        foreach (SketchLabel label in sheet.Labels)
        {
            float ground = heights.HeightAt(label.Place.X, label.Place.Y);
            float distance = Vector3.Distance(eye, new Vector3(label.Place.X, ground, label.Place.Y));
            // above the ground, more the further away (the ground's own triangles would otherwise lie over its foot)
            Vector3 at = new Vector3(label.Place.X, ground + 1.5f + 0.0025f * distance, label.Place.Y);
            if (HiddenByGround(heights, eye, at))
            {
                continue;
            }
            float ogridsPerPixel = 2 * distance * MathF.Tan(EditorCamera.FieldOfView / 2) / Math.Max(1, size.Y);
            float pixels = Math.Clamp(label.Size / Math.Max(ogridsPerPixel, 1e-6f), SmallestLabel, LargestLabel) * scale;
            int device = LabelSizes.MinBy(step => Math.Abs(step - pixels));
            if (labels.Get(label, device) is not LabelBitmap bitmap)
            {
                continue;
            }
            if (!_labelTextures.TryGetValue(bitmap, out uint texture))
            {
                texture = MakeLabelTexture(bitmap);
                _labelTextures[bitmap] = texture;
            }
            if (!prepared)
            {
                _label.Use();
                _label.Set("uViewProjection", viewProjection);
                _label.Set("uViewSize", size.X, size.Y);
                _label.Set("uScale", scale);
                _label.Set("uLabel", 1);
                _label.Set("uAlpha", opacity);
                _gl.Enable(EnableCap.Blend);
                _gl.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
                // a label is seen whole or not at all (HiddenByGround), so it is not tested against the depth buffer, which would
                // cut its lower part where the ground in front of it stands higher
                _gl.Disable(EnableCap.DepthTest);
                _gl.DepthMask(false);
                _gl.BindVertexArray(_layerVao);
                prepared = true;
            }
            _label.Set("uAt", at.X, at.Y, at.Z);
            _label.Set("uSize", bitmap.Width / scale, bitmap.Height / scale);
            _gl.ActiveTexture(TextureUnit.Texture1);
            _gl.BindTexture(TextureTarget.Texture2D, texture);
            _gl.BindSampler(1, 0);
            _gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
            drawn++;
        }
        if (prepared)
        {
            _gl.Enable(EnableCap.DepthTest);
            _gl.ActiveTexture(TextureUnit.Texture0);
            _gl.BindVertexArray(0);
        }
        return drawn;
    }

    /// <summary>
    /// Whether the terrain lies between the eye and a place: the ground along the line is higher than the line.
    /// </summary>
    private static bool HiddenByGround(Heightmap heights, Vector3 eye, Vector3 at)
    {
        int steps = Math.Clamp((int)Vector3.Distance(eye, at), 8, 256);
        // the last steps before the place are the ground's own, where it rises to the place: left out
        for (int i = 1; i < steps - 1; i++)
        {
            Vector3 along = Vector3.Lerp(eye, at, (float)i / steps);
            if (along.X >= 0 && along.Z >= 0 && along.X <= heights.Samples.Width - 1 && along.Z <= heights.Samples.Height - 1 && heights.HeightAt(along.X, along.Z) > along.Y)
            {
                return true;
            }
        }
        return false;
    }

    private uint MakeLabelTexture(LabelBitmap bitmap)
    {
        uint texture = _gl.GenTexture();
        _gl.ActiveTexture(TextureUnit.Texture1);
        _gl.BindTexture(TextureTarget.Texture2D, texture);
        _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        fixed (byte* data = bitmap.Rgba)
        {
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)bitmap.Width, (uint)bitmap.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, data);
        }
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        return texture;
    }

    /// <summary>
    /// Draws a batch's lines: first the halos of its black and white lines (a little wider, in the opposite tone), then
    /// the lines, so that no halo lies over a line of the batch.
    /// </summary>
    private void DrawWithHalos(Batch batch, float alpha)
    {
        _program.Set("uAlpha", alpha);
        _program.Set("uHalo", SketchLook.HaloPixels);
        batch.Draw();
        _program.Set("uHalo", 0f);
        batch.Draw();
    }

    /// <summary>
    /// Draws a sheet at full opacity into the layer, then lays the layer over the scene at
    /// <paramref name="opacity"/>.
    /// </summary>
    /// <returns>False when the layer cannot be made: the card refuses the frame buffer.</returns>
    private bool DrawLayer(Batch batch, float opacity)
    {
        if (batch.Count == 0)
        {
            return true;
        }
        _gl.GetInteger(GetPName.DrawFramebufferBinding, out int scene);
        _gl.GetFramebufferAttachmentParameter(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, FramebufferAttachmentParameterName.ObjectType, out int depthType);
        _gl.GetFramebufferAttachmentParameter(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, FramebufferAttachmentParameterName.ObjectName, out int depth);
        Span<int> viewport = stackalloc int[4];
        _gl.GetInteger(GetPName.Viewport, viewport);
        int width = viewport[2];
        int height = viewport[3];
        if (depth == 0 || depthType != (int)GLEnum.Renderbuffer || width <= 0 || height <= 0)
        {
            LayersWork = false;
            return false;
        }
        if (_layerFrame == 0)
        {
            _layerFrame = _gl.GenFramebuffer();
            _layerTexture = _gl.GenTexture();
        }
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _layerFrame);
        if (_layerSize != (width, height))
        {
            _gl.ActiveTexture(TextureUnit.Texture1);
            _gl.BindTexture(TextureTarget.Texture2D, _layerTexture);
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, null);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
            _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _layerTexture, 0);
            _gl.ActiveTexture(TextureUnit.Texture0);
            _layerSize = (width, height);
        }
        if (_layerDepth != (uint)depth)
        {
            // the scene's own depth buffer, only read: the sheet's lines hide behind the hills as they do in the scene
            _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, (uint)depth);
            _layerDepth = (uint)depth;
        }
        if (_gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != GLEnum.FramebufferComplete)
        {
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)scene);
            LayersWork = false;
            return false;
        }
        _gl.ClearColor(0, 0, 0, 0);
        _gl.Clear(ClearBufferMask.ColorBufferBit);
        _program.Use();
        DrawWithHalos(batch, 1f);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)scene);

        // over the scene, whatever its depth: the layer's lines were tested already
        _gl.Disable(EnableCap.DepthTest);
        _lay.Use();
        _gl.ActiveTexture(TextureUnit.Texture1);
        _gl.BindTexture(TextureTarget.Texture2D, _layerTexture);
        _gl.BindSampler(1, 0);
        _lay.Set("uLayer", 1);
        _lay.Set("uAlpha", opacity);
        _gl.BindVertexArray(_layerVao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.Enable(EnableCap.DepthTest);
        return true;
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
        _lay.Dispose();
        _label.Dispose();
        foreach (uint texture in _labelTextures.Values)
        {
            _gl.DeleteTexture(texture);
        }
        _labelTextures.Clear();
        _gl.DeleteVertexArray(_layerVao);
        if (_layerFrame != 0)
        {
            _gl.DeleteFramebuffer(_layerFrame);
            _gl.DeleteTexture(_layerTexture);
        }
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
        private int _count;
        private float[] _cpu = [];
        private ImmutableArray<SketchLine> _lines = [];

        // the pieces of the lines added since the last Flush
        private readonly List<float> _added = [];

        public Batch(GL gl)
        {
            _gl = gl;
            _vao = gl.GenVertexArray();
            _buffer = gl.GenBuffer();
        }

        /// <summary>
        /// How many pieces the batch holds.
        /// </summary>
        public int Count => _count;

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
            _count = 0;
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
            int count = 0;
            if (points.Length == 1)
            {
                AddPiece(points[0], points[0], packed, pixels);
                count = 1;
            }
            for (int i = 1; i < points.Length; i++)
            {
                Vector2 a = points[i - 1];
                Vector2 b = points[i];
                int pieces = Math.Clamp((int)MathF.Ceiling(Vector2.Distance(a, b) / Piece), 1, 4096);
                for (int p = 0; p < pieces; p++)
                {
                    AddPiece(Vector2.Lerp(a, b, (float)p / pieces), Vector2.Lerp(a, b, (float)(p + 1) / pieces), packed, pixels);
                    count++;
                }
            }
            return count;
        }

        private void AddPiece(Vector2 a, Vector2 b, uint colour, float width)
        {
            _added.Add(a.X);
            _added.Add(a.Y);
            _added.Add(b.X);
            _added.Add(b.Y);
            _added.Add(BitConverter.UInt32BitsToSingle(colour));
            _added.Add(width);
        }

        /// <summary>
        /// Puts the pieces added after those held, on the card.
        /// </summary>
        public void Flush()
        {
            if (_added.Count == 0)
            {
                return;
            }
            int needed = _added.Count / 6;
            bool regrown = false;
            if (_count + needed > _capacity)
            {
                // a larger buffer, all sent again
                _capacity = Math.Max(256, (_count + needed) * 2);
                Array.Resize(ref _cpu, _capacity * 6);
                regrown = true;
            }
            _added.CopyTo(_cpu, _count * 6);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _buffer);
            if (regrown)
            {
                fixed (float* source = _cpu)
                {
                    _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)((nint)_capacity * Stride), source, BufferUsageARB.DynamicDraw);
                }
            }
            else
            {
                fixed (float* source = &_cpu[_count * 6])
                {
                    _gl.BufferSubData(BufferTargetARB.ArrayBuffer, (nint)_count * Stride, (nuint)((nint)needed * Stride), source);
                }
            }
            _count += needed;
            _added.Clear();
        }

        public void Draw()
        {
            if (_count == 0)
            {
                return;
            }
            _gl.BindVertexArray(_vao);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _buffer);
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(0, 4, VertexAttribPointerType.Float, false, Stride, null);
            _gl.VertexAttribDivisor(0, 1);
            _gl.EnableVertexAttribArray(1);
            _gl.VertexAttribPointer(1, 4, VertexAttribPointerType.UnsignedByte, true, Stride, (void*)(4 * sizeof(float)));
            _gl.VertexAttribDivisor(1, 1);
            _gl.EnableVertexAttribArray(2);
            _gl.VertexAttribPointer(2, 1, VertexAttribPointerType.Float, false, Stride, (void*)(5 * sizeof(float)));
            _gl.VertexAttribDivisor(2, 1);
            _gl.DrawArraysInstanced(PrimitiveType.TriangleStrip, 0, 4, (uint)_count);
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
        uniform float uHalo;
        flat out vec2 vA;
        flat out vec2 vB;
        flat out vec4 vColour;
        flat out float vHalf;
        out vec2 vPixel;

        // a place on the ground, lifted towards the eye by a little more the further away it is, and by more the
        // steeper the ground is there (the terrain's triangles stray from the bilinear height by about the slope)
        vec3 onGround(vec2 p)
        {
            vec3 place = vec3(p.x, heightAt(p), p.y);
            vec2 slope = vec2(heightAt(p + vec2(1.0, 0.0)) - heightAt(p - vec2(1.0, 0.0)), heightAt(p + vec2(0.0, 1.0)) - heightAt(p - vec2(0.0, 1.0))) * 0.5;
            return place + vec3(0.0, 0.2 + 0.0025 * distance(uEye, place) + 0.5 * length(slope), 0.0);
        }

        void main()
        {
            // the halo pass: only black and white lines, a little wider, in the other tone
            vec3 colour = aColour.rgb;
            bool white = min(colour.r, min(colour.g, colour.b)) > 0.85;
            bool black = max(colour.r, max(colour.g, colour.b)) < 0.2;
            if (uHalo > 0.0 && !white && !black)
            {
                gl_Position = vec4(2.0, 2.0, 2.0, 1.0);
                return;
            }
            if (uHalo > 0.0)
            {
                colour = white ? vec3(0.086) : vec3(0.957, 0.945, 0.918);
            }
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
            float halfWidth = aWidth * 0.5 + uHalo;
            float reach = halfWidth + 1.0;
            float end = float(gl_VertexID & 1);
            float across = float(gl_VertexID >> 1) * 2.0 - 1.0;
            vec2 pixel = mix(pa, pb, end) + dir * (end * 2.0 - 1.0) * reach + side * across * reach;
            float t = length2 > 1e-4 ? clamp(dot(pixel - pa, dir) / length2, 0.0, 1.0) : 0.0;
            vA = pa;
            vB = pb;
            vColour = vec4(colour, aColour.a);
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

    // a quad that faces the screen, centred over a place on the ground
    private static string LabelVertex(bool es) => TerrainShaders.Header(es) + """
        uniform mat4 uViewProjection;
        uniform vec2 uViewSize;
        uniform vec3 uAt;
        uniform vec2 uSize;
        uniform float uScale;
        out vec2 vUv;

        void main()
        {
            vec4 clip = uViewProjection * vec4(uAt, 1.0);
            if (clip.w < 0.05)
            {
                gl_Position = vec4(2.0, 2.0, 2.0, 1.0);
                return;
            }
            vec3 n = clip.xyz / clip.w;
            vec2 centre = vec2(n.x * 0.5 + 0.5, 0.5 - n.y * 0.5) * uViewSize;
            // on whole pixels of the card, so the text stays sharp
            vec2 topLeft = floor((centre - uSize * 0.5) * uScale + 0.5) / uScale;
            vec2 corner = vec2(float(gl_VertexID & 1), float(gl_VertexID >> 1));
            vec2 pixel = topLeft + corner * uSize;
            vUv = corner;
            gl_Position = vec4(pixel.x / uViewSize.x * 2.0 - 1.0, 1.0 - pixel.y / uViewSize.y * 2.0, n.z, 1.0);
        }
        """;

    private static string LabelFragment(bool es) => TerrainShaders.Header(es) + """
        uniform sampler2D uLabel;
        uniform float uAlpha;
        in vec2 vUv;
        out vec4 outColor;

        void main()
        {
            outColor = texture(uLabel, vUv) * uAlpha;
        }
        """;

    // a triangle over the whole view
    private static string LayVertex(bool es) => TerrainShaders.Header(es) + """
        void main()
        {
            vec2 corner = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2));
            gl_Position = vec4(corner * 2.0 - 1.0, 0.0, 1.0);
        }
        """;

    private static string LayFragment(bool es) => TerrainShaders.Header(es) + """
        uniform sampler2D uLayer;
        uniform float uAlpha;
        out vec4 outColor;

        void main()
        {
            outColor = texelFetch(uLayer, ivec2(gl_FragCoord.xy), 0) * uAlpha;
        }
        """;
}
