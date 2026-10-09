using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using FAForever.MapEditor.App.Diagnostics;

namespace FAForever.MapEditor.App.Controls;

/// <summary>
/// The picture of an asset (a PNG of the workspace's assets), filling the control and cut to fit,
/// over a checker where it is transparent. It is read and made small on a pool thread when the
/// control first shows, and kept for the next control that shows it (<see cref="Cache"/>), so the
/// shelf reads only the pictures of the tiles in sight.
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

    static AssetThumbnail()
    {
        AffectsRender<AssetThumbnail>(SourceProperty);
    }

    /// <summary>
    /// The PNG to show; null for none.
    /// </summary>
    public string? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public int DecodeWidth
    {
        get => GetValue(DecodeWidthProperty);
        set => SetValue(DecodeWidthProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceProperty || change.Property == DecodeWidthProperty)
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
        if (Source is string source && VisualRoot is not null)
        {
            _ = LoadAsync(source, DecodeWidth);
        }
    }

    private async Task LoadAsync(string source, int width)
    {
        await Cache.LoadAsync(source, width);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (Source == source)
            {
                InvalidateVisual();
            }
        });
    }

    public override void Render(DrawingContext context)
    {
        Rect bounds = new Rect(Bounds.Size);
        if (Source is not string source || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }
        // a checker shows what is transparent: decals are partly so
        if (this.TryFindResource("ControlBrush", ActualThemeVariant, out object? dark) && dark is IBrush darkBrush
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
        if (Cache.Get(source, DecodeWidth) is Bitmap bitmap)
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
    /// The pictures read, small, by file and width, with the time the file was written: the most
    /// recent few hundred.
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
        /// The picture, if it is read; null while it is not (or could not be).
        /// </summary>
        public static Bitmap? Get(string file, int width)
        {
            lock (Gate)
            {
                if (Entries.TryGetValue((file, width), out var node))
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
        public static Task LoadAsync(string file, int width)
        {
            (string, int) key = (file, width);
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
                        using Performance.Span measure = Performance.Measure("assets.thumbnail");
                        using FileStream stream = File.OpenRead(file);
                        bitmap = Bitmap.DecodeToWidth(stream, width, BitmapInterpolationMode.MediumQuality);
                    }
                    catch (Exception)
                    {
                        // a file that is no picture (or gone) shows as none: it is the asset's
                        // own problem, and the 3D view reports textures it cannot read
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
    }
}
