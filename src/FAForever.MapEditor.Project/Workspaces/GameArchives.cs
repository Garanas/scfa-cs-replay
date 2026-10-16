using System.IO.Compression;

namespace FAForever.MapEditor.Project.Workspaces
{
    /// <summary>
    /// The game's archives that the assets come from, laid over each other as the game mounts them:
    /// a file of FAF's <c>env.nx2</c> wins from the file with the same path in the game's
    /// <c>env.scd</c>. Paths are compared without regard to case, as the game does.
    /// </summary>
    /// <remarks>
    /// Reading is safe from several threads: each archive reads one file at a time.
    /// </remarks>
    public sealed class GameArchives : IDisposable
    {
        /// <summary>
        /// The archives the editor converts and needs: the environment (terrain textures, decals,
        /// props) and the units.
        /// </summary>
        public static readonly IReadOnlyList<string> Names = ["env", "units"];

        /// <summary>
        /// The archives the editor converts a part of when the game has them: of <c>textures</c>
        /// the folders of <see cref="TextureFolders"/>.
        /// </summary>
        public static readonly IReadOnlyList<string> OptionalNames = ["textures"];

        /// <summary>
        /// The folders of the <c>textures</c> archive the editor converts, in the form of
        /// <see cref="Files"/>: the cube maps and backgrounds of the sky (<c>environment</c>), the
        /// water's textures (<c>engine</c>) and the units' icons as the game's build menu shows them.
        /// </summary>
        public static readonly IReadOnlyList<string> TextureFolders = ["textures/environment/", "textures/engine/", "textures/ui/common/icons/units/"];

        /// <summary>
        /// Whether the editor converts a file of the archives (<see cref="Files"/>'s form): everything
        /// of <c>env</c> and <c>units</c>, of <c>textures</c> only the folders of <see cref="TextureFolders"/>.
        /// </summary>
        public static bool IsConverted(string file) =>
            !file.StartsWith("textures/", StringComparison.Ordinal) || TextureFolders.Any(folder => file.StartsWith(folder, StringComparison.Ordinal));

        private readonly List<(ZipArchive Zip, object Gate)> _archives = [];
        private readonly Dictionary<string, (ZipArchiveEntry Entry, object Gate)> _files = new Dictionary<string, (ZipArchiveEntry, object)>(StringComparer.Ordinal);

        private GameArchives()
        {
        }

        /// <summary>
        /// The files of all archives, in lower case without a leading slash, e.g.
        /// <c>env/evergreen/layers/eg_grass001_albedo.dds</c>.
        /// </summary>
        public IReadOnlyCollection<string> Files => _files.Keys;

        /// <summary>
        /// The archives to read, in the order they are laid over each other: per name the game's
        /// <c>gamedata/&lt;name&gt;.scd</c>, then FAF's <c>&lt;name&gt;.nx2</c> when there is one; the
        /// optional ones (<see cref="OptionalNames"/>) when the game has them.
        /// </summary>
        /// <exception cref="MapProjectException">The game folder lacks one of its archives.</exception>
        public static IReadOnlyList<string> Find(string gameFolder, string? fafDataFolder)
        {
            List<string> archives = [];
            foreach (string name in Names.Concat(OptionalNames))
            {
                // in any case: on Linux the game's folder (Steam's Proton) may name them otherwise
                if (FilePaths.FindFile(gameFolder, $"gamedata/{name}.scd") is not string scd)
                {
                    if (OptionalNames.Contains(name))
                    {
                        continue;
                    }
                    throw new MapProjectException($"The game folder {gameFolder} has no gamedata/{name}.scd");
                }
                archives.Add(Path.GetFullPath(scd));

                if (fafDataFolder is not null && FilePaths.FindFile(fafDataFolder, name + ".nx2") is string nx2)
                {
                    archives.Add(Path.GetFullPath(nx2));
                }
            }
            return archives;
        }

        /// <summary>
        /// Whether the archive is one of FAF's, which change with every release of the game.
        /// </summary>
        public static bool IsFafArchive(string path) => path.EndsWith(".nx2", StringComparison.OrdinalIgnoreCase);

        /// <exception cref="MapProjectException">An archive cannot be read.</exception>
        public static GameArchives Open(IEnumerable<string> paths)
        {
            GameArchives archives = new GameArchives();
            try
            {
                foreach (string path in paths)
                {
                    ZipArchive zip;
                    try
                    {
                        zip = ZipFile.OpenRead(path);
                    }
                    catch (InvalidDataException exception)
                    {
                        throw new MapProjectException($"{path} is not an archive the game reads: {exception.Message}", exception);
                    }

                    object gate = new object();
                    archives._archives.Add((zip, gate));
                    foreach (ZipArchiveEntry entry in zip.Entries)
                    {
                        if (entry.FullName.EndsWith('/'))
                        {
                            continue;
                        }
                        archives._files[Normalize(entry.FullName)] = (entry, gate);
                    }
                }
                return archives;
            }
            catch
            {
                archives.Dispose();
                throw;
            }
        }

        /// <summary>
        /// A game path in the form of <see cref="Files"/>: lower case, forward slashes, no leading slash.
        /// </summary>
        public static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/').ToLowerInvariant();

        public bool Contains(string path) => _files.ContainsKey(Normalize(path));

        public byte[] Read(string path)
        {
            (ZipArchiveEntry entry, object gate) = _files[Normalize(path)];
            lock (gate)
            {
                using Stream stream = entry.Open();
                byte[] bytes = new byte[entry.Length];
                stream.ReadExactly(bytes);
                return bytes;
            }
        }

        public void Dispose()
        {
            foreach ((ZipArchive zip, _) in _archives)
            {
                zip.Dispose();
            }
            _archives.Clear();
            _files.Clear();
        }
    }
}
