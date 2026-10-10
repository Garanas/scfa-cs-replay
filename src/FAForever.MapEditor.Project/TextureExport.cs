using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;
using ImageMagick;

namespace FAForever.MapEditor.Project
{
    /// <summary>
    /// What a texture is used for, which decides how it is written as DDS (<see cref="TextureExport"/>).
    /// </summary>
    public enum TextureRole
    {
        /// <summary>A picture: DXT5 with mipmaps when it has alpha, DXT1 with mipmaps otherwise.</summary>
        Plain,

        /// <summary>A normal map (stratum, decal, wave): DXT5 with mipmaps, the channels as they are.</summary>
        Normal,

        /// <summary>A colour ramp the game looks colours up in (the water's): uncompressed A8R8G8B8, no mipmaps.</summary>
        Ramp,

        /// <summary>A cube map: a DDS cube of six faces, DXT1 or DXT5 with mipmaps.</summary>
        Cube,
    }

    /// <summary>
    /// One of the map's own textures the export converted to DDS.
    /// </summary>
    /// <param name="Source">The file as the map has it, e.g. <c>env/layers/rock_albedo.png</c>.</param>
    /// <param name="Target">The DDS file the export writes in its place, e.g. <c>env/layers/rock_albedo.dds</c>.</param>
    /// <param name="Format">How it was written, e.g. <c>DXT5 with mipmaps</c>.</param>
    /// <param name="Cached">Whether the DDS came from <see cref="TextureExportCache"/> (the same file converted before).</param>
    public sealed record ConvertedTexture(string Source, string Target, TextureRole Role, string Format, bool Cached);

    /// <summary>
    /// DDS files made from pictures, by the SHA-256 of the picture and its role, so an export that
    /// follows another converts only what changed. Thread safe; it keeps at most
    /// <see cref="Capacity"/> bytes, dropping the oldest first.
    /// </summary>
    public sealed class TextureExportCache
    {
        private readonly ConcurrentDictionary<string, byte[]> _entries = new ConcurrentDictionary<string, byte[]>(StringComparer.Ordinal);
        private readonly ConcurrentQueue<string> _order = new ConcurrentQueue<string>();
        private long _bytes;

        public TextureExportCache(long capacity = 512L * 1024 * 1024) => Capacity = capacity;

        /// <summary>
        /// The most bytes of DDS files kept.
        /// </summary>
        public long Capacity { get; }

        public int Count => _entries.Count;

        internal static string KeyOf(byte[] picture, TextureRole role) => $"{role}:{Convert.ToHexString(SHA256.HashData(picture))}";

        internal bool TryGet(string key, out byte[] dds) => _entries.TryGetValue(key, out dds!);

        internal void Add(string key, byte[] dds)
        {
            if (dds.Length > Capacity || !_entries.TryAdd(key, dds))
            {
                return;
            }
            _order.Enqueue(key);
            Interlocked.Add(ref _bytes, dds.Length);
            while (Interlocked.Read(ref _bytes) > Capacity && _order.TryDequeue(out string? oldest))
            {
                if (_entries.TryRemove(oldest, out byte[]? removed))
                {
                    Interlocked.Add(ref _bytes, -removed.Length);
                }
            }
        }
    }

