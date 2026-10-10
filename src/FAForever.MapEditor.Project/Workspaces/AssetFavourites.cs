namespace FAForever.MapEditor.Project.Workspaces
{
    /// <summary>
    /// What <c>favourites.json</c> of a workspace holds (user story F-9).
    /// </summary>
    public sealed record AssetFavouritesDto
    {
        public int FormatVersion { get; init; } = AssetFavourites.CurrentFormatVersion;

        /// <summary>
        /// The keys of the starred assets (<see cref="AssetEntry.Key"/>), in the order they were starred.
        /// </summary>
        public IReadOnlyList<string> Starred { get; init; } = [];

        /// <summary>
        /// The keys of the assets chosen last, the newest first.
        /// </summary>
        public IReadOnlyList<string> Recent { get; init; } = [];
    }

    /// <summary>
    /// The assets a map maker starred and those chosen last (user story F-9), kept per workspace in
    /// its <c>favourites.json</c> (<see cref="WorkspaceLayout.Favourites"/>), so the shelf and the
    /// picker offer them in every map of the workspace. Keys are the catalogue's
    /// (<see cref="AssetEntry.Key"/>). Each change is written at once.
    /// </summary>
    public sealed class AssetFavourites
    {
        public const int CurrentFormatVersion = 1;

        /// <summary>
        /// How many assets chosen last are kept.
        /// </summary>
        public const int MaxRecent = 24;

        private readonly List<string> _starred = [];
        private readonly HashSet<string> _starredSet = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> _recent = [];
        private string? _file;

        /// <summary>
        /// The starred keys, in the order they were starred.
        /// </summary>
        public IReadOnlyList<string> Starred => _starred;

        /// <summary>
        /// The keys chosen last, the newest first.
        /// </summary>
        public IReadOnlyList<string> Recent => _recent;

        /// <summary>
        /// The file it is kept in; null before a workspace opens it (nothing is written then).
        /// </summary>
        public string? File => _file;

        /// <summary>
        /// Raised after the stars or recents changed (also when another file is opened).
        /// </summary>
        public event Action? Changed;

        /// <summary>
        /// The file of a workspace.
        /// </summary>
        public static string FileOf(Workspace workspace) => Path.Combine(workspace.Folder, WorkspaceLayout.Favourites);

        /// <summary>
        /// Reads a workspace's file; a missing file is no stars and no recents.
        /// </summary>
        /// <exception cref="MapProjectException">The file cannot be read; it is then kept as it is, and
        /// the stars start empty until the next change writes it again.</exception>
        public void Open(string file)
        {
            _file = file;
            _starred.Clear();
            _starredSet.Clear();
            _recent.Clear();
            try
            {
                if (System.IO.File.Exists(file))
                {
                    AssetFavouritesDto dto = ProjectJson.Read<AssetFavouritesDto>(file);
                    foreach (string key in dto.Starred ?? [])
                    {
                        if (_starredSet.Add(key))
                        {
                            _starred.Add(key);
                        }
                    }
                    _recent.AddRange((dto.Recent ?? []).Distinct(StringComparer.Ordinal).Take(MaxRecent));
                }
            }
            finally
            {
                Changed?.Invoke();
            }
        }

        public bool IsStarred(string key) => _starredSet.Contains(key);

        /// <summary>
        /// Where a key is among the recents: 0 for the newest; -1 when it is none of them.
        /// </summary>
        public int RecentIndex(string key) => _recent.IndexOf(key);

        /// <summary>
        /// Stars an asset or takes its star away.
        /// </summary>
        /// <returns>Whether that changed anything.</returns>
        /// <exception cref="IOException">The file cannot be written.</exception>
        public bool Star(string key, bool on)
        {
            bool changed = on ? _starredSet.Add(key) : _starredSet.Remove(key);
            if (!changed)
            {
                return false;
            }
            if (on)
            {
                _starred.Add(key);
            }
            else
            {
                _starred.Remove(key);
            }
            Save();
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// Notes that an asset was chosen: it becomes the newest of the recents.
        /// </summary>
        /// <exception cref="IOException">The file cannot be written.</exception>
        public void Used(string key)
        {
            if (_recent.Count > 0 && _recent[0] == key)
            {
                return;
            }
            _recent.Remove(key);
            _recent.Insert(0, key);
            if (_recent.Count > MaxRecent)
            {
                _recent.RemoveRange(MaxRecent, _recent.Count - MaxRecent);
            }
            Save();
            Changed?.Invoke();
        }

        private void Save()
        {
            if (_file is null)
            {
                return;
            }
            ProjectJson.Write(_file, new AssetFavouritesDto { Starred = [.. _starred], Recent = [.. _recent] });
        }
    }
}
