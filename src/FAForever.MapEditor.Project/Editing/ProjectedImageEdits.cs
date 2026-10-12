using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// Edits of the map's projected images (user story N-2), each a function from one version of the
    /// document to the next for <see cref="EditSession.Edit"/> or <see cref="EditTransaction.Apply"/>.
    /// An image is named by its name; the list's order is the order they are drawn in.
    /// </summary>
    public static class ProjectedImageEdits
    {
        /// <summary>
        /// The image of that name (in any case), or null.
        /// </summary>
        public static ProjectedImage? FindImage(this MapDocument document, string name) =>
            document.Images.FirstOrDefault(image => image.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is { } found ? found : null;

        /// <summary>
        /// Adds an image on top of the others of its kind, in the middle of the map and as large as
        /// the map lets it be (<see cref="ProjectedImages.Fitted"/>), under a free name.
        /// </summary>
        /// <param name="added">The image as added, with the name it got.</param>
        public static MapDocument AddImage(this MapDocument document, ProjectedImage image, out ProjectedImage added)
        {
            added = ProjectedImages.Fitted(image with { Name = ProjectedImages.FreeName(document, image.Name) }, 0, 0, document.Width, document.Height);
            return document with { Images = document.Images.Add(added) };
        }

        /// <exception cref="KeyNotFoundException">No image has the name.</exception>
        public static MapDocument UpdateImage(this MapDocument document, string name, Func<ProjectedImage, ProjectedImage> update) =>
            document with { Images = document.Images.Update(image => image.Name == name, update, $"image {name}") };

        /// <exception cref="KeyNotFoundException">No image has the name.</exception>
        public static MapDocument RemoveImage(this MapDocument document, string name) =>
            document with { Images = document.Images.RemoveAt(document.Images.FindIndex(image => image.Name == name, name)) };

        /// <summary>
        /// Gives an image another name, which no other image has (<see cref="ProjectedImages.Problem"/>).
        /// </summary>
        /// <exception cref="ArgumentException">The name cannot be an image's.</exception>
        /// <exception cref="KeyNotFoundException">No image has the name.</exception>
        public static MapDocument RenameImage(this MapDocument document, string name, string to)
        {
            if (ProjectedImages.Problem(document, to, except: name) is string problem)
            {
                throw new ArgumentException(problem, nameof(to));
            }
            return document.UpdateImage(name, image => image with { Name = to });
        }

        /// <summary>
        /// Puts another picture in an image: the place, size and turn stay; the size follows the
        /// picture's proportions only when proportions are kept (the width stays, the height follows).
        /// </summary>
        public static MapDocument ReplacePicture(this MapDocument document, string name, ProjectedImage picture) =>
            document.UpdateImage(name, image =>
            {
                ProjectedImage replaced = image with { Picture = picture.Picture, PixelWidth = picture.PixelWidth, PixelHeight = picture.PixelHeight };
                return image.KeepProportions ? replaced with { Height = image.Width * picture.PixelHeight / picture.PixelWidth } : replaced;
            });

        /// <summary>
        /// Fits an image to the whole map.
        /// </summary>
        public static MapDocument FitImageToMap(this MapDocument document, string name) =>
            document.UpdateImage(name, image => ProjectedImages.Fitted(image, 0, 0, document.Width, document.Height));

        /// <summary>
        /// Fits an image to the playable area; a map without areas is playable as a whole.
        /// </summary>
        public static MapDocument FitImageToPlayableArea(this MapDocument document, string name)
        {
            (double x0, double z0, double x1, double z1) = document.PlayableArea is { } area ? (area.X0, area.Z0, area.X1, area.Z1) : (0, 0, document.Width, document.Height);
            return document.UpdateImage(name, image => ProjectedImages.Fitted(image, x0, z0, x1, z1));
        }

        private static int FindIndex<T>(this System.Collections.Immutable.ImmutableArray<T> items, Func<T, bool> match, string name)
        {
            for (int i = 0; i < items.Length; i++)
            {
                if (match(items[i]))
                {
                    return i;
                }
            }
            throw new KeyNotFoundException($"The map has no image {name}");
        }
    }
}