    /// <summary>
    /// Turns the map's own textures that are pictures of another format (PNG, JPEG, TGA, ...) into
    /// DDS files for the version an export writes, and points every reference at the DDS file: the
    /// strata, decals and decal sets, the water's ramp, waves and cube map, the sky's background,
    /// cube maps and sky dome, and the paths in the map's Lua files.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What is converted: every picture the map names (in the <c>.scmap</c>'s fields or a Lua file
    /// of the map) and every picture in a folder of the map (<c>env/</c>, <c>textures/</c>, ...); a
    /// picture at the top of the map's folder that nothing names (the vault's <c>.small.png</c>, a
    /// scenario preview) is no texture and stays. DDS files are left as they are, and textures of
    /// the game's or FAF's archives are only named, never copied.
    /// </para>
    /// <para>
    /// The channels are written as the picture holds them: the map's own pictures are in the layout
    /// the game reads from its DDS files (the editor's converted assets are too), e.g. a decal's
    /// normals with x in alpha and z in green. Normal maps (strata, normals decals, the water's waves)
    /// become DXT5 with a full chain of mipmaps, as nearly every map of the vault has them; ramps
    /// (the water's and the wave generators') uncompressed A8R8G8B8 without mipmaps, as the game's
    /// <c>waterramp_tundra.dds</c>, so their gradient keeps every step; cube maps a DDS cube
    /// (<see cref="CubeMaps"/>: a strip of six faces, or one picture on every side); anything else
    /// DXT5 with mipmaps when any pixel is not opaque, DXT1 with mipmaps otherwise.
    /// </para>
    /// <para>
    /// The DDS file takes the picture's name with <c>.dds</c>; when the map has a file of that name
    /// already, <c>_2</c>, <c>_3</c>, ... is added.
    /// </para>
    /// </remarks>
    public static class TextureExport
    {
        /// <summary>
        /// The cache the editor and <c>faf-map pack</c> share within one process.
        /// </summary>
        public static TextureExportCache SharedCache { get; } = new TextureExportCache();

        private static readonly string[] PictureExtensions = [".png", ".jpg", ".jpeg", ".tga", ".bmp", ".gif", ".tif", ".tiff", ".webp"];

        private static readonly string[] TextExtensions = [".lua", ".bp"];

        /// <summary>
        /// Whether a file is a picture of a format other than DDS, by its extension.
        /// </summary>
        public static bool IsPicture(string path) =>
            PictureExtensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// The map's own files the export converts, with the role each is converted for; empty when
        /// every texture is DDS already.
        /// </summary>
        public static IReadOnlyDictionary<string, TextureRole> Pending(MapDocument document)
        {
            Dictionary<string, TextureRole> roles = new Dictionary<string, TextureRole>(StringComparer.Ordinal);
            Dictionary<string, string> byLowerCase = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in document.Files.Keys)
            {
                if (IsPicture(file))
                {
                    byLowerCase.TryAdd(file, file);
                }
            }
            if (byLowerCase.Count == 0)
            {
                return roles;
            }

            void Named(string reference, TextureRole role)
            {
                if (OwnPath(reference) is string own && byLowerCase.TryGetValue(own, out string? file))
                {
                    // a file used in two ways is written for the one that needs more of it
                    roles[file] = roles.TryGetValue(file, out TextureRole known) ? Stronger(known, role) : role;
                }
            }

            foreach ((string reference, TextureRole role) in References(document))
            {
                Named(reference, role);
            }
            // paths in the map's Lua files, e.g. a script that places a decal
            foreach (string text in TextFiles(document).Select(entry => entry.Text))
            {
                foreach (string file in byLowerCase.Values)
                {
                    if (!roles.ContainsKey(file) && text.Contains("/" + file, StringComparison.OrdinalIgnoreCase))
                    {
                        roles[file] = RoleByName(file);
                    }
                }
            }
            // a picture in a folder of the map is a texture, also when nothing names it (a prop's,
            // found by the engine by its name)
            foreach (string file in byLowerCase.Values)
            {
                if (!roles.ContainsKey(file) && file.Contains('/'))
                {
                    roles[file] = RoleByName(file);
                }
            }
            return roles;
        }

