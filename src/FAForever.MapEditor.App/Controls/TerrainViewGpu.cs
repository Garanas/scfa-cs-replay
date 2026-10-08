using System.Numerics;
using Vector = Avalonia.Vector;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Rendering;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Threading;
using FAForever.MapEditor.App.Rendering.Gpu;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;
using ImageMagick;
using Silk.NET.OpenGL;

namespace FAForever.MapEditor.App.Controls;

/// <summary>
/// The map in 3D on the graphics card (OpenGL, through ANGLE on Windows): the terrain with its
/// layers and decals, lit as in the game (<see cref="TerrainRenderer"/>). The camera works as the
/// FAF map editor's (<see cref="EditorCamera"/>): the wheel zooms towards the mouse, the middle
/// mouse button moves over the map, space and the mouse turn and tilt, Home shows the whole map.
/// The left mouse button is left for editing.
/// </summary>
/// <remarks>
/// It draws again whenever the document changes (an edit, an undo, a project file read again) and
/// whenever a texture of the workspace's assets changes on disk: a texture a tool saves shows a
/// moment later. Each texture file is on the card once, however many decals use it.
/// </remarks>
public sealed class TerrainViewGpu : OpenGlControlBase, ICustomHitTest
{
    /// <summary>
    /// The whole view takes the mouse. Avalonia hits only what a control draws itself, and the
    /// picture comes from the graphics card: without this, the mouse falls through to the tabs.
    /// </summary>
    public bool HitTest(Point point) => new Rect(Bounds.Size).Contains(point);

    private GL? _gl;
    private TerrainRenderer? _renderer;
    private MainViewModel? _model;
    private ProjectWatcher? _assetWatcher;
    private string? _watched;
    private TaskCompletionSource<string>? _capture;
    private string? _capturePath;

    private readonly EditorCamera _camera;
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private TimeSpan _lastFrame;
    private bool _placed;
    private bool _turning;
    private Point? _panFrom;
    private Point? _turnFrom;
    private Point _mouse;

    public TerrainViewGpu()
    {
        _camera = new EditorCamera((x, z, radius) => Smooth().Height(x, z, radius), GroundHeight);
        Focusable = true;
    }

    /// <summary>
    /// What went wrong when the view started (no OpenGL 3, a shader that did not compile), or null.
    /// </summary>
    public string? Error { get; private set; }

    /// <summary>
    /// The OpenGL the view runs on, e.g. <c>OpenGLES 3.0</c>; null until it has drawn.
    /// </summary>
    public string? Renderer { get; private set; }

    /// <summary>
    /// The textures on the card, and the last one that could not be read.
    /// </summary>
    public (int Textures, string? Error) TextureState => _renderer?.TextureState ?? (0, null);

    /// <summary>
    /// The decals drawn, the draws they take and the textures they use.
    /// </summary>
    public (int Decals, int Draws, int Textures) DecalState => _renderer?.DecalState ?? (0, 0, 0);

    /// <summary>
    /// What the terrain shows, to find out what is wrong with a picture: 0 the picture, 1 the
    /// albedo alone, 2 the light alone, 3 the normals, 4 the heights.
    /// </summary>
    public int Debug
    {
        get => _debug;
        set
        {
            _debug = value;
            RequestNextFrameRendering();
        }
    }

    private int _debug;

    /// <summary>
    /// The camera: the point of the ground it looks at (in ogrids), how far it is, and its yaw
    /// and pitch in degrees (90 is straight down).
    /// </summary>
    public (double X, double Z, double Distance, double Yaw, double Pitch) Camera
    {
        get
        {
            (float x, float z, float distance, float yaw, float pitch) = _camera.Now;
            return (x, z, distance, yaw, pitch);
        }
    }

    /// <summary>
    /// Points the camera; null keeps a value as it is.
    /// </summary>
    public void Look(double? x = null, double? z = null, double? distance = null, double? yaw = null, double? pitch = null)
    {
        _camera.Look((float?)x, (float?)z, (float?)distance, (float?)yaw, (float?)pitch);
        _placed = true;
        RequestNextFrameRendering();
    }

    /// <summary>
    /// Draws a frame and saves it as a PNG, for automation: the picture is on the graphics card,
    /// where a screenshot of the window does not reach.
    /// </summary>
    public Task<string> CaptureAsync(string path)
    {
        _capture?.TrySetCanceled();
        _capture = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _capturePath = Path.GetFullPath(path);
        RequestNextFrameRendering();
        return _capture.Task;
    }

