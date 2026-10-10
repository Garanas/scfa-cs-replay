using FAForever.MapEditor.App.Diagnostics;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using FAForever.MapEditor.App.Rendering;
using FAForever.MapEditor.App.Rendering.Gpu;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
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
    /// <summary>
    /// What the view draws besides the terrain and the markers' own colours, from the theme's
    /// tokens (<c>Styles/Tokens.axaml</c>).
    /// </summary>
    internal sealed record Overlay(IBrush Canvas, IBrush Strong, IPen Brush, IPen Stroke, IBrush Fill, IPen Shadow, IPen Ring, Typeface LabelFont, double LabelSize);

    private Overlay? _overlay;

    protected override void OnAttachedToLogicalTree(Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        // another theme variant: read the tokens again
        ActualThemeVariantChanged -= OnThemeChanged;
        ActualThemeVariantChanged += OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        _overlay = null;
        InvalidateVisual();
    }

    private Overlay Tokens() => _overlay ??= new Overlay(
        Token<IBrush>("CanvasBrush"),
        Token<IBrush>("OverlayBrush"),
        new Pen(Token<IBrush>("OverlayBrush"), 1.5),
        new Pen(Token<IBrush>("OverlayStrokeBrush"), 1.5),
        Token<IBrush>("OverlayFillBrush"),
        new Pen(Token<IBrush>("OverlayShadowBrush"), 1),
        new Pen(Token<IBrush>("OverlayBrush"), 2),
        new Typeface(Token<FontFamily>("SansFontFamily"), FontStyle.Normal, FontWeight.Bold),
        Token<double>("FontSizeMapLabel"));

    private T Token<T>(string key) => this.TryFindResource(key, ActualThemeVariant, out object? value) && value is T token
        ? token
        : throw new KeyNotFoundException($"The theme has no token {key} (Styles/Tokens.axaml)");
    private readonly MarkerLayer _markers = new MarkerLayer();

    private readonly TerrainPicture _picture = new TerrainPicture();
    private MainViewModel? _model;
    private WriteableBitmap? _bitmap;

    // a point of the map (in samples) is shown at map * _zoom + _offset
    private double _zoom = 1;
    private Vector _offset;
    private bool _fitted;
    private Point? _panFrom;
    // the tool the left mouse was pressed with, until it is released
    private IMapTool? _pressed;
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
            _model.MapChanged -= OnMapChanged;
            _model.DocumentChanged -= OnDocumentChanged;
            _model.PropertyChanged -= OnModelChanged;
            _model.OverlaysChanged -= OnOverlayChanged;
            _model.CameraMoved -= OnOverlayChanged;
            _model.CursorChanged -= OnOverlayChanged;
        }
        _model = DataContext as MainViewModel;
        if (_model is not null)
        {
            _model.MapChanged += OnMapChanged;
            _model.DocumentChanged += OnDocumentChanged;
            _model.PropertyChanged += OnModelChanged;
            _model.OverlaysChanged += OnOverlayChanged;
            _model.CameraMoved += OnOverlayChanged;
            _model.CursorChanged += OnOverlayChanged;
        }
        OnMapChanged(this, EventArgs.Empty);
    }

    // another layer's mask shows: the picture changes, as when the document does
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ShownMask))
        {
            OnDocumentChanged(sender, e);
        }
    }

    // only the overlay changes (markers turned on or off, the camera, the brush around the mouse):
    // the terrain's picture stays
    private void OnOverlayChanged(object? sender, EventArgs e)
    {
        if (IsVisible)
        {
            InvalidateVisual();
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (!_fitted)
        {
            Fit();
        }
    }

    private WriteableBitmap? _brushPicture;
    private (BrushShape Shape, IBrush Color)? _brushPictureOf;

    /// <summary>
    /// A brush's picture as the 3D view draws it, a ramp from near black where it does little to
    /// the overlay's colour where it works fully; made again only for another shape or theme.
    /// </summary>
    private WriteableBitmap? BrushPicture(BrushShape shape, IBrush color)
    {
        if (_brushPictureOf is { } made && ReferenceEquals(made.Shape, shape) && ReferenceEquals(made.Color, color))
        {
            return _brushPicture;
        }
        if (color is not ISolidColorBrush solid)
        {
            return null;
        }
        _brushPicture?.Dispose();
        _brushPicture = new WriteableBitmap(new PixelSize(shape.Width, shape.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (ILockedFramebuffer frame = _brushPicture.Lock())
        {
            ReadOnlySpan<float> values = shape.Values;
            byte[] row = new byte[shape.Width * 4];
            for (int y = 0; y < shape.Height; y++)
            {
                for (int x = 0; x < shape.Width; x++)
                {
                    // the square root shows where it works at all, as the 3D view does
                    float weight = MathF.Sqrt(values[y * shape.Width + x]);
                    const float alpha = 0.55f;
                    const float dark = 0.05f * 255;
                    row[x * 4] = (byte)((dark + (solid.Color.B - dark) * weight) * alpha);
                    row[x * 4 + 1] = (byte)((dark + (solid.Color.G - dark) * weight) * alpha);
                    row[x * 4 + 2] = (byte)((dark + (solid.Color.R - dark) * weight) * alpha);
                    row[x * 4 + 3] = (byte)(255 * alpha);
                }
                System.Runtime.InteropServices.Marshal.Copy(row, 0, frame.Address + y * frame.RowBytes, row.Length);
            }
        }
        _brushPictureOf = (shape, color);
        return _brushPicture;
    }

    public override void Render(DrawingContext context)
    {
        using Performance.Span measure = Performance.Measure("top.render");
        Overlay overlay = Tokens();
        context.FillRectangle(overlay.Canvas, new Rect(Bounds.Size));
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
            _markers.Render(context, overlay, document.Markers, _model.ShowMarkers, _model.ShowPathNodes, _zoom, _offset);
        }

        if (_model is { ShowSymmetry: true, Symmetry.IsOn: true, ShownDocument: MapDocument mirrored })
        {
            DrawSymmetry(context, overlay, _model.Symmetry.Guides(mirrored));
        }

        if (_model is { ShowCamera: true, HasMap: true })
        {
            DrawCamera(context, overlay, _model.Camera);
        }

        if (_cursor is Point cursor && _model?.ActiveTool?.Tool is { CursorRadius: > 0 } tool)
        {
            double radius = tool.CursorRadius * _zoom;
            if (tool is IShapedBrushTool { Shape: { IsShaped: true } shape })
            {
                // the picture, faded, in its square, turned as it paints, with a tick on its top edge
                (double sin, double cos) = Math.SinCos(shape.Rotation * Math.PI / 180);
                if (shape.Shown is BrushShape picture && BrushPicture(picture, overlay.Strong) is WriteableBitmap bitmap)
                {
                    double longest = Math.Max(picture.Width, picture.Height);
                    double width = 2 * radius * picture.Width / longest;
                    double height = 2 * radius * picture.Height / longest;
                    using (context.PushTransform(Matrix.CreateRotation(shape.Rotation * Math.PI / 180) * Matrix.CreateTranslation(cursor.X, cursor.Y)))
                    {
                        context.DrawImage(bitmap, new Rect(-width / 2, -height / 2, width, height));
                    }
                }
                Point Corner(double u, double v) => cursor + new Vector((u * cos - v * sin) * radius, (u * sin + v * cos) * radius);
                StreamGeometry square = new StreamGeometry();
                using (StreamGeometryContext figure = square.Open())
                {
                    figure.BeginFigure(Corner(-1, -1), false);
                    figure.LineTo(Corner(1, -1));
                    figure.LineTo(Corner(1, 1));
                    figure.LineTo(Corner(-1, 1));
                    figure.EndFigure(true);
                }
                context.DrawGeometry(null, overlay.Brush, square);
                context.DrawLine(overlay.Brush, Corner(0, -1), Corner(0, -0.8));
            }
            else
            {
                context.DrawEllipse(null, overlay.Brush, cursor, radius, radius);
            }
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
        if (buttons.IsLeftButtonPressed && _model?.HasMap == true && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            // control and a click: the 3D view looks here
            Point place = ControlToMap(at);
            _model.MoveCameraTo((float)place.X, (float)place.Y);
        }
        else if (buttons.IsLeftButtonPressed && _model is { HasMap: true, ActiveTool.Tool: IMapTool tool })
        {
            // the left mouse is the active tool's
            Point map = ControlToMap(at);
            _pressed = tool;
            tool.Press((float)map.X, (float)map.Y);
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
        else if (_pressed is IMapTool tool)
        {
            Point map = ControlToMap(at);
            tool.Drag((float)map.X, (float)map.Y);
        }
        ReportCursor(at);
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_pressed is IMapTool tool)
        {
            _pressed = null;
            tool.Release();
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

    private void OnMapChanged(object? sender, EventArgs e)
    {
        _bitmap?.Dispose();
        _bitmap = null;
        _fitted = false;
        OnDocumentChanged(sender, e);
        Fit();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // a hidden view does no work; it catches up when it is shown
        if (change.Property == IsVisibleProperty && IsVisible && _stale)
        {
            OnDocumentChanged(this, EventArgs.Empty);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // the tab control takes a tab's view out of the tree while another tab shows
        if (_stale)
        {
            OnDocumentChanged(this, EventArgs.Empty);
        }
    }

    private bool _stale;

    private void OnDocumentChanged(object? sender, EventArgs e)
    {
        if (!IsEffectivelyVisible && _bitmap is not null)
        {
            _stale = true;
            return;
        }
        _stale = false;
        using Performance.Span measure = Performance.Measure("top.document-changed");
        MapDocument? document = _model?.ShownDocument;
        if (document is null)
        {
            _bitmap?.Dispose();
            _bitmap = null;
            InvalidateVisual();
            return;
        }

        IReadOnlyList<SampleRect> changed = _picture.Update(document, _model!.ShownMask);
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

        public void Render(DrawingContext context, Overlay overlay, MarkerSet markers, bool showMarkers, bool showPathNodes, double zoom, Vector offset)
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
                    context.DrawEllipse(Fill(node.Color), overlay.Shadow, At(node), 2.5, 2.5);
                }
            }

            if (!showMarkers)
            {
                return;
            }

            foreach (WeatherGenerator weather in markers.WeatherGenerators)
            {
                Triangle(context, overlay, Fill(weather.Color), At(weather), 6);
            }
            foreach (WeatherDefinition weather in markers.WeatherDefinitions)
            {
                Triangle(context, overlay, Fill(weather.Color), At(weather), 8);
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
                context.DrawGeometry(Fill(marker.Color), overlay.Shadow, diamond);
            }
            foreach (ResourceMarker resource in markers.Resources)
            {
                double half = resource.Kind == ResourceKind.Mass ? 4 : 6;
                Point at = At(resource);
                context.DrawRectangle(Fill(resource.Color), overlay.Shadow, new Rect(at.X - half, at.Y - half, half * 2, half * 2));
            }
            foreach (Spawn spawn in markers.Spawns)
            {
                Point at = At(spawn);
                context.DrawEllipse(Fill(spawn.Color), overlay.Ring, at, 10, 10);
                string label = spawn.Army.StartsWith("ARMY_", StringComparison.Ordinal) ? spawn.Army[5..] : spawn.Army;
                FormattedText text = new FormattedText(label, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, overlay.LabelFont, overlay.LabelSize, overlay.Strong);
                context.DrawText(text, new Point(at.X - text.Width / 2, at.Y - text.Height / 2));
            }
        }

        private static void Triangle(DrawingContext context, Overlay overlay, IBrush fill, Point at, double size)
        {
            StreamGeometry triangle = new StreamGeometry();
            using (StreamGeometryContext g = triangle.Open())
            {
                g.BeginFigure(new Point(at.X, at.Y - size), true);
                g.LineTo(new Point(at.X + size, at.Y + size * 0.7));
                g.LineTo(new Point(at.X - size, at.Y + size * 0.7));
                g.EndFigure(true);
            }
            context.DrawGeometry(fill, overlay.Shadow, triangle);
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


    /// <summary>
    /// What the 3D view's camera sees of the ground, the camera itself as a dot, and a line to the
    /// place it looks at.
    /// </summary>
    private void DrawCamera(DrawingContext context, Overlay overlay, EditorCamera camera)
    {
        System.Numerics.Vector2[] corners = camera.Footprint();
        StreamGeometry footprint = new StreamGeometry();
        using (StreamGeometryContext figure = footprint.Open())
        {
            figure.BeginFigure(MapToControl(corners[0].X, corners[0].Y), true);
            for (int i = 1; i < corners.Length; i++)
            {
                figure.LineTo(MapToControl(corners[i].X, corners[i].Y));
            }
            figure.EndFigure(true);
        }
        using (context.PushClip(new Rect(Bounds.Size)))
        {
            context.DrawGeometry(overlay.Fill, overlay.Stroke, footprint);
            System.Numerics.Vector3 eye = camera.Eye;
            (float x, float z, _, _, _) = camera.Now;
            Point at = MapToControl(eye.X, eye.Z);
            context.DrawLine(overlay.Stroke, at, MapToControl(x, z));
            context.DrawEllipse(overlay.Strong, overlay.Stroke, at, 4, 4);
        }
    }

    /// <summary>
    /// The lines between the parts of the symmetry (user story F-12), dashed, over a darker line
    /// so that they show on light and dark terrain.
    /// </summary>
    private void DrawSymmetry(DrawingContext context, Overlay overlay, IReadOnlyList<(System.Numerics.Vector2 From, System.Numerics.Vector2 To)> guides)
    {
        Pen shadow = new Pen(overlay.Shadow.Brush, overlay.Stroke.Thickness * 2);
        Pen line = new Pen(overlay.Stroke.Brush, overlay.Stroke.Thickness, DashStyle.Dash);
        using (context.PushClip(new Rect(Bounds.Size)))
        {
            foreach ((System.Numerics.Vector2 from, System.Numerics.Vector2 to) in guides)
            {
                Point start = MapToControl(from.X, from.Y);
                Point end = MapToControl(to.X, to.Y);
                context.DrawLine(shadow, start, end);
                context.DrawLine(line, start, end);
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
