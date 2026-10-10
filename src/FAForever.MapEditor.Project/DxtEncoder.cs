using System.Buffers.Binary;
using System.Numerics;

namespace FAForever.MapEditor.Project
{
    /// <summary>
    /// The block compressions of the DDS textures the editor writes with a chain of mipmaps (the
    /// map-wide decals, as Kaali's are written).
    /// </summary>
    public enum DdsCompression
    {
        /// <summary>
        /// Colour only, 8 bytes per block of 4 by 4 (a specular map).
        /// </summary>
        Dxt1,

        /// <summary>
        /// Colour and alpha, 16 bytes per block of 4 by 4 (albedo, normals, shadow).
        /// </summary>
        Dxt5,
    }

    /// <summary>
    /// Compresses pictures into DXT1 and DXT5 blocks, and makes their mipmaps: plain managed code that
    /// works on many blocks at once, so a picture of 8192 by 8192 takes seconds and no more memory
    /// than its pixels and its blocks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A colour block takes its two colours from the line through the block's colours along which
    /// they spread most (a few rounds of power iteration on their covariance), cut to where the
    /// colours lie on it, and each pixel the nearest of the four colours that line gives. It is
    /// always in the four-colour mode (the first colour greater than the second), which DXT5's colour
    /// block requires. An alpha block takes the lowest and highest alpha and each pixel the nearest
    /// of the eight steps between them.
    /// </para>
    /// <para>
    /// A mipmap is the level above it made smaller by two in each direction, each pixel the mean of
    /// the two by two it covers (one by two at an odd edge of one pixel), down to one by one.
    /// </para>
    /// </remarks>
    internal static class DxtEncoder
    {
        /// <summary>
        /// The bytes of the blocks of a picture: rows of blocks of 4 by 4 pixels, the top row first;
        /// a block beyond the picture's edge repeats its last row and column.
        /// </summary>
        /// <param name="rgba">Red, green, blue and alpha bytes per pixel, the first row first.</param>
        public static byte[] Compress(int width, int height, ReadOnlySpan<byte> rgba, DdsCompression compression)
        {
            int blocksX = Math.Max(1, (width + 3) / 4);
            int blocksY = Math.Max(1, (height + 3) / 4);
            int blockSize = compression == DdsCompression.Dxt1 ? 8 : 16;
            byte[] output = new byte[blocksX * blocksY * blockSize];
            byte[] pixels = rgba.ToArray();
            Parallel.For(0, blocksY, by =>
            {
                Span<byte> block = stackalloc byte[64];
                for (int bx = 0; bx < blocksX; bx++)
                {
                    for (int y = 0; y < 4; y++)
                    {
                        int row = Math.Min(by * 4 + y, height - 1);
                        for (int x = 0; x < 4; x++)
                        {
                            int column = Math.Min(bx * 4 + x, width - 1);
                            int from = (row * width + column) * 4;
                            int to = (y * 4 + x) * 4;
                            block[to] = pixels[from];
                            block[to + 1] = pixels[from + 1];
                            block[to + 2] = pixels[from + 2];
                            block[to + 3] = pixels[from + 3];
                        }
                    }
                    Span<byte> target = output.AsSpan((by * blocksX + bx) * blockSize, blockSize);
                    if (compression == DdsCompression.Dxt5)
                    {
                        AlphaBlock(block, target[..8]);
                        ColorBlock(block, target[8..]);
                    }
                    else
                    {
                        ColorBlock(block, target);
                    }
                }
            });
            return output;
        }

        /// <summary>
        /// The next smaller level of a picture: half its width and height (at least 1), each pixel the
        /// mean of the pixels it covers.
        /// </summary>
        public static (int Width, int Height, byte[] Rgba) Halve(int width, int height, ReadOnlySpan<byte> rgba)
        {
            int w = Math.Max(1, width / 2);
            int h = Math.Max(1, height / 2);
            byte[] result = new byte[w * h * 4];
            byte[] source = rgba.ToArray();
            Parallel.For(0, h, y =>
            {
                int y0 = Math.Min(y * 2, height - 1);
                int y1 = Math.Min(y * 2 + 1, height - 1);
                for (int x = 0; x < w; x++)
                {
                    int x0 = Math.Min(x * 2, width - 1);
                    int x1 = Math.Min(x * 2 + 1, width - 1);
                    int a = (y0 * width + x0) * 4, b = (y0 * width + x1) * 4, c = (y1 * width + x0) * 4, d = (y1 * width + x1) * 4;
                    int to = (y * w + x) * 4;
                    for (int channel = 0; channel < 4; channel++)
                    {
                        result[to + channel] = (byte)((source[a + channel] + source[b + channel] + source[c + channel] + source[d + channel] + 2) / 4);
                    }
                }
            });
            return (w, h, result);
        }

        /// <summary>
        /// How many levels a full chain of mipmaps of a picture has: down to one by one.
        /// </summary>
        public static int LevelsOf(int width, int height) => BitOperations.Log2((uint)Math.Max(1, Math.Max(width, height))) + 1;

