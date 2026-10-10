using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using FAForever.FileFormats.Mesh;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.Project.Workspaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FAForever.MapEditor.App.Controls;

/// <summary>
/// The picture of an asset (a PNG of the workspace's assets), filling the control and cut to fit,
/// over a checker where it is transparent; or a prop's or unit's mesh (<see cref="Model"/>), drawn
/// with its albedo by <see cref="AssetPreview"/>. It is read and made small (or drawn) on a pool
/// thread when the control first shows, and kept for the next control that shows it
/// (<see cref="Cache"/>), so the shelf reads only the pictures of the tiles in sight.
/// </summary>
public sealed class AssetThumbnail : Control
{
    public static readonly StyledProperty<string?> SourceProperty =
        AvaloniaProperty.Register<AssetThumbnail, string?>(nameof(Source));

    /// <summary>
    /// The width the picture is read at, in pixels.
    /// </summary>
    public static readonly StyledProperty<int> DecodeWidthProperty =
        AvaloniaProperty.Register<AssetThumbnail, int>(nameof(DecodeWidth), 128);

    /// <summary>
    /// The mesh to draw, when there is no <see cref="Source"/>.
    /// </summary>
    public static readonly StyledProperty<AssetModel?> ModelProperty =
        AvaloniaProperty.Register<AssetThumbnail, AssetModel?>(nameof(Model));

    static AssetThumbnail()
    {
        AffectsRender<AssetThumbnail>(SourceProperty, ModelProperty);
    }

