using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project
{
    /// <summary>
    /// Converts images between red, green, blue and alpha bytes per pixel (as ImageMagick and the
    /// DDS textures hand them over) and the document's grids of <see cref="Color32"/>.
    /// </summary>
    internal static class Pixels
    {
        public static TiledBuffer<Color32> FromRgba(int width, int height, ReadOnlySpan<byte> rgba)
        {
            Color32[] pixels = new Color32[width * height];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32(rgba[i * 4 + 3], rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2]);
            }
            return TiledBuffer<Color32>.FromArray(width, height, pixels);
        }

        public static byte[] ToRgba(TiledBuffer<Color32> image)
        {
            Color32[] pixels = image.ToArray();
            byte[] rgba = new byte[pixels.Length * 4];
            for (int i = 0; i < pixels.Length; i++)
            {
                rgba[i * 4] = pixels[i].Red;
                rgba[i * 4 + 1] = pixels[i].Green;
                rgba[i * 4 + 2] = pixels[i].Blue;
                rgba[i * 4 + 3] = pixels[i].Alpha;
            }
            return rgba;
        }
    }
}
