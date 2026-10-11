namespace FAForever.MapEditor.Project.Workspaces
{
    /// <summary>
    /// File names the editor makes from names a map maker typed (presets, templates, stamps). They
    /// keep to what Windows allows on every system, so a workspace or project made on Linux or macOS
    /// opens on Windows too (and in git, which carries the names as they are).
    /// </summary>
    public static class FileNames
    {
        /// <summary>
        /// The characters Windows refuses in a file name, which <see cref="Path.GetInvalidFileNameChars"/>
        /// gives only on Windows: control characters and <c>"&lt;&gt;|:*?\/</c>.
        /// </summary>
        public static bool IsInvalid(char c) => c < ' ' || c is '"' or '<' or '>' or '|' or ':' or '*' or '?' or '\\' or '/';

        /// <summary>
        /// <paramref name="name"/> trimmed, with every character a file name cannot hold
        /// (<see cref="IsInvalid"/>) made a dash; empty when the name is blank.
        /// </summary>
        public static string Clean(string name) => new string([.. name.Trim().Select(c => IsInvalid(c) ? '-' : c)]);
    }
}
