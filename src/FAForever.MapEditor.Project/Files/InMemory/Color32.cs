namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// A colour of four bytes: alpha, red, green and blue.
    /// </summary>
    public readonly record struct Color32(byte Alpha, byte Red, byte Green, byte Blue)
    {
        /// <summary>
        /// A colour stored as one number, alpha in the highest byte (<c>0xAARRGGBB</c>).
        /// </summary>
        public static Color32 FromArgb(uint argb) => new Color32((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

        public uint ToArgb() => (uint)Alpha << 24 | (uint)Red << 16 | (uint)Green << 8 | Blue;
    }
}
