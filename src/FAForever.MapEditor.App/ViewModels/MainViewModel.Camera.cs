using FAForever.MapEditor.App.Diagnostics;
using System.Diagnostics;
using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.App.Rendering.Gpu;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.ViewModels;

public sealed partial class MainViewModel
{
    private readonly Stopwatch _cameraClock = Stopwatch.StartNew();
    private EditorCamera? _camera;
    private SmoothGround? _smooth;
    private Heightmap? _smoothOf;
    private TimeSpan _smoothMade;

    /// <summary>
    /// The 3D view's camera. The top view shows what it sees and moves it (control and click).
    /// It starts over the whole map whenever another map is opened.
    /// </summary>
    public EditorCamera Camera => _camera ??= new EditorCamera((x, z, radius) => Smooth().Height(x, z, radius), TerrainHeight);

    /// <summary>
    /// Raised when the camera moved: the top view draws what it sees again.
    /// </summary>
    public event EventHandler? CameraMoved;

    /// <summary>
    /// Whether the top view shows what the 3D view's camera sees.
    /// </summary>
    [ObservableProperty]
    public partial bool ShowCamera { get; set; } = true;

    partial void OnShowCameraChanged(bool value) => CameraMoved?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Says that the camera moved; the 3D view calls it while the camera glides, and every
    /// operation that sends it somewhere.
    /// </summary>
    public void NotifyCameraMoved() => CameraMoved?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Sends the 3D camera to look at a place of the map, keeping its distance and angles; at
    /// once, as the top view asks for it while the 3D view may be hidden.
    /// </summary>
    public void MoveCameraTo(float x, float z)
    {
        Camera.MoveTo(new Vector2(x, z));
        NotifyCameraMoved();
    }

    /// <summary>
    /// Sends the 3D camera to see a place of the map and <paramref name="radius"/> ogrids around it.
    /// </summary>
    public void FocusCamera(float x, float z, float radius)
    {
        Camera.Focus(new Vector2(x, z), radius);
        NotifyCameraMoved();
    }

    private void ResetCamera()
    {
        if (Map?.Document is MapDocument document)
        {
            Camera.Reset(document.Width, document.Height);
            NotifyCameraMoved();
        }
    }

    /// <summary>
    /// The blurred terrain the camera rests on, made again when the heights change; while they
    /// keep changing (a brush stroke) at most twice a second, as making it reads every sample.
    /// </summary>
    private SmoothGround Smooth()
    {
        Heightmap? heightmap = ShownDocument?.Terrain.Heightmap;
        if (heightmap is null)
        {
            return _smooth ??= SmoothGround.Flat(0);
        }
        if (!ReferenceEquals(heightmap, _smoothOf) && (_smooth is null || _smoothOf?.Width != heightmap.Width
            || _smoothOf.Height != heightmap.Height || _cameraClock.Elapsed - _smoothMade > TimeSpan.FromSeconds(0.5)))
        {
            using Performance.Span measure = Performance.Measure("camera.smooth-ground");
            _smooth = SmoothGround.From(heightmap);
            _smoothOf = heightmap;
            _smoothMade = _cameraClock.Elapsed;
        }
        return _smooth!;
    }

    /// <summary>
    /// The height of the shown terrain, between its samples; outside the map that of its edge.
    /// </summary>
    private float TerrainHeight(float x, float z)
    {
        Heightmap? heightmap = ShownDocument?.Terrain.Heightmap;
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
}
