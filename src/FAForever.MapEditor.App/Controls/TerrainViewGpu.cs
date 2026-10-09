using FAForever.MapEditor.App.Diagnostics;
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
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;
using ImageMagick;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Silk.NET.OpenGL;

namespace FAForever.MapEditor.App.Controls;

/// <summary>
/// The map in 3D on the graphics card (OpenGL, through ANGLE on Windows): the terrain with its
/// layers and decals, lit as in the game (<see cref="TerrainRenderer"/>). The camera works as the
/// FAF map editor's (<see cref="EditorCamera"/>): the wheel zooms towards the mouse, the middle
/// mouse button moves over the map, space and the mouse turn and tilt, F focuses on the place
/// under the mouse, Home shows the whole map. The left mouse button is left for editing. The
/// camera is the view model's, which the top view shows and moves too.
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

    // where each texture path comes from, as long as the map's files and the assets stay as they are:
    // looking a source up asks the file system, and a frame draws hundreds of decal runs
    private readonly Dictionary<string, TextureSource?> _sources = new Dictionary<string, TextureSource?>(StringComparer.OrdinalIgnoreCase);
    private object? _sourcesOf;

    private TextureSource? FindTexture(string path, MapDocument document, Workspace? workspace)
    {
        if (!ReferenceEquals(_sourcesOf, document.Files) || workspace?.AssetsFolder != _watched)
        {
            _sources.Clear();
            _sourcesOf = document.Files;
        }
        if (!_sources.TryGetValue(path, out TextureSource? source))
        {
            source = GameTextures.Find(path, document, workspace);
            _sources[path] = source;
        }
        return source;
    }
    private string? _watched;
    private TaskCompletionSource<string>? _capture;
    private string? _capturePath;
    private Vector2? _captureBrushAt;

    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private TimeSpan _lastFrame;
    private bool _turning;
    private Point? _panFrom;
    private Point? _turnFrom;
    private Point _mouse;
    private bool _mouseInside;
    private IMapTool? _pressed;

    private string? _loggedTextureError;

    public TerrainViewGpu()
    {
        Focusable = true;
    }

    private ILogger Log => _model?.Logger("Rendering") ?? NullLogger.Instance;

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
    /// The place of the terrain under the mouse, as "x 12, z 34, height 5.67", or empty; whenever the
    /// mouse or the camera moves.
    /// </summary>
    public event Action<string>? CursorMoved;

    /// <summary>
    /// Draws a frame and saves it as a PNG, for automation: the picture is on the graphics card,
    /// where a screenshot of the window does not reach.
    /// </summary>
    /// <param name="brushAt">Where the active tool's brush shows in that frame, in ogrids, as if the
    /// mouse pointed there; null for where the mouse is.</param>
    public Task<string> CaptureAsync(string path, Vector2? brushAt = null)
    {
        _capture?.TrySetCanceled();
        _capture = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _capturePath = Path.GetFullPath(path);
        _captureBrushAt = brushAt;
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
            Log.LogInformation("The 3D view runs on {Renderer}", Renderer);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Silk.NET.Core.Loader.SymbolLoadingException)
        {
            Error = exception.Message;
            _renderer = null;
            Log.LogError(exception, "The 3D view could not start on {Renderer}: {Reason}", Renderer, exception.Message);
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
        using Performance.Span measure = Performance.Measure("3d.frame");
        if (_gl is null)
        {
            return;
        }
        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        int width = Math.Max(1, (int)(Bounds.Width * scaling));
        int height = Math.Max(1, (int)(Bounds.Height * scaling));
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)fb);
        _gl.Viewport(0, 0, (uint)width, (uint)height);
        // the sky: the canvas of the views (Styles/Tokens.axaml)
        Avalonia.Media.Color sky = this.TryFindResource("CanvasBrush", ActualThemeVariant, out object? canvas) && canvas is Avalonia.Media.ISolidColorBrush brush
            ? brush.Color
            : default;
        _gl.ClearColor(sky.R / 255f, sky.G / 255f, sky.B / 255f, 1);
        _gl.ClearDepth(1);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        MapDocument? document = _model?.ShownDocument;
        if (document is not null && _renderer is not null)
        {
            EditorCamera camera = _model!.Camera;
            camera.Aspect = (float)width / height;
            TimeSpan now = _clock.Elapsed;
            float seconds = (float)Math.Min(0.1, (now - _lastFrame).TotalSeconds);
            _lastFrame = now;
            if (camera.Update(seconds))
            {
                RequestNextFrameRendering();
                _model.NotifyCameraMoved();
                ReportCursor();
            }
            Workspace? workspace = _model?.Setup.Workspace;
            _renderer.Debug = _debug;
            try
            {
                _renderer.Render(document, camera.ViewProjection((float)width / height), path =>
                {
                    using Performance.Span lookup = Performance.Measure("3d.find-texture");
                    return FindTexture(path, document, workspace);
                }, BrushUnderMouse(document), BrushColor());
            }
            catch (InvalidOperationException exception)
            {
                if (exception.Message != Error)
                {
                    Log.LogError(exception, "The 3D view could not draw: {Reason}", exception.Message);
                }
                Error = exception.Message;
            }
            if (_renderer.TextureState.Error is string textureError && textureError != _loggedTextureError)
            {
                _loggedTextureError = textureError;
                Log.LogWarning("A texture could not be read: {Reason}", textureError);
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
    /// A point of the view in normalized device coordinates: -1 to 1, y up.
    /// </summary>
    private Vector2 InView(Point at) => new Vector2(
        (float)(2 * at.X / Math.Max(1, Bounds.Width) - 1),
        (float)(1 - 2 * at.Y / Math.Max(1, Bounds.Height)));

    /// <summary>
    /// The place of the map under the mouse, in ogrids; null when the mouse is outside the view or
    /// the map.
    /// </summary>
    private Vector3? PlaceUnderMouse(MapDocument document) =>
        _mouseInside && _model?.Camera.PointAt(InView(_mouse)) is Vector3 place
            && place.X >= 0 && place.Z >= 0 && place.X <= document.Width && place.Z <= document.Height
            ? place
            : null;

    /// <summary>
    /// The active tool's brush where the mouse points, as the top view shows it; null without one.
    /// </summary>
    private BrushPreview? BrushUnderMouse(MapDocument document)
    {
        using Performance.Span measure = Performance.Measure("3d.brush-place");
        if (_model?.ActiveTool?.Tool is not { CursorRadius: > 0 } tool)
        {
            return null;
        }
        if (_capture is not null && _captureBrushAt is Vector2 at)
        {
            return BrushPreview.Of(tool, at.X, at.Y);
        }
        return PlaceUnderMouse(document) is Vector3 place ? BrushPreview.Of(tool, place.X, place.Z) : null;
    }

    private Vector3 BrushColor() =>
        this.TryFindResource("OverlayBrush", ActualThemeVariant, out object? overlay) && overlay is Avalonia.Media.ISolidColorBrush brush
            ? new Vector3(brush.Color.R / 255f, brush.Color.G / 255f, brush.Color.B / 255f)
            : Vector3.One;

    private void ReportCursor()
    {
        using Performance.Span measure = Performance.Measure("3d.cursor");
        if (_model?.ShownDocument is not MapDocument document || !_mouseInside)
        {
            return;
        }
        CursorMoved?.Invoke(PlaceUnderMouse(document) is Vector3 place
            ? $"x {place.X:0}, z {place.Z:0}, height {place.Y:0.00}"
            : "");
    }

    /// <summary>
    /// The camera moved: draw again, and let the top view know.
    /// </summary>
    private void Moved()
    {
        RequestNextFrameRendering();
        _model?.NotifyCameraMoved();
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
            _assetWatcher.Changed += _ => Dispatcher.UIThread.Post(() =>
            {
                // look the textures up again: their versions changed
                _sources.Clear();
                RequestNextFrameRendering();
            });
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_model is not null)
        {
            _model.MapChanged -= OnMapChanged;
            _model.DocumentChanged -= OnDocumentChanged;
            _model.CursorChanged -= OnDocumentChanged;
            _model.CameraMoved -= OnCameraMoved;
        }
        _model = DataContext as MainViewModel;
        if (_model is not null)
        {
            _model.MapChanged += OnMapChanged;
            _model.DocumentChanged += OnDocumentChanged;
            _model.CameraMoved += OnCameraMoved;
            _model.CursorChanged += OnDocumentChanged;
        }
        RequestNextFrameRendering();
    }

    private void OnMapChanged(object? sender, EventArgs e) => RequestNextFrameRendering();

    // sent somewhere by the top view or automation: glide there
    private void OnCameraMoved(object? sender, EventArgs e) => RequestNextFrameRendering();

    private void OnDocumentChanged(object? sender, EventArgs e) => RequestNextFrameRendering();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // shown again: the document may have changed meanwhile
        if (change.Property == IsVisibleProperty && IsVisible)
        {
            RequestNextFrameRendering();
        }
    }

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
        _mouseInside = true;
        // space turns the camera while the mouse is over the view
        Focus();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _mouseInside = false;
        CursorMoved?.Invoke("");
        RequestNextFrameRendering();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        PointerPointProperties buttons = e.GetCurrentPoint(this).Properties;
        if (buttons.IsMiddleButtonPressed)
        {
            _panFrom = e.GetPosition(this);
            e.Pointer.Capture(this);
            e.Handled = true;
        }
        else if (buttons.IsLeftButtonPressed && _model is { ShownDocument: MapDocument document, ActiveTool.Tool: IMapTool tool })
        {
            // the left mouse is the active tool's, on the place of the terrain it points at
            _mouse = e.GetPosition(this);
            _mouseInside = true;
            if (PlaceUnderMouse(document) is Vector3 place)
            {
                _pressed = tool;
                tool.Press(place.X, place.Z);
                e.Pointer.Capture(this);
            }
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
            _model?.Camera.Pan(new Vector2((float)delta.X, (float)delta.Y), (float)Bounds.Height);
            Moved();
        }
        if (_turning && _turnFrom is Point turnFrom)
        {
            Vector delta = _mouse - turnFrom;
            _turnFrom = _mouse;
            _model?.Camera.Rotate(new Vector2((float)delta.X, (float)delta.Y));
            Moved();
        }
        if (_pressed is IMapTool tool && _model?.ShownDocument is MapDocument document && PlaceUnderMouse(document) is Vector3 place)
        {
            tool.Drag(place.X, place.Z);
        }
        if (_model?.ActiveTool?.Tool is { CursorRadius: > 0 })
        {
            // the brush follows the mouse
            RequestNextFrameRendering();
        }
        ReportCursor();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_panFrom is not null)
        {
            _panFrom = null;
            e.Pointer.Capture(null);
        }
        if (_pressed is IMapTool tool)
        {
            _pressed = null;
            tool.Release();
            e.Pointer.Capture(null);
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        int notches = Math.Sign(e.Delta.Y) * Math.Max(1, (int)Math.Round(Math.Abs(e.Delta.Y)));
        _model?.Camera.Zoom(InView(e.GetPosition(this)), (float)(Bounds.Width / Math.Max(1, Bounds.Height)), notches);
        Moved();
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
        else if (e.Key == Key.Home && _model?.ShownDocument is not null)
        {
            // as the command camera.home
            _model.ResetCamera();
            Moved();
            e.Handled = true;
        }
        else if (e.Key == Key.F && _model?.Camera.PointAt(InView(_mouse)) is Vector3 place)
        {
            // the FAF map editor focuses on the selection; with none, on what the mouse points at, half as far
            _model.FocusCamera(place.X, place.Z, _model.Camera.Now.Distance * 0.26f);
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
