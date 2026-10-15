using ImageMagick;
using ImageMagick.Drawing;

namespace FAForever.MapEditor.Cli.Guide
{
    /// <summary>
    /// The guide's pictures: crops of the editor's screenshots, the 3D view put in where a window
    /// screenshot leaves it empty, and the overview's numbered outlines. Pictures carry no text but
    /// the badges' numbers.
    /// </summary>
    public static class GuideImages
    {
        /// <summary>
        /// The tour's ring colour (<c>Blue300</c>) and its width and padding, as the tokens say.
        /// </summary>
        private static readonly MagickColor Ring = new MagickColor("#6CB8F6");
        private const int RingThickness = 2;
        private const int HolePadding = 4;

        /// <summary>
        /// The scrim's share of light that is left under it (its alpha is 0xB3 in the dark theme).
        /// </summary>
        private const double ScrimLight = 1 - 0xB3 / 255.0;

        /// <summary>
        /// The badges' font, a file beside the program: ImageMagick finds no font by name on a
        /// machine without fontconfig (Linux without its configuration).
        /// </summary>
        private static readonly string BadgeFont = Path.Combine(AppContext.BaseDirectory, "Fonts", "IBMPlexSans-SemiBold.ttf");

        /// <summary>
        /// Whether this build of Magick.NET writes WebP; the guide uses PNG when it does not.
        /// </summary>
        public static string Extension { get; } = WebpWorks() ? ".webp" : ".png";

        private static bool WebpWorks()
        {
            try
            {
                using MagickImage probe = new MagickImage(MagickColors.Black, 4, 4);
                probe.Format = MagickFormat.WebP;
                probe.ToByteArray();
                return true;
            }
            catch (MagickException)
            {
                return false;
            }
        }

        /// <summary>
        /// Writes the image as a picture of the guide, small: WebP, or PNG without it.
        /// </summary>
        public static void Save(IMagickImage<ushort> image, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (Extension == ".webp")
            {
                image.Quality = 82;
                image.Settings.SetDefine(MagickFormat.WebP, "method", "6");
                image.Write(path, MagickFormat.WebP);
            }
            else
            {
                image.Write(path, MagickFormat.Png);
            }
        }

        /// <summary>
        /// Puts a frame of the 3D view (a screenshot of the window leaves it empty) into the window's
        /// picture at the view's bounds. Under the tour's scrim it is dimmed as the rest of the window
        /// is, but for the hole around the target, where the ring is drawn again.
        /// </summary>
        /// <param name="scrimmed">Whether a tour's dimming lies over the window's picture.</param>
        /// <param name="target">The step's target in the window's pixels, when it has one.</param>
        public static void PutIn3D(IMagickImage<ushort> window, IMagickImage<ushort> frame, int[] view, bool scrimmed, int[]? target, double scale)
        {
            using IMagickImage<ushort> placed = frame.Clone();
            placed.Resize((uint)Math.Round(view[2] * scale), (uint)Math.Round(view[3] * scale));
            int x = (int)Math.Round(view[0] * scale);
            int y = (int)Math.Round(view[1] * scale);
            if (!scrimmed)
            {
                window.Composite(placed, x, y, CompositeOperator.Over);
                return;
            }

            int[]? hole = target is null ? null : GuideGeometry.Intersect(Scaled(GuideGeometry.Inflate(target, HolePadding), scale), Scaled(view, scale));
            using (IMagickImage<ushort> dimmed = placed.Clone())
            {
                dimmed.Evaluate(Channels.RGB, EvaluateOperator.Multiply, ScrimLight);
                window.Composite(dimmed, x, y, CompositeOperator.Over);
            }
            if (hole is not null)
            {
                using IMagickImage<ushort> bright = placed.Clone();
                bright.Crop(new MagickGeometry(hole[0] - x, hole[1] - y, (uint)hole[2], (uint)hole[3]));
                bright.ResetPage();
                window.Composite(bright, hole[0], hole[1], CompositeOperator.Over);
                int[] ring = Scaled(GuideGeometry.Inflate(target!, HolePadding), scale);
                new Drawables()
                    .StrokeColor(Ring)
                    .StrokeWidth(RingThickness)
                    .FillColor(MagickColors.Transparent)
                    .RoundRectangle(ring[0], ring[1], ring[0] + ring[2], ring[1] + ring[3], 6, 6)
                    .Draw(window);
            }
        }

        /// <summary>
        /// Crops a picture to a region in the window's pixels.
        /// </summary>
        public static IMagickImage<ushort> Crop(IMagickImage<ushort> window, int[] region, double scale)
        {
            IMagickImage<ushort> crop = window.Clone();
            int[] pixels = Scaled(region, scale);
            crop.Crop(new MagickGeometry(pixels[0], pixels[1], (uint)pixels[2], (uint)pixels[3]));
            crop.ResetPage();
            return crop;
        }

        /// <summary>
        /// Draws an outline round each place and a badge with its number at the place's top left
        /// corner; a badge that would cover another moves to the right.
        /// </summary>
        public static void DrawOverview(IMagickImage<ushort> window, IReadOnlyList<(int Number, int[] Bounds)> places, double scale)
        {
            const int badge = 22;
            List<(int X, int Y)> used = [];
            foreach ((int number, int[] bounds) in places)
            {
                int[] box = Scaled(bounds, scale);
                new Drawables()
                    .StrokeColor(Ring)
                    .StrokeWidth(2)
                    .FillColor(MagickColors.Transparent)
                    .RoundRectangle(box[0], box[1], box[0] + box[2] - 1, box[1] + box[3] - 1, 4, 4)
                    .Draw(window);
            }
            foreach ((int number, int[] bounds) in places)
            {
                int[] box = Scaled(bounds, scale);
                // across the corner, so little of what lies in the place is covered
                int x = Math.Max(1, box[0] - badge / 2);
                int y = Math.Max(1, box[1] - badge / 2);
                while (used.Any(other => Math.Abs(other.X - x) < badge + 2 && Math.Abs(other.Y - y) < badge + 2))
                {
                    x += badge + 4;
                }
                used.Add((x, y));
                IDrawables<ushort> drawing = new Drawables()
                    .StrokeColor(MagickColors.Black)
                    .StrokeWidth(1)
                    .FillColor(Ring)
                    .Circle(x + badge / 2.0, y + badge / 2.0, x + badge / 2.0, y);
                drawing.Draw(window);
                using IMagickImage<ushort> text = Number(number, badge);
                window.Composite(text, x, y, CompositeOperator.Over);
            }
        }

        /// <summary>
        /// A number in a square of the given size, drawn as dark digits.
        /// </summary>
        private static IMagickImage<ushort> Number(int number, int size)
        {
            MagickImage image = new MagickImage(MagickColors.Transparent, (uint)size, (uint)size);
            new Drawables()
                .Font(BadgeFont)
                .FontPointSize(number >= 10 ? 11 : 13)
                .StrokeColor(MagickColors.Transparent)
                .FillColor(new MagickColor("#0B1320"))
                .TextAlignment(TextAlignment.Center)
                .Text(size / 2.0, size / 2.0 + 5, number.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Draw(image);
            return image;
        }

        private static int[] Scaled(int[] box, double scale) =>
            scale == 1 ? box : [(int)Math.Round(box[0] * scale), (int)Math.Round(box[1] * scale), (int)Math.Round(box[2] * scale), (int)Math.Round(box[3] * scale)];
    }
}
