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
            if (Uncompressed(dds) is DecodedTexture own)
            {
                using MagickImage image = new MagickImage(own.Rgba, new PixelReadSettings((uint)own.Width, (uint)own.Height, StorageType.Char, PixelMapping.RGBA));
                image.Settings.SetDefine(MagickFormat.Png, "compression-level", "4");
                return image.ToByteArray(MagickFormat.Png);
            }
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
            if (IsCubeMap(bytes) && Uncompressed(bytes) is DecodedTexture own)
            {
                return own;
            }
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

        /// <summary>
        /// The strip of an uncompressed cube map of 32 bits a pixel, read by its own masks: each
        /// face's first level, its smaller levels skipped. ImageMagick lays such a file out wrong
        /// when its pixel format has no alpha (<c>skycube_desert01.dds</c>: DDPF_RGB without
        /// DDPF_ALPHAPIXELS), mixing the faces with their smaller levels. Null for any other kind.
        /// </summary>
        public static DecodedTexture? Uncompressed(ReadOnlySpan<byte> dds)
        {
            const uint Rgb = 0x40;
            const uint AlphaPixels = 0x1;
            const uint MipMapCount = 0x20000;
            if (!IsCubeMap(dds))
            {
                return null;
            }
            uint flags = BinaryPrimitives.ReadUInt32LittleEndian(dds[8..]);
            int height = (int)BinaryPrimitives.ReadUInt32LittleEndian(dds[12..]);
            int width = (int)BinaryPrimitives.ReadUInt32LittleEndian(dds[16..]);
            int levels = (flags & MipMapCount) != 0 ? Math.Max(1, (int)BinaryPrimitives.ReadUInt32LittleEndian(dds[28..])) : 1;
            uint format = BinaryPrimitives.ReadUInt32LittleEndian(dds[80..]);
            uint bits = BinaryPrimitives.ReadUInt32LittleEndian(dds[88..]);
            if ((format & Rgb) == 0 || bits != 32 || width <= 0 || width != height)
            {
                return null;
            }
            uint[] masks = new uint[4];
            for (int i = 0; i < 4; i++)
            {
                masks[i] = BinaryPrimitives.ReadUInt32LittleEndian(dds[(92 + 4 * i)..]);
            }
            if ((format & AlphaPixels) == 0)
            {
                masks[3] = 0;
            }
            long chain = 0;
            for (int level = 0, size = width; level < levels; level++, size = Math.Max(1, size / 2))
            {
                chain += (long)size * size * 4;
            }
            if (128 + 6 * chain > dds.Length)
            {
                return null;
            }
            byte[] rgba = new byte[6 * width * height * 4];
            for (int face = 0; face < 6; face++)
            {
                ReadOnlySpan<byte> pixels = dds.Slice((int)(128 + face * chain), width * height * 4);
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        uint pixel = BinaryPrimitives.ReadUInt32LittleEndian(pixels[((y * width + x) * 4)..]);
                        int at = (y * 6 * width + face * width + x) * 4;
                        for (int channel = 0; channel < 4; channel++)
                        {
                            rgba[at + channel] = masks[channel] == 0 ? (byte)255 : Channel(pixel, masks[channel]);
                        }
                    }
                }
            }
            return new DecodedTexture(6 * width, height, rgba);
        }

        /// <summary>
        /// A channel of a pixel by its mask, stretched to 8 bits.
        /// </summary>
        private static byte Channel(uint pixel, uint mask)
        {
            int shift = System.Numerics.BitOperations.TrailingZeroCount(mask);
            uint value = (pixel & mask) >> shift;
            uint top = mask >> shift;
            return (byte)(top == 255 ? value : value * 255 / top);
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
