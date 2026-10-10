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
using FAForever.MapEditor.App.Rendering;
using FAForever.MapEditor.App.Rendering.Gpu;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;
using ImageMagick;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Silk.NET.OpenGL;
using FAForever.MapEditor.Project.Editing;

namespace FAForever.MapEditor.App.Controls;

/// <summary>
/// The map in 3D on the graphics card (OpenGL, through ANGLE on Windows): the terrain with its
/// layers and decals, lit as in the game (<see cref="TerrainRenderer"/>), with the props, units and
/// markers (<see cref="ObjectRenderer"/>; <see cref="PickAt"/> finds the one under a point). The camera works as the
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
    private ObjectRenderer? _objects;
    private ShapeRenderer? _shapes;
    // X and Z held: moves go along one axis
    private ToolModifiers _axes;
    // the mouse moved since the thing under it was picked
    private bool _hoverStale;
    private TimeSpan _lastHoverPick;
    private readonly ObjectModels _models;
    private IReadOnlySet<ObjectRef>? _highlighted;
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
    private string? _loggedModelError;

    private GpuTimer? _timer;

    public TerrainViewGpu()
    {
        Focusable = true;
        // an asset dragged from the shelf lands on the place of the terrain under the mouse
        AssetDrag.Attach(this, () => _model, at =>
            _model?.ShownDocument is MapDocument document && _model.Camera.PointAt(InView(at)) is Vector3 place
                && place.X >= 0 && place.Z >= 0 && place.X <= document.Width && place.Z <= document.Height
                ? new Point(place.X, place.Z)
                : null,
            text => CursorMoved?.Invoke(text));
        _models = new ObjectModels(() => Dispatcher.UIThread.Post(RequestNextFrameRendering));
    }

    /// <summary>
    /// The objects drawn highlighted (props, units and markers): the selection, set by whoever
    /// keeps it; null or empty for none.
    /// </summary>
    public IReadOnlySet<ObjectRef>? Highlighted
    {
        get => _highlighted;
        set
        {
            _highlighted = value;
            RequestNextFrameRendering();
        }
    }

    /// <summary>
    /// The props, units and markers drawn in the last frame, the draws they took, the models on
    /// the card, the models read and being read, the blueprints without a model (drawn as a box),
    /// and the triangles of the models.
    /// </summary>
    public (int Props, int Units, int Markers, int Draws, int Meshes, int Missing, int ModelsRead, int ModelsLoading, long Triangles) ObjectState
    {
        get
        {
            (int props, int units, int markers, int draws, int meshes, int missing, long triangles) = _objects?.State ?? default;
            return (props, units, markers, draws, meshes, missing, _models.Loaded, _models.Loading, triangles);
        }
    }

    /// <summary>
    /// The object under a point of the view (in its own coordinates, as the mouse's), among the
    /// kinds it shows, or null: the nearest prop, unit or marker the ray from the camera meets in
    /// front of the terrain, else the decal on top there (<see cref="ObjectPicking"/>).
    /// </summary>
    /// <param name="kinds">The kinds it may pick, of those shown; null for all of them.</param>
    public ObjectRef? PickAt(Point at, ObjectKinds? kinds = null)
    {
        using Performance.Span measure = Performance.Measure("3d.pick");
        if (_model?.ShownDocument is not MapDocument document)
        {
            return null;
        }
        EditorCamera camera = _model.Camera;
        Vector2 view = InView(at);
        (Vector3 origin, Vector3 direction) = camera.RayAt(view);
        float? terrain = camera.PointAt(view) is Vector3 ground ? Vector3.Distance(origin, ground) / direction.Length() : null;
        ObjectKinds chosen = kinds ?? ObjectKinds.All;
        PickOptions shown = new PickOptions(
            _model.ShowProps && chosen.HasFlag(ObjectKinds.Props),
            _model.ShowUnits && chosen.HasFlag(ObjectKinds.Units),
            _model.ShowMarkers && chosen.HasFlag(ObjectKinds.Markers),
            _model.ShowPathNodes && chosen.HasFlag(ObjectKinds.PathNodes),
            chosen.HasFlag(ObjectKinds.Decals),
            _model.Decals.HiddenDecals,
            _model.ShownMarkerKinds);
        return ObjectPicking.Pick(document, origin, direction, shown, _models.BoundsOf, terrain);
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
    /// The textures of the terrain, decals and models being read, not on the card yet; for
    /// automation, which waits for them before a settled screenshot.
    /// </summary>
    public int TexturesLoading => (_renderer?.TexturesLoading ?? 0) + (_objects?.TexturesLoading ?? 0);

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
            _objects = new ObjectRenderer(_gl, es, _models, () => Dispatcher.UIThread.Post(RequestNextFrameRendering));
            _shapes = new ShapeRenderer(_gl, es);
            _timer = new GpuTimer(_gl, GpuTimer.Offered(_gl, es, GlVersion.Major, GlVersion.Minor), es);
            Log.LogInformation("The 3D view times frames on the card with {Method}", _timer.HasQueries ? "timer queries" : "glFinish (no timer queries)");
            Error = null;
            Log.LogInformation("The 3D view runs on {Renderer}", Renderer);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Silk.NET.Core.Loader.SymbolLoadingException)
        {
            Error = exception.Message;
            _renderer = null;
            _objects?.Dispose();
            _objects = null;
            Log.LogError(exception, "The 3D view could not start on {Renderer}: {Reason}", Renderer, exception.Message);
        }
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        _timer?.Dispose();
        _timer = null;
        _renderer?.Dispose();
        _renderer = null;
        _objects?.Dispose();
        _objects = null;
        _shapes?.Dispose();
        _shapes = null;
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
        _timer?.Begin();
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
            UpdateHover();
            try
            {
                _renderer.Render(document, camera.ViewProjection((float)width / height), path =>
                {
                    using Performance.Span lookup = Performance.Measure("3d.find-texture");
                    return FindTexture(path, document, workspace);
                }, BrushUnderMouse(document), BrushColor(), Objects(camera), Decals(), Overlays(document));
                _shapes?.Draw(ToolShapes(document), new Vector2((float)Bounds.Width, (float)Bounds.Height), Ink);
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
            if (_models.LastError is string modelError && modelError != _loggedModelError)
            {
                _loggedModelError = modelError;
                Log.LogWarning("A model could not be read; it shows as a box: {Reason}", modelError);
            }
            WatchAssets(workspace);
        }

        _timer?.End();
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
    /// The active tool's brush where the mouse points, as the top view shows it, with its images in
    /// the other parts while the symmetry mirrors edits (F-12); null without one.
    /// </summary>
    private IReadOnlyList<BrushPreview>? BrushUnderMouse(MapDocument document)
    {
        using Performance.Span measure = Performance.Measure("3d.brush-place");
        if (_model?.ActiveTool?.Tool is not { CursorRadius: > 0 } tool)
        {
            return null;
        }
        if (_capture is not null && _captureBrushAt is Vector2 at)
        {
            return BrushPreview.Of(tool, at.X, at.Y)?.Images(_model!.EditTransforms(document));
        }
        return PlaceUnderMouse(document) is Vector3 place ? BrushPreview.Of(tool, place.X, place.Z)?.Images(_model!.EditTransforms(document)) : null;
    }

    private Vector3 BrushColor() => ThemeColor("OverlayBrush");

    /// <summary>
    /// The decals of hidden groups, not drawn, and the selected decals, drawn highlighted.
    /// </summary>
    private (IReadOnlySet<int>?, IReadOnlySet<int>?, Vector3)? Decals()
    {
        if (_model is null)
        {
            return null;
        }
        if (!ReferenceEquals(_highlighted, _highlightedDecalsOf))
        {
            _highlightedDecalsOf = _highlighted;
            _highlightedDecals = _highlighted?.Where(thing => thing.Kind == ObjectKind.Decal).Select(thing => thing.Id).ToHashSet() is { Count: > 0 } ids ? ids : null;
        }
        return (_model.Decals.HiddenDecals, _highlightedDecals, ThemeColor("SelectedStrokeBrush"));
    }

    private IReadOnlySet<ObjectRef>? _highlightedDecalsOf;
    private IReadOnlySet<int>? _highlightedDecals;

    private Vector3 ThemeColor(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out object? token) && token is Avalonia.Media.ISolidColorBrush brush
            ? new Vector3(brush.Color.R / 255f, brush.Color.G / 255f, brush.Color.B / 255f)
            : Vector3.One;

    /// <summary>
    /// The objects to draw with the terrain and what of them shows (the overlays); null when the
    /// renderer could not start or the workspace's assets are not read yet.
    /// </summary>
    private (ObjectRenderer, ObjectFrame)? Objects(EditorCamera camera)
    {
        if (_objects is null || _model is null)
        {
            return null;
        }
        ObjectFrame frame = new ObjectFrame(_model.Assets.Catalog, _model.ShowProps, _model.ShowUnits, _model.ShowMarkers, _model.ShowPathNodes,
            _highlighted, camera.Eye, ThemeColor("SelectedStrokeBrush"), ThemeColor("WarningBrush"), _model.ShownMarkerKinds, _model.Selection.Hovered);
        return (_objects, frame);
    }

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
            _model.Selection.Changed -= OnSelectionChanged;
            _model.CameraMoved -= OnCameraMoved;
            _model.OverlaysChanged -= OnDocumentChanged;
        }
        _model = DataContext as MainViewModel;
        if (_model is not null)
        {
            _model.MapChanged += OnMapChanged;
            _model.DocumentChanged += OnDocumentChanged;
            _model.CameraMoved += OnCameraMoved;
            _model.CursorChanged += OnDocumentChanged;
            _model.Selection.Changed += OnSelectionChanged;
            _model.OverlaysChanged += OnDocumentChanged;
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
        _hoverStale = true;
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
            if ((PlaceUnderMouse(document) ?? PlaneUnderMouse()) is Vector3 place)
            {
                _pressed = tool;
                // the thing under the mouse is the one the ray meets, not the nearest on the ground
                tool.Press(place.X, place.Z, InputAt(place, e.KeyModifiers) with { Picks = true, Picked = PickAt(_mouse, _model.SelectableKinds) });
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
        if (_pressed is IMapTool tool && _model?.ShownDocument is MapDocument document && (PlaceUnderMouse(document) ?? PlaneUnderMouse()) is Vector3 place)
        {
            tool.Drag(place.X, place.Z, InputAt(place, e.KeyModifiers));
        }
        if (_model?.ActiveTool?.Tool is { CursorRadius: > 0 } or SelectTool or DecalSelectTool or AreaTool or SunTool)
        {
            // the brush follows the mouse; the handle and the thing under it light up
            _hoverStale = true;
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
        // control and the wheel: the brush's radius (shift too: its strength)
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && _model?.BrushWheel(Math.Sign(e.Delta.Y) * Math.Max(1, (int)Math.Round(Math.Abs(e.Delta.Y))), e.KeyModifiers.HasFlag(KeyModifiers.Shift)) == true)
        {
            e.Handled = true;
            return;
        }
        int notches = Math.Sign(e.Delta.Y) * Math.Max(1, (int)Math.Round(Math.Abs(e.Delta.Y)));
        _model?.Camera.Zoom(InView(e.GetPosition(this)), (float)(Bounds.Width / Math.Max(1, Bounds.Height)), notches);
        Moved();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        _axes |= HeightmapView.AxisOf(e.Key);
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
        else if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.None && _model?.FocusOnSelection() == true)
        {
            // as the FAF map editor: on the selection
            e.Handled = true;
        }
        else if (e.Key == Key.F && _model?.Camera.PointAt(InView(_mouse)) is Vector3 place)
        {
            // with nothing selected, on what the mouse points at, half as far
            _model.FocusCamera(place.X, place.Z, _model.Camera.Now.Distance * 0.26f);
            e.Handled = true;
        }
        // the keys the view does not use itself are the active tool's (the select tool's Q, E, arrows, Escape)
        else if (_model?.ToolKey(e.Key.ToString(), HeightmapView.ModifiersOf(e.KeyModifiers)) == true)
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// What a tool needs to know with a place: the keys held, and how large a pixel of the view is
    /// on the ground there.
    /// </summary>
    private ToolInput InputAt(Vector3 place, KeyModifiers modifiers)
    {
        float distance = _model is null ? 1 : Vector3.Distance(_model.Camera.Eye, place);
        float ogridsPerPixel = 2 * distance * MathF.Tan(EditorCamera.FieldOfView / 2) / (float)Math.Max(1, Bounds.Height);
        return new ToolInput(HeightmapView.ModifiersOf(modifiers) | _axes, ogridsPerPixel, View: ToolViewOf());
    }

    /// <summary>
    /// The place on the level plane at height 0 the mouse points at, for a press or drag where the
    /// mouse points at no terrain (the sky, beyond the map): a handle may be dragged there.
    /// </summary>
    private Vector3? PlaneUnderMouse()
    {
        if (_model is null)
        {
            return null;
        }
        (Vector3 origin, Vector3 direction) = _model.Camera.RayAt(InView(_mouse));
        // pointing at the sky: the place under the eye, the ray goes with the press
        Vector2 hit = HandleMaths.PlaneHit(origin, direction, 0) ?? new Vector2(origin.X, origin.Z);
        return new Vector3(hit.X, 0, hit.Y);
    }

    /// <summary>
    /// The view as a tool sees it: where a place shows in the view's pixels, how large a pixel is
    /// on the map at a place, the mouse and the ray under it.
    /// </summary>
    public ToolView ToolViewOf()
    {
        EditorCamera camera = _model?.Camera ?? new EditorCamera((_, _, _) => 0, (_, _) => 0);
        Size size = Bounds.Size;
        float height = (float)Math.Max(1, size.Height);
        Vector2? mouse = _mouseInside ? new Vector2((float)_mouse.X, (float)_mouse.Y) : null;
        // the camera's matrix once for this view (a box selection places thousands of things with it)
        float width = (float)size.Width;
        Matrix4x4 viewProjection = camera.ViewProjection(width / height);
        Vector2? ToScreen(Vector3 place)
        {
            if (size.Width <= 0 || size.Height <= 0)
            {
                return null;
            }
            Vector4 clip = Vector4.Transform(new Vector4(place, 1), viewProjection);
            return clip.W <= 1e-6f || !float.IsFinite(clip.X / clip.W) || !float.IsFinite(clip.Y / clip.W)
                ? null
                : new Vector2((clip.X / clip.W + 1) / 2 * width, (1 - clip.Y / clip.W) / 2 * (float)size.Height);
        }
        return new ToolView(false, mouse, ToScreen,
            place => 2 * Vector3.Distance(camera.Eye, place) * MathF.Tan(EditorCamera.FieldOfView / 2) / height,
            camera.RayAt(InView(_mouse)), camera.Eye);
    }

    /// <summary>
    /// What the 3D view draws at its size on screen: the links between path nodes while they show,
    /// and what the active tool draws: its shapes (the handles of the selection, the sun's arrow,
    /// the box), and its marks (the area tool's handles, the ruler's line, a curve) at the terrain's
    /// height.
    /// </summary>
    private IReadOnlyList<ToolShape> ToolShapes(MapDocument document)
    {
        if (_model is null)
        {
            return [];
        }
        using Performance.Span measure = Performance.Measure("3d.tool-shapes");
        ToolView view = ToolViewOf();
        List<ToolShape> shapes = [];
        if ((_model.ShownMarkerKinds & MarkerKinds.PathNodes) != 0)
        {
            PathLinks(document, view, shapes);
        }
        if (_model.ActiveTool?.Tool is not IMapTool tool)
        {
            return shapes;
        }
        if (tool.Marks is ToolMarks marks && tool is not SunTool)
        {
            Heightmap heights = document.Terrain.Heightmap;
            Vector2? At(float x, float z) => view.ToScreen(new Vector3(x, heights.HeightAt(x, z), z));
            if (marks.Path is { Count: >= 2 } path && TerrainLine(heights, view, path) is Vector2[] line)
            {
                // a curve on the terrain (T-4)
                shapes.Add(new ToolLine(line, false, 4, ToolInk.Shadow));
                shapes.Add(new ToolLine(line, false, 2, ToolInk.Selection));
            }
            if (marks.Line is (float x0, float z0, float x1, float z1) && At(x0, z0) is Vector2 from && At(x1, z1) is Vector2 to)
            {
                shapes.Add(new ToolLine([from, to], false, 4, ToolInk.Shadow));
                shapes.Add(new ToolLine([from, to], false, 2, ToolInk.Selection));
            }
            foreach ((float x, float z) in marks.Handles)
            {
                if (At(x, z) is Vector2 at)
                {
                    shapes.Add(new ToolSquare(at, 4, ToolInk.Fill, ToolInk.Selection));
                }
            }
        }
        shapes.AddRange(tool.Shapes(view));
        return shapes;
    }

    /// <summary>
    /// A line through places of the map on the terrain, in the view's pixels: each part cut into
    /// pieces of at most two ogrids, so that it follows the ground; null when a piece lies behind
    /// the camera.
    /// </summary>
    private static Vector2[]? TerrainLine(Heightmap heights, ToolView view, IReadOnlyList<(float X, float Z)> path)
    {
        List<Vector2> points = [];
        for (int i = 0; i < path.Count; i++)
        {
            (float x1, float z1) = path[i];
            int pieces = 1;
            if (i > 0)
            {
                (float x0, float z0) = path[i - 1];
                pieces = Math.Clamp((int)MathF.Ceiling(MathF.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0)) / 2), 1, 64);
                for (int piece = 1; piece < pieces; piece++)
                {
                    float t = (float)piece / pieces;
                    float x = x0 + (x1 - x0) * t;
                    float z = z0 + (z1 - z0) * t;
                    if (view.ToScreen(new Vector3(x, heights.HeightAt(x, z), z)) is not Vector2 between)
                    {
                        return null;
                    }
                    points.Add(between);
                }
            }
            if (view.ToScreen(new Vector3(x1, heights.HeightAt(x1, z1), z1)) is not Vector2 at)
            {
                return null;
            }
            points.Add(at);
        }
        return [.. points];
    }

    private MarkerSet? _linked;
    private List<(PathLayer Layer, Vector3 From, Vector3 To, Vector4 Color)> _links = [];

    /// <summary>
    /// The links between path nodes of the layers that show, each a line in its layer's colour from
    /// a little above one node to a little above the other (as the top view draws them); the pairs
    /// are made once per version of the markers, placed on screen every frame.
    /// </summary>
    private void PathLinks(MapDocument document, ToolView view, List<ToolShape> shapes)
    {
        using Performance.Span measure = Performance.Measure("3d.path-links");
        MarkerSet markers = document.Markers;
        if (!ReferenceEquals(markers, _linked))
        {
            _linked = markers;
            _links = [];
            Dictionary<int, PathNode> nodes = markers.PathNodes.ToDictionary(node => node.Id);
            Vector3 lift = new Vector3(0, 0.5f, 0);
            foreach (PathNode node in markers.PathNodes)
            {
                foreach (int id in node.AdjacentTo)
                {
                    // a link both ways once
                    if (nodes.TryGetValue(id, out PathNode? other) && (id > node.Id || !other.AdjacentTo.Contains(node.Id)))
                    {
                        Vector4 color = new Vector4(node.Color.Red / 255f, node.Color.Green / 255f, node.Color.Blue / 255f, 0.85f);
                        _links.Add((node.Layer, node.Position + lift, other.Position + lift, color));
                    }
                }
            }
        }
        // placed with the camera's matrix made once for the frame (thousands of links on a large map)
        MarkerKinds shown = _model!.ShownMarkerKinds;
        float width = (float)Bounds.Width;
        float height = (float)Bounds.Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }
        Matrix4x4 viewProjection = _model.Camera.ViewProjection(width / height);
        Vector2? OnScreen(Vector3 place)
        {
            Vector4 clip = Vector4.Transform(new Vector4(place, 1), viewProjection);
            return clip.W <= 1e-6f ? null : new Vector2((clip.X / clip.W + 1) / 2 * width, (1 - clip.Y / clip.W) / 2 * height);
        }
        bool Outside(Vector2 a, Vector2 b) =>
            (a.X < 0 && b.X < 0) || (a.Y < 0 && b.Y < 0) || (a.X > width && b.X > width) || (a.Y > height && b.Y > height);
        foreach ((PathLayer layer, Vector3 from, Vector3 to, Vector4 color) in _links)
        {
            if ((shown & MarkerEdits.KindOf(layer)) != 0 && OnScreen(from) is Vector2 a && OnScreen(to) is Vector2 b && !Outside(a, b))
            {
                shapes.Add(new ToolMapLine(a, b, 2, color));
            }
        }
    }

    private Vector4 Ink(ToolInk ink)
    {
        string key = ink switch
        {
            ToolInk.Selection => "MapSelectionBrush",
            ToolInk.Fill => "CanvasBrush",
            ToolInk.Hover => "OverlayBrush",
            ToolInk.AxisX => "MapHandleAxisXBrush",
            ToolInk.AxisZ => "MapHandleAxisZBrush",
            ToolInk.Shadow => "OverlayShadowBrush",
            _ => "WarningBrush",
        };
        return ThemeColorWithAlpha(key);
    }

    private Vector4 ThemeColorWithAlpha(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out object? token) && token is Avalonia.Media.ISolidColorBrush brush
            ? new Vector4(brush.Color.R / 255f, brush.Color.G / 255f, brush.Color.B / 255f, brush.Color.A / 255f * (float)brush.Opacity)
            : Vector4.One;

    /// <summary>
    /// What is under the mouse, while a select tool is active: picked again once a frame after the
    /// mouse moved, for the light highlight and automation.
    /// </summary>
    private void UpdateHover()
    {
        if (_model is null || !_hoverStale)
        {
            return;
        }
        // a pick costs milliseconds on a map of thousands of props: at most every 60 ms, the last place
        if (_clock.Elapsed - _lastHoverPick < TimeSpan.FromMilliseconds(60))
        {
            RequestNextFrameRendering();
            return;
        }
        _lastHoverPick = _clock.Elapsed;
        _hoverStale = false;
        bool selecting = _model.ActiveTool?.Tool is SelectTool or DecalSelectTool && _pressed is null;
        _model.Selection.Hovered = selecting && _mouseInside ? PickAt(_mouse, _model.SelectableKinds) : null;
    }

    /// <summary>
    /// What lies over the terrain: the areas (the playable one marked, the selected and hovered
    /// ones in their colours), walkability, the grid being painted or the mask for tools, and the
    /// symmetry's guides, each while its overlay is on; null for nothing.
    /// </summary>
    private TerrainOverlays? Overlays(MapDocument document)
    {
        if (_model is not MainViewModel model)
        {
            return null;
        }
        using Performance.Span measure = Performance.Measure("3d.overlays");
        Func<uint[]>? walk = null;
        object? walkOf = null;
        int walkWidth = 0;
        int walkHeight = 0;
        if (model.ShowWalkability && model.Walkability.Grid is FAForever.FileFormats.Map.NavGrid grid)
        {
            Project.Generation.WalkabilityChanges? changes = model.Walkability.ShowChanges && model.Walkability.Changes is { } shown && shown.Width == grid.Width && shown.Height == grid.Height ? shown : null;
            Vector4 blocked = ThemeColorWithAlpha("MapBlockedBrush");
            Vector4 newly = ThemeColorWithAlpha("MapNewlyBlockedBrush");
            Vector4 opened = ThemeColorWithAlpha("MapOpenedBrush");
            walkOf = (grid, changes, blocked, newly, opened);
            walk = () => WalkabilityPixels.Of(grid, changes, blocked, newly, opened);
            walkWidth = grid.Width;
            walkHeight = grid.Height;
        }
        Rendering.GridOverlay? shownGrid = model.ShownGrid;
        TiledBuffer<byte>? painted = shownGrid?.Select(document);
        IReadOnlyList<Area>? areas = model.ShowAreas ? document.Markers.Areas : null;
        float[] states = [];
        if (areas is not null)
        {
            states = new float[areas.Count];
            for (int i = 0; i < areas.Count; i++)
            {
                ObjectRef area = ObjectRef.Area(areas[i].Name);
                states[i] = model.Selection.Contains(area) ? 1 : model.Selection.Hovered == area ? 2 : 0;
            }
        }
        IReadOnlyList<(Vector2 From, Vector2 To)> guides = model is { ShowSymmetry: true, Symmetry.IsOn: true } ? model.Symmetry.Guides(document) : [];
        Vector4 hover = ThemeColorWithAlpha("SelectedStrokeBrush");
        TerrainOverlays overlays = new TerrainOverlays(walkOf, walk, walkWidth, walkHeight, painted, shownGrid?.Palette, ThemeColorWithAlpha("MapMaskBrush"),
            areas, states, guides, ThemeColorWithAlpha("MapAreaBrush"), ThemeColorWithAlpha("MapSelectionBrush"), hover,
            ThemeColorWithAlpha("MapOutsidePlayableBrush"), ThemeColorWithAlpha("OverlayStrokeBrush"), ThemeColorWithAlpha("OverlayShadowBrush"),
            model.Camera.Eye, 2 * MathF.Tan(EditorCamera.FieldOfView / 2) / (float)Math.Max(1, Bounds.Height));
        return overlays.IsEmpty ? null : overlays;
    }

    /// <summary>
    /// The selection drawn highlighted.
    /// </summary>
    private void OnSelectionChanged(object? sender, EventArgs e) =>
        Highlighted = _model is { Selection.IsEmpty: false } ? _model.Selection.Items.ToHashSet() : null;

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        _axes = ToolModifiers.None;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        _axes &= ~HeightmapView.AxisOf(e.Key);
        if (e.Key == Key.Space)
        {
            _turning = false;
            _turnFrom = null;
            e.Handled = true;
        }
    }

}