    /// <summary>
    /// The PNG to show; null for none.
    /// </summary>
    public string? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public AssetModel? Model
    {
        get => GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    public int DecodeWidth
    {
        get => GetValue(DecodeWidthProperty);
        set => SetValue(DecodeWidthProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceProperty || change.Property == ModelProperty || change.Property == DecodeWidthProperty)
        {
            Load();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ActualThemeVariantChanged += OnThemeChanged;
        Load();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ActualThemeVariantChanged -= OnThemeChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnThemeChanged(object? sender, EventArgs e) => InvalidateVisual();

    private void Load()
    {
        // the cache reads it again when the file changed since (a brush the map maker saved)
        if (VisualRoot is null)
        {
            return;
        }
        if (Source is string source)
        {
            _ = RedrawAfter(Cache.LoadAsync(source, DecodeWidth));
        }
        else if (Model is AssetModel model)
        {
            _ = RedrawAfter(Cache.LoadAsync(model, DecodeWidth));
        }
    }

    private async Task RedrawAfter(Task loading)
    {
        await loading;
        await Dispatcher.UIThread.InvokeAsync(InvalidateVisual);
    }

    public override void Render(DrawingContext context)
    {
        Rect bounds = new Rect(Bounds.Size);
        if ((Source is null && Model is null) || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }
        Bitmap? bitmap = Source is string source ? Cache.Get(source, DecodeWidth)
            : Model is AssetModel model ? Cache.Get(model, DecodeWidth)
            : null;
        // a checker shows what is transparent: decals are partly so; a mesh stands on the tile itself
        if (Source is not null && this.TryFindResource("ControlBrush", ActualThemeVariant, out object? dark) && dark is IBrush darkBrush
            && this.TryFindResource("ControlRaisedBrush", ActualThemeVariant, out object? light) && light is IBrush lightBrush)
        {
            context.FillRectangle(darkBrush, bounds);
            const double square = 8;
            for (double y = 0; y < bounds.Height; y += square)
            {
                for (double x = (y / square % 2) * square; x < bounds.Width; x += 2 * square)
                {
                    context.FillRectangle(lightBrush, new Rect(x, y, Math.Min(square, bounds.Width - x), Math.Min(square, bounds.Height - y)));
                }
            }
        }
        if (bitmap is not null)
        {
            // cut to fill: the middle of the picture in the control's proportions
            Size size = bitmap.Size;
            double scale = Math.Max(bounds.Width / size.Width, bounds.Height / size.Height);
            double width = bounds.Width / scale;
            double height = bounds.Height / scale;
            context.DrawImage(bitmap, new Rect((size.Width - width) / 2, (size.Height - height) / 2, width, height), bounds);
        }
    }

    /// <summary>
    /// The pictures read, small, or drawn, by file (a mesh's with its albedo) and width, with the
    /// time the file was written: the most recent few hundred.
    /// </summary>
    public static class Cache
    {
        private const int Capacity = 600;
        private static readonly object Gate = new object();
        private static readonly Dictionary<(string, int), LinkedListNode<((string, int) Key, Bitmap? Bitmap, DateTime Written)>> Entries = [];
        private static readonly LinkedList<((string, int) Key, Bitmap? Bitmap, DateTime Written)> Order = new();
        private static readonly Dictionary<(string, int), Task> Loading = [];
        private static readonly SemaphoreSlim Readers = new SemaphoreSlim(Math.Max(2, Environment.ProcessorCount / 2));

        /// <summary>
        /// Where a mesh that cannot be drawn is reported, in the category <c>Assets</c>.
        /// </summary>
        public static ILogger Logger { get; set; } = NullLogger.Instance;

        /// <summary>
        /// The pictures being read or drawn; for automation, which waits for them before a settled screenshot.
        /// </summary>
        public static int Pending
        {
            get
            {
                lock (Gate)
                {
                    return Loading.Count;
                }
            }
        }

        /// <summary>
        /// The picture, if it is read; null while it is not (or could not be).
        /// </summary>
        public static Bitmap? Get(string file, int width) => Get((file, width));

        /// <summary>
        /// The picture of a mesh, if it is drawn; null while it is not (or could not be).
        /// </summary>
        public static Bitmap? Get(AssetModel model, int width) => Get((KeyOf(model), width));

        private static Bitmap? Get((string, int) key)
        {
            lock (Gate)
            {
                if (Entries.TryGetValue(key, out var node))
                {
                    Order.Remove(node);
                    Order.AddFirst(node);
                    return node.Value.Bitmap;
                }
                return null;
            }
        }

        /// <summary>
        /// Reads the picture unless it is read (from the file as it is now) or being read; a file
        /// that cannot be read is kept as none.
        /// </summary>
        public static Task LoadAsync(string file, int width) =>
            LoadAsync((file, width), file, "assets.thumbnail", () =>
            {
                using FileStream stream = File.OpenRead(file);
                return Bitmap.DecodeToWidth(stream, width, BitmapInterpolationMode.MediumQuality);
            }, report: false);

        /// <summary>
        /// Draws the picture of a mesh, <paramref name="width"/> pixels square, unless it is drawn
        /// (from the mesh as it is now) or being drawn; a mesh that cannot be read is kept as none.
        /// </summary>
        public static Task LoadAsync(AssetModel model, int width) =>
            LoadAsync((KeyOf(model), width), model.Mesh, "assets.preview", () => ToBitmap(AssetPreview.Render(model, width)), report: true);

        /// <summary>
        /// The variants of a prop share a mesh and differ in their textures.
        /// </summary>
        private static string KeyOf(AssetModel model) => string.Join('|', model.Mesh, model.Albedo, model.Normals, model.Specular, model.Shader);

        private static Task LoadAsync((string, int) key, string file, string span, Func<Bitmap> read, bool report)
        {
            DateTime written = File.GetLastWriteTimeUtc(file);
            lock (Gate)
            {
                if (Entries.TryGetValue(key, out var node))
                {
                    if (node.Value.Written == written)
                    {
                        return Task.CompletedTask;
                    }
                    Entries.Remove(key);
                    Order.Remove(node);
                }
                if (Loading.TryGetValue(key, out Task? loading))
                {
                    return loading;
                }
                Task task = Task.Run(async () =>
                {
                    await Readers.WaitAsync();
                    Bitmap? bitmap = null;
                    try
                    {
                        using Performance.Span measure = Performance.Measure(span);
                        bitmap = read();
                    }
                    catch (Exception exception)
                    {
                        // a file that is no picture or mesh (or gone) shows as none: it is the
                        // asset's own problem; the 3D view reports textures it cannot read, and
                        // this a mesh
                        if (report)
                        {
                            Logger.LogWarning(exception, "Could not draw the picture of {Mesh}", file);
                        }
                    }
                    finally
                    {
                        Readers.Release();
                    }
                    lock (Gate)
                    {
                        Loading.Remove(key);
                        if (Entries.Remove(key, out var stale))
                        {
                            Order.Remove(stale);
                        }
                        Entries[key] = Order.AddFirst((key, bitmap, written));
                        while (Order.Count > Capacity)
                        {
                            // not disposed: a control may be drawing it on the window's thread
                            Entries.Remove(Order.Last!.Value.Key);
                            Order.RemoveLast();
                        }
                    }
                });
                Loading[key] = task;
                return task;
            }
        }

        private static unsafe Bitmap ToBitmap(RgbaImage image)
        {
            // the window draws premultiplied blue, green, red and alpha
            byte[] bgra = new byte[image.Pixels.Length];
            for (int i = 0; i < bgra.Length; i += 4)
            {
                int alpha = image.Pixels[i + 3];
                bgra[i] = (byte)(image.Pixels[i + 2] * alpha / 255);
                bgra[i + 1] = (byte)(image.Pixels[i + 1] * alpha / 255);
                bgra[i + 2] = (byte)(image.Pixels[i] * alpha / 255);
                bgra[i + 3] = (byte)alpha;
            }
            fixed (byte* pixels = bgra)
            {
                return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, (nint)pixels, new PixelSize(image.Width, image.Height), new Vector(96, 96), image.Width * 4);
            }
        }
    }
}
