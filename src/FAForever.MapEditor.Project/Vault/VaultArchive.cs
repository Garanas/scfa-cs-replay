using System.IO.Compression;

namespace FAForever.MapEditor.Project.Vault
{
    /// <summary>
    /// The zip of a map folder as the vault takes it (user story M-8): one folder at the root, named
    /// as the folder in lower case (<c>theta_passage.v0004/</c>), holding every file of the map
    /// folder and its subfolders, as the FAF client's <c>MapUploadTask</c> zips it.
    /// </summary>
    public static class VaultArchive
    {
        /// <summary>
        /// The endings of the files the API wants in the folder (<c>MANDATORY_FILES</c>).
        /// </summary>
        public static IReadOnlyList<string> RequiredEndings { get; } = ["_scenario.lua", ".scmap", "_save.lua", "_script.lua"];

        /// <summary>
        /// What an adaptive map needs besides (<c>ADAPTIVE_REQUIRED_FILES</c>).
        /// </summary>
        public static IReadOnlyList<string> AdaptiveEndings { get; } = ["_options.lua", "_tables.lua"];

        /// <summary>
        /// The files the folder lacks that the vault wants.
        /// </summary>
        public static IReadOnlyList<string> MissingFiles(string mapFolder, bool adaptive)
        {
            string[] files = [.. Directory.EnumerateFiles(mapFolder).Select(Path.GetFileName).OfType<string>()];
            return [.. RequiredEndings.Concat(adaptive ? AdaptiveEndings : [])
                .Where(ending => !files.Any(file => file.EndsWith(ending, StringComparison.OrdinalIgnoreCase)))];
        }

        /// <summary>
        /// The zip of <paramref name="mapFolder"/>.
        /// </summary>
        public static byte[] Create(string mapFolder)
        {
            string root = Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(mapFolder))).ToLowerInvariant();
            using MemoryStream stream = new MemoryStream();
            using (ZipArchive zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                zip.CreateEntry(root + "/");
                foreach (string file in Directory.EnumerateFiles(mapFolder, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
                {
                    string relative = Path.GetRelativePath(mapFolder, file).Replace('\\', '/');
                    ZipArchiveEntry entry = zip.CreateEntry($"{root}/{relative}", CompressionLevel.Optimal);
                    entry.LastWriteTime = File.GetLastWriteTime(file);
                    using Stream target = entry.Open();
                    using FileStream source = File.OpenRead(file);
                    source.CopyTo(target);
                }
            }
            return stream.ToArray();
        }

        /// <summary>
        /// The file name the upload gives the zip: the folder's in lower case with <c>.zip</c>, which
        /// the API asks for.
        /// </summary>
        public static string FileName(string mapFolder) =>
            Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(mapFolder))).ToLowerInvariant() + ".zip";
    }
}