        /// <summary>
        /// The document with its own pictures converted to DDS (<see cref="Pending"/>) and every
        /// reference to them pointed at the DDS file; the document itself when there is nothing to convert.
        /// </summary>
        /// <param name="converted">What was converted, in the order of the files.</param>
        /// <param name="cache">DDS files made before, by the picture's hash; <see cref="SharedCache"/> when null.</param>
        /// <exception cref="MapProjectException">A file is no picture ImageMagick reads.</exception>
        public static MapDocument Apply(MapDocument document, out IReadOnlyList<ConvertedTexture> converted, TextureExportCache? cache = null, CancellationToken cancel = default)
        {
            cache ??= SharedCache;
            IReadOnlyDictionary<string, TextureRole> pending = Pending(document);
            if (pending.Count == 0)
            {
                converted = [];
                return document;
            }

            ImmutableSortedDictionary<string, ImmutableArray<byte>> files = document.Files;
            HashSet<string> taken = new HashSet<string>(files.Keys.Where(file => !pending.ContainsKey(file)), StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> renamed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            List<(string Source, string Target, TextureRole Role)> work = [];
            foreach ((string source, TextureRole role) in pending.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                string target = FreeName(taken, Path.ChangeExtension(source, ".dds"));
                taken.Add(target);
                renamed[source] = target;
                work.Add((source, target, role));
            }

            ConvertedTexture[] made = new ConvertedTexture[work.Count];
            byte[][] written = new byte[work.Count][];
            Parallel.For(0, work.Count, new ParallelOptions { CancellationToken = cancel }, i =>
            {
                (string source, string target, TextureRole role) = work[i];
                byte[] picture = ImmutableCollectionsMarshal.AsArray(files[source])!;
                string key = TextureExportCache.KeyOf(picture, role);
                bool cached = cache.TryGet(key, out byte[] dds);
                if (!cached)
                {
                    dds = Convert(picture, source, role);
                    cache.Add(key, dds);
                }
                written[i] = dds;
                made[i] = new ConvertedTexture(source, target, role, FormatOf(dds), cached);
            });

            ImmutableSortedDictionary<string, ImmutableArray<byte>>.Builder builder = files.ToBuilder();
            for (int i = 0; i < work.Count; i++)
            {
                builder.Remove(work[i].Source);
                builder[work[i].Target] = ImmutableCollectionsMarshal.AsImmutableArray(written[i]);
            }
            foreach ((string path, string text) in TextFiles(document))
            {
                string changed = RewriteText(text, renamed);
                if (!ReferenceEquals(changed, text))
                {
                    builder[path] = ImmutableCollectionsMarshal.AsImmutableArray(MapImporter.LuaEncoding.GetBytes(changed));
                }
            }

            string Rewrite(string reference) =>
                OwnPath(reference) is string own && renamed.TryGetValue(own, out string? target)
                    ? reference[..^own.Length] + target
                    : reference;

            MapDocument result = RewriteReferences(document, Rewrite) with { Files = builder.ToImmutable() };
            if (document.Script is { } script)
            {
                string text = MapImporter.LuaEncoding.GetString(ImmutableCollectionsMarshal.AsArray(script)!);
                string changed = RewriteText(text, renamed);
                if (!ReferenceEquals(changed, text))
                {
                    result = result with { Script = ImmutableCollectionsMarshal.AsImmutableArray(MapImporter.LuaEncoding.GetBytes(changed)) };
                }
            }
            converted = made;
            return result;
        }

        /// <summary>
        /// A picture of any format ImageMagick reads as a DDS file for <paramref name="role"/>
        /// (<see cref="TextureExport"/>); DDS bytes are given back as they are.
        /// </summary>
        /// <param name="name">The file's name: its extension tells a TGA (which has no signature), and errors name it.</param>
        /// <exception cref="MapProjectException">The bytes are no picture.</exception>
        public static byte[] Convert(byte[] picture, string name, TextureRole role)
        {
            if (picture.Length >= 4 && picture[0] == 'D' && picture[1] == 'D' && picture[2] == 'S' && picture[3] == ' ')
            {
                return picture;
            }
            try
            {
                if (role == TextureRole.Cube)
                {
                    DecodedTexture strip = CubeMaps.Decode(Normalized(picture, name));
                    return WriteCube(strip, Opaque(strip.Rgba) ? DdsCompression.Dxt1 : DdsCompression.Dxt5);
                }
                DecodedTexture decoded = Decode(picture, name);
                return role switch
                {
                    TextureRole.Ramp => DdsTexture.Write(decoded.Width, decoded.Height, TextureFormat.A8R8G8B8, decoded.Rgba),
                    TextureRole.Normal => DdsTexture.WriteMipmapped(decoded.Width, decoded.Height, DdsCompression.Dxt5, decoded.Rgba),
                    _ => DdsTexture.WriteMipmapped(decoded.Width, decoded.Height, Opaque(decoded.Rgba) ? DdsCompression.Dxt1 : DdsCompression.Dxt5, decoded.Rgba),
                };
            }
            catch (MagickException exception)
            {
                throw new MapProjectException($"The texture {name} is no picture: {exception.Message}");
            }
        }

        /// <summary>
        /// The texture references of a document with the role each is used for.
        /// </summary>
        private static IEnumerable<(string Reference, TextureRole Role)> References(MapDocument d)
        {
            foreach (TerrainLayer layer in d.Terrain.Layers)
            {
                yield return (layer.AlbedoPath, TextureRole.Plain);
                yield return (layer.NormalPath, TextureRole.Normal);
            }
            foreach (Decal decal in d.Decals)
            {
                for (int slot = 0; slot < decal.Textures.Length; slot++)
                {
                    yield return (decal.Textures[slot], slot == 0 && decal.Type == DecalType.Normals ? TextureRole.Normal : TextureRole.Plain);
                }
                if (decal.Set is DecalSet set)
                {
                    if (set.Normals is string normals)
                    {
                        yield return (normals, TextureRole.Normal);
                    }
                    foreach (string mask in set.Masks.Values)
                    {
                        yield return (mask, TextureRole.Plain);
                    }
                }
            }
            Water water = d.Water;
            yield return (water.Cubemap, TextureRole.Cube);
            yield return (water.Ramp, TextureRole.Ramp);
            foreach (WaveTexture wave in water.WaveTextures)
            {
                yield return (wave.Texture, TextureRole.Normal);
            }
            foreach (WaveGenerator generator in water.WaveGenerators)
            {
                yield return (generator.Texture, TextureRole.Plain);
                yield return (generator.Ramp, TextureRole.Ramp);
            }
            Sky sky = d.Sky;
            yield return (sky.Background, TextureRole.Plain);
            yield return (sky.Cubemap, TextureRole.Cube);
            foreach (Cubemap cube in sky.EnvironmentCubemaps)
            {
                yield return (cube.Texture, TextureRole.Cube);
            }
            if (sky.Skybox is Skybox skybox)
            {
                yield return (skybox.Albedo, TextureRole.Plain);
                yield return (skybox.Glow, TextureRole.Plain);
                yield return (skybox.CirrusTexture, TextureRole.Plain);
            }
        }

        private static MapDocument RewriteReferences(MapDocument d, Func<string, string> rewrite) => d with
        {
            Terrain = d.Terrain with
            {
                Layers = [.. d.Terrain.Layers.Select(layer => layer with { AlbedoPath = rewrite(layer.AlbedoPath), NormalPath = rewrite(layer.NormalPath) })],
            },
            Decals = [.. d.Decals.Select(decal => decal with
            {
                Textures = [.. decal.Textures.Select(rewrite)],
                Set = decal.Set is DecalSet set
                    ? set with
                    {
                        Normals = set.Normals is string normals ? rewrite(normals) : null,
                        Masks = set.Masks.ToImmutableSortedDictionary(mask => mask.Key, mask => rewrite(mask.Value), StringComparer.Ordinal),
                    }
                    : null,
            })],
            Water = d.Water with
            {
                Cubemap = rewrite(d.Water.Cubemap),
                Ramp = rewrite(d.Water.Ramp),
                WaveTextures = [.. d.Water.WaveTextures.Select(wave => wave with { Texture = rewrite(wave.Texture) })],
                WaveGenerators = [.. d.Water.WaveGenerators.Select(generator => generator with { Texture = rewrite(generator.Texture), Ramp = rewrite(generator.Ramp) })],
            },
            Sky = d.Sky with
            {
                Background = rewrite(d.Sky.Background),
                Cubemap = rewrite(d.Sky.Cubemap),
                EnvironmentCubemaps = [.. d.Sky.EnvironmentCubemaps.Select(cube => cube with { Texture = rewrite(cube.Texture) })],
                Skybox = d.Sky.Skybox is Skybox skybox
                    ? skybox with { Albedo = rewrite(skybox.Albedo), Glow = rewrite(skybox.Glow), CirrusTexture = rewrite(skybox.CirrusTexture) }
                    : null,
            },
        };

        /// <summary>
        /// The path in the map's folder a reference names when it points into a map's folder
        /// (<c>/maps/&lt;folder&gt;/env/a.png</c> is <c>env/a.png</c>, as written); null otherwise.
        /// </summary>
        private static string? OwnPath(string reference)
        {
            if (string.IsNullOrWhiteSpace(reference))
            {
                return null;
            }
            string path = reference.Replace('\\', '/');
            int start = 0;
            while (start < path.Length && path[start] == '/')
            {
                start++;
            }
            if (string.Compare(path, start, "maps/", 0, 5, StringComparison.OrdinalIgnoreCase) != 0)
            {
                return null;
            }
            int folderEnd = path.IndexOf('/', start + 5);
            return folderEnd < 0 ? null : reference[(folderEnd + 1)..].Replace('\\', '/');
        }

        /// <summary>
        /// The map's Lua and blueprint files as text, by their path.
        /// </summary>
        private static IEnumerable<(string Path, string Text)> TextFiles(MapDocument document) =>
            document.Files
                .Where(entry => TextExtensions.Any(extension => entry.Key.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
                .Select(entry => (entry.Key, MapImporter.LuaEncoding.GetString(ImmutableCollectionsMarshal.AsArray(entry.Value)!)));

        /// <summary>
        /// The text with every path of a converted file (after a slash, in any case) pointed at its DDS
        /// file; the same string when nothing changed.
        /// </summary>
        private static string RewriteText(string text, IReadOnlyDictionary<string, string> renamed)
        {
            string result = text;
            foreach ((string source, string target) in renamed)
            {
                if (result.Contains("/" + source, StringComparison.OrdinalIgnoreCase))
                {
                    result = result.Replace("/" + source, "/" + target, StringComparison.OrdinalIgnoreCase);
                }
                string backslashed = source.Replace('/', '\\');
                if (backslashed != source && result.Contains("\\" + backslashed, StringComparison.OrdinalIgnoreCase))
                {
                    result = result.Replace("\\" + backslashed, "\\" + target.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase);
                }
            }
            return result;
        }

        private static TextureRole RoleByName(string file)
        {
            string name = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
            return name.Contains("normal") ? TextureRole.Normal
                : name.Contains("ramp") ? TextureRole.Ramp
                : name.Contains("cube") ? TextureRole.Cube
                : TextureRole.Plain;
        }

        private static TextureRole Stronger(TextureRole a, TextureRole b) => (TextureRole)Math.Max((int)a, (int)b);

        private static string FreeName(HashSet<string> taken, string path)
        {
            string stem = path[..^4];
            string free = path;
            for (int n = 2; taken.Contains(free); n++)
            {
                free = $"{stem}_{n}.dds";
            }
            return free;
        }

        private static bool Opaque(byte[] rgba)
        {
            for (int i = 3; i < rgba.Length; i += 4)
            {
                if (rgba[i] != 255)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// A TGA as PNG bytes, since ImageMagick cannot tell a TGA by its bytes; any other picture as it is.
        /// </summary>
        private static byte[] Normalized(byte[] picture, string name)
        {
            if (!name.EndsWith(".tga", StringComparison.OrdinalIgnoreCase))
            {
                return picture;
            }
            using MagickImage image = new MagickImage(picture, new MagickReadSettings { Format = MagickFormat.Tga });
            return image.ToByteArray(MagickFormat.Png);
        }

        private static DecodedTexture Decode(byte[] picture, string name)
        {
            MagickReadSettings settings = new MagickReadSettings();
            if (name.EndsWith(".tga", StringComparison.OrdinalIgnoreCase))
            {
                settings.Format = MagickFormat.Tga;
            }
            using MagickImage image = new MagickImage(picture, settings);
            image.Alpha(AlphaOption.Set);
            using IPixelCollection<ushort> pixels = image.GetPixels();
            return new DecodedTexture((int)image.Width, (int)image.Height, pixels.ToByteArray(PixelMapping.RGBA)!);
        }

        /// <summary>
        /// How a DDS file is written, read from its header: <c>DXT5 with mipmaps</c>, <c>DXT1 cube with mipmaps</c>, <c>A8R8G8B8</c>.
        /// </summary>
        public static string FormatOf(ReadOnlySpan<byte> dds)
        {
            if (dds.Length < 128)
            {
                return "unknown";
            }
            uint flags = BinaryPrimitives.ReadUInt32LittleEndian(dds[8..]);
            uint levels = (flags & 0x20000) != 0 ? BinaryPrimitives.ReadUInt32LittleEndian(dds[28..]) : 1;
            uint pixelFlags = BinaryPrimitives.ReadUInt32LittleEndian(dds[80..]);
            string format = (pixelFlags & 0x4) != 0 ? Encoding.ASCII.GetString(dds.Slice(84, 4)) : "A8R8G8B8";
            string cube = CubeMaps.IsCubeMap(dds) ? " cube" : "";
            return levels > 1 ? $"{format}{cube} with mipmaps" : $"{format}{cube}";
        }

        /// <summary>
        /// A DDS cube map of a strip of six square faces (+x, -x, +y, -y, +z, -z), each face DXT1 or
        /// DXT5 with its full chain of mipmaps, one face after the other.
        /// </summary>
        internal static byte[] WriteCube(DecodedTexture strip, DdsCompression compression)
        {
            int size = strip.Height;
            int levels = DxtEncoder.LevelsOf(size, size);
            List<byte[]> blocks = new List<byte[]>(6 * levels);
            for (int face = 0; face < 6; face++)
            {
                (int w, int h, byte[] pixels) = (size, size, CubeMaps.Face(strip, face));
                for (int level = 0; level < levels; level++)
                {
                    blocks.Add(DxtEncoder.Compress(w, h, pixels, compression));
                    if (level + 1 < levels)
                    {
                        (w, h, pixels) = DxtEncoder.Halve(w, h, pixels);
                    }
                }
            }

            const int HeaderSize = 128;
            byte[] dds = new byte[HeaderSize + blocks.Sum(level => level.Length)];
            Span<byte> header = dds.AsSpan(0, HeaderSize);
            BinaryPrimitives.WriteUInt32LittleEndian(header, 0x20534444); // "DDS "
            BinaryPrimitives.WriteInt32LittleEndian(header[4..], 124);
            BinaryPrimitives.WriteInt32LittleEndian(header[8..], 0xa1007); // caps, height, width, pixel format, mipmap count, linear size
            BinaryPrimitives.WriteInt32LittleEndian(header[12..], size);
            BinaryPrimitives.WriteInt32LittleEndian(header[16..], size);
            BinaryPrimitives.WriteInt32LittleEndian(header[20..], blocks[0].Length);
            BinaryPrimitives.WriteInt32LittleEndian(header[28..], levels);
            BinaryPrimitives.WriteInt32LittleEndian(header[76..], 32);
            BinaryPrimitives.WriteInt32LittleEndian(header[80..], 0x4);
            BinaryPrimitives.WriteUInt32LittleEndian(header[84..], compression == DdsCompression.Dxt1 ? 0x31545844u : 0x35545844u); // "DXT1", "DXT5"
            BinaryPrimitives.WriteInt32LittleEndian(header[108..], 0x401008); // complex, texture, mipmaps
            BinaryPrimitives.WriteInt32LittleEndian(header[112..], 0xfe00); // a cube map with all six faces
            int at = HeaderSize;
            foreach (byte[] level in blocks)
            {
                level.CopyTo(dds, at);
                at += level.Length;
            }
            return dds;
        }
    }
}
