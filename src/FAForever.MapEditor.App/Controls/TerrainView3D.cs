using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using FAForever.MapEditor.App.Rendering;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Controls;

/// <summary>
/// The terrain in 3D, around a point of the map: the left mouse button turns the camera around
/// it, the right or middle one moves it over the map, the wheel moves the camera closer.
/// </summary>
/// <remarks>
/// <para>
/// A heightfield renderer in software (in the manner of "voxel space"): for every column of the
/// picture it walks along the ground away from the camera, projects each height onto the column
/// and fills it from the bottom up, the nearest terrain hiding what lies behind. The colours are
/// those of the top view (<see cref="TerrainPicture"/>), faded into a haze with the distance; the
/// water is a flat surface at its elevation.
/// </para>
/// <para>
/// It needs no graphics card and its pictures are ordinary bitmaps, so automation's screenshots
/// show them. A column is a vertical plane, which is exact for a level camera and close enough for
/// a tilted one.
/// </para>
/// </remarks>
public sealed class TerrainView3D : Control
{
    private const double FieldOfView = 60 * Math.PI / 180;
    private static readonly IBrush Background = new SolidColorBrush(Color.FromRgb(24, 24, 24));
    private const uint Void = 0xff181818;

    private readonly TerrainPicture _picture = new TerrainPicture();
    private MainViewModel? _model;
    private WriteableBitmap? _frame;
    private float[] _heights = [];
    private TiledBuffer<ushort>? _heightsOf;
    private float _water = float.NegativeInfinity;
    private bool _stale = true;

    // the camera looks at (_targetX, _targetZ) from _distance away, turned _yaw around it and tilted _pitch down
    private double _targetX;
    private double _targetZ;
    private double _yaw = Math.PI / 4;
    private double _pitch = 40 * Math.PI / 180;
    private double _distance = 300;
    private bool _placed;
    private Point? _turnFrom;
    private Point? _moveFrom;

    /// <summary>
    /// The camera: where it looks (in ogrids), how far it is, and its angles in degrees.
    /// </summary>
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
        _pitch = Math.Clamp(pitch is double tilt ? tilt * Math.PI / 180 : _pitch, 5 * Math.PI / 180, 85 * Math.PI / 180);
        _placed = true;
        InvalidateVisual();
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
        OnMapChanged(this, EventArgs.Empty);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // a hidden view does no work; it catches up when it is shown
        if (change.Property == IsVisibleProperty && IsVisible)
        {
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Background, new Rect(Bounds.Size));
        MapDocument? document = _model?.ShownDocument;
        if (document is null || Bounds.Width < 2 || Bounds.Height < 2)
        {
            return;
        }

        if (_stale)
        {
            Prepare(document);
        }
        if (!_placed)
        {
            _targetX = document.Width / 2.0;
            _targetZ = document.Height / 2.0;
            _distance = Math.Max(document.Width, document.Height) * 1.1;
            _placed = true;
        }

        // half the resolution of the control: four times fewer columns and rows to walk
        int width = Math.Max(1, (int)(Bounds.Width / 2));
        int height = Math.Max(1, (int)(Bounds.Height / 2));
        if (_frame is null || _frame.PixelSize.Width != width || _frame.PixelSize.Height != height)
        {
            _frame?.Dispose();
            _frame = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        }
        Draw(document.Width, document.Height);
        context.DrawImage(_frame, new Rect(0, 0, width, height), new Rect(Bounds.Size));
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
            // across and along the ground, as the camera faces it
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

    private void OnMapChanged(object? sender, EventArgs e)
    {
        _placed = false;
        _heightsOf = null;
        OnDocumentChanged(sender, e);
    }

