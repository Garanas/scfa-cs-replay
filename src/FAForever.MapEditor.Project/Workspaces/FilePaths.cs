namespace FAForever.MapEditor.Project.Workspaces
{
    /// <summary>
    /// Finds files whatever the case of their names, as the game does (its file system ignores case)
    /// and as Windows does. On Linux, and on a case-sensitive macOS volume, the game's folders, maps
    /// and workspaces copied from Windows name files in another case than they have
    /// (<c>gamedata/Env.scd</c>, <c>Plugin.lua</c>, <c>images/Hill.png</c>).
    /// </summary>
    /// <remarks>
    /// The converted assets need none of this: the conversion writes the game's paths in lower case,
    /// and <see cref="WorkspaceLayout.AssetOf"/> and <see cref="AssetCatalog.KeyOf"/> look them up in
    /// lower case.
    /// </remarks>
    public static class FilePaths
    {
        /// <summary>
        /// Lists files whose names match a pattern (<c>*.scmap</c>) in any case, as
        /// <see cref="Directory.GetFiles(string, string)"/> does on Windows: hidden files too, and the
        /// pattern's old Windows rules (<c>*.lua</c> also matches <c>a.luax</c> there; use exact
        /// extensions after it).
        /// </summary>
        public static EnumerationOptions IgnoringCase(bool recursive = false) => new EnumerationOptions
        {
            MatchCasing = MatchCasing.CaseInsensitive,
            MatchType = MatchType.Win32,
            AttributesToSkip = 0,
            IgnoreInaccessible = false,
            RecurseSubdirectories = recursive,
        };

        /// <summary>
        /// The file or folder <paramref name="path"/> names, in the case it has on disk: the path
        /// itself when it exists as written (always on Windows, whose file system ignores case),
        /// else the entry each part of the path matches ignoring case (the first in ordinal order when
        /// several do). When nothing matches, the path with the folders that do exist in their case
        /// and the rest as written, so a file made there joins the folder that is there.
        /// </summary>
        public static string Existing(string path)
        {
            if (string.IsNullOrEmpty(path) || File.Exists(path) || Directory.Exists(path) || OperatingSystem.IsWindows())
            {
                return path;
            }
            string full = Path.GetFullPath(path);
            string? parent = Path.GetDirectoryName(full);
            string name = Path.GetFileName(full);
            if (parent is null || name.Length == 0)
            {
                return path;
            }
            string inCase = Existing(parent);
            if (!Directory.Exists(inCase))
            {
                return path;
            }
            string? match = null;
            try
            {
                foreach (string entry in Directory.EnumerateFileSystemEntries(inCase))
                {
                    string entryName = Path.GetFileName(entry);
                    if (string.Equals(entryName, name, StringComparison.OrdinalIgnoreCase)
                        && (match is null || string.CompareOrdinal(entryName, Path.GetFileName(match)) < 0))
                    {
                        match = entry;
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return path;
            }
            return match ?? Path.Combine(inCase, name);
        }

        /// <summary>
        /// A path a project or map keeps (written on any system, with either separator) in the form
        /// of this system: <c>procedural\temp\draft.png</c> becomes <c>procedural/temp/draft.png</c> on Linux.
        /// </summary>
        public static string Native(string path) =>
            path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);

        /// <summary>
        /// The full path of a file a project names relative to its folder (or absolutely), with
        /// either separator and in any case (<see cref="Native"/>, <see cref="Existing"/>).
        /// </summary>
        public static string InFolder(string folder, string path) =>
            Existing(Path.GetFullPath(Path.Combine(folder, Native(path))));

        /// <summary>
        /// <see cref="Existing"/> of a path below <paramref name="folder"/>, e.g.
        /// <c>Find(game, "gamedata/env.scd")</c>; null when there is no such file.
        /// </summary>
        public static string? FindFile(string folder, string relative)
        {
            string path = Existing(Path.Combine(folder, Native(relative)));
            return File.Exists(path) ? path : null;
        }

        /// <summary>
        /// <see cref="Existing"/> of a folder; null when there is no such folder.
        /// </summary>
        public static string? FindFolder(string folder)
        {
            string path = Existing(folder);
            return Directory.Exists(path) ? path : null;
        }
    }
}
