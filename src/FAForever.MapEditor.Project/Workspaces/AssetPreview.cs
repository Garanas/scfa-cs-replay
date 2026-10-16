using FAForever.FileFormats.Mesh;
using ImageMagick;

namespace FAForever.MapEditor.Project.Workspaces
{
    /// <summary>
    /// Draws the picture of a prop or unit from its converted mesh and textures (<see cref="AssetModel"/>),
    /// with <see cref="MeshPreview"/>: a still from above and in front, on a transparent background,
    /// shaded as the game's shader the blueprint names.
    /// </summary>
    public static class AssetPreview
    {
        /// <summary>
        /// Draws a picture of <paramref name="size"/> by <paramref name="size"/> pixels. The textures
        /// are read at most twice that wide, which is as sharp as the picture can show them; a
        /// texture that is missing is left out (without an albedo the mesh is grey).
        /// </summary>
        /// <exception cref="IOException">The mesh cannot be read from disk.</exception>
        /// <exception cref="GlbFormatException">The mesh is no glTF file.</exception>
        /// <exception cref="MagickException">A texture is no picture.</exception>
        public static RgbaImage Render(AssetModel model, int size)
        {
            Scm mesh = GlbReader.Read(File.ReadAllBytes(model.Mesh));
            int width = Math.Max(64, 2 * size);
            PreviewTextures textures = new PreviewTextures(Read(model.Albedo, width), Read(model.Normals, width), Read(model.Specular, width));
            (PreviewShader shader, float? alphaTest) = MeshPreview.ShaderOf(model.Shader);
            return MeshPreview.Render(mesh, textures, new MeshPreviewOptions { Size = size, Shader = shader, AlphaTest = alphaTest });
        }

        private static RgbaImage? Read(string? file, int maxWidth)
        {
            if (file is null || !File.Exists(file))
            {
                return null;
            }
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
