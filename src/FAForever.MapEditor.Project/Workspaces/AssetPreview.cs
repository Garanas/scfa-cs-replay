using FAForever.FileFormats.Mesh;
using ImageMagick;

namespace FAForever.MapEditor.Project.Workspaces
{
    /// <summary>
    /// Draws the picture of a prop or unit from its converted mesh and albedo (<see cref="AssetModel"/>),
    /// with <see cref="MeshPreview"/>: a still from above and in front, on a transparent background.
    /// </summary>
    public static class AssetPreview
    {
        /// <summary>
        /// Draws a picture of <paramref name="size"/> by <paramref name="size"/> pixels. The albedo is
        /// read at most twice that wide, which is as sharp as the picture can show it; without an
        /// albedo the mesh is grey.
        /// </summary>
        /// <exception cref="IOException">The mesh cannot be read from disk.</exception>
        /// <exception cref="GlbFormatException">The mesh is no glTF file.</exception>
        /// <exception cref="MagickException">The albedo is no picture.</exception>
        public static RgbaImage Render(AssetModel model, int size)
        {
            Scm mesh = GlbReader.Read(File.ReadAllBytes(model.Mesh));
            RgbaImage? albedo = model.Albedo is string file && File.Exists(file) ? ReadAlbedo(file, Math.Max(64, 2 * size)) : null;
            return MeshPreview.Render(mesh, albedo, new MeshPreviewOptions { Size = size, Alpha = MeshPreview.AlphaOf(model.Shader) });
        }

        private static RgbaImage ReadAlbedo(string file, int maxWidth)
        {
            // ImageMagick gets the bytes: its own file access fails on long paths
            using MagickImage image = new MagickImage(File.ReadAllBytes(file));
            if (image.Width > maxWidth)
            {
                image.Resize((uint)maxWidth, 0);
            }
            image.Alpha(AlphaOption.Set);
            using IPixelCollection<ushort> pixels = image.GetPixels();
            return new RgbaImage((int)image.Width, (int)image.Height, pixels.ToByteArray(PixelMapping.RGBA)!);
        }
    }
}