    private void OnDocumentChanged(object? sender, EventArgs e)
    {
        _stale = true;
        if (IsVisible)
        {
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Brings the colours and heights up to the shown document.
    /// </summary>
    private void Prepare(MapDocument document)
    {
        _picture.Update(document, 0);
        TiledBuffer<ushort> samples = document.Terrain.Heightmap.Samples;
        if (!ReferenceEquals(samples, _heightsOf))
        {
            ushort[] values = samples.ToArray();
            float scale = document.Terrain.Heightmap.Scale;
            if (_heights.Length != values.Length)
            {
                _heights = new float[values.Length];
            }
            for (int i = 0; i < values.Length; i++)
            {
                _heights[i] = values[i] * scale;
            }
            _heightsOf = samples;
        }
        _water = document.Water.HasWater ? document.Water.Elevation : float.NegativeInfinity;
        _stale = false;
    }

    /// <summary>
    /// Renders the terrain into the frame, column by column, the columns spread over the processors.
    /// </summary>
    private unsafe void Draw(int mapWidth, int mapHeight)
    {
        int width = _frame!.PixelSize.Width;
        int height = _frame.PixelSize.Height;
        int samplesX = _picture.Width;
        uint[] colours = _picture.Pixels;
        float[] heights = _heights;
        float water = _water;

        (double sinYaw, double cosYaw) = Math.SinCos(_yaw);
        (double sinPitch, double cosPitch) = Math.SinCos(_pitch);
        // the camera hangs over the target, behind it as seen along the yaw
        double targetHeight = GroundHeight(heights, samplesX, mapWidth, mapHeight, _targetX, _targetZ, water);
        double cameraX = _targetX + sinYaw * cosPitch * _distance;
        double cameraZ = _targetZ + cosYaw * cosPitch * _distance;
        double cameraY = targetHeight + sinPitch * _distance;
        double focal = width / 2.0 / Math.Tan(FieldOfView / 2);
        double far = _distance * 2 + Math.Sqrt((double)mapWidth * mapWidth + (double)mapHeight * mapHeight);

        using ILockedFramebuffer frame = _frame.Lock();
        nint address = frame.Address;
        int rowBytes = frame.RowBytes;

        Parallel.For(0, width, column =>
        {
            // the column's direction over the ground, and how far it is off the camera's axis
            double across = (column - width / 2.0) / focal;
            double angle = Math.Atan(across);
            double directionX = -Math.Sin(_yaw + angle);
            double directionZ = -Math.Cos(_yaw + angle);
            double onAxis = Math.Cos(angle);

            int lowest = height;
            double step = 0.25;
            bool crossed = false;
            for (double d = 1; d < far && lowest > 0; d += step, step = Math.Max(0.25, d * 0.004))
            {
                double x = cameraX + directionX * d;
                double z = cameraZ + directionZ * d;
                // in front of the map the ground is a flat void, so that the map does not stretch down
                // to it; behind the map there is only sky
                bool outside = x < 0 || z < 0 || x > mapWidth || z > mapHeight;
                if (outside && crossed)
                {
                    break;
                }
                crossed |= !outside;
                double ground = outside ? 0 : GroundHeight(heights, samplesX, mapWidth, mapHeight, x, z, water);
                double forward = d * onAxis;
                double up = ground - cameraY;
                double depth = forward * cosPitch - up * sinPitch;
                if (depth <= 0.1)
                {
                    continue;
                }
                double above = forward * sinPitch + up * cosPitch;
                int row = (int)(height / 2.0 - above / depth * focal);
                if (row >= lowest)
                {
                    continue;
                }
                uint colour = outside ? Void : Haze(Colour(colours, samplesX, mapWidth, mapHeight, x, z), d / far);
                for (int y = Math.Max(0, row); y < lowest; y++)
                {
                    ((uint*)(address + y * rowBytes))[column] = colour;
                }
                lowest = Math.Max(0, row);
            }

            // the sky over what is left of the column
            for (int y = 0; y < lowest; y++)
            {
                ((uint*)(address + y * rowBytes))[column] = Sky(y / (double)height);
            }
        });
    }

    /// <summary>
    /// The height of the terrain, or of the water above it, at a point of the map: between the
    /// four samples around it.
    /// </summary>
    private static double GroundHeight(float[] heights, int samplesX, int mapWidth, int mapHeight, double x, double z, float water)
    {
        (int x0, int z0, double fx, double fz) = Cell(mapWidth, mapHeight, x, z);
        int i = z0 * samplesX + x0;
        int right = x0 < mapWidth ? 1 : 0;
        int down = z0 < mapHeight ? samplesX : 0;
        double top = heights[i] + (heights[i + right] - heights[i]) * fx;
        double bottom = heights[i + down] + (heights[i + down + right] - heights[i + down]) * fx;
        return Math.Max(top + (bottom - top) * fz, water);
    }

    /// <summary>
    /// The colour at a point of the map: between the four samples around it.
    /// </summary>
    private static uint Colour(uint[] colours, int samplesX, int mapWidth, int mapHeight, double x, double z)
    {
        (int x0, int z0, double fx, double fz) = Cell(mapWidth, mapHeight, x, z);
        int i = z0 * samplesX + x0;
        int right = x0 < mapWidth ? 1 : 0;
        int down = z0 < mapHeight ? samplesX : 0;
        uint a = colours[i], b = colours[i + right], c = colours[i + down], d = colours[i + down + right];
        uint Channel(int shift)
        {
            double top = (a >> shift & 0xff) + ((double)(b >> shift & 0xff) - (a >> shift & 0xff)) * fx;
            double bottom = (c >> shift & 0xff) + ((double)(d >> shift & 0xff) - (c >> shift & 0xff)) * fx;
            return (uint)(top + (bottom - top) * fz);
        }
        return 0xff000000u | Channel(16) << 16 | Channel(8) << 8 | Channel(0);
    }

    /// <summary>
    /// The sample at the top left of the cell a point lies in, and where in the cell it lies.
    /// </summary>
    private static (int X, int Z, double Fx, double Fz) Cell(int mapWidth, int mapHeight, double x, double z)
    {
        x = Math.Clamp(x, 0, mapWidth);
        z = Math.Clamp(z, 0, mapHeight);
        int x0 = Math.Min((int)x, mapWidth);
        int z0 = Math.Min((int)z, mapHeight);
        return (x0, z0, x - x0, z - z0);
    }

    /// <summary>
    /// A colour faded towards the haze of the distance (0 near, 1 at the far end).
    /// </summary>
    private static uint Haze(uint colour, double distance)
    {
        double t = Math.Clamp((distance - 0.25) / 0.75, 0, 1) * 0.7;
        (byte hazeR, byte hazeG, byte hazeB) = (150, 165, 185);
        uint r = (uint)((colour >> 16 & 0xff) * (1 - t) + hazeR * t);
        uint g = (uint)((colour >> 8 & 0xff) * (1 - t) + hazeG * t);
        uint b = (uint)((colour & 0xff) * (1 - t) + hazeB * t);
        return 0xff000000u | r << 16 | g << 8 | b;
    }

    /// <summary>
    /// The sky from the top of the picture (0) down (1): deep blue fading to haze at the horizon.
    /// </summary>
    private static uint Sky(double t)
    {
        uint r = (uint)(70 + 80 * t);
        uint g = (uint)(105 + 60 * t);
        uint b = (uint)(150 + 35 * t);
        return 0xff000000u | r << 16 | g << 8 | b;
    }
}
