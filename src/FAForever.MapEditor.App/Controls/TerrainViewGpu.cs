using System.Numerics;
using Vector = Avalonia.Vector;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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
/// layers and decals, lit as in the game (<see cref="TerrainRenderer"/>). The camera works as in
/// the software view: the left mouse button turns it around a point of the map, the right or
/// middle one moves it, the wheel moves it closer.
/// </summary>
/// <remarks>
/// It draws again whenever the document changes (an edit, an undo, a project file read again) and
/// whenever a texture of the workspace's assets changes on disk: a texture a tool saves shows a
/// moment later. Each texture file is on the card once, however many decals use it.
/// </remarks>
public sealed class TerrainViewGpu : OpenGlControlBase
{
    private const float FieldOfView = 60 * MathF.PI / 180;

    private GL? _gl;
    private TerrainRenderer? _renderer;
    private MainViewModel? _model;
    private ProjectWatcher? _assetWatcher;
    private string? _watched;
    private TaskCompletionSource<string>? _capture;
    private string? _capturePath;

    private double _targetX;
    private double _targetZ;
    private double _yaw = Math.PI / 4;
    private double _pitch = 40 * Math.PI / 180;
    private double _distance = 300;
    private bool _placed;
    private Point? _turnFrom;
    private Point? _moveFrom;

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

    public (double X, double Z, double Distance, double Yaw, double Pitch) Camera =>
        (_targetX, _targetZ, _distance, _yaw * 180 / Math.PI, _pitch * 180 / Math.PI);

    /// <summary>
    /// Points the camera; null keeps a value as it is.
    /// </summary>
    public void Look(double? x = null, double? z = null, double? distance = null, double? yaw = null, double? pitch = null)
    {
        _targetX = x ?? _targetX;
        _targetZ = z ?? _targetZ;
        _distance = Math.Clamp(distance ?? _distance, 5, 20000);
        _yaw = yaw is double degrees ? degrees * Math.PI / 180 : _yaw;
        _pitch = Math.Clamp(pitch is double tilt ? tilt * Math.PI / 180 : _pitch, 5 * Math.PI / 180, 89 * Math.PI / 180);
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
                _targetX = document.Width / 2.0;
                _targetZ = document.Height / 2.0;
                _distance = Math.Max(document.Width, document.Height) * 1.1;
                _placed = true;
            }
            Workspace? workspace = _model?.Workspace;
            _renderer.Debug = _debug;
            try
            {
                _renderer.Render(document, ViewProjection(document, (float)width / height), path => GameTextures.Find(path, document, workspace));
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
    /// The camera hangs over the point it looks at, behind it as seen along the yaw.
    /// </summary>
    private Matrix4x4 ViewProjection(MapDocument document, float aspect)
    {
        Heightmap heightmap = document.Terrain.Heightmap;
        int sampleX = Math.Clamp((int)_targetX, 0, heightmap.Width - 1);
        int sampleZ = Math.Clamp((int)_targetZ, 0, heightmap.Height - 1);
        float targetY = heightmap.GetHeight(sampleX, sampleZ);
        (double sinYaw, double cosYaw) = Math.SinCos(_yaw);
        (double sinPitch, double cosPitch) = Math.SinCos(_pitch);
        Vector3 target = new Vector3((float)_targetX, targetY, (float)_targetZ);
        Vector3 eye = target + new Vector3((float)(sinYaw * cosPitch), (float)sinPitch, (float)(cosYaw * cosPitch)) * (float)_distance;

        float size = Math.Max(document.Width, document.Height);
        float near = Math.Max(0.5f, (float)_distance * 0.01f);
        float far = (float)_distance * 4 + size * 2;
        return Matrix4x4.CreateLookAt(eye, target, Vector3.UnitY) * Matrix4x4.CreatePerspectiveFieldOfView(FieldOfView, aspect, near, far);
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

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        PointerPointProperties buttons = e.GetCurrentPoint(this).Properties;
        Point at = e.GetPosition(this);
        if (buttons.IsLeftButtonPressed)
        {
            _turnFrom = at;
        }
        else if (buttons.IsRightButtonPressed || buttons.IsMiddleButtonPressed)
        {
            _moveFrom = at;
        }
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Point at = e.GetPosition(this);
        if (_turnFrom is Point turnFrom)
        {
            Vector delta = at - turnFrom;
            _turnFrom = at;
            Look(yaw: (_yaw - delta.X * 0.005) * 180 / Math.PI, pitch: (_pitch + delta.Y * 0.005) * 180 / Math.PI);
        }
        else if (_moveFrom is Point moveFrom)
        {
            Vector delta = at - moveFrom;
            _moveFrom = at;
            double scale = _distance / Math.Max(1, Bounds.Height);
            (double sin, double cos) = Math.SinCos(_yaw);
            double right = -delta.X * scale;
            double forward = delta.Y * scale;
            Look(x: _targetX + right * cos - forward * sin, z: _targetZ - right * sin - forward * cos);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _turnFrom = null;
        _moveFrom = null;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        Look(distance: _distance * Math.Pow(0.85, e.Delta.Y));
        e.Handled = true;
    }
}
