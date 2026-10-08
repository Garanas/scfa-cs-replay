namespace FAForever.MapEditor.Project.Files
{
    /// <summary>
    /// How a texture is stored in the <c>.scmap</c>; every texture in the maps of the vault is one of these.
    /// </summary>
    public enum TextureFormat
    {
        /// <summary>
        /// Uncompressed, 32 bits per pixel (blue, green, red and alpha bytes).
        /// </summary>
        A8R8G8B8,

        /// <summary>
        /// Block compressed (BC3), with interpolated alpha.
        /// </summary>
        Dxt5,
    }
}
