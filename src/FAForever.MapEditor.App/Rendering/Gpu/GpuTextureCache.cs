using System.Collections.Concurrent;
using FAForever.MapEditor.Project.Workspaces;
using Silk.NET.OpenGL;

namespace FAForever.MapEditor.App.Rendering.Gpu;

/// <summary>
/// The textures on the graphics card, one per texture file however many layers or decals use it.
/// A texture is decoded on a thread of the pool and uploaded with the next frame; until then the
/// caller's fallback stands in. A source whose version changed (a map file read again, an asset a
/// tool saved) is loaded again, and the old texture shows until the new one is there.
/// </summary>
internal sealed class GpuTextureCache(GL gl, Action loaded) : IDisposable
{
    private sealed class Entry
    {
        public uint Id;
        public object? Version;
        public object? Loading;
        public bool Used;
    }

    private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
    private readonly ConcurrentQueue<(string Key, object Version, DecodedTexture? Image, string? Error)> _done = new();

    /// <summary>
    /// The textures on the card; for the automation's state.
    /// </summary>
    public int Count => _entries.Count(e => e.Value.Id != 0);

    /// <summary>
    /// The textures being read on the pool, not on the card yet; for automation's settling.
    /// </summary>
    public int Loading => _entries.Count(e => e.Value.Loading is not null);

    /// <summary>
    /// The last texture that could not be read, with why.
    /// </summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// The texture of <paramref name="source"/>, or <paramref name="fallback"/> while it loads or
    /// when there is none.
    /// </summary>
    public uint Get(TextureSource? source, uint fallback)
    {
        if (source is null)
        {
            return fallback;
        }
        if (!_entries.TryGetValue(source.Key, out Entry? entry))
        {
            entry = new Entry();
            _entries[source.Key] = entry;
        }
        entry.Used = true;
        if (!Equals(entry.Version, source.Version) && !Equals(entry.Loading, source.Version))
        {
            entry.Loading = source.Version;
            string key = source.Key;
            _ = Task.Run(() =>
            {
                try
                {
                    _done.Enqueue((key, source.Version, source.Read(), null));
                }
                catch (Exception exception)
                {
                    _done.Enqueue((key, source.Version, null, $"{key}: {exception.Message}"));
                }
                loaded();
            });
        }
        return entry.Id != 0 ? entry.Id : fallback;
    }

    /// <summary>
    /// Uploads the textures decoded since the last frame; call with the context current.
    /// </summary>
    public void Upload()
    {
        while (_done.TryDequeue(out (string Key, object Version, DecodedTexture? Image, string? Error) done))
        {
            if (!_entries.TryGetValue(done.Key, out Entry? entry))
            {
                continue;
            }
            entry.Version = done.Version;
            if (Equals(entry.Loading, done.Version))
            {
                entry.Loading = null;
            }
            if (done.Image is null)
            {
                LastError = done.Error;
                continue;
            }
            if (entry.Id == 0)
            {
                entry.Id = gl.GenTexture();
            }
            UploadRgba(gl, entry.Id, done.Image.Width, done.Image.Height, done.Image.Rgba, mipmaps: true);
        }
    }

    /// <summary>
    /// Deletes the textures no draw asked for since the last call.
    /// </summary>
    public void Sweep()
    {
        foreach ((string key, Entry entry) in _entries.ToList())
        {
            if (!entry.Used && entry.Loading is null)
            {
                if (entry.Id != 0)
                {
                    gl.DeleteTexture(entry.Id);
                }
                _entries.Remove(key);
            }
            entry.Used = false;
        }
    }

    public static void UploadRgba(GL gl, uint texture, int width, int height, ReadOnlySpan<byte> rgba, bool mipmaps)
    {
        gl.BindTexture(TextureTarget.Texture2D, texture);
        gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, rgba);
        if (mipmaps)
        {
            gl.GenerateMipmap(TextureTarget.Texture2D);
        }
    }

    public void Dispose()
    {
        foreach (Entry entry in _entries.Values)
        {
            if (entry.Id != 0)
            {
                gl.DeleteTexture(entry.Id);
            }
        }
        _entries.Clear();
    }
}
