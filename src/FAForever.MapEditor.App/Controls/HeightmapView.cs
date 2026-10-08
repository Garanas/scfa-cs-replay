using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Controls;

/// <summary>
/// The terrain seen from above: shaded by a light from the north-west, coloured by height, and
/// blue below the water. The left mouse button paints with the brush, the right or middle one
/// moves the map, the wheel zooms.
/// </summary>
/// <remarks>
/// The picture has one pixel per sample of the heightmap. After a change only the tiles of the
/// heightmap that changed are drawn again (<see cref="TiledBuffer{T}.ChangedTiles"/>), so a brush
/// stroke on a large map stays smooth.
/// </remarks>
public sealed class HeightmapView : Control
{
    private static readonly IBrush Background = new SolidColorBrush(Color.FromRgb(24, 24, 24));
    private static readonly IPen BrushOutline = new Pen(Brushes.White, 1.5);

    private MainViewModel? _model;
    private WriteableBitmap? _bitmap;
    private TiledBuffer<ushort>? _shown;
    private float _scale;
    private float _water = float.NegativeInfinity;
    private float _low;
    private float _high;

    // a point of the map (in samples) is shown at map * _zoom + _offset
    private double _zoom = 1;
    private Vector _offset;
    private bool _fitted;
    private Point? _panFrom;
    private bool _painting;
    private Point? _cursor;

    /// <summary>
    /// A line about the place under the cursor, for the status bar.
    /// </summary>
    public event Action<string>? CursorMoved;

    public double Zoom => _zoom;

    /// <summary>
    /// Where a point of the map (in ogrids) is on the control.
    /// </summary>
    public Point MapToControl(double x, double z) => new Point(x * _zoom + _offset.X, z * _zoom + _offset.Y);

    /// <summary>
    /// Which point of the map (in ogrids) is at a point of the control.
    /// </summary>
    public Point ControlToMap(Point point) => new Point((point.X - _offset.X) / _zoom, (point.Y - _offset.Y) / _zoom);

    /// <summary>
    /// Shows the whole map.
    /// </summary>
    public void Fit()
    {
        if (_bitmap is null || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }
        PixelSize size = _bitmap.PixelSize;
        _zoom = Math.Min(Bounds.Width / size.Width, Bounds.Height / size.Height) * 0.95;
        _offset = new Vector((Bounds.Width - size.Width * _zoom) / 2, (Bounds.Height - size.Height * _zoom) / 2);
        _fitted = true;
        InvalidateVisual();
    }

