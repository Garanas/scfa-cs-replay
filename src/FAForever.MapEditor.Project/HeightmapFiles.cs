using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project
{
    /// <summary>
    /// A heightmap as an image of its own, of any size (user story T-3): what World Machine, Gaea
    /// or another map's export hands over, brought into the map resampled to its size; and the map's
    /// heightmap written out for those tools.
    /// </summary>
    /// <remarks>
    /// <c>.png</c> (and the other images ImageMagick reads: 8 or 16 bits, grey or colour) is read as
    /// grey at 16 bits; <c>.r16</c> and <c>.raw</c> are little-endian 16-bit samples of a square,
    /// as World Machine and Gaea write them. A sample is the height divided by the map's height
    /// scale, as in the project's <c>heightmap.png</c>.
    /// </remarks>
    public static class HeightmapFiles
    {
        /// <summary>
        /// The extensions of the raw files: 16-bit samples, little-endian, row by row.
        /// </summary>
        public static IReadOnlyList<string> RawExtensions { get; } = [".r16", ".raw"];

        /// <summary>
        /// Reads a heightmap of any size.
        /// </summary>
        /// <exception cref="MapProjectException">The file is missing, not an image, or a raw file that is not a square.</exception>
        public static (int Width, int Height, ushort[] Samples) Read(string path)
        {
            if (!File.Exists(path))
            {
                throw new MapProjectException($"{path} is missing");
            }
            if (IsRaw(path))
            {
                byte[] bytes = File.ReadAllBytes(path);
                int side = (int)Math.Round(Math.Sqrt(bytes.Length / 2.0));
                if (side < 2 || side * side * 2 != bytes.Length)
                {
                    throw new MapProjectException($"{path} holds {bytes.Length} bytes: a raw heightmap is a square of 16-bit samples");
                }
                ushort[] samples = new ushort[side * side];
                Buffer.BlockCopy(bytes, 0, samples, 0, bytes.Length);
                if (!BitConverter.IsLittleEndian)
                {
                    for (int i = 0; i < samples.Length; i++)
                    {
                        samples[i] = (ushort)(samples[i] << 8 | samples[i] >> 8);
                    }
                }
                return (side, side, samples);
            }
            (int width, int height) = ProjectImages.Size(path);
            return (width, height, ProjectImages.ReadGray16(path, width, height));
        }

        /// <summary>
        /// Writes the map's heightmap: a 16-bit grey PNG, or raw samples for <c>.r16</c> and <c>.raw</c>.
        /// </summary>
        public static void Write(string path, Heightmap heightmap)
        {
            TiledBuffer<ushort> samples = heightmap.Samples;
            ushort[] values = samples.ToArray();
            if (IsRaw(path))
            {
                byte[] bytes = new byte[values.Length * 2];
                for (int i = 0; i < values.Length; i++)
                {
                    bytes[i * 2] = (byte)values[i];
                    bytes[i * 2 + 1] = (byte)(values[i] >> 8);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                File.WriteAllBytes(path, bytes);
            }
            else
            {
                ProjectImages.WriteGray16(path, samples.Width, samples.Height, values);
            }
        }

        /// <summary>
        /// Samples of one size made into another, read between the four nearest samples (bilinear);
        /// the corners stay on the corners.
        /// </summary>
        public static ushort[] Resample(ushort[] samples, int width, int height, int toWidth, int toHeight)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(toWidth, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(toHeight, 1);
            if (samples.Length != width * height)
            {
                throw new ArgumentException($"{samples.Length} samples do not fill {width} by {height}", nameof(samples));
            }
            if (width == toWidth && height == toHeight)
            {
                return (ushort[])samples.Clone();
            }
            ushort[] result = new ushort[toWidth * toHeight];
            double stepX = toWidth > 1 ? (width - 1) / (double)(toWidth - 1) : 0;
            double stepY = toHeight > 1 ? (height - 1) / (double)(toHeight - 1) : 0;
            Parallel.For(0, toHeight, y =>
            {
                double sy = y * stepY;
                int y0 = Math.Min((int)sy, height - 1);
                int y1 = Math.Min(y0 + 1, height - 1);
                double v = sy - y0;
                for (int x = 0; x < toWidth; x++)
                {
                    double sx = x * stepX;
                    int x0 = Math.Min((int)sx, width - 1);
                    int x1 = Math.Min(x0 + 1, width - 1);
                    double u = sx - x0;
                    double top = samples[y0 * width + x0] * (1 - u) + samples[y0 * width + x1] * u;
                    double bottom = samples[y1 * width + x0] * (1 - u) + samples[y1 * width + x1] * u;
                    result[y * toWidth + x] = (ushort)Math.Clamp(Math.Round(top * (1 - v) + bottom * v), 0, ushort.MaxValue);
                }
            });
            return result;
        }

        /// <summary>
        /// The document with the heightmap of a file in place of its own, resampled to the map's size
        /// ((width + 1) by (height + 1) samples).
        /// </summary>
        /// <param name="original">The size of the file before resampling, to tell the person.</param>
        /// <exception cref="MapProjectException">The file cannot be read.</exception>
        public static MapDocument Import(MapDocument document, string path, out (int Width, int Height) original)
        {
            (int width, int height, ushort[] samples) = Read(path);
            original = (width, height);
            int toWidth = document.Width + 1;
            int toHeight = document.Height + 1;
            ushort[] resampled = Resample(samples, width, height, toWidth, toHeight);
            return document with
            {
                Terrain = document.Terrain with
                {
                    Heightmap = document.Terrain.Heightmap with { Samples = TiledBuffer<ushort>.FromArray(toWidth, toHeight, resampled) },
                },
            };
        }

        private static bool IsRaw(string path) => RawExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    }
}
