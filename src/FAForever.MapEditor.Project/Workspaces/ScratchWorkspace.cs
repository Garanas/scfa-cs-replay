using System.Diagnostics;

namespace FAForever.MapEditor.Project.Workspaces
{
    /// <summary>
    /// A workspace to test in without touching a real one: its own projects, presets, brushes and
    /// vault (<c>vault/maps</c> inside it, where exports go), and the converted assets of another
    /// workspace, linked rather than copied (gigabytes): a directory junction on Windows, which needs
    /// no administrator, a symbolic link elsewhere; a copy only when neither can be made.
    /// </summary>
    /// <remarks>
    /// The linked assets are the other workspace's: converting the assets in a scratch workspace
    /// would replace them there. Its brushes are copied, so a test may change them.
    /// </remarks>
    public static class ScratchWorkspace
    {
        /// <summary>
        /// How the assets came into the scratch workspace.
        /// </summary>
        public enum AssetLink
        {
            Junction,
            SymbolicLink,
            Copy,
            None,
        }

        /// <summary>
        /// Makes a scratch workspace in <paramref name="folder"/> (absent or empty) with the assets of
        /// <paramref name="source"/>, and its sandbox made of them.
        /// </summary>
        /// <exception cref="MapProjectException">The folder holds something else.</exception>
        /// <param name="assetsInto">A folder of its own for the assets, linked in instead of the
        /// source's: made when absent, and converted into by the caller (<c>faf-map workspace scratch
        /// --convert-into</c>), so a change of the assets' format is tried without touching the
        /// source's. Several scratch workspaces may share it.</param>
        public static (Workspace Workspace, AssetLink Assets) Create(string folder, Workspace source, string? assetsInto = null)
        {
            string vault = Path.Combine(Path.GetFullPath(folder), "vault");
            Workspace workspace = Workspace.Create(folder, source.GameFolder, source.FafDataFolder, vault);
            Directory.CreateDirectory(workspace.MapsFolder);

            AssetLink link = AssetLink.None;
            if (assetsInto is not null)
            {
                string target = Path.GetFullPath(assetsInto);
                if (string.Equals(target.TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(source.AssetsFolder).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                {
                    throw new MapProjectException($"{target} holds the assets of {source.Folder}: convert into a folder of its own");
                }
                Directory.CreateDirectory(target);
                if (!Directory.Exists(workspace.AssetsFolder))
                {
                    link = Link(workspace.AssetsFolder, target);
                }
            }
            else if (Directory.Exists(source.AssetsFolder) && !Directory.Exists(workspace.AssetsFolder))
            {
                link = Link(workspace.AssetsFolder, source.AssetsFolder);
            }
            if (Directory.Exists(source.BrushesFolder))
            {
                Copy(source.BrushesFolder, workspace.BrushesFolder);
            }
            // the plain sandbox written at setup becomes one made of the assets
            SandboxMap.Ensure(workspace);
            ExampleUnitPresets.Ensure(workspace);
            return (workspace, link);
        }

        /// <summary>
        /// Deletes a scratch workspace: the link to the assets first (.NET does not delete a
        /// junction while it deletes a folder recursively), then the rest. The linked assets stay.
        /// </summary>
        public static void Delete(string folder)
        {
            string assets = Path.Combine(folder, WorkspaceLayout.Assets);
            if (Directory.Exists(assets) && new DirectoryInfo(assets).Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                Directory.Delete(assets);
            }
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        private static AssetLink Link(string link, string target)
        {
            if (OperatingSystem.IsWindows())
            {
                ProcessStartInfo start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                start.ArgumentList.Add("/c");
                start.ArgumentList.Add("mklink");
                start.ArgumentList.Add("/J");
                start.ArgumentList.Add(link);
                start.ArgumentList.Add(target);
                using Process? process = Process.Start(start);
                process?.StandardOutput.ReadToEnd();
                process?.WaitForExit();
                if (process?.ExitCode == 0 && Directory.Exists(link))
                {
                    return AssetLink.Junction;
                }
            }
            else
            {
                try
                {
                    Directory.CreateSymbolicLink(link, target);
                    return AssetLink.SymbolicLink;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // copied below
                }
            }
            Copy(target, link);
            return AssetLink.Copy;
        }

        private static void Copy(string from, string to)
        {
            foreach (string file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
            {
                string copy = Path.Combine(to, Path.GetRelativePath(from, file));
                Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                File.Copy(file, copy, overwrite: true);
            }
        }
    }
}