    /// <summary>
    /// Centres the view on a place of the map (in ogrids) at a zoom (control pixels per ogrid).
    /// </summary>
    public void Show(double zoom, double x, double z)
    {
        _zoom = Math.Clamp(zoom, 0.05, 64);
        _offset = new Vector(Bounds.Width / 2 - x * _zoom, Bounds.Height / 2 - z * _zoom);
        _fitted = true;
        InvalidateVisual();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_model is not null)
        {
            _model.WorkspaceChanged -= OnWorkspaceChanged;
            _model.DocumentChanged -= OnDocumentChanged;
        }
        _model = DataContext as MainViewModel;
        if (_model is not null)
        {
            _model.WorkspaceChanged += OnWorkspaceChanged;
            _model.DocumentChanged += OnDocumentChanged;
        }
        OnWorkspaceChanged(this, EventArgs.Empty);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (!_fitted)
        {
            Fit();
        }
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Background, new Rect(Bounds.Size));
        if (_bitmap is null)
        {
            return;
        }

        PixelSize size = _bitmap.PixelSize;
        Rect destination = new Rect(_offset.X, _offset.Y, size.Width * _zoom, size.Height * _zoom);
        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = _zoom >= 2 ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality }))
        {
            context.DrawImage(_bitmap, new Rect(0, 0, size.Width, size.Height), destination);
        }

        if (_cursor is Point cursor && _model is not null)
        {
            double radius = _model.BrushRadius * _zoom;
            context.DrawEllipse(null, BrushOutline, cursor, radius, radius);
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        Point at = e.GetPosition(this);
        Point map = ControlToMap(at);
        _zoom = Math.Clamp(_zoom * Math.Pow(1.2, e.Delta.Y), 0.05, 64);
        _offset = new Vector(at.X - map.X * _zoom, at.Y - map.Y * _zoom);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        PointerPointProperties buttons = e.GetCurrentPoint(this).Properties;
        Point at = e.GetPosition(this);
        if (buttons.IsLeftButtonPressed && _model?.HasMap == true)
        {
            Point map = ControlToMap(at);
            _painting = true;
            _model.BeginStroke((float)map.X, (float)map.Y);
            e.Pointer.Capture(this);
        }
        else if (buttons.IsRightButtonPressed || buttons.IsMiddleButtonPressed)
        {
            _panFrom = at;
            e.Pointer.Capture(this);
        }
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Point at = e.GetPosition(this);
        _cursor = at;
        if (_panFrom is Point from)
        {
            _offset += at - from;
            _panFrom = at;
        }
        else if (_painting)
        {
            Point map = ControlToMap(at);
            _model?.ContinueStroke((float)map.X, (float)map.Y);
        }
        ReportCursor(at);
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_painting)
        {
            _painting = false;
            _model?.EndStroke();
        }
        _panFrom = null;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _cursor = null;
        InvalidateVisual();
    }

    private void OnWorkspaceChanged(object? sender, EventArgs e)
    {
        _bitmap?.Dispose();
        _bitmap = null;
        _shown = null;
        _fitted = false;
        OnDocumentChanged(sender, e);
        Fit();
    }

    private void OnDocumentChanged(object? sender, EventArgs e)
    {
        MapDocument? document = _model?.ShownDocument;
        if (document is null)
        {
            _bitmap?.Dispose();
            _bitmap = null;
            _shown = null;
            InvalidateVisual();
            return;
        }

        Heightmap heightmap = document.Terrain.Heightmap;
        TiledBuffer<ushort> samples = heightmap.Samples;
        float water = document.Water.HasWater ? document.Water.Elevation : float.NegativeInfinity;
        bool redrawAll = _bitmap is null
            || _shown is null
            || _bitmap.PixelSize.Width != samples.Width
            || _bitmap.PixelSize.Height != samples.Height
            || _scale != heightmap.Scale
            || _water != water;

        if (redrawAll)
        {
            _bitmap?.Dispose();
            _bitmap = new WriteableBitmap(new PixelSize(samples.Width, samples.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            _scale = heightmap.Scale;
            _water = water;
            ushort[] values = samples.ToArray();
            _low = values.Min() * _scale;
            _high = Math.Max(_low + 1, values.Max() * _scale);
            DrawAll(values, samples.Width, samples.Height);
        }
        else if (!ReferenceEquals(samples, _shown))
        {
            foreach ((int tileX, int tileY) in samples.ChangedTiles(_shown!))
            {
                // one sample more on each side: the shading of a sample reads its neighbours
                int x0 = tileX * TiledBuffer<ushort>.TileSize - 1;
                int y0 = tileY * TiledBuffer<ushort>.TileSize - 1;
                Draw(samples, x0, y0, x0 + TiledBuffer<ushort>.TileSize + 1, y0 + TiledBuffer<ushort>.TileSize + 1);
            }
        }
        _shown = samples;
        InvalidateVisual();
    }

    /// <summary>
    /// Draws the whole picture from the samples row by row, the rows spread over the processors:
    /// a 4097 by 4097 heightmap is 16.8 million samples.
    /// </summary>
    private unsafe void DrawAll(ushort[] values, int width, int height)
    {
        using ILockedFramebuffer frame = _bitmap!.Lock();
        nint address = frame.Address;
        int rowBytes = frame.RowBytes;
        Parallel.For(0, height, y =>
        {
            uint* row = (uint*)(address + y * rowBytes);
            int above = Math.Max(0, y - 1) * width;
            int here = y * width;
            int below = Math.Min(height - 1, y + 1) * width;
            for (int x = 0; x < width; x++)
            {
                int left = Math.Max(0, x - 1);
                int right = Math.Min(width - 1, x + 1);
                float slopeX = (values[here + right] - values[here + left]) * _scale / 2;
                float slopeZ = (values[below + x] - values[above + x]) * _scale / 2;
                row[x] = Shade(values[here + x] * _scale, slopeX, slopeZ);
            }
        });
    }

    /// <summary>
    /// Draws the samples of a rectangle (inclusive) into the picture.
    /// </summary>
    private unsafe void Draw(TiledBuffer<ushort> samples, int x0, int y0, int x1, int y1)
    {
        int width = samples.Width;
        int height = samples.Height;
        x0 = Math.Max(0, x0);
        y0 = Math.Max(0, y0);
        x1 = Math.Min(width - 1, x1);
        y1 = Math.Min(height - 1, y1);

        using ILockedFramebuffer frame = _bitmap!.Lock();
        for (int y = y0; y <= y1; y++)
        {
            uint* row = (uint*)(frame.Address + y * frame.RowBytes);
            for (int x = x0; x <= x1; x++)
            {
                float h = samples[x, y] * _scale;
                float west = samples[Math.Max(0, x - 1), y] * _scale;
                float east = samples[Math.Min(width - 1, x + 1), y] * _scale;
                float north = samples[x, Math.Max(0, y - 1)] * _scale;
                float south = samples[x, Math.Min(height - 1, y + 1)] * _scale;
                row[x] = Shade(h, (east - west) / 2, (south - north) / 2);
            }
        }
    }

    /// <summary>
    /// The colour of a sample: by height (green low, brown, grey high), blue under water, darker
    /// where the slope faces away from a light in the north-west.
    /// </summary>
    private uint Shade(float height, float slopeX, float slopeZ)
    {
        // the normal is (-slopeX, 1, -slopeZ); the light comes from (-1, 1.4, -1)
        const float LightX = -0.5f, LightY = 0.7f, LightZ = -0.5f;
        float length = MathF.Sqrt(slopeX * slopeX + 1 + slopeZ * slopeZ);
        float light = Math.Clamp((-slopeX * LightX + LightY - slopeZ * LightZ) / (length * 0.995f), 0, 1);

        float t = Math.Clamp((height - _low) / (_high - _low), 0, 1);
        (float r, float g, float b) = t < 0.5f
            ? Mix((70, 110, 60), (150, 135, 95), t * 2)
            : Mix((150, 135, 95), (225, 225, 225), (t - 0.5f) * 2);
        if (height < _water)
        {
            float depth = Math.Clamp((_water - height) / 20, 0, 1);
            (r, g, b) = Mix((r, g, b), Mix((50, 110, 160), (20, 45, 90), depth), 0.75f);
        }

        float brightness = 0.3f + 0.7f * light;
        return 0xFF000000u
            | (uint)Math.Clamp(r * brightness, 0, 255) << 16
            | (uint)Math.Clamp(g * brightness, 0, 255) << 8
            | (uint)Math.Clamp(b * brightness, 0, 255);
    }

    private static (float, float, float) Mix((float R, float G, float B) a, (float R, float G, float B) b, float t) =>
        (a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);

    private void ReportCursor(Point at)
    {
        MapDocument? document = _model?.ShownDocument;
        if (document is null)
        {
            return;
        }
        Point map = ControlToMap(at);
        int x = (int)Math.Round(map.X);
        int z = (int)Math.Round(map.Y);
        Heightmap heightmap = document.Terrain.Heightmap;
        CursorMoved?.Invoke(x >= 0 && z >= 0 && x <= heightmap.Width && z <= heightmap.Height
            ? $"x {x}, z {z}, height {heightmap.GetHeight(x, z):0.00}"
            : "");
    }
}
