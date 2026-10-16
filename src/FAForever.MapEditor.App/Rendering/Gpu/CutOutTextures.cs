using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.Rendering.Gpu;

/// <summary>
/// Textures of models whose pixels below an alpha are cut out (leaves), made ready for their mipmaps.
/// </summary>
public static class CutOutTextures
{
    /// <summary>
    /// The picture with the colour of each pixel whose alpha is below <paramref name="alphaTest"/>
    /// (0 to 1) set to the average colour of the others; the alpha stays.
    /// </summary>
    public static DecodedTexture Fill(DecodedTexture image, float alphaTest)
    {
        byte cut = (byte)Math.Clamp(MathF.Round(alphaTest * 255), 1, 255);
        byte[] rgba = image.Rgba;
        long red = 0, green = 0, blue = 0, kept = 0;
        for (int i = 0; i + 3 < rgba.Length; i += 4)
        {
            if (rgba[i + 3] >= cut)
            {
                red += rgba[i];
                green += rgba[i + 1];
                blue += rgba[i + 2];
                kept++;
            }
        }
        if (kept == 0 || kept * 4 == rgba.Length)
        {
            return image;
        }
        byte[] filled = (byte[])rgba.Clone();
        (byte r, byte g, byte b) = ((byte)(red / kept), (byte)(green / kept), (byte)(blue / kept));
        for (int i = 0; i + 3 < filled.Length; i += 4)
        {
            if (filled[i + 3] < cut)
            {
                (filled[i], filled[i + 1], filled[i + 2]) = (r, g, b);
            }
        }
        return image with { Rgba = filled };
    }
}
