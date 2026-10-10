using System.IO.Compression;
using FAForever.MapEditor.Project.Workspaces.Serialized;

namespace FAForever.MapEditor.Project.Workspaces
{
    /// <summary>
    /// Where an asset comes from (user story F-8).
    /// </summary>
    public enum AssetOrigin
    {
        /// <summary>None of the archives the assets were converted from holds it (they moved, or the manifest is missing).</summary>
        Unknown,

        /// <summary>The game's own archives (<c>gamedata/env.scd</c>, <c>units.scd</c>).</summary>
        Game,

        /// <summary>FAF's archives (<c>env.nx2</c>, <c>units.nx2</c>), which add to the game's and replace some of its files.</summary>
        Faf,

        /// <summary>The workspace's own: the map maker's brushes.</summary>
        Workspace,

        /// <summary>The map's own files, in its folder (<c>/maps/...</c>).</summary>
        Map,
    }

    /// <summary>
    /// Where an asset comes from: its origin, the archive, and whether FAF's file replaces the game's.
    /// </summary>
    /// <param name="Archive">The archive's file name (<c>env.nx2</c>), for the game and FAF.</param>
    /// <param name="ReplacesGame">Whether FAF's archive holds the file the game's has too, and wins.</param>
    public sealed record AssetSource(AssetOrigin Origin, string? Archive = null, bool ReplacesGame = false)
    {
        public static AssetSource Unknown { get; } = new AssetSource(AssetOrigin.Unknown);

        /// <summary>
        /// The origin in words: <c>FAF (env.nx2), replacing the game's</c>.
        /// </summary>
        public string Title => Origin switch
        {
            AssetOrigin.Game => $"The game ({Archive})",
            AssetOrigin.Faf => ReplacesGame ? $"FAF ({Archive}), replacing the game's" : $"FAF ({Archive})",
            AssetOrigin.Workspace => "The workspace's own",
            AssetOrigin.Map => "The map's own files",
            _ => "Unknown: not in the archives the assets were converted from",
        };

        /// <summary>
        /// The origin's short name, for a filter: <c>Game</c>, <c>FAF</c>, <c>Workspace</c>, <c>Map</c>, <c>Unknown</c>.
        /// </summary>
        public string Short => Origin == AssetOrigin.Faf ? "FAF" : Origin.ToString();
    }

    /// <summary>
    /// Which archive each of the game's files comes from, as the converter laid them over each
    /// other (<see cref="GameArchives"/>: FAF's <c>.nx2</c> over the game's <c>.scd</c>). Read from
    /// the archives' lists of files only, never their content.
    /// </summary>
    public sealed class AssetOrigins
    {
        private readonly Dictionary<string, AssetSource> _byPath;

        private AssetOrigins(Dictionary<string, AssetSource> byPath) => _byPath = byPath;

        public static AssetOrigins Empty { get; } = new AssetOrigins(new Dictionary<string, AssetSource>(StringComparer.Ordinal));

        /// <summary>
        /// How many files the archives hold.
        /// </summary>
        public int Count => _byPath.Count;

        /// <summary>
        /// The origins of the files of <paramref name="archives"/>, in the order they are laid over
        /// each other; an archive that is missing or cannot be read is left out.
        /// </summary>
        public static AssetOrigins Read(IEnumerable<string> archives)
        {
            Dictionary<string, AssetSource> byPath = new Dictionary<string, AssetSource>(StringComparer.Ordinal);
            foreach (string archive in archives)
            {
                if (!File.Exists(archive))
                {
                    continue;
                }
                bool faf = GameArchives.IsFafArchive(archive);
                string name = Path.GetFileName(archive).ToLowerInvariant();
                try
                {
                    using ZipArchive zip = ZipFile.OpenRead(archive);
                    foreach (ZipArchiveEntry entry in zip.Entries)
                    {
                        if (entry.FullName.EndsWith('/'))
                        {
                            continue;
                        }
                        string path = GameArchives.Normalize(entry.FullName);
                        bool replaces = faf && byPath.TryGetValue(path, out AssetSource? before) && before.Origin == AssetOrigin.Game;
                        byPath[path] = new AssetSource(faf ? AssetOrigin.Faf : AssetOrigin.Game, name, replaces);
                    }
                }
                catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
                {
                    // the asset says it does not know where it comes from
                }
            }
            return new AssetOrigins(byPath);
        }

        /// <summary>
        /// The origins of the archives a workspace's assets were converted from (its manifest);
        /// empty without one.
        /// </summary>
        public static AssetOrigins Read(Workspace workspace)
        {
            string manifest = Path.Combine(workspace.AssetsFolder, WorkspaceLayout.Manifest);
            try
            {
                return File.Exists(manifest)
                    ? Read(ProjectJson.Read<AssetManifestDto>(manifest).Archives.Select(archive => archive.Path))
                    : Empty;
            }
            catch (Exception exception) when (exception is MapProjectException or IOException or System.Text.Json.JsonException)
            {
                return Empty;
            }
        }

        /// <summary>
        /// The game path an asset was converted from: a prop's blueprint, a texture's <c>.dds</c>,
        /// a unit's blueprint (<c>units/uec1101/uec1101_unit.bp</c>); null for a brush.
        /// </summary>
        public static string? GamePathOf(AssetEntry entry) => entry.Kind switch
        {
            AssetKind.Brush => null,
            AssetKind.Unit => $"units/{entry.Key}/{entry.Key}_unit.bp",
            _ => entry.Key,
        };

        /// <summary>
        /// Where an asset of the workspace comes from.
        /// </summary>
        public AssetSource Of(AssetEntry entry) => GamePathOf(entry) is string path ? Of(path) : new AssetSource(AssetOrigin.Workspace);

        /// <summary>
        /// Where a file a map names comes from: its own folder (<c>/maps/...</c>), else the archive.
        /// </summary>
        public AssetSource Of(string gamePath)
        {
            string path = AssetCatalog.KeyOf(gamePath);
            if (path.StartsWith("maps/", StringComparison.Ordinal))
            {
                return new AssetSource(AssetOrigin.Map);
            }
            return _byPath.GetValueOrDefault(path) ?? AssetSource.Unknown;
        }
    }
}
