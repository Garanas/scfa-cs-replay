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

        // a cube map: the source is a strip of six faces (CubeMaps), uploaded as a cube
        public bool Cube;
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
    public uint Get(TextureSource? source, uint fallback) => Get(source, fallback, cube: false);

    /// <summary>
    /// The texture of a model whose pixels below an alpha are cut out (leaves): the colour of
    /// those pixels, which never shows, becomes the average of the others before the mipmaps are
    /// made, so a tree seen from afar keeps its green rather than turning dark with the black of
    /// its holes (<see cref="CutOutTextures.Fill"/>).
    /// </summary>
    public uint GetCutOut(TextureSource? source, uint fallback, float alphaTest) => Get(source, fallback, cube: false, cutOut: alphaTest);

    /// <summary>
    /// The cube map of <paramref name="source"/> (a strip of six faces, <see cref="CubeMaps"/>), or
    /// <paramref name="fallback"/> while it loads or when there is none.
    /// </summary>
    public uint GetCube(TextureSource? source, uint fallback) => Get(source, fallback, cube: true);

    private uint Get(TextureSource? source, uint fallback, bool cube, float? cutOut = null)
    {
        if (source is null)
        {
            return fallback;
        }
        if (!_entries.TryGetValue(source.Key, out Entry? entry))
        {
            entry = new Entry { Cube = cube };
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
                    DecodedTexture image = source.Read();
                    _done.Enqueue((key, source.Version, cutOut is float cut ? CutOutTextures.Fill(image, cut) : image, null));
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
            if (entry.Cube && !CubeMaps.IsStrip(done.Image.Width, done.Image.Height))
            {
                LastError = $"{done.Key}: not a cube map of six square faces ({done.Image.Width} by {done.Image.Height})";
                continue;
            }
            if (entry.Id == 0)
            {
                entry.Id = gl.GenTexture();
            }
            if (entry.Cube)
            {
                UploadCube(gl, entry.Id, done.Image);
            }
            else
            {
                UploadRgba(gl, entry.Id, done.Image.Width, done.Image.Height, done.Image.Rgba, mipmaps: true);
            }
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

    /// <summary>
    /// A strip of six faces as a cube map, with mipmaps: the faces in the order of OpenGL and DDS
    /// (+x, -x, +y, -y, +z, -z), each uploaded as the DDS holds it.
    /// </summary>
    public static void UploadCube(GL gl, uint texture, DecodedTexture strip)
    {
        int size = strip.Height;
        gl.BindTexture(TextureTarget.TextureCubeMap, texture);
        gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        for (int face = 0; face < 6; face++)
        {
            gl.TexImage2D(TextureTarget.TextureCubeMapPositiveX + face, 0, InternalFormat.Rgba8, (uint)size, (uint)size, 0, PixelFormat.Rgba, PixelType.UnsignedByte,
                (ReadOnlySpan<byte>)CubeMaps.Face(strip, face));
        }
        gl.GenerateMipmap(TextureTarget.TextureCubeMap);
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
