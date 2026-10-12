using System.Globalization;

namespace FAForever.MapEditor.Cli.Guide
{
    /// <summary>
    /// The crop maths of the guide's pictures, in the window's pixels.
    /// </summary>
    public static class GuideGeometry
    {
        /// <summary>
        /// The margin around a target when a step gives none.
        /// </summary>
        public const int DefaultMargin = 40;

        /// <summary>
        /// A crop covering at least this share of the window is the whole window instead.
        /// </summary>
        public const double WholeWindowShare = 0.55;

        /// <summary>
        /// The region of the picture of a target: its bounds and the margin around them, kept inside
        /// the window; null when it is so large that the whole window serves better (a target that
        /// fills the window, or a step without a target).
        /// </summary>
        public static int[]? Crop(int[]? bounds, int margin, int windowWidth, int windowHeight)
        {
            if (bounds is null || bounds.Length != 4 || bounds[2] <= 0 || bounds[3] <= 0)
            {
                return null;
            }
            int left = Math.Max(0, bounds[0] - margin);
            int top = Math.Max(0, bounds[1] - margin);
            int right = Math.Min(windowWidth, bounds[0] + bounds[2] + margin);
            int bottom = Math.Min(windowHeight, bounds[1] + bounds[3] + margin);
            if (right <= left || bottom <= top)
            {
                return null;
            }
            int[] crop = [left, top, right - left, bottom - top];
            return (double)crop[2] * crop[3] >= WholeWindowShare * windowWidth * windowHeight ? null : crop;
        }

        /// <summary>
        /// The smallest rectangle holding both.
        /// </summary>
        public static int[] Union(int[] a, int[] b)
        {
            int left = Math.Min(a[0], b[0]);
            int top = Math.Min(a[1], b[1]);
            int right = Math.Max(a[0] + a[2], b[0] + b[2]);
            int bottom = Math.Max(a[1] + a[3], b[1] + b[3]);
            return [left, top, right - left, bottom - top];
        }

        /// <summary>
        /// The part of a lying inside b, or null when they do not meet.
        /// </summary>
        public static int[]? Intersect(int[] a, int[] b)
        {
            int left = Math.Max(a[0], b[0]);
            int top = Math.Max(a[1], b[1]);
            int right = Math.Min(a[0] + a[2], b[0] + b[2]);
            int bottom = Math.Min(a[1] + a[3], b[1] + b[3]);
            return right > left && bottom > top ? [left, top, right - left, bottom - top] : null;
        }

        /// <summary>
        /// A rectangle grown by <paramref name="by"/> on every side.
        /// </summary>
        public static int[] Inflate(int[] bounds, int by) => [bounds[0] - by, bounds[1] - by, bounds[2] + 2 * by, bounds[3] + 2 * by];

        /// <summary>
        /// Reads a size such as 1280x800.
        /// </summary>
        public static (int Width, int Height) ParseSize(string text)
        {
            string[] parts = text.ToLowerInvariant().Split('x');
            if (parts.Length != 2
                || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int width)
                || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int height)
                || width < 400 || height < 300)
            {
                throw new GuideException($"'{text}' is not a window size such as 1280x800 (at least 400x300)");
            }
            return (width, height);
        }
    }
}
