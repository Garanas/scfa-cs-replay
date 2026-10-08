using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using FAForever.MapEditor.App.Rendering;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Files;
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
    private static readonly IPen MarkerOutline = new Pen(new SolidColorBrush(Color.FromArgb(220, 0, 0, 0)), 1);
    private static readonly Typeface LabelFont = new Typeface("Inter", FontStyle.Normal, FontWeight.Bold);

    private readonly MarkerLayer _markers = new MarkerLayer();

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

        if (_model?.ShownDocument is MapDocument document)
        {
            _markers.Render(context, document.Markers, _model.ShowMarkers, _model.ShowPathNodes, _zoom, _offset);
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

    /// <summary>
    /// The markers over the terrain: small shapes of a fixed size on screen in the colour of their
    /// kind, with a dark outline. Spawns are circles with their army's number, deposits squares, AI
    /// markers diamonds, weather triangles, path nodes dots with their links.
    /// </summary>
    private sealed class MarkerLayer
    {
        private readonly Dictionary<uint, IBrush> _brushes = [];
        private readonly Dictionary<uint, IPen> _linkPens = [];
        private MarkerSet? _linked;
        private List<(Color32 Color, StreamGeometry Lines)> _links = [];

        public void Render(DrawingContext context, MarkerSet markers, bool showMarkers, bool showPathNodes, double zoom, Vector offset)
        {
            Point At(Marker marker) => new Point(marker.Position.X * zoom + offset.X, marker.Position.Z * zoom + offset.Y);

            if (showPathNodes)
            {
                // the links in map units, drawn scaled: built once per version of the markers
                if (!ReferenceEquals(markers, _linked))
                {
                    _links = Links(markers);
                    _linked = markers;
                }
                using (context.PushTransform(Matrix.CreateScale(zoom, zoom) * Matrix.CreateTranslation(offset.X, offset.Y)))
                {
                    foreach ((Color32 color, StreamGeometry lines) in _links)
                    {
                        context.DrawGeometry(null, LinkPen(color, 1 / zoom), lines);
                    }
                }
                foreach (PathNode node in markers.PathNodes)
                {
                    context.DrawEllipse(Fill(node.Color), MarkerOutline, At(node), 2.5, 2.5);
                }
            }

            if (!showMarkers)
            {
                return;
            }

            foreach (WeatherGenerator weather in markers.WeatherGenerators)
            {
                Triangle(context, Fill(weather.Color), At(weather), 6);
            }
            foreach (WeatherDefinition weather in markers.WeatherDefinitions)
            {
                Triangle(context, Fill(weather.Color), At(weather), 8);
            }
            foreach (AiMarker marker in markers.AiMarkers)
            {
                Point at = At(marker);
                StreamGeometry diamond = new StreamGeometry();
                using (StreamGeometryContext g = diamond.Open())
                {
                    g.BeginFigure(new Point(at.X, at.Y - 5), true);
                    g.LineTo(new Point(at.X + 5, at.Y));
                    g.LineTo(new Point(at.X, at.Y + 5));
                    g.LineTo(new Point(at.X - 5, at.Y));
                    g.EndFigure(true);
                }
                context.DrawGeometry(Fill(marker.Color), MarkerOutline, diamond);
            }
            foreach (ResourceMarker resource in markers.Resources)
            {
                double half = resource.Kind == ResourceKind.Mass ? 4 : 6;
                Point at = At(resource);
                context.DrawRectangle(Fill(resource.Color), MarkerOutline, new Rect(at.X - half, at.Y - half, half * 2, half * 2));
            }
            foreach (Spawn spawn in markers.Spawns)
            {
                Point at = At(spawn);
                context.DrawEllipse(Fill(spawn.Color), new Pen(Brushes.White, 2), at, 10, 10);
                string label = spawn.Army.StartsWith("ARMY_", StringComparison.Ordinal) ? spawn.Army[5..] : spawn.Army;
                FormattedText text = new FormattedText(label, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, LabelFont, 11, Brushes.White);
                context.DrawText(text, new Point(at.X - text.Width / 2, at.Y - text.Height / 2));
            }
        }

        private static void Triangle(DrawingContext context, IBrush fill, Point at, double size)
        {
            StreamGeometry triangle = new StreamGeometry();
            using (StreamGeometryContext g = triangle.Open())
            {
                g.BeginFigure(new Point(at.X, at.Y - size), true);
                g.LineTo(new Point(at.X + size, at.Y + size * 0.7));
                g.LineTo(new Point(at.X - size, at.Y + size * 0.7));
                g.EndFigure(true);
            }
            context.DrawGeometry(fill, MarkerOutline, triangle);
        }

        /// <summary>
        /// The links between path nodes as lines in map units, one geometry per colour (layer);
        /// a link both ways is drawn once.
        /// </summary>
        private static List<(Color32, StreamGeometry)> Links(MarkerSet markers)
        {
            Dictionary<int, PathNode> nodes = markers.PathNodes.ToDictionary(node => node.Id);
            List<(Color32, StreamGeometry)> links = [];
            foreach (IGrouping<Color32, PathNode> layer in markers.PathNodes.GroupBy(node => node.Color))
            {
                StreamGeometry lines = new StreamGeometry();
                using (StreamGeometryContext g = lines.Open())
                {
                    foreach (PathNode node in layer)
                    {
                        foreach (int id in node.AdjacentTo)
                        {
                            if (id > node.Id || !nodes.TryGetValue(id, out PathNode? other) || !other.AdjacentTo.Contains(node.Id))
                            {
                                if (nodes.TryGetValue(id, out PathNode? target))
                                {
                                    g.BeginFigure(new Point(node.Position.X, node.Position.Z), false);
                                    g.LineTo(new Point(target.Position.X, target.Position.Z));
                                    g.EndFigure(false);
                                }
                            }
                        }
                    }
                }
                links.Add((layer.Key, lines));
            }
            return links;
        }

        private IBrush Fill(Color32 color)
        {
            uint key = color.ToArgb();
            if (!_brushes.TryGetValue(key, out IBrush? brush))
            {
                brush = new SolidColorBrush(Color.FromArgb(255, color.Red, color.Green, color.Blue));
                _brushes[key] = brush;
            }
            return brush;
        }

        private IPen LinkPen(Color32 color, double thickness)
        {
            uint key = color.ToArgb();
            if (!_linkPens.TryGetValue(key, out IPen? pen) || Math.Abs(pen.Thickness - thickness) > 1e-9)
            {
                pen = new Pen(new SolidColorBrush(Color.FromArgb(150, color.Red, color.Green, color.Blue)), thickness);
                _linkPens[key] = pen;
            }
            return pen;
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
