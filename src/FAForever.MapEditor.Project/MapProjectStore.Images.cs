using System.Collections.Immutable;
using System.Runtime.InteropServices;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Files.Serialized;

namespace FAForever.MapEditor.Project
{
    /// <summary>
    /// The projected images (user story N-2) on disk: <c>sketch/images.json</c> lists them in the order
    /// they are drawn, and each picture is the PNG <c>sketch/images/&lt;name&gt;.png</c>. Written only
    /// for a map with images; part of the sketch, the project's own, never exported.
    /// </summary>
    public static partial class MapProjectStore
    {
        private static void SaveImages(MapDocument document, string folder, MapDocument? previous)
        {
            string list = Path.Combine(folder, MapProjectLayout.ImagesFile);
            string pictures = Path.Combine(folder, MapProjectLayout.ImagesFolder);
            if (document.Images.IsEmpty)
            {
                if (File.Exists(list))
                {
                    File.Delete(list);
                }
                if (Directory.Exists(pictures))
                {
                    Directory.Delete(pictures, recursive: true);
                }
                return;
            }

            Directory.CreateDirectory(pictures);
            // gone, or renamed to another case of its name (Windows compares file names without case): written again below
            HashSet<string> wanted = new HashSet<string>(document.Images.Select(image => Path.GetFileName(MapProjectLayout.Image(image.Name))), StringComparer.Ordinal);
            foreach (string file in Directory.EnumerateFiles(pictures).ToList())
            {
                if (!wanted.Contains(Path.GetFileName(file)))
                {
                    File.Delete(file);
                }
            }
            foreach (ProjectedImage image in document.Images)
            {
                string file = Path.Combine(folder, MapProjectLayout.Image(image.Name));
                bool unchanged = previous is not null && previous.Images.Any(before => before.Name == image.Name && before.Picture == image.Picture) && File.Exists(file);
                if (!unchanged)
                {
                    File.WriteAllBytes(file, ImmutableCollectionsMarshal.AsArray(image.Picture)!);
                }
            }
            ProjectJson.Write(list, document.Images.Select(ToDto).ToList());
        }

        private static MapDocument WithImages(MapDocument document, string folder) =>
            document with { Images = ReadImages(folder) };

        /// <summary>
        /// The document with its projected images read again from the files; itself when they say the same.
        /// </summary>
        private static MapDocument ReloadImages(MapDocument document, string folder)
        {
            ImmutableArray<ProjectedImage> read = ReadImages(folder);
            return SameImages(read, document.Images) ? document : document with { Images = read };
        }

        private static bool SameImages(ImmutableArray<ProjectedImage> a, ImmutableArray<ProjectedImage> b) =>
            a.Length == b.Length && a.Zip(b).All(pair => pair.First == pair.Second with { Picture = pair.First.Picture } && pair.First.Picture.AsSpan().SequenceEqual(pair.Second.Picture.AsSpan()));

        /// <exception cref="MapProjectException">The list does not read, or a picture is missing or is no picture.</exception>
        private static ImmutableArray<ProjectedImage> ReadImages(string folder)
        {
            string list = Path.Combine(folder, MapProjectLayout.ImagesFile);
            if (!File.Exists(list))
            {
                return [];
            }
            List<ProjectedImageDto> dtos = ProjectJson.Read<List<ProjectedImageDto>>(list);
            ImmutableArray<ProjectedImage>.Builder images = ImmutableArray.CreateBuilder<ProjectedImage>(dtos.Count);
            foreach (ProjectedImageDto dto in dtos)
            {
                string file = Path.Combine(folder, MapProjectLayout.Image(dto.Name));
                if (!File.Exists(file))
                {
                    throw new MapProjectException($"{file} is missing: {list} names the image {dto.Name}");
                }
                byte[] bytes = File.ReadAllBytes(file);
                (int width, int height) = ImageSizeOf(bytes, file);
                images.Add(ToModel(dto, ImmutableCollectionsMarshal.AsImmutableArray(bytes), width, height));
            }
            return images.MoveToImmutable();
        }

        private static (int Width, int Height) ImageSizeOf(byte[] bytes, string file)
        {
            try
            {
                return ProjectedImages.SizeOf(bytes);
            }
            catch (ImageMagick.MagickException exception)
            {
                throw new MapProjectException($"{file} cannot be read as an image: {exception.Message}", exception);
            }
        }

        private static ProjectedImageDto ToDto(ProjectedImage image) => new ProjectedImageDto
        {
            Name = image.Name,
            CentreX = image.CentreX,
            CentreZ = image.CentreZ,
            Width = image.Width,
            Height = image.Height,
            Turn = image.Turn,
            Opacity = image.Opacity,
            Greyscale = image.Greyscale,
            Locked = image.Locked,
            Shown = image.Shown,
            AboveSketch = image.AboveSketch,
            KeepProportions = image.KeepProportions,
        };

        private static ProjectedImage ToModel(ProjectedImageDto dto, ImmutableArray<byte> picture, int width, int height) => new ProjectedImage
        {
            Name = dto.Name,
            Picture = picture,
            PixelWidth = width,
            PixelHeight = height,
            CentreX = dto.CentreX,
            CentreZ = dto.CentreZ,
            Width = dto.Width,
            Height = dto.Height,
            Turn = dto.Turn,
            Opacity = dto.Opacity,
            Greyscale = dto.Greyscale,
            Locked = dto.Locked,
            Shown = dto.Shown,
            AboveSketch = dto.AboveSketch,
            KeepProportions = dto.KeepProportions,
        };
    }
}