    protected override void OnOpenGlInit(GlInterface gl)
    {
        base.OnOpenGlInit(gl);
        try
        {
            _gl = GL.GetApi(gl.GetProcAddress);
            bool es = GlVersion.Type == GlProfileType.OpenGLES;
            Renderer = $"{GlVersion.Type} {GlVersion.Major}.{GlVersion.Minor}: {_gl.GetStringS(StringName.Renderer)}";
            if (GlVersion.Major < 3 || (!es && GlVersion.Major == 3 && GlVersion.Minor < 3))
            {
                throw new InvalidOperationException($"The 3D view needs OpenGL ES 3.0 or OpenGL 3.3; this computer offers {Renderer}");
            }
            _renderer = new TerrainRenderer(_gl, es, () => Dispatcher.UIThread.Post(RequestNextFrameRendering));
            Error = null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or Silk.NET.Core.Loader.SymbolLoadingException)
        {
            Error = exception.Message;
            _renderer = null;
        }
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        _renderer?.Dispose();
        _renderer = null;
        _gl = null;
        base.OnOpenGlDeinit(gl);
    }

    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (_gl is null)
        {
            return;
        }
        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        int width = Math.Max(1, (int)(Bounds.Width * scaling));
        int height = Math.Max(1, (int)(Bounds.Height * scaling));
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)fb);
        _gl.Viewport(0, 0, (uint)width, (uint)height);
        _gl.ClearColor(24 / 255f, 24 / 255f, 24 / 255f, 1);
        _gl.ClearDepth(1);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        MapDocument? document = _model?.ShownDocument;
        if (document is not null && _renderer is not null)
        {
            if (!_placed)
            {
                _camera.Reset(document.Width, document.Height);
                _placed = true;
            }
            TimeSpan now = _clock.Elapsed;
            float seconds = (float)Math.Min(0.1, (now - _lastFrame).TotalSeconds);
            _lastFrame = now;
            if (_camera.Update(seconds))
            {
                RequestNextFrameRendering();
            }
            Workspace? workspace = _model?.Workspace;
            _renderer.Debug = _debug;
            try
            {
                _renderer.Render(document, _camera.ViewProjection((float)width / height), path => GameTextures.Find(path, document, workspace));
            }
            catch (InvalidOperationException exception)
            {
                Error = exception.Message;
            }
            WatchAssets(workspace);
        }

        if (_capture is { } capture && _capturePath is string path)
        {
            _capture = null;
            try
            {
                capture.TrySetResult(Save(width, height, path));
            }
            catch (Exception exception) when (exception is IOException or MagickException)
            {
                capture.TrySetException(exception);
            }
        }
    }

    private unsafe string Save(int width, int height, string path)
    {
        byte[] pixels = new byte[width * height * 4];
        _gl!.PixelStore(PixelStoreParameter.PackAlignment, 1);
        fixed (byte* data = pixels)
        {
            _gl.ReadPixels(0, 0, (uint)width, (uint)height, PixelFormat.Rgba, PixelType.UnsignedByte, data);
        }
        // OpenGL's first row is the bottom one
        byte[] flipped = new byte[pixels.Length];
        int row = width * 4;
        for (int y = 0; y < height; y++)
        {
            Array.Copy(pixels, (height - 1 - y) * row, flipped, y * row, row);
        }
        using MagickImage image = new MagickImage(flipped, new PixelReadSettings((uint)width, (uint)height, StorageType.Char, PixelMapping.RGBA));
        image.Alpha(AlphaOption.Off);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, image.ToByteArray(MagickFormat.Png));
        return path;
    }

    /// <summary>
    /// The blurred terrain the camera rests on, made again when the heights change; while they
    /// keep changing (a brush stroke) at most twice a second, as making it reads every sample.
    /// </summary>
    private SmoothGround Smooth()
    {
        Heightmap? heightmap = _model?.ShownDocument?.Terrain.Heightmap;
        if (heightmap is null)
        {
            return _smooth ??= SmoothGround.Flat(0);
        }
        if (!ReferenceEquals(heightmap, _smoothOf) && (_smooth is null || _smoothOf?.Width != heightmap.Width
            || _smoothOf.Height != heightmap.Height || _clock.Elapsed - _smoothMade > TimeSpan.FromSeconds(0.5)))
        {
            _smooth = SmoothGround.From(heightmap);
            _smoothOf = heightmap;
            _smoothMade = _clock.Elapsed;
        }
        return _smooth!;
    }

    private SmoothGround? _smooth;
    private Heightmap? _smoothOf;
    private TimeSpan _smoothMade;

    /// <summary>
    /// The height of the shown terrain, between its samples; outside the map that of its edge.
    /// </summary>
    private float GroundHeight(float x, float z)
    {
        Heightmap? heightmap = _model?.ShownDocument?.Terrain.Heightmap;
        if (heightmap is null)
        {
            return 0;
        }
        float cx = Math.Clamp(x, 0, heightmap.Width - 1);
        float cz = Math.Clamp(z, 0, heightmap.Height - 1);
        int x0 = (int)cx;
        int z0 = (int)cz;
        int x1 = Math.Min(x0 + 1, heightmap.Width - 1);
        int z1 = Math.Min(z0 + 1, heightmap.Height - 1);
        float fx = cx - x0;
        float fz = cz - z0;
        float top = heightmap.GetHeight(x0, z0) * (1 - fx) + heightmap.GetHeight(x1, z0) * fx;
        float bottom = heightmap.GetHeight(x0, z1) * (1 - fx) + heightmap.GetHeight(x1, z1) * fx;
        return top * (1 - fz) + bottom * fz;
    }

    /// <summary>
    /// Draws again when a texture of the workspace's assets changes on disk.
    /// </summary>
    private void WatchAssets(Workspace? workspace)
    {
        string? folder = workspace?.AssetsFolder;
        if (folder == _watched)
        {
            return;
        }
        _assetWatcher?.Dispose();
        _assetWatcher = null;
        _watched = folder;
        if (folder is not null && Directory.Exists(folder))
        {
            _assetWatcher = new ProjectWatcher(folder);
            _assetWatcher.Changed += _ => Dispatcher.UIThread.Post(RequestNextFrameRendering);
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_model is not null)
        {
            _model.MapChanged -= OnMapChanged;
            _model.DocumentChanged -= OnDocumentChanged;
        }
        _model = DataContext as MainViewModel;
        if (_model is not null)
        {
            _model.MapChanged += OnMapChanged;
            _model.DocumentChanged += OnDocumentChanged;
        }
        _placed = false;
        RequestNextFrameRendering();
    }

    private void OnMapChanged(object? sender, EventArgs e)
    {
        _placed = false;
        RequestNextFrameRendering();
    }

    private void OnDocumentChanged(object? sender, EventArgs e) => RequestNextFrameRendering();

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _assetWatcher?.Dispose();
        _assetWatcher = null;
        _watched = null;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        // space turns the camera while the mouse is over the view
        Focus();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        if (e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed)
        {
            _panFrom = e.GetPosition(this);
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _mouse = e.GetPosition(this);
        if (_panFrom is Point panFrom)
        {
            Vector delta = _mouse - panFrom;
            _panFrom = _mouse;
            _camera.Pan(new Vector2((float)delta.X, (float)delta.Y), (float)Bounds.Height);
            RequestNextFrameRendering();
        }
        if (_turning && _turnFrom is Point turnFrom)
        {
            Vector delta = _mouse - turnFrom;
            _turnFrom = _mouse;
            _camera.Rotate(new Vector2((float)delta.X, (float)delta.Y));
            RequestNextFrameRendering();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_panFrom is not null)
        {
            _panFrom = null;
            e.Pointer.Capture(null);
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        Point at = e.GetPosition(this);
        Vector2 mouse = new Vector2(
            (float)(2 * at.X / Math.Max(1, Bounds.Width) - 1),
            (float)(1 - 2 * at.Y / Math.Max(1, Bounds.Height)));
        int notches = Math.Sign(e.Delta.Y) * Math.Max(1, (int)Math.Round(Math.Abs(e.Delta.Y)));
        _camera.Zoom(mouse, (float)(Bounds.Width / Math.Max(1, Bounds.Height)), notches);
        RequestNextFrameRendering();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Space)
        {
            if (!_turning)
            {
                _turning = true;
                _turnFrom = _mouse;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Home && _model?.ShownDocument is MapDocument document)
        {
            _camera.Reset(document.Width, document.Height);
            RequestNextFrameRendering();
            e.Handled = true;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Key == Key.Space)
        {
            _turning = false;
            _turnFrom = null;
            e.Handled = true;
        }
    }

}
