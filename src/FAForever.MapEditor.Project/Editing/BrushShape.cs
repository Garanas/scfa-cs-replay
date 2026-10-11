using ImageMagick;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// The shape of a brush from a greyscale picture (the workspace's <c>brushes</c> folder): black
    /// does nothing, white works fully. The picture spans the brush's square, its longer side from
    /// one edge of the radius to the other, its top towards the north of the map.
    /// </summary>
    public sealed class BrushShape
    {
        /// <summary>
        /// The longest side a picture is reduced to: more detail than a brush of the widest radius
        /// (128 ogrids, 256 samples across) can paint.
        /// </summary>
        public const int MaxSide = 256;

        private readonly float[] _values;
        private readonly int _side;

        /// <param name="values">From 0 to 1, row by row from the top.</param>
        /// <exception cref="ArgumentException">The values do not fill the size.</exception>
        public BrushShape(int width, int height, float[] values)
        {
            if (width <= 0 || height <= 0 || values.Length != width * height)
            {
                throw new ArgumentException($"A shape of {width} by {height} needs that many values, not {values.Length}", nameof(values));
            }
            Width = width;
            Height = height;
            _values = values;
            _side = Math.Max(width, height);
        }

        public int Width { get; }

        public int Height { get; }

        /// <summary>
        /// The values from 0 to 1, row by row from the top: for a picture of the shape.
        /// </summary>
        public ReadOnlySpan<float> Values => _values;

        /// <summary>
        /// Reads a picture of any depth or colour as its brightness, reduced to at most
        /// <see cref="MaxSide"/> pixels and stretched so that its brightest pixel is 1.
        /// </summary>
        /// <exception cref="MapProjectException">The file is missing or no picture.</exception>
        public static BrushShape Read(string path)
        {
            byte[] bytes;
            try
            {
                // shared for writing and deleting: the brushes folder is the map maker's, and a brush
                // replaced or deleted while it is read must not fail (the watcher reads it again)
                using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                bytes = new byte[stream.Length];
                stream.ReadExactly(bytes);
            }
            catch (IOException exception)
            {
                throw new MapProjectException($"The brush {path} cannot be read: {exception.Message}", exception);
            }

            try
            {
                using MagickImage image = new MagickImage(bytes);
                image.Alpha(AlphaOption.Off);
                image.Grayscale();
                if (image.Width > MaxSide || image.Height > MaxSide)
                {
                    image.Resize(MaxSide, MaxSide);
                }
                image.Depth = 16;
                image.Endian = Endian.LSB;
                byte[] grey = image.ToByteArray(MagickFormat.Gray);
                int width = (int)image.Width;
                int height = (int)image.Height;
                float[] values = new float[width * height];
                ushort brightest = 0;
                for (int i = 0; i < values.Length; i++)
                {
                    brightest = Math.Max(brightest, BitConverter.ToUInt16(grey, i * 2));
                }
                float scale = brightest == 0 ? 0 : 1f / brightest;
                for (int i = 0; i < values.Length; i++)
                {
                    values[i] = BitConverter.ToUInt16(grey, i * 2) * scale;
                }
                return new BrushShape(width, height, values);
            }
            catch (MagickException exception)
            {
                throw new MapProjectException($"The brush {path} cannot be read as a picture: {exception.Message}", exception);
            }
        }

        /// <summary>
        /// The value at a place of the brush's square, from (-1, -1) at its top left to (1, 1) at
        /// its bottom right, between the pixels' centres; 0 outside the picture.
        /// </summary>
        public float Sample(float u, float v) => Sample(u, v, clamp: false);

        /// <summary>
        /// The value at a place of the brush's square as <see cref="Sample(float, float)"/> gives it,
        /// or with <paramref name="clamp"/> the value of the picture's nearest pixel outside it: a
        /// stamp keeps its edge's value to the picture's side, where a brush fades to nothing.
        /// </summary>
        public float Sample(float u, float v, bool clamp)
        {
            // pixels from the square's top left; a shorter side is centred in the square
            float px = (u + 1) * 0.5f * _side - (_side - Width) * 0.5f - 0.5f;
            float py = (v + 1) * 0.5f * _side - (_side - Height) * 0.5f - 0.5f;
            if (clamp)
            {
                px = Math.Clamp(px, 0, Width - 1);
                py = Math.Clamp(py, 0, Height - 1);
            }
            int x0 = (int)MathF.Floor(px);
            int y0 = (int)MathF.Floor(py);
            float fx = px - x0;
            float fy = py - y0;
            float top = At(x0, y0) + (At(x0 + 1, y0) - At(x0, y0)) * fx;
            float bottom = At(x0, y0 + 1) + (At(x0 + 1, y0 + 1) - At(x0, y0 + 1)) * fx;
            return top + (bottom - top) * fy;
        }

        /// <summary>
        /// How far a place of the brush's square (as <see cref="Sample"/> takes it) lies inside the
        /// picture: the distance to the picture's nearest side, as a part of half the square's side;
        /// 0 or less outside it. A stamp (<see cref="HeightStamp"/>) fades out towards the sides by it.
        /// </summary>
        public float Border(float u, float v)
        {
            float px = (u + 1) * 0.5f * _side - (_side - Width) * 0.5f;
            float py = (v + 1) * 0.5f * _side - (_side - Height) * 0.5f;
            float pixels = MathF.Min(MathF.Min(px, Width - px), MathF.Min(py, Height - py));
            return pixels / (_side * 0.5f);
        }

        private float At(int x, int y) => x < 0 || y < 0 || x >= Width || y >= Height ? 0 : _values[y * Width + x];
    }
}
