using FAForever.MapEditor.App.Diagnostics;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using FAForever.MapEditor.App.Rendering;
using FAForever.MapEditor.App.Rendering.Gpu;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;
using FAForever.FileFormats.Map;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.Controls;

/// <summary>
/// The terrain seen from above (<see cref="TerrainPicture"/>), with the grid being painted (a mask,
/// the terrain types) laid over it, and what lies on the map over that: markers, areas, props and units, and
/// the outlines of what is selected. The left mouse button is the active tool's, alt and a click
/// send the 3D camera there, the right or middle button moves the map, the wheel zooms. While it has
/// the keyboard (after a click), the keys it does not use go to the active tool.
/// </summary>
/// <remarks>
/// The picture has one pixel per sample of the heightmap. After a change only what the picture
/// coloured again is copied into the bitmap, so a brush stroke on a large map stays smooth.
/// </remarks>
public sealed partial class HeightmapView : Control
{
    /// <summary>
    /// What the view draws besides the terrain and the markers' own colours, from the theme's
    /// tokens (<c>Styles/Tokens.axaml</c>).
    /// </summary>
    internal sealed record Overlay(IBrush Canvas, IBrush Strong, IPen Brush, IPen Stroke, IBrush Fill, IPen Shadow, IPen Ring, Typeface LabelFont, double LabelSize,
        IBrush Prop, IBrush PropSparse, IBrush PropRim, IBrush Unit, IPen Area, IBrush AreaLabel, IPen Selection, IBrush SelectionFill, IPen Box,
        IBrush Blocked, IBrush NewlyBlocked, IBrush Opened, IPen Wreck, IPen WreckShadow, IPen GridLine, IPen DecalOutline);

