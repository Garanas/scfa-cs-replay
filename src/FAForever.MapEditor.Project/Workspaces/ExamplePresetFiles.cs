using System.Security.Cryptography;
using System.Text;

namespace FAForever.MapEditor.Project.Workspaces
{
    /// <summary>
    /// Example presets of one kind that ship with the editor as embedded JSON files, written once
    /// into a workspace's folder of that kind (<see cref="ExampleUnitPresets"/>, <see cref="ExampleUnitPalettes"/>).
    /// </summary>
    /// <remarks>
    /// <see cref="Ensure"/> writes them into the folder once, so every new workspace starts with
    /// them. One the user deletes is not written again: the hidden file <c>.examples</c> there
    /// names the ones written. A copy still equal to an earlier version of its example
    /// (<paramref name="earlier"/>), untouched by the user, is brought up to date; one the user
    /// changed is left alone. An untouched copy of an example that no longer ships
    /// (<paramref name="removed"/>) is deleted.
    /// </remarks>
    /// <param name="prefix">The logical name the files are embedded under, with its slash: <c>ExampleUnitPresets/</c>.</param>
    /// <param name="earlier">The SHA-256 of earlier versions of the examples' files (lines ending in a
    /// line feed), as <c>file:hash</c>: a copy still equal to one was not changed by the user and is
    /// replaced by the current version. A file changed for a release adds its old hash.</param>
    /// <param name="removed">The SHA-256 of examples that no longer ship, as <paramref name="earlier"/>:
    /// a copy still equal to one was not changed by the user and is deleted; one the user changed stays.</param>
    internal sealed class ExamplePresetFiles(string prefix, IReadOnlySet<string> earlier, IReadOnlySet<string> removed)
    {
        private const string Written = ".examples";

        /// <summary>
        /// The examples' file names, e.g. <c>Aeon base, small.json</c>.
        /// </summary>
        public IReadOnlyList<string> Names => [.. Resources().Select(resource => resource.File).Order(StringComparer.Ordinal)];

        /// <summary>
        /// The shipped text of an example's file.
        /// </summary>
        public string Text(string file)
        {
            string resource = Resources().First(r => r.File == file).Resource;
            using StreamReader reader = new StreamReader(typeof(ExamplePresetFiles).Assembly.GetManifestResourceStream(resource)!);
            return reader.ReadToEnd();
        }

        /// <summary>
        /// Writes the examples the folder never had, brings untouched copies of earlier versions up
        /// to date and deletes untouched copies of examples that no longer ship.
        /// </summary>
        /// <returns>The files written or brought up to date now.</returns>
        public IReadOnlyList<string> Ensure(string folder)
        {
            string written = Path.Combine(folder, Written);
            HashSet<string> had = new HashSet<string>(File.Exists(written) ? File.ReadAllLines(written) : [], StringComparer.OrdinalIgnoreCase);
            List<string> now = [];
            List<string> recorded = [];
            RemoveRetired(folder);
            foreach ((string file, _) in Resources())
            {
                string path = Path.Combine(folder, file);
                string shipped = Text(file);
                if (File.Exists(path))
                {
                    string onDisk = File.ReadAllText(path);
                    if (Normal(onDisk) != Normal(shipped) && earlier.Contains($"{file}:{Hash(onDisk)}"))
                    {
                        File.WriteAllText(path, shipped);
                        now.Add(file);
                    }
                    if (!had.Contains(file))
                    {
                        recorded.Add(file);
                    }
                    continue;
                }
                if (had.Contains(file))
                {
                    continue;
                }
                Directory.CreateDirectory(folder);
                File.WriteAllText(path, shipped);
                now.Add(file);
                recorded.Add(file);
            }
            if (recorded.Count > 0)
            {
                File.WriteAllLines(written, [.. had.Concat(recorded).Order(StringComparer.OrdinalIgnoreCase)]);
            }
            return now;
        }

        /// <summary>
        /// Deletes the untouched copies of examples that no longer ship.
        /// </summary>
        /// <returns>The files deleted.</returns>
        public IReadOnlyList<string> RemoveRetired(string folder)
        {
            List<string> deleted = [];
            if (!Directory.Exists(folder))
            {
                return deleted;
            }
            foreach (string file in removed.Select(entry => entry[..entry.LastIndexOf(':')]).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string path = Path.Combine(folder, file);
                if (File.Exists(path) && removed.Contains($"{file}:{Hash(File.ReadAllText(path))}"))
                {
                    File.Delete(path);
                    deleted.Add(file);
                }
            }
            return deleted;
        }

        private static string Normal(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

        /// <summary>
        /// The SHA-256 of a file's text with its lines ending in a line feed, as <see cref="Ensure"/>
        /// compares copies: what a changed example's old version adds to <c>earlier</c>.
        /// </summary>
        public static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Normal(text))));

        private IEnumerable<(string File, string Resource)> Resources() =>
            typeof(ExamplePresetFiles).Assembly.GetManifestResourceNames()
                .Where(resource => resource.StartsWith(prefix, StringComparison.Ordinal))
                .Select(resource => (resource[prefix.Length..], resource));
    }
}
