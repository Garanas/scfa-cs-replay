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
/// The terrain seen from above (<see cref="TerrainPicture"/>), with the mask of the layer being
/// painted laid over it. The left mouse button paints with the brush, the right or middle one
/// moves the map, the wheel zooms.
/// </summary>
/// <remarks>
/// The picture has one pixel per sample of the heightmap. After a change only what the picture
/// coloured again is copied into the bitmap, so a brush stroke on a large map stays smooth.
/// </remarks>
public sealed class HeightmapView : Control
{
    private static readonly IBrush Background = new SolidColorBrush(Color.FromRgb(24, 24, 24));
    private static readonly IPen BrushOutline = new Pen(Brushes.White, 1.5);

    private readonly TerrainPicture _picture = new TerrainPicture();
    private MainViewModel? _model;
    private WriteableBitmap? _bitmap;

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
            InvalidateVisual();
            return;
        }

        IReadOnlyList<SampleRect> changed = _picture.Update(document, _model!.PaintedLayer);
        if (_bitmap is null || _bitmap.PixelSize.Width != _picture.Width || _bitmap.PixelSize.Height != _picture.Height)
        {
            _bitmap?.Dispose();
            _bitmap = new WriteableBitmap(new PixelSize(_picture.Width, _picture.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            changed = [new SampleRect(0, 0, _picture.Width - 1, _picture.Height - 1)];
        }
        Copy(changed);
        InvalidateVisual();
    }

    /// <summary>
    /// Copies rectangles of the picture into the bitmap.
    /// </summary>
    private unsafe void Copy(IReadOnlyList<SampleRect> rects)
    {
        if (rects.Count == 0)
        {
            return;
        }
        using ILockedFramebuffer frame = _bitmap!.Lock();
        uint[] pixels = _picture.Pixels;
        int width = _picture.Width;
        foreach (SampleRect rect in rects)
        {
            int columns = rect.X1 - rect.X0 + 1;
            for (int y = rect.Y0; y <= rect.Y1; y++)
            {
                Span<uint> row = new Span<uint>((void*)(frame.Address + y * frame.RowBytes + rect.X0 * 4), columns);
                pixels.AsSpan(y * width + rect.X0, columns).CopyTo(row);
            }
        }
    }

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