    public HeightmapView()
    {
        // a click gives it the keyboard, for the active tool's keys
        Focusable = true;
        // an asset dragged from the shelf lands on the place under the mouse
        AssetDrag.Attach(this, () => _model, at =>
            _model?.ShownDocument is MapDocument document && ControlToMap(at) is { X: >= 0, Y: >= 0 } map
                && map.X <= document.Width && map.Y <= document.Height
                ? map
                : null,
            text => CursorMoved?.Invoke(text));
    }

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
        _inks = null;
        // a mask over the terrain takes the variant's colour
        OnDocumentChanged(this, EventArgs.Empty);
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
        Token<double>("FontSizeMapLabel"),
        Token<IBrush>("MapPropBrush"),
        Token<IBrush>("MapPropSparseBrush"),
        Token<IBrush>("MapPropRimBrush"),
        Token<IBrush>("MapUnitBrush"),
        new Pen(Token<IBrush>("MapAreaBrush"), 1.5, new DashStyle([4, 3], 0)),
        Token<IBrush>("MapAreaBrush"),
        new Pen(Token<IBrush>("MapSelectionBrush"), 2),
        Token<IBrush>("MapSelectionFillBrush"),
        new Pen(Token<IBrush>("MapSelectionBrush"), 1, new DashStyle([4, 3], 0)),
        Token<IBrush>("MapBlockedBrush"),
        Token<IBrush>("MapNewlyBlockedBrush"),
        Token<IBrush>("MapOpenedBrush"),
        new Pen(Token<IBrush>("MapWreckBrush"), 2),
        new Pen(Token<IBrush>("OverlayShadowBrush"), 4),
        new Pen(Token<IBrush>("MapGridLineBrush"), 1),
        new Pen(Token<IBrush>("MapDecalBrush"), 1));

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
    // the view shows the whole map as Fit left it (nobody zoomed or moved it since): a new size fits it again
    private bool _wholeMap;
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
    /// The place of the map (in ogrids) in the middle of the view.
    /// </summary>
    public Point Centre => ControlToMap(new Point(Bounds.Width / 2, Bounds.Height / 2));

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
        _wholeMap = true;
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
        _wholeMap = false;
        InvalidateVisual();
    }

    /// <summary>
    /// Centres the view on a rectangle of the map (in ogrids), as large as fits; a view not laid
    /// out yet takes the zoom when it gets its size.
    /// </summary>
    public void ShowArea(double x0, double z0, double x1, double z1)
    {
        double fit = Bounds.Width > 0 && Bounds.Height > 0
            ? Math.Min(Bounds.Width / Math.Max(1, x1 - x0), Bounds.Height / Math.Max(1, z1 - z0))
            : _zoom;
        Show(fit, (x0 + x1) / 2, (z0 + z1) / 2);
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
            _model.SelectionChanged -= OnOverlayChanged;
            _model.Walkability.Changed -= OnOverlayChanged;
            _model.Handles.Changed -= OnOverlayChanged;
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
            _model.SelectionChanged += OnOverlayChanged;
            _model.Walkability.Changed += OnOverlayChanged;
            _model.Handles.Changed += OnOverlayChanged;
        }
        OnMapChanged(this, EventArgs.Empty);
    }

    // another grid shows over the terrain (a layer's mask, the terrain types): the picture changes, as when the document does
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ShownGrid) || e.PropertyName == nameof(MainViewModel.ShownHeights))
        {
            OnDocumentChanged(sender, e);
        }
        else if (e.PropertyName == nameof(MainViewModel.SelectedMode))
        {
            // the decals' outlines and the tool's shapes depend on the mode
            OnOverlayChanged(sender, e);
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
        if (!_fitted || _wholeMap)
        {
            // laid out for the first time, or showing the whole map (side by side and back, a panel shown): the whole map again
            Fit();
        }
        else
        {
            // a panel or splitter moved (user story V-4) after one zoomed in: the place in the middle stays in the middle
            _offset += new Vector(e.NewSize.Width - e.PreviousSize.Width, e.NewSize.Height - e.PreviousSize.Height) / 2;
            InvalidateVisual();
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

        if (_model is { ShowWalkability: true })
        {
            DrawWalkability(context, overlay, _model.Walkability);
        }
        if (_model is { ShowNavigation: true })
        {
            DrawNavigation(context, overlay, _model.Navigation);
        }

        if (_model?.ShownDocument is MapDocument document)
        {
            using (context.PushClip(new Rect(Bounds.Size)))
            {
                DecalOutlinesDrawn = 0;
                GridLinesDrawn = 0;
                if (_model.ShowDecals)
                {
                    DrawDecals(context, overlay, document, _model.HiddenDecalIds, DecalOutlineMinimum(_model));
                }
                if (_model.ShowGrid)
                {
                    DrawGrid(context, overlay, document, _model.GridSpacing);
                }
                DrawThings(context, overlay, document, _model);
                _markers.Render(context, overlay, _model.DrawnMarkers(document), _model.ShownMarkerKinds, _zoom, _offset);
                DrawGeneratedPathNodes(context, overlay);
                DrawSelection(context, overlay, document, _model.Selection.Items);
            }
        }

        if (_model?.ActiveTool?.Tool is { Box: (float x0, float z0, float x1, float z1) })
        {
            Rect box = new Rect(MapToControl(x0, z0), MapToControl(x1, z1)).Normalize();
            context.DrawRectangle(overlay.SelectionFill, overlay.Box, box);
        }

        if (_model?.ActiveTool?.Tool is { Marks: ToolMarks marks })
        {
            DrawMarks(context, overlay, marks);
        }

        if (_model is { HasMap: true, ActiveTool.Tool: IMapTool shaped })
        {
            DrawShapes(context, shaped.Shapes(ToolViewOf(_cursor)));
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
            using Performance.Span brush = Performance.Measure("top.brush");
            double radius = tool.CursorRadius * _zoom;
            BrushShapeSettings? brushShape = tool is IShapedBrushTool { Shape: { IsShaped: true } shape } ? shape : null;
            float rotation = (float)(brushShape?.Rotation ?? 0);
            // with the symmetry, every image of the brush where a stroke paints (F-12)
            Point map = ControlToMap(cursor);
            IReadOnlyList<SymmetricDab> images = _model.ShownDocument is MapDocument shown
                ? SymmetricDabs.Of(_model.EditTransforms(shown), (float)map.X, (float)map.Y, rotation, brushShape is null)
                : [new SymmetricDab((float)map.X, (float)map.Y, rotation, false, MapSymmetryTransform.Identity)];
            foreach (SymmetricDab image in images)
            {
                DrawBrush(context, overlay, MapToControl(image.X, image.Z), radius, brushShape?.Shown, brushShape is not null, image.Rotation, image.Mirrored);
            }
        }
    }

    /// <summary>
    /// A brush's outline around a point of the view: a circle, or the picture, faded, in its square,
    /// turned as it paints (flipped first when mirrored), with a tick on its top edge.
    /// </summary>
    private void DrawBrush(DrawingContext context, Overlay overlay, Point at, double radius, BrushShape? picture, bool shaped, float rotation, bool mirrored)
    {
        if (!shaped)
        {
            context.DrawEllipse(null, overlay.Brush, at, radius, radius);
            return;
        }
        (double sin, double cos) = Math.SinCos(rotation * Math.PI / 180);
        if (picture is not null && BrushPicture(picture, overlay.Strong) is WriteableBitmap bitmap)
        {
            double longest = Math.Max(picture.Width, picture.Height);
            double width = 2 * radius * picture.Width / longest;
            double height = 2 * radius * picture.Height / longest;
            Matrix flip = mirrored ? Matrix.CreateScale(-1, 1) : Matrix.Identity;
            using (context.PushTransform(flip * Matrix.CreateRotation(rotation * Math.PI / 180) * Matrix.CreateTranslation(at.X, at.Y)))
            {
                context.DrawImage(bitmap, new Rect(-width / 2, -height / 2, width, height));
            }
        }
        Point Corner(double u, double v) => at + new Vector((u * cos - v * sin) * radius, (u * sin + v * cos) * radius);
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

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        // control and the wheel: the brush's radius (shift too: its strength)
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && _model?.BrushWheel(Math.Sign(e.Delta.Y) * Math.Max(1, (int)Math.Round(Math.Abs(e.Delta.Y))), e.KeyModifiers.HasFlag(KeyModifiers.Shift)) == true)
        {
            e.Handled = true;
            return;
        }
        // alt and the wheel: the brush's picture turns (some systems give alt's wheel sideways)
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt) && !e.KeyModifiers.HasFlag(KeyModifiers.Control)
            && _model?.BrushTurnWheel(Tools.BrushKeys.Notches(e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X)) == true)
        {
            e.Handled = true;
            return;
        }
        Point at = e.GetPosition(this);
        Point map = ControlToMap(at);
        _zoom = Math.Clamp(_zoom * Math.Pow(1.2, e.Delta.Y), 0.05, 64);
        _offset = new Vector(at.X - map.X * _zoom, at.Y - map.Y * _zoom);
        _wholeMap = false;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        PointerPointProperties buttons = e.GetCurrentPoint(this).Properties;
        Point at = e.GetPosition(this);
        Focus();
        if (buttons.IsLeftButtonPressed && _model?.HasMap == true && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            // alt and a click: the 3D view looks here
            Point place = ControlToMap(at);
            _model.MoveCameraTo((float)place.X, (float)place.Y);
        }
        else if (buttons.IsLeftButtonPressed && _model is { HasMap: true, ActiveTool.Tool: IMapTool tool })
        {
            // the left mouse is the active tool's
            Point map = ControlToMap(at);
            _pressed = tool;
            tool.Press((float)map.X, (float)map.Y, InputOf(e.KeyModifiers) with { Clicks = Math.Max(1, e.ClickCount) });
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
            _wholeMap = false;
            _panFrom = at;
        }
        else if (_pressed is IMapTool tool)
        {
            Point map = ControlToMap(at);
            tool.Drag((float)map.X, (float)map.Y, InputOf(e.KeyModifiers));
        }
        if (_model is not null)
        {
            Point place = ControlToMap(at);
            _model.Pointer = ((float)place.X, (float)place.Y);
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
        if (_model is not null)
        {
            _model.Pointer = null;
        }
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        _axes |= AxisOf(e.Key);
        // the keys the view does not use itself are the active tool's (the select tool's Q, E, arrows, Escape)
        if (!e.Handled && _model?.ToolKey(e.Key.ToString(), ModifiersOf(e.KeyModifiers)) == true)
        {
            e.Handled = true;
        }
    }

    private ToolInput InputOf(KeyModifiers modifiers) => new ToolInput(ModifiersOf(modifiers) | _axes, (float)(1 / _zoom), View: ToolViewOf(_cursor));

    // X and Z held: moves go along one axis
    private ToolModifiers _axes;

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        _axes &= ~AxisOf(e.Key);
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        _axes = ToolModifiers.None;
    }

    /// <summary>
    /// What a key held changes about a drag: X or Z hold a move to that axis, R turns the
    /// selection; else none.
    /// </summary>
    internal static ToolModifiers AxisOf(Key key) => key switch
    {
        Key.X => ToolModifiers.AxisX,
        Key.Z => ToolModifiers.AxisZ,
        Key.R => ToolModifiers.Turn,
        _ => ToolModifiers.None,
    };

    /// <summary>
    /// The view as a tool sees it: straight down, a place shows where <see cref="MapToControl"/> puts it.
    /// </summary>
    public ToolView ToolViewOf(Point? mouse)
    {
        double zoom = _zoom;
        Vector offset = _offset;
        return new ToolView(true, mouse is Point at ? new System.Numerics.Vector2((float)at.X, (float)at.Y) : null,
            place => new System.Numerics.Vector2((float)(place.X * zoom + offset.X), (float)(place.Z * zoom + offset.Y)),
            _ => (float)(1 / zoom));
    }

    private Dictionary<ToolInk, IBrush>? _inks;

    private IBrush InkOf(ToolInk ink) => (_inks ??= new Dictionary<ToolInk, IBrush>
    {
        [ToolInk.Selection] = Token<IBrush>("MapSelectionBrush"),
        [ToolInk.Fill] = Token<IBrush>("CanvasBrush"),
        [ToolInk.Hover] = Token<IBrush>("OverlayBrush"),
        [ToolInk.AxisX] = Token<IBrush>("MapHandleAxisXBrush"),
        [ToolInk.AxisZ] = Token<IBrush>("MapHandleAxisZBrush"),
        [ToolInk.Shadow] = Token<IBrush>("OverlayShadowBrush"),
        [ToolInk.Sun] = Token<IBrush>("WarningBrush"),
        [ToolInk.Danger] = Token<IBrush>("DangerBrush"),
    })[ink];

    /// <summary>
    /// What the active tool draws at its size on screen (<see cref="IMapTool.Shapes"/>): the handles of the selection.
    /// </summary>
    private void DrawShapes(DrawingContext context, IReadOnlyList<ToolShape> shapes)
    {
        static Point P(System.Numerics.Vector2 v) => new Point(v.X, v.Y);
        foreach (ToolShape shape in shapes)
        {
            switch (shape)
            {
                case ToolLine { Points.Length: >= 2 } line:
                    StreamGeometry path = new StreamGeometry();
                    using (StreamGeometryContext figure = path.Open())
                    {
                        figure.BeginFigure(P(line.Points[0]), false);
                        for (int i = 1; i < line.Points.Length; i++)
                        {
                            figure.LineTo(P(line.Points[i]));
                        }
                        figure.EndFigure(line.Closed);
                    }
                    context.DrawGeometry(null, new Pen(InkOf(line.Ink), line.Width, line.Dash switch
                    {
                        ToolDash.Dashed => ReachDashes(false, line.Width),
                        ToolDash.Dotted => ReachDashes(true, line.Width),
                        _ => null,
                    }), path);
                    break;
                case ToolDisc disc:
                    context.DrawEllipse(InkOf(disc.Fill), new Pen(InkOf(disc.Stroke), 1.5), P(disc.Centre), disc.Radius, disc.Radius);
                    break;
                case ToolSquare square:
                    context.DrawRectangle(InkOf(square.Fill), new Pen(InkOf(square.Stroke), 1.5),
                        new Rect(square.Centre.X - square.Half, square.Centre.Y - square.Half, 2 * square.Half, 2 * square.Half));
                    break;
                case ToolTriangle triangle:
                    StreamGeometry head = new StreamGeometry();
                    using (StreamGeometryContext figure = head.Open())
                    {
                        figure.BeginFigure(P(triangle.A), true);
                        figure.LineTo(P(triangle.B));
                        figure.LineTo(P(triangle.C));
                        figure.EndFigure(true);
                    }
                    context.DrawGeometry(InkOf(triangle.Ink), null, head);
                    break;
            }
        }
    }

    internal static ToolModifiers ModifiersOf(KeyModifiers modifiers) =>
        (modifiers.HasFlag(KeyModifiers.Shift) ? ToolModifiers.Shift : ToolModifiers.None)
        | (modifiers.HasFlag(KeyModifiers.Control) ? ToolModifiers.Control : ToolModifiers.None)
        | (modifiers.HasFlag(KeyModifiers.Alt) ? ToolModifiers.Alt : ToolModifiers.None);

    private WriteableBitmap? _walkability;
    private (NavGrid Grid, WalkabilityChanges? Changes, Overlay Overlay)? _walkabilityOf;

    /// <summary>
    /// Walkability (user story T-7) over the terrain, one pixel an ogrid: the ogrids units of the
    /// chosen layer cannot go tinted, and, while the changes show, those blocked or opened since the
    /// time before in strong colours. Made again only for another result, layer or theme.
    /// </summary>
    private void DrawWalkability(DrawingContext context, Overlay overlay, WalkabilityViewModel walkability)
    {
        if (walkability.Grid is not NavGrid grid)
        {
            return;
        }
        WalkabilityChanges? changes = walkability.ShowChanges && walkability.Changes is { } shown && shown.Width == grid.Width && shown.Height == grid.Height
            ? shown
            : null;
        if (_walkabilityOf is not { } made || !ReferenceEquals(made.Grid, grid) || !ReferenceEquals(made.Changes, changes) || !ReferenceEquals(made.Overlay, overlay))
        {
            using Performance.Span measure = Performance.Measure("top.walkability");
            _walkability?.Dispose();
            _walkability = WalkabilityPicture(grid, changes, overlay);
            _walkabilityOf = (grid, changes, overlay);
        }
        if (_walkability is null)
        {
            return;
        }
        Rect destination = new Rect(_offset.X, _offset.Y, grid.Width * _zoom, grid.Height * _zoom);
        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = _zoom >= 2 ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality }))
        {
            context.DrawImage(_walkability, new Rect(0, 0, grid.Width, grid.Height), destination);
        }
    }

    private static unsafe WriteableBitmap? WalkabilityPicture(NavGrid grid, WalkabilityChanges? changes, Overlay overlay)
    {
        if (overlay.Blocked is not ISolidColorBrush blocked || overlay.NewlyBlocked is not ISolidColorBrush newlyBlocked || overlay.Opened is not ISolidColorBrush opened)
        {
            return null;
        }
        static uint Premultiplied(Color color) =>
            (uint)color.A << 24 | (uint)(color.R * color.A / 255) << 16 | (uint)(color.G * color.A / 255) << 8 | (uint)(color.B * color.A / 255);
        uint blockedPixel = Premultiplied(blocked.Color);
        uint newlyBlockedPixel = Premultiplied(newlyBlocked.Color);
        uint openedPixel = Premultiplied(opened.Color);

        WriteableBitmap bitmap = new WriteableBitmap(new PixelSize(grid.Width, grid.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using ILockedFramebuffer frame = bitmap.Lock();
        nint address = frame.Address;
        int rowBytes = frame.RowBytes;
        int[] cells = grid.Cells;
        WalkabilityChange[]? changed = changes?.Cells;
        int width = grid.Width;
        Parallel.For(0, grid.Height, z =>
        {
            uint* row = (uint*)(address + z * rowBytes);
            for (int x = 0; x < width; x++)
            {
                int i = z * width + x;
                row[x] = changed?[i] switch
                {
                    WalkabilityChange.Opened => openedPixel,
                    WalkabilityChange.Blocked => newlyBlockedPixel,
                    _ => cells[i] > 0 ? 0u : blockedPixel,
                };
            }
        });
        return bitmap;
    }

    /// <summary>
    /// The areas as dashed rectangles with their names, the props as dots and the units (see <see cref="DrawUnits"/>),
    /// each when its overlay is on; only what lies in the view.
    /// </summary>
    private void DrawThings(DrawingContext context, Overlay overlay, MapDocument document, MainViewModel model)
    {
        using Performance.Span measure = Performance.Measure("top.things");
        Rect shown = new Rect(Bounds.Size).Inflate(8);
        if (model.ShowAreas)
        {
            string? playable = document.PlayableArea?.Name;
            for (int i = 0; i < document.Markers.Areas.Length; i++)
            {
                Area area = document.Markers.Areas[i];
                if (!model.IsDrawn(ObjectRef.Area(area.Name)))
                {
                    continue;
                }
                Rect rect = new Rect(MapToControl(area.X0, area.Z0), MapToControl(area.X1, area.Z1)).Normalize();
                context.DrawRectangle(null, overlay.Area, rect);
                // the playable area: the one the scenario names, else the first (user stories M-3, M-5)
                FormattedText name = new FormattedText(area.Name == playable ? $"{area.Name} (playable area)" : area.Name, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, overlay.LabelFont, overlay.LabelSize, overlay.AreaLabel);
                context.DrawText(name, rect.TopLeft + new Vector(4, 2));
            }
        }
        if (model.ShowProps)
        {
            DrawProps(context, overlay, document, shown, model);
        }
        if (model.ShowUnits)
        {
            DrawUnits(context, overlay, document, model, shown);
        }
    }

    /// <summary>
    /// The grid's lines (user story V-3) every <paramref name="spacing"/> ogrids over the map, in
    /// view; none while they would lie closer than a few pixels.
    /// </summary>
    private void DrawGrid(DrawingContext context, Overlay overlay, MapDocument document, int spacing)
    {
        using Performance.Span measure = Performance.Measure("top.grid");
        GridLinesDrawn = 0;
        if (spacing <= 0 || spacing * _zoom < MinGridLineGap)
        {
            return;
        }
        Point topLeft = ControlToMap(new Point(0, 0));
        Point bottomRight = ControlToMap(new Point(Bounds.Width, Bounds.Height));
        double x0 = Math.Max(0, Math.Ceiling(topLeft.X / spacing) * spacing);
        double z0 = Math.Max(0, Math.Ceiling(topLeft.Y / spacing) * spacing);
        double x1 = Math.Min(document.Width, bottomRight.X);
        double z1 = Math.Min(document.Height, bottomRight.Y);
        double top = Math.Max(0, topLeft.Y);
        double bottom = Math.Min(document.Height, bottomRight.Y);
        double left = Math.Max(0, topLeft.X);
        double right = Math.Min(document.Width, bottomRight.X);
        StreamGeometry lines = new StreamGeometry();
        int drawn = 0;
        using (StreamGeometryContext geometry = lines.Open())
        {
            for (double x = x0; x <= x1; x += spacing)
            {
                // on the pixel's middle, so a line a pixel wide stays sharp
                double at = Math.Round(MapToControl(x, 0).X) + 0.5;
                geometry.BeginFigure(new Point(at, MapToControl(0, top).Y), false);
                geometry.LineTo(new Point(at, MapToControl(0, bottom).Y));
                geometry.EndFigure(false);
                drawn++;
            }
            for (double z = z0; z <= z1; z += spacing)
            {
                double at = Math.Round(MapToControl(0, z).Y) + 0.5;
                geometry.BeginFigure(new Point(MapToControl(left, 0).X, at), false);
                geometry.LineTo(new Point(MapToControl(right, 0).X, at));
                geometry.EndFigure(false);
                drawn++;
            }
        }
        context.DrawGeometry(null, overlay.GridLine, lines);
        GridLinesDrawn = drawn;
    }

    /// <summary>
    /// The fewest pixels between the grid's lines: closer, the top view draws none.
    /// </summary>
    public const double MinGridLineGap = 4;

    /// <summary>
    /// How many of the grid's lines the last frame drew (for tests and automation).
    /// </summary>
    public int GridLinesDrawn { get; private set; }

    /// <summary>
    /// The decals (user story V-3) as faint outlines, each turned as it lies (its rectangle from its
    /// corner along its two edges, as the 3D view draws it); only those in view and not hidden,
    /// and none smaller than <paramref name="minimum"/> pixels (<see cref="DecalOutlineMinimum"/>).
    /// </summary>
    private void DrawDecals(DrawingContext context, Overlay overlay, MapDocument document, IReadOnlySet<int> hidden, double minimum)
    {
        using Performance.Span measure = Performance.Measure("top.decals");
        Rect shown = new Rect(Bounds.Size);
        StreamGeometry outlines = new StreamGeometry();
        int drawn = 0;
        using (StreamGeometryContext geometry = outlines.Open())
        {
            foreach (Decal decal in document.Decals)
            {
                if (hidden.Contains(decal.Id) || Math.Max(Math.Abs(decal.Scale.X), Math.Abs(decal.Scale.Z)) * _zoom < minimum)
                {
                    continue;
                }
                (float sin, float cos) = MathF.SinCos(decal.Rotation.Y);
                double cx = decal.Position.X;
                double cz = decal.Position.Z;
                double ux = decal.Scale.X * cos, uz = decal.Scale.X * sin;
                double vx = -decal.Scale.Z * sin, vz = decal.Scale.Z * cos;
                Point a = MapToControl(cx, cz);
                Point b = MapToControl(cx + ux, cz + uz);
                Point c = MapToControl(cx + ux + vx, cz + uz + vz);
                Point d = MapToControl(cx + vx, cz + vz);
                double minX = Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X));
                double maxX = Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X));
                double minY = Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y));
                double maxY = Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y));
                if (maxX < shown.Left || minX > shown.Right || maxY < shown.Top || minY > shown.Bottom)
                {
                    continue;
                }
                geometry.BeginFigure(a, false);
                geometry.LineTo(b);
                geometry.LineTo(c);
                geometry.LineTo(d);
                geometry.EndFigure(true);
                drawn++;
            }
        }
        // what sticks out past the map's edge is cut off at it
        Point corner = MapToControl(0, 0);
        Point opposite = MapToControl(document.Width, document.Height);
        using (context.PushClip(new Rect(corner, opposite)))
        {
            context.DrawGeometry(null, overlay.DecalOutline, outlines);
        }
        DecalOutlinesDrawn = drawn;
    }

    /// <summary>
    /// The fewest pixels a decal's longer side takes for the top view to draw its outline in the
    /// Decals mode, where one works with them.
    /// </summary>
    public const double MinDecalSize = 8;

    /// <summary>
    /// The fewest pixels a decal's longer side takes for its outline in the other modes: on a map
    /// covered in decals (Archsimkats Valley's 3634) the outlines of all of them would hide the
    /// terrain, so only the large ones show from afar and the rest come as one zooms in.
    /// </summary>
    public const double MinDecalSizeOutsideDecals = 40;

    /// <summary>
    /// How large a decal must show for its outline: <see cref="MinDecalSize"/> in the Decals mode,
    /// else <see cref="MinDecalSizeOutsideDecals"/>.
    /// </summary>
    public static double DecalOutlineMinimum(MainViewModel model) =>
        model.SelectedMode?.Definition.Id == "decals" ? MinDecalSize : MinDecalSizeOutsideDecals;

    /// <summary>
    /// How many decal outlines the last frame drew (for tests and automation).
    /// </summary>
    public int DecalOutlinesDrawn { get; private set; }

    /// <summary>
    /// Below this zoom (pixels an ogrid) the props are drawn sparse: smaller, at most one dot per
    /// <see cref="PropCell"/> pixels square, so a forest of thousands of props does not hide the
    /// terrain. Each dot lies on a dark rim, so it shows on bright and green ground alike. Picking
    /// takes every prop whatever is drawn.
    /// </summary>
    public const double PropSparseZoom = 2;

    private const int PropCell = 4;

    /// <summary>
    /// The props as dots, sparse when zoomed out (<see cref="PropSparseZoom"/>); the number drawn is
    /// <see cref="PropDotsDrawn"/>.
    /// </summary>
    private void DrawProps(DrawingContext context, Overlay overlay, MapDocument document, Rect shown, MainViewModel model)
    {
        bool sparse = _zoom < PropSparseZoom;
        HashSet<long>? taken = sparse ? [] : null;
        double size = sparse ? 2 : 3;
        double rim = size + 1.5;
        IBrush brush = sparse ? overlay.PropSparse : overlay.Prop;
        int drawn = 0;
        foreach (Prop prop in document.Props)
        {
            Point at = MapToControl(prop.Position.X, prop.Position.Z);
            if (!shown.Contains(at)
                || !model.IsDrawn(ObjectRef.Prop(prop.Id))
                || (taken is not null && !taken.Add(((long)Math.Floor(at.X / PropCell) << 32) | (uint)(int)Math.Floor(at.Y / PropCell))))
            {
                continue;
            }
            context.FillRectangle(overlay.PropRim, new Rect(at.X - rim / 2, at.Y - rim / 2, rim, rim));
            context.FillRectangle(brush, new Rect(at.X - size / 2, at.Y - size / 2, size, size));
            drawn++;
        }
        PropDotsDrawn = drawn;
    }

    /// <summary>
    /// How many prop dots the last frame drew (for tests and automation).
    /// </summary>
    public int PropDotsDrawn { get; private set; }

    /// <summary>
    /// The dashes of a curve's width line (long) and fade line (dots), in lengths of a pen that is
    /// <paramref name="thickness"/> wide, so a shadow under a line breaks where it does.
    /// </summary>
    private static DashStyle ReachDashes(bool dotted, double thickness) =>
        dotted ? new DashStyle([1.5 / thickness, 3.5 / thickness], 0) : new DashStyle([6 / thickness, 4 / thickness], 0);

    /// <summary>
    /// What the active tool shows (<see cref="IMapTool.Marks"/>): its line with the label at its
    /// end, such as the ruler's measurement, and its handles, such as a selected area's corners.
    /// </summary>
    private void DrawMarks(DrawingContext context, Overlay overlay, ToolMarks marks)
    {
        StreamGeometry? Line(IReadOnlyList<(float X, float Z)> path)
        {
            if (path.Count < 2)
            {
                return null;
            }
            StreamGeometry line = new StreamGeometry();
            using (StreamGeometryContext figure = line.Open())
            {
                figure.BeginFigure(MapToControl(path[0].X, path[0].Z), false);
                for (int i = 1; i < path.Count; i++)
                {
                    figure.LineTo(MapToControl(path[i].X, path[i].Z));
                }
                figure.EndFigure(false);
            }
            return line;
        }
        foreach (IReadOnlyList<(float X, float Z)> image in marks.Images ?? [])
        {
            // the images of a mirrored curve: the guide's dashes over a shadow
            if (Line(image) is StreamGeometry line)
            {
                context.DrawGeometry(null, overlay.Shadow, line);
                context.DrawGeometry(null, new Pen(overlay.Selection.Brush, overlay.Selection.Thickness, DashStyle.Dash), line);
            }
        }
        foreach (MarkOutline outline in marks.Outlines ?? [])
        {
            // how far a curve reaches (T-4): the width's edge dashed, the fade's end dotted, an image thinner
            if (Line(outline.Points) is StreamGeometry line)
            {
                double thickness = outline.Thin ? 1 : 1.5;
                context.DrawGeometry(null, new Pen(overlay.Shadow.Brush, thickness + 1.5, ReachDashes(outline.Dotted, thickness + 1.5)), line);
                context.DrawGeometry(null, new Pen(overlay.Selection.Brush, thickness, ReachDashes(outline.Dotted, thickness)), line);
            }
        }
        if (marks.Path is { Count: >= 2 } path && Line(path) is StreamGeometry pathLine)
        {
            using (context.PushTransform(Matrix.CreateTranslation(1, 1)))
            {
                context.DrawGeometry(null, overlay.Shadow, pathLine);
            }
            context.DrawGeometry(null, overlay.Selection, pathLine);
        }
        if (marks.Line is (float x0, float z0, float x1, float z1))
        {
            Point from = MapToControl(x0, z0);
            Point to = MapToControl(x1, z1);
            context.DrawLine(overlay.Shadow, from + new Vector(1, 1), to + new Vector(1, 1));
            context.DrawLine(overlay.Selection, from, to);
            if (marks.Label is { Length: > 0 } label)
            {
                FormattedText text = new FormattedText(label, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, overlay.LabelFont, overlay.LabelSize, overlay.Strong);
                // beside the line's end, kept inside the view (left of the end near the right edge, above it near the bottom)
                Size size = new Size(text.Width + 6, text.Height + 2);
                double left = to.X + 8 + size.Width <= Bounds.Width ? to.X + 8 : to.X - 8 - size.Width;
                double top = to.Y + 4 + size.Height <= Bounds.Height ? to.Y + 4 : to.Y - 4 - size.Height;
                Rect back = new Rect(new Point(Math.Clamp(left, 0, Math.Max(0, Bounds.Width - size.Width)), Math.Clamp(top, 0, Math.Max(0, Bounds.Height - size.Height))), size);
                context.FillRectangle(overlay.Canvas, back);
                context.DrawText(text, back.TopLeft + new Vector(3, 1));
            }
        }
        for (int i = 0; i < marks.Handles.Count; i++)
        {
            Point at = MapToControl(marks.Handles[i].X, marks.Handles[i].Z);
            if (marks.ChosenHandles is null)
            {
                context.DrawRectangle(overlay.Canvas, overlay.Selection, new Rect(at.X - 4, at.Y - 4, 8, 8));
                continue;
            }
            // a curve's points: round, a sharp corner square, a chosen point larger and filled
            bool chosen = marks.ChosenHandles.Contains(i);
            double half = chosen ? 6 : 4;
            IBrush fill = chosen ? overlay.Selection.Brush! : overlay.Canvas;
            if (marks.CornerHandles?.Contains(i) == true)
            {
                context.DrawRectangle(fill, overlay.Selection, new Rect(at.X - half, at.Y - half, 2 * half, 2 * half));
            }
            else
            {
                context.DrawEllipse(fill, overlay.Selection, at, half, half);
            }
        }
    }

    /// <summary>
    /// An outline around each selected thing: its rectangle for a decal or an area, a ring for the others.
    /// </summary>
    private void DrawSelection(DrawingContext context, Overlay overlay, MapDocument document, IReadOnlyList<ObjectRef> selected)
    {
        if (selected.Count == 0)
        {
            return;
        }
        using Performance.Span measure = Performance.Measure("top.selection");
        Rect shown = new Rect(Bounds.Size).Inflate(16);
        foreach (ObjectRef thing in selected)
        {
            System.Numerics.Vector2[] outline = document.OutlineOf(thing);
            if (outline.Length > 0)
            {
                StreamGeometry shape = new StreamGeometry();
                using (StreamGeometryContext figure = shape.Open())
                {
                    figure.BeginFigure(MapToControl(outline[0].X, outline[0].Y), true);
                    for (int i = 1; i < outline.Length; i++)
                    {
                        figure.LineTo(MapToControl(outline[i].X, outline[i].Y));
                    }
                    figure.EndFigure(true);
                }
                context.DrawGeometry(overlay.SelectionFill, overlay.Selection, shape);
            }
            else if (document.PlaceOf(thing) is System.Numerics.Vector3 place)
            {
                Point at = MapToControl(place.X, place.Z);
                if (shown.Contains(at))
                {
                    double radius = document.Find(thing) is Spawn ? 13 : 8;
                    context.DrawEllipse(null, overlay.Selection, at, radius, radius);
                }
            }
        }
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
        MapDocument? document = _model?.ViewDocument;
        if (document is null)
        {
            _bitmap?.Dispose();
            _bitmap = null;
            InvalidateVisual();
            return;
        }

        if (_colouring)
        {
            // the whole picture is being coloured on the pool: this version follows when it is done
            _colourAgain = true;
            return;
        }
        GridOverlay? grid = _model!.ShownGrid;
        _picture.MaskColour = Token<ISolidColorBrush>("MapMaskBrush").Color.ToUInt32();
        if ((long)document.Terrain.Heightmap.Samples.Width * document.Terrain.Heightmap.Samples.Height >= ColourOnPool
            && _picture.ColoursAll(document, grid))
        {
            ColourOnThePool(document, grid);
            return;
        }
        Show(_picture.Update(document, grid));
    }

    /// <summary>
    /// From this many samples on (a map of 20 km and more) a picture coloured anew is coloured on a
    /// thread of the pool: an 81 km map's takes a few hundred milliseconds.
    /// </summary>
    private const long ColourOnPool = 1025L * 1025;

    private bool _colouring;
    private bool _colourAgain;

    /// <summary>
    /// Whether the whole picture is being coloured on a thread of the pool (a large map opened):
    /// automation's settled screenshot waits for it.
    /// </summary>
    public bool IsColouring => _colouring;

    /// <summary>
    /// Colours the whole picture on a thread of the pool, shows it, then the version of the map
    /// shown by then (a change made meanwhile only colours its tiles).
    /// </summary>
    private void ColourOnThePool(MapDocument document, GridOverlay? grid)
    {
        _colouring = true;
        _ = Task.Run(() =>
        {
            using Performance.Span measure = Performance.Measure("top.colour-all");
            return _picture.Update(document, grid);
        }).ContinueWith(task => Dispatcher.UIThread.Post(() =>
        {
            _colouring = false;
            if (task.IsCompletedSuccessfully)
            {
                Show(task.Result);
            }
            else if (task.Exception is not null)
            {
                _model?.Logger("Views").LogError(task.Exception, "Colouring the top view's picture failed: {Reason}", task.Exception.Message);
            }
            if (_colourAgain || !ReferenceEquals(_model?.ViewDocument, document))
            {
                _colourAgain = false;
                OnDocumentChanged(this, EventArgs.Empty);
            }
        }), TaskScheduler.Default);
    }

    /// <summary>
    /// Puts the rectangles of the picture that were coloured into the bitmap and draws again.
    /// </summary>
    private void Show(IReadOnlyList<SampleRect> changed)
    {
        if (_bitmap is null || _bitmap.PixelSize.Width != _picture.Width || _bitmap.PixelSize.Height != _picture.Height)
        {
            _bitmap?.Dispose();
            _bitmap = new WriteableBitmap(new PixelSize(_picture.Width, _picture.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            changed = [new SampleRect(0, 0, _picture.Width - 1, _picture.Height - 1)];
        }
        Copy(changed);
        if (!_fitted)
        {
            // a map opened: the whole of it, also when its picture was coloured on the pool after the map changed
            Fit();
        }
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
        private List<(PathLayer Layer, Color32 Color, StreamGeometry Lines)> _links = [];

        public void Render(DrawingContext context, Overlay overlay, MarkerSet markers, MarkerKinds shown, double zoom, Vector offset)
        {
            Point At(Marker marker) => new Point(marker.Position.X * zoom + offset.X, marker.Position.Z * zoom + offset.Y);

            bool Shows(Marker marker) => (shown & MarkerEdits.KindOf(marker)) != 0;

            if ((shown & MarkerKinds.PathNodes) != 0)
            {
                // the links in map units, drawn scaled: built once per version of the markers
                if (!ReferenceEquals(markers, _linked))
                {
                    _links = Links(markers);
                    _linked = markers;
                }
                using (context.PushTransform(Matrix.CreateScale(zoom, zoom) * Matrix.CreateTranslation(offset.X, offset.Y)))
                {
                    foreach ((PathLayer layer, Color32 color, StreamGeometry lines) in _links)
                    {
                        if ((shown & MarkerEdits.KindOf(layer)) != 0)
                        {
                            context.DrawGeometry(null, LinkPen(color, 1 / zoom), lines);
                        }
                    }
                }
                foreach (PathNode node in markers.PathNodes.Where(Shows))
                {
                    context.DrawEllipse(Fill(node.Color), overlay.Shadow, At(node), 2.5, 2.5);
                }
            }

            if ((shown & ~MarkerKinds.PathNodes) == 0)
            {
                return;
            }

            foreach (WeatherGenerator weather in markers.WeatherGenerators.Where(Shows))
            {
                Triangle(context, overlay, Fill(weather.Color), At(weather), 6);
            }
            foreach (WeatherDefinition weather in markers.WeatherDefinitions.Where(Shows))
            {
                Triangle(context, overlay, Fill(weather.Color), At(weather), 8);
            }
            foreach (AiMarker marker in markers.AiMarkers.Where(Shows))
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
                context.DrawGeometry(Fill(MarkersViewModel.ShownColour(marker)), overlay.Shadow, diamond);
            }
            foreach (ResourceMarker resource in markers.Resources.Where(Shows))
            {
                double half = resource.Kind == ResourceKind.Mass ? 4 : 6;
                Point at = At(resource);
                context.DrawRectangle(Fill(resource.Color), overlay.Shadow, new Rect(at.X - half, at.Y - half, half * 2, half * 2));
            }
            foreach (Spawn spawn in markers.Spawns.Where(Shows))
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
        /// The links between path nodes as lines in map units, one geometry per layer, with its colour;
        /// a link both ways is drawn once.
        /// </summary>
        private static List<(PathLayer, Color32, StreamGeometry)> Links(MarkerSet markers)
        {
            Dictionary<int, PathNode> nodes = markers.PathNodes.ToDictionary(node => node.Id);
            List<(PathLayer, Color32, StreamGeometry)> links = [];
            foreach (IGrouping<PathLayer, PathNode> layer in markers.PathNodes.GroupBy(node => node.Layer))
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
                links.Add((layer.Key, layer.First().Color, lines));
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
    /// What the 3D view's camera sees of the map's ground (cut to the map: beyond its edge the
    /// footprint would lie over the view's backdrop like a grey slab), the camera itself as a dot,
    /// and a line to the place it looks at.
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
        Rect map = _model?.ShownDocument is MapDocument document
            ? new Rect(MapToControl(0, 0), MapToControl(document.Width, document.Height)).Intersect(new Rect(Bounds.Size))
            : new Rect(Bounds.Size);
        using (context.PushClip(map))
        {
            context.DrawGeometry(overlay.Fill, overlay.Stroke, footprint);
        }
        using (context.PushClip(new Rect(Bounds.Size)))
        {
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
        MapDocument? document = _model?.ViewDocument;
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
