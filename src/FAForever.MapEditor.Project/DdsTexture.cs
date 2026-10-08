using System.Buffers.Binary;
using FAForever.MapEditor.Project.Files;
using ImageMagick;

namespace FAForever.MapEditor.Project
{
    /// <summary>
    /// The DDS files a <c>.scmap</c> embeds, as red, green, blue and alpha bytes per pixel.
    /// </summary>
    /// <remarks>
    /// Every texture in the 100 maps of a local vault copy is uncompressed A8R8G8B8 or DXT5, without
    /// mipmaps. The header written here is the one the FAF map editor writes (flags 0x1007, caps
    /// 0x1002); ImageMagick only compresses and decompresses the DXT5 blocks.
    /// </remarks>
    internal static class DdsTexture
    {
        private const int HeaderSize = 128;

        /// <exception cref="MapProjectException">The texture is in another format.</exception>
        public static (int Width, int Height, TextureFormat Format, byte[] Rgba) Read(byte[] dds, string name)
        {
            if (dds.Length < HeaderSize || BinaryPrimitives.ReadUInt32LittleEndian(dds) != 0x20534444)
            {
                throw new MapProjectException($"The {name} is not a DDS file");
            }

            int height = BinaryPrimitives.ReadInt32LittleEndian(dds.AsSpan(12));
            int width = BinaryPrimitives.ReadInt32LittleEndian(dds.AsSpan(16));
            uint pixelFlags = BinaryPrimitives.ReadUInt32LittleEndian(dds.AsSpan(80));
            uint fourCc = BinaryPrimitives.ReadUInt32LittleEndian(dds.AsSpan(84));
            int bits = BinaryPrimitives.ReadInt32LittleEndian(dds.AsSpan(88));
            uint redMask = BinaryPrimitives.ReadUInt32LittleEndian(dds.AsSpan(92));

            if ((pixelFlags & 0x4) != 0 && fourCc == 0x35545844) // "DXT5"
            {
                using MagickImage image = new MagickImage(dds, MagickFormat.Dds);
                image.Alpha(AlphaOption.Set);
                using IPixelCollection<ushort> pixels = image.GetPixels();
                return (width, height, TextureFormat.Dxt5, pixels.ToByteArray(PixelMapping.RGBA)!);
            }

            if ((pixelFlags & 0x40) != 0 && bits == 32 && redMask == 0xff0000)
            {
                int length = checked(width * height * 4);
                if (dds.Length < HeaderSize + length)
                {
                    throw new MapProjectException($"The {name} is shorter than its size of {width} by {height}");
                }
                byte[] rgba = new byte[length];
                ReadOnlySpan<byte> bgra = dds.AsSpan(HeaderSize, length);
                for (int i = 0; i < length; i += 4)
                {
                    rgba[i] = bgra[i + 2];
                    rgba[i + 1] = bgra[i + 1];
                    rgba[i + 2] = bgra[i];
                    rgba[i + 3] = bgra[i + 3];
                }
                return (width, height, TextureFormat.A8R8G8B8, rgba);
            }

            throw new MapProjectException($"The {name} is in a DDS format this tool does not read (pixel flags {pixelFlags:x}, {bits} bits)");
        }

        public static byte[] Write(int width, int height, TextureFormat format, byte[] rgba)
        {
            byte[] payload = format switch
            {
                TextureFormat.A8R8G8B8 => ToBgra(rgba),
                TextureFormat.Dxt5 => CompressDxt5(width, height, rgba),
                _ => throw new ArgumentOutOfRangeException(nameof(format)),
            };

            byte[] dds = new byte[HeaderSize + payload.Length];
            Span<byte> header = dds.AsSpan(0, HeaderSize);
            BinaryPrimitives.WriteUInt32LittleEndian(header, 0x20534444); // "DDS "
            BinaryPrimitives.WriteInt32LittleEndian(header[4..], 124);
            BinaryPrimitives.WriteInt32LittleEndian(header[8..], 0x1007); // caps, height, width, pixel format
            BinaryPrimitives.WriteInt32LittleEndian(header[12..], height);
            BinaryPrimitives.WriteInt32LittleEndian(header[16..], width);
            BinaryPrimitives.WriteInt32LittleEndian(header[76..], 32);
            if (format == TextureFormat.Dxt5)
            {
                BinaryPrimitives.WriteInt32LittleEndian(header[80..], 0x4);
                BinaryPrimitives.WriteUInt32LittleEndian(header[84..], 0x35545844);
            }
            else
            {
                BinaryPrimitives.WriteInt32LittleEndian(header[80..], 0x41); // RGB with alpha
                BinaryPrimitives.WriteInt32LittleEndian(header[88..], 32);
                BinaryPrimitives.WriteUInt32LittleEndian(header[92..], 0xff0000);
                BinaryPrimitives.WriteUInt32LittleEndian(header[96..], 0xff00);
                BinaryPrimitives.WriteUInt32LittleEndian(header[100..], 0xff);
                BinaryPrimitives.WriteUInt32LittleEndian(header[104..], 0xff000000);
            }
            BinaryPrimitives.WriteInt32LittleEndian(header[108..], 0x1002);
            payload.CopyTo(dds.AsSpan(HeaderSize));
            return dds;
        }

        private static byte[] ToBgra(byte[] rgba)
        {
            byte[] bgra = new byte[rgba.Length];
            for (int i = 0; i < rgba.Length; i += 4)
            {
                bgra[i] = rgba[i + 2];
                bgra[i + 1] = rgba[i + 1];
                bgra[i + 2] = rgba[i];
                bgra[i + 3] = rgba[i + 3];
            }
            return bgra;
        }

        private static byte[] CompressDxt5(int width, int height, byte[] rgba)
        {
            using MagickImage image = new MagickImage(rgba, new PixelReadSettings((uint)width, (uint)height, StorageType.Char, PixelMapping.RGBA));
            image.Settings.SetDefine(MagickFormat.Dds, "compression", "dxt5");
            image.Settings.SetDefine(MagickFormat.Dds, "mipmaps", "0");
            byte[] dds = image.ToByteArray(MagickFormat.Dds);

            int blocks = Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4);
            if (BinaryPrimitives.ReadUInt32LittleEndian(dds.AsSpan(84)) != 0x35545844 || dds.Length != HeaderSize + blocks * 16)
            {
                throw new InvalidOperationException("ImageMagick did not write a DXT5 texture without mipmaps");
            }
            return dds[HeaderSize..];
        }
    }
}