        private static void AlphaBlock(ReadOnlySpan<byte> block, Span<byte> target)
        {
            byte low = 255, high = 0;
            for (int i = 0; i < 16; i++)
            {
                byte alpha = block[i * 4 + 3];
                low = Math.Min(low, alpha);
                high = Math.Max(high, alpha);
            }
            target.Clear();
            target[0] = high;
            target[1] = low;
            if (high == low)
            {
                return;
            }
            // with a0 > a1: index 0 is a0, 1 is a1, 2 to 7 the steps from a0 to a1
            Span<int> values = stackalloc int[8];
            values[0] = high;
            values[1] = low;
            for (int step = 1; step <= 6; step++)
            {
                values[step + 1] = ((7 - step) * high + step * low + 3) / 7;
            }
            ulong bits = 0;
            for (int i = 0; i < 16; i++)
            {
                int alpha = block[i * 4 + 3];
                int best = 0, distance = int.MaxValue;
                for (int index = 0; index < 8; index++)
                {
                    int d = Math.Abs(values[index] - alpha);
                    if (d < distance)
                    {
                        distance = d;
                        best = index;
                    }
                }
                bits |= (ulong)best << (3 * i);
            }
            for (int i = 0; i < 6; i++)
            {
                target[2 + i] = (byte)(bits >> (8 * i));
            }
        }

        private static void ColorBlock(ReadOnlySpan<byte> block, Span<byte> target)
        {
            // the mean and the axis along which the colours spread most
            Vector3 mean = Vector3.Zero;
            for (int i = 0; i < 16; i++)
            {
                mean += new Vector3(block[i * 4], block[i * 4 + 1], block[i * 4 + 2]);
            }
            mean /= 16;
            float xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
            for (int i = 0; i < 16; i++)
            {
                Vector3 d = new Vector3(block[i * 4], block[i * 4 + 1], block[i * 4 + 2]) - mean;
                xx += d.X * d.X;
                xy += d.X * d.Y;
                xz += d.X * d.Z;
                yy += d.Y * d.Y;
                yz += d.Y * d.Z;
                zz += d.Z * d.Z;
            }
            Vector3 axis = new Vector3(1, 1, 1);
            for (int round = 0; round < 6; round++)
            {
                axis = new Vector3(xx * axis.X + xy * axis.Y + xz * axis.Z, xy * axis.X + yy * axis.Y + yz * axis.Z, xz * axis.X + yz * axis.Y + zz * axis.Z);
                float length = axis.Length();
                if (length < 1e-6f)
                {
                    axis = new Vector3(1, 1, 1);
                    break;
                }
                axis /= length;
            }
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < 16; i++)
            {
                float t = Vector3.Dot(new Vector3(block[i * 4], block[i * 4 + 1], block[i * 4 + 2]) - mean, axis);
                min = MathF.Min(min, t);
                max = MathF.Max(max, t);
            }
            ushort c0 = To565(mean + axis * max);
            ushort c1 = To565(mean + axis * min);
            if (c0 < c1)
            {
                (c0, c1) = (c1, c0);
            }
            BinaryPrimitives.WriteUInt16LittleEndian(target, c0);
            BinaryPrimitives.WriteUInt16LittleEndian(target[2..], c1);
            uint indices = 0;
            if (c0 != c1)
            {
                Span<Vector3> palette = stackalloc Vector3[4];
                palette[0] = From565(c0);
                palette[1] = From565(c1);
                palette[2] = (2 * palette[0] + palette[1]) / 3;
                palette[3] = (palette[0] + 2 * palette[1]) / 3;
                for (int i = 0; i < 16; i++)
                {
                    Vector3 color = new Vector3(block[i * 4], block[i * 4 + 1], block[i * 4 + 2]);
                    int best = 0;
                    float distance = float.MaxValue;
                    for (int index = 0; index < 4; index++)
                    {
                        float d = Vector3.DistanceSquared(color, palette[index]);
                        if (d < distance)
                        {
                            distance = d;
                            best = index;
                        }
                    }
                    indices |= (uint)best << (2 * i);
                }
            }
            BinaryPrimitives.WriteUInt32LittleEndian(target[4..], indices);
        }

        private static ushort To565(Vector3 color)
        {
            int r = Math.Clamp((int)MathF.Round(color.X * 31 / 255), 0, 31);
            int g = Math.Clamp((int)MathF.Round(color.Y * 63 / 255), 0, 63);
            int b = Math.Clamp((int)MathF.Round(color.Z * 31 / 255), 0, 31);
            return (ushort)((r << 11) | (g << 5) | b);
        }

        private static Vector3 From565(ushort color)
        {
            int r = (color >> 11) & 31, g = (color >> 5) & 63, b = color & 31;
            return new Vector3((r << 3) | (r >> 2), (g << 2) | (g >> 4), (b << 3) | (b >> 2));
        }
    }
}
