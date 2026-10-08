using ImageMagick;

namespace FAForever.MapEditor.Project
{
    /// <summary>
    /// Reads and writes the images of a project with ImageMagick, which reads whatever a map maker's
    /// tools save (8 or 16 bits, grey or colour, with or without alpha) and converts it to what the
    /// map needs. Every read checks the size.
    /// </summary>
    internal static class ProjectImages
    {
        public static byte[] ReadGray8(string path, int width, int height)
        {
            using MagickImage image = Open(path, width, height);
            image.Depth = 8;
            return image.ToByteArray(MagickFormat.Gray);
        }

        public static ushort[] ReadGray16(string path, int width, int height)
        {
            using MagickImage image = Open(path, width, height);
            image.Depth = 16;
            image.Endian = Endian.LSB;
            byte[] bytes = image.ToByteArray(MagickFormat.Gray);
            ushort[] samples = new ushort[width * height];
            Buffer.BlockCopy(bytes, 0, samples, 0, bytes.Length);
            return samples;
        }

        /// <summary>
        /// Red, green, blue and alpha bytes of every pixel; an image without alpha reads as opaque.
        /// </summary>
        public static byte[] ReadRgba(string path, int width, int height)
        {
            using MagickImage image = Open(path, width, height);
            image.Alpha(AlphaOption.Set);
            using IPixelCollection<ushort> pixels = image.GetPixels();
            return pixels.ToByteArray(PixelMapping.RGBA)!;
        }

        /// <summary>
        /// The size of an image without reading its pixels.
        /// </summary>
        public static (int Width, int Height) Size(string path)
        {
            MagickImageInfo info = new MagickImageInfo(File.ReadAllBytes(path));
            return ((int)info.Width, (int)info.Height);
        }

        public static void WriteGray8(string path, int width, int height, byte[] pixels)
        {
            MagickReadSettings settings = new MagickReadSettings { Width = (uint)width, Height = (uint)height, Format = MagickFormat.Gray, Depth = 8 };
            using MagickImage image = new MagickImage(pixels, settings);
            Save(image, path, 8);
        }

        public static void WriteGray16(string path, int width, int height, ushort[] samples)
        {
            byte[] bytes = new byte[samples.Length * 2];
            Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
            MagickReadSettings settings = new MagickReadSettings { Width = (uint)width, Height = (uint)height, Format = MagickFormat.Gray, Depth = 16, Endian = Endian.LSB };
            using MagickImage image = new MagickImage(bytes, settings);
            Save(image, path, 16);
        }

        public static void WriteRgba(string path, int width, int height, byte[] rgba)
        {
            using MagickImage image = new MagickImage(rgba, new PixelReadSettings((uint)width, (uint)height, StorageType.Char, PixelMapping.RGBA));
            Save(image, path, 8);
        }

        private static MagickImage Open(string path, int width, int height)
        {
            if (!File.Exists(path))
            {
                throw new MapProjectException($"{path} is missing");
            }

            MagickImage image;
            try
            {
                image = new MagickImage(File.ReadAllBytes(path));
            }
            catch (MagickException exception)
            {
                throw new MapProjectException($"{path} cannot be read as an image: {exception.Message}", exception);
            }

            if (image.Width != width || image.Height != height)
            {
                string size = $"{image.Width} by {image.Height}";
                image.Dispose();
                throw new MapProjectException($"{path} is {size} pixels, expected {width} by {height}");
            }
            return image;
        }

        private static void Save(MagickImage image, string path, uint depth)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            image.Depth = depth;
            // .NET reads and writes the files: ImageMagick's own file access fails on paths longer than Windows' old limit of 260
            File.WriteAllBytes(path, image.ToByteArray(MagickFormat.Png));
        }
    }
}
