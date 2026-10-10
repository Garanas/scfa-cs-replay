using System.Collections.Immutable;
using System.Runtime.InteropServices;
using FAForever.MapEditor.Project.Files.InMemory;
using ImageMagick;

namespace FAForever.MapEditor.Project.Workspaces
{
    /// <summary>
    /// A texture as red, green, blue and alpha bytes per pixel, the first row first.
    /// </summary>
    public sealed record DecodedTexture(int Width, int Height, byte[] Rgba);

    /// <summary>
    /// Where the texture of a game path comes from, and how to read it.
    /// </summary>
    /// <param name="Key">The same for every path that names this texture: lower case, no leading slash.</param>
    /// <param name="Version">Changes when the texture does: the bytes of a map's own file, or the
    /// time a converted asset was written.</param>
    /// <param name="Read">Decodes the texture; slow, so call it on a thread of the pool.</param>
    public sealed record TextureSource(string Key, object Version, Func<DecodedTexture> Read);

    /// <summary>
    /// Finds the textures a map names (terrain layers, decals) the way the game does: a path into
    /// the map's own folder (<c>/maps/&lt;folder&gt;/env/rock.dds</c>) is one of the map's files, any other
    /// path one of the game's, converted into the workspace (<see cref="Workspace.FindAsset"/>).
    /// </summary>
    public static class GameTextures
    {
        /// <summary>
        /// The texture of <paramref name="gamePath"/>, or null when neither the map nor the workspace has it.
        /// </summary>
        public static TextureSource? Find(string gamePath, MapDocument document, Workspace? workspace) =>
            Find(gamePath, document, workspace, "", Decode);

        /// <summary>
        /// The cube map of <paramref name="gamePath"/> as a strip of its six faces
        /// (<see cref="CubeMaps.Decode"/>), or null when neither the map nor the workspace has it.
        /// Its <see cref="TextureSource.Key"/> differs from the one <see cref="Find"/> gives the same path.
        /// </summary>
        public static TextureSource? FindCube(string gamePath, MapDocument document, Workspace? workspace) =>
            Find(gamePath, document, workspace, "cube:", CubeMaps.Decode);

        private static TextureSource? Find(string gamePath, MapDocument document, Workspace? workspace, string prefix, Func<byte[], DecodedTexture> decode)
        {
            if (string.IsNullOrWhiteSpace(gamePath))
            {
                return null;
            }

            string key = GameArchives.Normalize(gamePath);
            if (key.StartsWith("maps/", StringComparison.Ordinal))
            {
                int folderEnd = key.IndexOf('/', "maps/".Length);
                string inFolder = folderEnd < 0 ? "" : key[(folderEnd + 1)..];
                foreach ((string file, ImmutableArray<byte> bytes) in document.Files)
                {
                    if (string.Equals(file, inFolder, StringComparison.OrdinalIgnoreCase))
                    {
                        byte[] content = ImmutableCollectionsMarshal.AsArray(bytes)!;
                        return new TextureSource(prefix + "map:" + inFolder, bytes, () => decode(content));
                    }
                }
                return null;
            }

            if (workspace?.FindAsset(key) is string asset)
            {
                return new TextureSource(prefix + key, File.GetLastWriteTimeUtc(asset), () => decode(File.ReadAllBytes(asset)));
            }
            return null;
        }

        /// <summary>
        /// Decodes a PNG, DDS or any other image ImageMagick reads into 8-bit red, green, blue and alpha.
        /// </summary>
        /// <exception cref="MagickException">The bytes are no image.</exception>
        public static DecodedTexture Decode(byte[] bytes)
        {
            MagickReadSettings settings = new MagickReadSettings();
            if (bytes.Length >= 4 && bytes[0] == 'D' && bytes[1] == 'D' && bytes[2] == 'S')
            {
                settings.Format = MagickFormat.Dds;
                settings.SetDefine(MagickFormat.Dds, "skip-mipmaps", "true");
            }
            using MagickImage image = new MagickImage(bytes, settings);
            image.Alpha(AlphaOption.Set);
            using IPixelCollection<ushort> pixels = image.GetPixels();
            return new DecodedTexture((int)image.Width, (int)image.Height, pixels.ToByteArray(PixelMapping.RGBA)!);
        }
    }
}
