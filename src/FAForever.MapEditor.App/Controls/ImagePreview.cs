using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.Rendering;

namespace FAForever.MapEditor.App.Controls;

/// <summary>
/// A small picture (about 32 pixels, its size the layout's) of a file or a grid of the document
/// (<see cref="PreviewPicture"/>: a mask, a layer, a heightmap with a height ramp), with a larger
/// one (up to <see cref="LargeSize"/> pixels) in its tool tip while the mouse rests on it. The
/// pictures are made once per version of their grid and size and kept while the grid lives
/// (<see cref="Cache"/>), so a list of previews costs nothing until a grid changes.
/// </summary>
/// <remarks>
/// Use it for every picture in a panel or inspector: each layer of a stack (the stack control),
/// masks, the draft and final heightmaps, stratum textures (a <see cref="FilePicture"/>), the
/// water's maps, map-wide decals and cube maps.
/// </remarks>
public sealed class ImagePreview : Control
{
    public static readonly StyledProperty<PreviewPicture?> PictureProperty =
        AvaloniaProperty.Register<ImagePreview, PreviewPicture?>(nameof(Picture));

    /// <summary>
    /// The longer side of the picture in the tool tip, in pixels; 0 for no tool tip.
    /// </summary>
    public static readonly StyledProperty<int> LargeSizeProperty =
        AvaloniaProperty.Register<ImagePreview, int>(nameof(LargeSize), 512);

    static ImagePreview()
    {
        AffectsRender<ImagePreview>(PictureProperty);
    }

    public ImagePreview()
    {
        // beside the panel, over the views: under the pointer the large picture would hide the list
        ToolTip.SetPlacement(this, PlacementMode.Right);
    }

    public PreviewPicture? Picture
    {
        get => GetValue(PictureProperty);
        set => SetValue(PictureProperty, value);
    }

    public int LargeSize
    {
        get => GetValue(LargeSizeProperty);
        set => SetValue(LargeSizeProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PictureProperty || change.Property == LargeSizeProperty)
        {
            UpdateTip();
        }
    }

    /// <summary>
    /// The tool tip holds a large preview of the same picture, drawn only when it first shows; the
    /// large one has no tool tip of its own (its <see cref="LargeSize"/> is set first).
    /// </summary>
    private void UpdateTip()
    {
        if (Picture is not PreviewPicture picture || LargeSize <= 0)
        {
            ToolTip.SetTip(this, null);
            return;
        }
        (int width, int height) = picture.GridSize is (int w, int h) ? Fit(w, h, LargeSize) : (LargeSize, LargeSize);
        ToolTip.SetTip(this, new ImagePreview { LargeSize = 0, Picture = picture, Width = width, Height = height });
    }

    private static (int Width, int Height) Fit(int width, int height, int size)
    {
        int longer = Math.Max(1, Math.Max(width, height));
        return (Math.Max(1, width * size / longer), Math.Max(1, height * size / longer));
    }

    public override void Render(DrawingContext context)
    {
        Rect bounds = new Rect(Bounds.Size);
        if (Picture is not PreviewPicture picture || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }
        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        int size = (int)Math.Ceiling(Math.Max(bounds.Width, bounds.Height) * scaling);
        if (Cache.Get(picture, size) is not Bitmap bitmap)
        {
            return;
        }
        // the whole picture in its proportions, in the middle
        Size pixels = bitmap.Size;
        double scale = Math.Min(bounds.Width / pixels.Width, bounds.Height / pixels.Height);
        Size shown = new Size(pixels.Width * scale, pixels.Height * scale);
        Rect target = new Rect((bounds.Width - shown.Width) / 2, (bounds.Height - shown.Height) / 2, shown.Width, shown.Height);
        context.DrawImage(bitmap, new Rect(pixels), target);
    }

    /// <summary>
    /// The pictures made: per grid (weakly, so a grid no version holds any more takes its pictures
    /// along) by how it is drawn and size; per file by its path, size and the time it was written.
    /// </summary>
    public static class Cache
    {
        private const int FileCapacity = 128;
        private static readonly ConditionalWeakTable<object, Dictionary<(string Variant, int Size), Bitmap?>> Grids = [];
        private static readonly Dictionary<(string Path, int Size), (Bitmap? Bitmap, DateTime Written)> Files = [];
        private static readonly object Gate = new object();

        /// <summary>
        /// How many pictures of grids were made; for tests, which check that a preview is made once
        /// per version.
        /// </summary>
        public static int Made { get; private set; }

        /// <summary>
        /// The picture at a size (rounded up to a step, so a few sizes serve every control), made
        /// when it is not kept yet; null when there is nothing to draw.
        /// </summary>
        public static Bitmap? Get(PreviewPicture picture, int size)
        {
            size = Step(size);
            if (picture is FilePicture file)
            {
                return GetFile(file.Path, size);
            }
            lock (Gate)
            {
                Dictionary<(string, int), Bitmap?> pictures = Grids.GetOrCreateValue(picture.Key);
                if (pictures.TryGetValue((picture.Variant, size), out Bitmap? kept))
                {
                    return kept;
                }
                using Performance.Span span = Performance.Measure("preview.render");
                Bitmap? made = picture.Render(size) is (uint[] pixels, int width, int height) ? ToBitmap(pixels, width, height) : null;
                pictures[(picture.Variant, size)] = made;
                Made++;
                return made;
            }
        }

        private static int Step(int size) => size <= 32 ? 32 : size <= 64 ? 64 : size <= 128 ? 128 : size <= 256 ? 256 : 512;

        private static Bitmap? GetFile(string path, int size)
        {
            DateTime written = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            lock (Gate)
            {
                if (Files.TryGetValue((path, size), out (Bitmap? Bitmap, DateTime Written) kept) && kept.Written == written)
                {
                    return kept.Bitmap;
                }
                Bitmap? bitmap = null;
                if (written != DateTime.MinValue)
                {
                    try
                    {
                        using FileStream stream = File.OpenRead(path);
                        bitmap = Bitmap.DecodeToWidth(stream, size);
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
                    {
                        bitmap = null;
                    }
                }
                if (Files.Count >= FileCapacity)
                {
                    Files.Clear();
                }
                Files[(path, size)] = (bitmap, written);
                return bitmap;
            }
        }

        private static WriteableBitmap ToBitmap(uint[] pixels, int width, int height)
        {
            WriteableBitmap bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
            using ILockedFramebuffer frame = bitmap.Lock();
            int[] row = new int[width];
            for (int y = 0; y < height; y++)
            {
                Buffer.BlockCopy(pixels, y * width * 4, row, 0, width * 4);
                Marshal.Copy(row, 0, frame.Address + y * frame.RowBytes, width);
            }
            return bitmap;
        }
    }
}
