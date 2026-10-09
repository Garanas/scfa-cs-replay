using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.App.Rendering.Gpu;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.ViewModels;

public sealed partial class MainViewModel
{
    private EditorCamera? _camera;

    /// <summary>
    /// The 3D view's camera, on the shown terrain. The top view shows what it sees and moves it
    /// (control and click). It starts over the whole map whenever another map is opened.
    /// </summary>
    public EditorCamera Camera => _camera ??= NewCamera();

    private EditorCamera NewCamera()
    {
        CameraGround ground = new CameraGround(() => ShownDocument?.Terrain.Heightmap);
        return new EditorCamera(ground.Smooth, ground.Exact);
    }

    /// <summary>
    /// Raised when the camera moved: the top view draws what it sees again.
    /// </summary>
    public event EventHandler? CameraMoved;

    /// <summary>
    /// Whether the top view shows what the 3D view's camera sees.
    /// </summary>
    [ObservableProperty]
    public partial bool ShowCamera { get; set; } = true;

    partial void OnShowCameraChanged(bool value)
    {
        RefreshOverlays();
        CameraMoved?.Invoke(this, EventArgs.Empty);
    }

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

    /// <summary>
    /// Puts the 3D camera over the whole map, straight down.
    /// </summary>
    public void ResetCamera()
    {
        if (Map?.Document is MapDocument document)
        {
            Camera.Reset(document.Width, document.Height);
            NotifyCameraMoved();
        }
    }
}
