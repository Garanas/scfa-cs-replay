using System.Collections.Immutable;
using FAForever.MapEditor.Project.Files.InMemory;
using ImageMagick;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// Where a mouse drag on a projected image takes hold of it (user story N-2).
    /// </summary>
    public enum ImageHandle
    {
        None,

        /// <summary>
        /// Inside the picture: the drag moves it.
        /// </summary>
        Move,

        TopLeft,
        TopRight,
        BottomRight,
        BottomLeft,

        /// <summary>
        /// The knob beyond the middle of the top edge: the drag turns the picture.
        /// </summary>
        Turn,
    }

    /// <summary>
    /// The projected images' own rules (user story N-2): their names, reading a picture into the
    /// project, and the geometry of an image on the map: its corners, where a point lies in it, and
    /// what dragging a handle makes of it. Pure functions of the image; the document edits are
    /// <see cref="ProjectedImageEdits"/>.
    /// </summary>
    public static class ProjectedImages
    {
        /// <summary>
        /// The longest side of a picture kept in the project, in pixels; a larger one is scaled down.
        /// </summary>
        public const int MaxPixels = 4096;

        /// <summary>
        /// The smallest an image is made by scaling, in ogrids on its shorter side.
        /// </summary>
        public const double MinimumSize = 1;

        /// <summary>
        /// The step of a turn with Shift held, in degrees.
        /// </summary>
        public const double TurnStep = 15;

        #region Names

        /// <summary>
        /// Why <paramref name="name"/> cannot be an image's name; null when it can. An image's name is
        /// the name of its file, so it holds letters, digits, spaces, dashes and underscores, and no
        /// other image has it in any case (Windows compares file names without case).
        /// </summary>
        /// <param name="except">The image that has the name now, which may keep it.</param>
        public static string? Problem(MapDocument document, string name, string? except = null)
        {
            if (name.Length == 0)
            {
                return "An image needs a name";
            }
            if (name != name.Trim())
            {
                return "An image's name does not start or end with a space";
            }
            if (!name.All(c => char.IsAsciiLetterOrDigit(c) || c is ' ' or '-' or '_'))
            {
                return $"'{name}' is no name for an image: use letters, digits, spaces, dashes and underscores";
            }
            if (document.Images.FirstOrDefault(other => other.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && other.Name != except) is { } taken)
            {
                return $"The map has an image '{taken.Name}' already";
            }
            return null;
        }

        /// <summary>
        /// A name no image has: <paramref name="stem"/> when free, else with the first free number after it.
        /// </summary>
        public static string FreeName(MapDocument document, string stem)
        {
            if (Problem(document, stem) is null)
            {
                return stem;
            }
            for (int number = 2; ; number++)
            {
                string name = $"{stem} {number}";
                if (Problem(document, name) is null)
                {
                    return name;
                }
            }
        }

        /// <summary>
        /// A name from a picture's file: its name without extension, whatever a name cannot hold
        /// turned into a dash; <c>Image</c> when no letter or digit is left. Either separator ends a
        /// folder, on every system (a path from Windows read on Linux).
        /// </summary>
        public static string NameOfFile(string path)
        {
            string stem = Path.GetFileNameWithoutExtension(path[(path.LastIndexOfAny(['/', '\\']) + 1)..]);
            string name = new string([.. stem.Select(c => char.IsAsciiLetterOrDigit(c) || c is ' ' or '-' or '_' ? c : '-')]).Trim();
            return name.Any(char.IsAsciiLetterOrDigit) ? name : "Image";
        }

        #endregion

        #region Pictures

        /// <summary>
        /// Reads a picture of any format ImageMagick reads (PNG, JPEG, WebP, BMP, TIFF, ...) the way the
        /// project keeps it: turned upright by its orientation tag, scaled down to <see cref="MaxPixels"/>
        /// on its longest side when larger, as a PNG.
        /// </summary>
        /// <exception cref="MapProjectException">The file is missing or is no picture.</exception>
        public static (ImmutableArray<byte> Png, int Width, int Height) Read(string path)
        {
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new MapProjectException($"{path} cannot be read: {exception.Message}", exception);
            }
            return Read(bytes, path);
        }

        /// <summary>
        /// <see cref="Read(string)"/> for the bytes of a file.
        /// </summary>
        /// <param name="name">What the error names: the file the bytes came from.</param>
        public static (ImmutableArray<byte> Png, int Width, int Height) Read(byte[] bytes, string name)
        {
            try
            {
                using MagickImage image = new MagickImage(bytes);
                image.AutoOrient();
                if (image.Width > MaxPixels || image.Height > MaxPixels)
                {
                    image.Resize(new MagickGeometry(MaxPixels, MaxPixels));
                }
                image.Depth = 8;
                // the pixel size is read before the bytes are made: both come from the same image
                int width = (int)image.Width;
                int height = (int)image.Height;
                return (System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsImmutableArray(image.ToByteArray(MagickFormat.Png)), width, height);
            }
            catch (MagickException exception)
            {
                throw new MapProjectException($"{name} cannot be read as a picture: {exception.Message}", exception);
            }
        }

        /// <summary>
        /// The size of a PNG in pixels, without reading its pixels.
        /// </summary>
        public static (int Width, int Height) SizeOf(ReadOnlySpan<byte> png)
        {
            MagickImageInfo info = new MagickImageInfo(png);
            return ((int)info.Width, (int)info.Height);
        }

        /// <summary>
        /// The picture without colour, as a PNG (the top view draws an image set to greyscale from it).
        /// </summary>
        public static byte[] Greyscale(ReadOnlySpan<byte> png)
        {
            using MagickImage image = new MagickImage(png);
            image.Grayscale();
            return image.ToByteArray(MagickFormat.Png);
        }

        #endregion

        #region Geometry

        /// <summary>
        /// The corners in the order top left, top right, bottom right, bottom left (as the picture is
        /// before it is turned), where they lie on the map.
        /// </summary>
        public static (double X, double Z)[] Corners(ProjectedImage image)
        {
            double w = image.Width / 2;
            double h = image.Height / 2;
            return [ToMap(image, -w, -h), ToMap(image, w, -h), ToMap(image, w, h), ToMap(image, -w, h)];
        }

        /// <summary>
        /// A place of the map in the picture's own frame: from its middle, x along its top edge, z down it.
        /// </summary>
        public static (double X, double Z) ToLocal(ProjectedImage image, double x, double z)
        {
            (double sin, double cos) = Math.SinCos(image.Turn * Math.PI / 180);
            double dx = x - image.CentreX;
            double dz = z - image.CentreZ;
            return (dx * cos + dz * sin, -dx * sin + dz * cos);
        }

        /// <summary>
        /// A place of the picture's own frame on the map.
        /// </summary>
        public static (double X, double Z) ToMap(ProjectedImage image, double x, double z)
        {
            (double sin, double cos) = Math.SinCos(image.Turn * Math.PI / 180);
            return (image.CentreX + x * cos - z * sin, image.CentreZ + x * sin + z * cos);
        }

        /// <summary>
        /// Whether a place of the map lies on the picture.
        /// </summary>
        public static bool Contains(ProjectedImage image, double x, double z)
        {
            (double lx, double lz) = ToLocal(image, x, z);
            return Math.Abs(lx) <= image.Width / 2 && Math.Abs(lz) <= image.Height / 2;
        }

        /// <summary>
        /// Where the turning knob lies: beyond the middle of the top edge, <paramref name="distance"/>
        /// ogrids from it (the view keeps it a few pixels away at every zoom).
        /// </summary>
        public static (double X, double Z) Knob(ProjectedImage image, double distance) => ToMap(image, 0, -(image.Height / 2 + distance));

        /// <summary>
        /// What a press at a place of the map takes hold of: the knob or a corner within
        /// <paramref name="reach"/> ogrids, else the inside of the picture, else nothing.
        /// </summary>
        /// <param name="knobDistance">How far beyond the top edge the knob lies, in ogrids.</param>
        public static ImageHandle HandleAt(ProjectedImage image, double x, double z, double reach, double knobDistance)
        {
            (double kx, double kz) = Knob(image, knobDistance);
            if (Distance(kx, kz, x, z) <= reach)
            {
                return ImageHandle.Turn;
            }
            (double X, double Z)[] corners = Corners(image);
            ImageHandle[] handles = [ImageHandle.TopLeft, ImageHandle.TopRight, ImageHandle.BottomRight, ImageHandle.BottomLeft];
            int nearest = -1;
            double best = reach;
            for (int i = 0; i < 4; i++)
            {
                double distance = Distance(corners[i].X, corners[i].Z, x, z);
                if (distance <= best)
                {
                    best = distance;
                    nearest = i;
                }
            }
            return nearest >= 0 ? handles[nearest] : Contains(image, x, z) ? ImageHandle.Move : ImageHandle.None;
        }

        private static double Distance(double x0, double z0, double x1, double z1) => Math.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0));

        /// <summary>
        /// The image moved by a distance on the map.
        /// </summary>
        public static ProjectedImage Moved(ProjectedImage image, double dx, double dz) =>
            image with { CentreX = image.CentreX + dx, CentreZ = image.CentreZ + dz };

        /// <summary>
        /// The image with the corner <paramref name="corner"/> dragged to a place of the map, the opposite
        /// corner staying where it is. Keeping the proportions the corner slides along the diagonal;
        /// otherwise it follows the place on both sides. Never smaller than <see cref="MinimumSize"/>
        /// on its shorter side, and never turned over.
        /// </summary>
        /// <param name="start">The image as it was when the drag began, so the drag is the same wherever the mouse has been.</param>
        public static ProjectedImage Scaled(ProjectedImage start, ImageHandle corner, double x, double z, bool keepProportions)
        {
            (int sx, int sz) = corner switch
            {
                ImageHandle.TopLeft => (-1, -1),
                ImageHandle.TopRight => (1, -1),
                ImageHandle.BottomRight => (1, 1),
                ImageHandle.BottomLeft => (-1, 1),
                _ => throw new ArgumentException($"{corner} is no corner of an image", nameof(corner)),
            };
            (double px, double pz) = ToLocal(start, x, z);
            // the fixed corner, in the picture's frame
            double ox = -sx * start.Width / 2;
            double oz = -sz * start.Height / 2;
            double width;
            double height;
            if (keepProportions)
            {
                double dx = sx * start.Width;
                double dz = sz * start.Height;
                double scale = ((px - ox) * dx + (pz - oz) * dz) / (dx * dx + dz * dz);
                scale = Math.Max(scale, MinimumSize / Math.Min(start.Width, start.Height));
                width = start.Width * scale;
                height = start.Height * scale;
            }
            else
            {
                width = Math.Max(MinimumSize, sx * (px - ox));
                height = Math.Max(MinimumSize, sz * (pz - oz));
            }
            (double cx, double cz) = ToMap(start, ox + sx * width / 2, oz + sz * height / 2);
            return start with { CentreX = cx, CentreZ = cz, Width = width, Height = height };
        }

        /// <summary>
        /// The image turned by the angle the mouse went about the image's middle, from the place it
        /// took hold at to the place it is now; <paramref name="snap"/> rounds the turn to
        /// <see cref="TurnStep"/> degrees.
        /// </summary>
        public static ProjectedImage Turned(ProjectedImage start, double fromX, double fromZ, double toX, double toZ, bool snap)
        {
            double before = Math.Atan2(fromZ - start.CentreZ, fromX - start.CentreX);
            double after = Math.Atan2(toZ - start.CentreZ, toX - start.CentreX);
            double turn = start.Turn + (after - before) * 180 / Math.PI;
            if (snap)
            {
                turn = Math.Round(turn / TurnStep) * TurnStep;
            }
            return start with { Turn = Normalized(turn) };
        }

        /// <summary>
        /// The image with its place, size and turn rounded to hundredths, as a drag leaves them: the
        /// mouse's float pixels do not make fields of seven decimals.
        /// </summary>
        public static ProjectedImage Rounded(ProjectedImage image) => image with
        {
            CentreX = Math.Round(image.CentreX, 2),
            CentreZ = Math.Round(image.CentreZ, 2),
            Width = Math.Max(MinimumSize, Math.Round(image.Width, 2)),
            Height = Math.Max(MinimumSize, Math.Round(image.Height, 2)),
            Turn = Math.Round(image.Turn, 2),
        };

        /// <summary>
        /// An angle in degrees brought into the range -180 (excluded) to 180.
        /// </summary>
        public static double Normalized(double degrees)
        {
            double turn = degrees % 360;
            if (turn > 180)
            {
                turn -= 360;
            }
            else if (turn <= -180)
            {
                turn += 360;
            }
            return turn;
        }

        /// <summary>
        /// The image fitted to a rectangle of the map: its middle, not turned, as large as the
        /// rectangle lets it be with its proportions (the picture's, not those a scaling gave it), or
        /// the rectangle itself when <see cref="ProjectedImage.KeepProportions"/> is off.
        /// </summary>
        public static ProjectedImage Fitted(ProjectedImage image, double x0, double z0, double x1, double z1)
        {
            double width = Math.Max(MinimumSize, Math.Abs(x1 - x0));
            double height = Math.Max(MinimumSize, Math.Abs(z1 - z0));
            if (image.KeepProportions)
            {
                double scale = Math.Min(width / image.PixelWidth, height / image.PixelHeight);
                width = image.PixelWidth * scale;
                height = image.PixelHeight * scale;
            }
            return image with { CentreX = (x0 + x1) / 2, CentreZ = (z0 + z1) / 2, Width = width, Height = height, Turn = 0 };
        }

        #endregion
    }
}
