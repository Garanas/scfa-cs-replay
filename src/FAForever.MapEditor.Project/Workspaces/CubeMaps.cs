using System.Buffers.Binary;
using ImageMagick;

namespace FAForever.MapEditor.Project.Workspaces
{
    /// <summary>
    /// Cube maps: the sky a map shows around it (<c>SkyCubemap</c>), what units reflect
    /// (<c>EnvironmentCubemaps</c>) and what the water reflects. The game keeps them as DDS files
    /// with six faces; the workspace keeps each as one PNG with the six faces side by side, square
    /// each, in the order of the DDS and OpenGL: +x, -x, +y, -y, +z, -z (a strip six times as wide
    /// as high).
    /// </summary>
    public static class CubeMaps
    {
        // DDSCAPS2_CUBEMAP of the header's dwCaps2 (offset 112 of the file, after the magic number)
        private const uint CubeMapCaps = 0x200;

        /// <summary>
        /// Whether DDS bytes hold a cube map.
        /// </summary>
        public static bool IsCubeMap(ReadOnlySpan<byte> dds) =>
            dds.Length >= 128 && dds[0] == 'D' && dds[1] == 'D' && dds[2] == 'S'
            && (BinaryPrimitives.ReadUInt32LittleEndian(dds[112..]) & CubeMapCaps) != 0;

        /// <summary>
        /// Whether a picture of this size is a strip of six square faces.
        /// </summary>
        public static bool IsStrip(int width, int height) => height > 0 && width == 6 * height;

        /// <summary>
        /// The six faces of a DDS cube map side by side, as an 8-bit PNG.
        /// </summary>
        /// <exception cref="MagickException">The bytes are no DDS ImageMagick reads.</exception>
        /// <exception cref="InvalidDataException">The cube map has not six faces.</exception>
        public static byte[] StripPng(byte[] dds)
        {
            using IMagickImage<ushort> strip = Strip(dds);
            strip.Depth = 8;
            strip.Settings.SetDefine(MagickFormat.Png, "compression-level", "4");
            return strip.ToByteArray(MagickFormat.Png);
        }

        /// <summary>
        /// Decodes a cube map into its strip of faces: a DDS cube map, or a PNG (or other picture)
        /// that already is a strip. A picture that is neither is taken as the same view on every side.
        /// </summary>
        /// <exception cref="MagickException">The bytes are no image.</exception>
        public static DecodedTexture Decode(byte[] bytes)
        {
            if (IsCubeMap(bytes))
            {
                using IMagickImage<ushort> strip = Strip(bytes);
                strip.Alpha(AlphaOption.Set);
                using IPixelCollection<ushort> pixels = strip.GetPixels();
                return new DecodedTexture((int)strip.Width, (int)strip.Height, pixels.ToByteArray(PixelMapping.RGBA)!);
            }
            DecodedTexture picture = GameTextures.Decode(bytes);
            if (IsStrip(picture.Width, picture.Height))
            {
                return picture;
            }
            // one picture on all six sides: square it, then repeat it
            int size = Math.Min(picture.Width, picture.Height);
            byte[] rgba = new byte[size * 6 * size * 4];
            for (int y = 0; y < size; y++)
            {
                for (int face = 0; face < 6; face++)
                {
                    Array.Copy(picture.Rgba, y * picture.Width * 4, rgba, (y * 6 * size + face * size) * 4, size * 4);
                }
            }
            return new DecodedTexture(6 * size, size, rgba);
        }

        /// <summary>
        /// One face of a strip as red, green, blue and alpha bytes, its first row first.
        /// </summary>
        /// <param name="face">0 to 5: +x, -x, +y, -y, +z, -z.</param>
        public static byte[] Face(DecodedTexture strip, int face)
        {
            int size = strip.Height;
            byte[] rgba = new byte[size * size * 4];
            for (int y = 0; y < size; y++)
            {
                Array.Copy(strip.Rgba, (y * strip.Width + face * size) * 4, rgba, y * size * 4, size * 4);
            }
            return rgba;
        }

        private static IMagickImage<ushort> Strip(byte[] dds)
        {
            MagickReadSettings settings = new MagickReadSettings { Format = MagickFormat.Dds };
            settings.SetDefine(MagickFormat.Dds, "skip-mipmaps", "true");
            using MagickImageCollection faces = new MagickImageCollection(dds, settings);
            if (faces.Count != 6)
            {
                throw new InvalidDataException($"A cube map has six faces, this one {faces.Count}");
            }
            return faces.AppendHorizontally();
        }
    }
}
