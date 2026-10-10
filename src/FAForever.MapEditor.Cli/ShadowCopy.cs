using System.Runtime.InteropServices;

namespace FAForever.MapEditor.Cli
{
    /// <summary>
    /// A copy of the editor's build output that a test editor runs from (<c>faf-map editor start</c>),
    /// so the build output itself stays free: <c>dotnet build</c> can write it while the test editor
    /// runs. Only what changed since the last copy is copied again, and of the native libraries
    /// (<c>runtimes/</c>) only this computer's.
    /// </summary>
    public static class ShadowCopy
    {
        /// <summary>
        /// The folder a test editor on a port runs from, by default: in the temporary folder.
        /// </summary>
        public static string DefaultFolder(int port) =>
            Path.Combine(Path.GetTempPath(), "faf-map-editor-shadow", port.ToString(System.Globalization.CultureInfo.InvariantCulture));

        /// <summary>
        /// Brings the copy in <paramref name="folder"/> up to date with the folder of
        /// <paramref name="exe"/>: files that are new or differ in size or time are copied, files
        /// gone from the build output are removed.
        /// </summary>
        /// <returns>The executable in the copy, and how many files were copied.</returns>
        /// <exception cref="IOException">A file of the copy is in use (an editor runs from it).</exception>
        public static (string Exe, int Copied) Prepare(string exe, string folder)
        {
            string source = Path.GetDirectoryName(Path.GetFullPath(exe))!;
            string target = Path.GetFullPath(folder);
            if (string.Equals(source.TrimEnd(Path.DirectorySeparatorChar), target.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("The shadow copy's folder is the build output itself");
            }
            Directory.CreateDirectory(target);
            HashSet<string> wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int copied = 0;
            foreach (string file in Files(source))
            {
                string relative = Path.GetRelativePath(source, file);
                wanted.Add(relative);
                string to = Path.Combine(target, relative);
                FileInfo from = new FileInfo(file);
                FileInfo there = new FileInfo(to);
                if (there.Exists && there.Length == from.Length && there.LastWriteTimeUtc == from.LastWriteTimeUtc)
                {
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Copy(file, to, overwrite: true);
                // the time of the build, so state.build tells which build the copy is
                File.SetLastWriteTimeUtc(to, from.LastWriteTimeUtc);
                copied++;
            }
            foreach (string stale in Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories)
                .Where(file => !wanted.Contains(Path.GetRelativePath(target, file))).ToList())
            {
                File.Delete(stale);
            }
            return (Path.Combine(target, Path.GetFileName(exe)), copied);
        }

        /// <summary>
        /// The files of the build output to copy: all but the native libraries of other systems.
        /// </summary>
        private static IEnumerable<string> Files(string source)
        {
            string runtimes = Path.Combine(source, "runtimes");
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.TopDirectoryOnly))
            {
                yield return file;
            }
            foreach (string directory in Directory.EnumerateDirectories(source))
            {
                bool isRuntimes = string.Equals(directory, runtimes, StringComparison.OrdinalIgnoreCase);
                IEnumerable<string> parts = isRuntimes
                    ? Directory.EnumerateDirectories(directory).Where(rid => IsThisSystem(Path.GetFileName(rid)))
                    : [directory];
                foreach (string part in parts)
                {
                    foreach (string file in Directory.EnumerateFiles(part, "*", SearchOption.AllDirectories))
                    {
                        yield return file;
                    }
                }
            }
        }

        /// <summary>
        /// Whether a folder of <c>runtimes/</c> (a runtime identifier: <c>win-x64</c>, <c>win</c>)
        /// holds libraries this computer loads.
        /// </summary>
        public static bool IsThisSystem(string rid)
        {
            string family = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
            string arch = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
            return string.Equals(rid, family, StringComparison.OrdinalIgnoreCase)
                || string.Equals(rid, $"{family}-{arch}", StringComparison.OrdinalIgnoreCase)
                || string.Equals(rid, RuntimeInformation.RuntimeIdentifier, StringComparison.OrdinalIgnoreCase);
        }
    }
}
