using System.Collections.Immutable;
using System.Runtime.InteropServices;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.Project.Workspaces;
using ImageMagick;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// PNG files on disk that show the map's own pictures on the shelf and in the picker
/// (<see cref="MapOwnAssets"/>): the tiles read pictures from files, and the map's own files live in
/// the document. A PNG is written as it is; any other picture (a DDS) is made small into a PNG on a
/// thread of the pool, after which <see cref="Ready"/> is raised on the thread that asked. Each file's
/// bytes are written once: a picture is known by its path and its bytes (the same array).
/// </summary>
public sealed class MapOwnPictures
{
    /// <summary>
    /// The longest side of a picture made from a DDS or another format.
    /// </summary>
    private const int Largest = 256;

    /// <summary>
    /// The largest file made into a picture: a map-wide decal of 8192 pixels (90 MB as DDS) would take
    /// hundreds of megabytes to decode, so its tile shows the kind's icon.
    /// </summary>
    private const int LargestFile = 24 * 1024 * 1024;

    private readonly string _folder;
    private readonly ILogger _log;
    private readonly Dictionary<string, (ImmutableArray<byte> Bytes, string? Png)> _known = new Dictionary<string, (ImmutableArray<byte>, string?)>(StringComparer.Ordinal);
    private int _next;

    /// <param name="folder">Where the pictures go; emptied first.</param>
    public MapOwnPictures(string folder, ILogger log)
    {
        _folder = folder;
        _log = log;
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log.LogWarning(exception, "Could not empty the folder of the map's own pictures {Folder}", folder);
        }
    }

    /// <summary>
    /// The folder of this process's pictures in the system's temporary folder.
    /// </summary>
    public static string DefaultFolder => Path.Combine(Path.GetTempPath(), "faf-map-editor", "map-files", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>
    /// Raised when a picture made on the pool is ready, on the thread that asked for it.
    /// </summary>
    public event Action? Ready;

    /// <summary>
    /// Whether pictures are still being made.
    /// </summary>
    public int Pending { get; private set; }

    /// <summary>
    /// The PNG that shows the map's file <paramref name="path"/> with these bytes; null while it is
    /// being made (or when it cannot be).
    /// </summary>
    public string? PictureOf(string path, ImmutableArray<byte> bytes)
    {
        if (_known.TryGetValue(path, out (ImmutableArray<byte> Bytes, string? Png) known) && known.Bytes == bytes)
        {
            return known.Png;
        }
        string png = Path.Combine(_folder, $"{_next++}-{Path.GetFileNameWithoutExtension(path)}.png");
        byte[] content = ImmutableCollectionsMarshal.AsArray(bytes)!;
        if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                Directory.CreateDirectory(_folder);
                File.WriteAllBytes(png, content);
                _known[path] = (bytes, png);
                return png;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _log.LogWarning(exception, "Could not write the picture of the map's file {Path} to {File}", path, png);
                _known[path] = (bytes, null);
                return null;
            }
        }
        _known[path] = (bytes, null);
        if (content.Length > LargestFile)
        {
            return null;
        }
        SynchronizationContext? asked = SynchronizationContext.Current;
        Pending++;
        _ = Task.Run(() =>
        {
            string? made = null;
            try
            {
                using Performance.Span measure = Performance.Measure("assets.map-picture");
                DecodedTexture decoded = GameTextures.Decode(content);
                using MagickImage image = new MagickImage(decoded.Rgba, new PixelReadSettings((uint)decoded.Width, (uint)decoded.Height, StorageType.Char, PixelMapping.RGBA));
                if (Math.Max(image.Width, image.Height) > Largest)
                {
                    image.Resize(new MagickGeometry(Largest, Largest));
                }
                Directory.CreateDirectory(_folder);
                image.Write(png, MagickFormat.Png);
                made = png;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or MagickException)
            {
                _log.LogWarning(exception, "Could not make a picture of the map's file {Path}", path);
            }
            void Done(object? _)
            {
                Pending--;
                if (_known.TryGetValue(path, out (ImmutableArray<byte> Bytes, string? Png) now) && now.Bytes == bytes)
                {
                    _known[path] = (bytes, made);
                    if (made is not null)
                    {
                        Ready?.Invoke();
                    }
                }
            }
            if (asked is null)
            {
                lock (_known)
                {
                    Done(null);
                }
            }
            else
            {
                asked.Post(Done, null);
            }
        });
        return null;
    }
}
