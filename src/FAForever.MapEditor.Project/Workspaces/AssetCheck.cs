using System.Security.Cryptography;
using FAForever.MapEditor.Project.Workspaces.Serialized;

namespace FAForever.MapEditor.Project.Workspaces
{
    public enum AssetState
    {
        /// <summary>
        /// Converted from the archives as they are, by this version of the editor.
        /// </summary>
        Current,

        /// <summary>
        /// Never converted, or a conversion did not finish.
        /// </summary>
        Missing,

        /// <summary>
        /// Converted, but FAF updated its archives or the editor was updated since.
        /// </summary>
        Outdated,
    }

    /// <summary>
    /// Whether a workspace's assets need converting (again), and why.
    /// </summary>
    /// <remarks>
    /// Only FAF's archives (<c>.nx2</c>) are checked: the game's own never change. An archive
    /// whose size and time are as they were is taken to be unchanged; otherwise its content is
    /// compared, so that copying FAF's folder does not ask for a conversion.
    /// </remarks>
    public sealed record AssetCheck(AssetState State, IReadOnlyList<string> Reasons, AssetManifestDto? Manifest)
    {
        public bool IsCurrent => State == AssetState.Current;

        internal static AssetCheck Of(Workspace workspace)
        {
            string manifestFile = Path.Combine(workspace.AssetsFolder, WorkspaceLayout.Manifest);
            if (!File.Exists(manifestFile))
            {
                return new AssetCheck(AssetState.Missing, [Directory.Exists(Path.Combine(workspace.Folder, WorkspaceLayout.AssetsInProgress))
                    ? "The conversion of the game's assets did not finish."
                    : "The game's assets have not been converted yet."], null);
            }

            AssetManifestDto manifest;
            try
            {
                manifest = ProjectJson.Read<AssetManifestDto>(manifestFile);
            }
            catch (MapProjectException exception)
            {
                return new AssetCheck(AssetState.Missing, [exception.Message], null);
            }

            List<string> reasons = [];
            if (manifest.FormatVersion != AssetConverter.FormatVersion || manifest.EditorVersion != AssetConverter.EditorVersion)
            {
                reasons.Add($"The editor was updated from {manifest.EditorVersion} to {AssetConverter.EditorVersion}.");
            }

            IReadOnlyList<string> archives;
            try
            {
                archives = workspace.FindArchives();
            }
            catch (MapProjectException exception)
            {
                reasons.Add(exception.Message);
                archives = [];
            }

            Dictionary<string, ArchiveDto> converted = manifest.Archives.ToDictionary(a => a.Path, StringComparer.OrdinalIgnoreCase);
            foreach (string archive in archives)
            {
                if (!converted.Remove(archive, out ArchiveDto? before))
                {
                    reasons.Add($"{Path.GetFileName(archive)} is new: {archive}.");
                }
                else if (GameArchives.IsFafArchive(archive) && HasChanged(archive, before))
                {
                    reasons.Add($"FAF updated {Path.GetFileName(archive)}.");
                }
            }
            foreach (string archive in converted.Keys)
            {
                reasons.Add($"{archive} is no longer used.");
            }

            return new AssetCheck(reasons.Count == 0 ? AssetState.Current : AssetState.Outdated, reasons, manifest);
        }

        private static bool HasChanged(string archive, ArchiveDto before)
        {
            FileInfo file = new FileInfo(archive);
            if (file.Length != before.Size)
            {
                return true;
            }
            return file.LastWriteTimeUtc != before.LastWriteUtc && Sha256(archive) != before.Sha256;
        }

        internal static string Sha256(string path)
        {
            using FileStream stream = File.OpenRead(path);
            return Convert.ToHexStringLower(SHA256.HashData(stream));
        }
    }
}
