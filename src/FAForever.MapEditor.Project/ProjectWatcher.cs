namespace FAForever.MapEditor.Project
{
    /// <summary>
    /// Watches a project folder and reports the files that changed, a moment after the last change:
    /// tools save in bursts (a temporary file, then a rename), so the changes of one save arrive
    /// together.
    /// </summary>
    /// <remarks>
    /// <see cref="Changed"/> is raised on a thread of the pool; a user interface hands it to its own
    /// thread before it touches the map.
    /// </remarks>
    public sealed class ProjectWatcher : IDisposable
    {
        private readonly FileSystemWatcher _watcher;
        private readonly Timer _timer;
        private readonly object _gate = new object();
        private readonly HashSet<string> _pending = new HashSet<string>(StringComparer.Ordinal);
        private readonly string _folder;

        /// <param name="quiet">How long the folder must be quiet before the changes are reported.</param>
        public ProjectWatcher(string folder, TimeSpan? quiet = null)
        {
            _folder = Path.GetFullPath(folder);
            Quiet = quiet ?? TimeSpan.FromMilliseconds(400);
            _timer = new Timer(_ => Report());
            _watcher = new FileSystemWatcher(_folder)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            _watcher.Changed += (_, e) => Note(e.FullPath);
            _watcher.Created += (_, e) => Note(e.FullPath);
            _watcher.Deleted += (_, e) => Note(e.FullPath);
            _watcher.Renamed += (_, e) =>
            {
                Note(e.OldFullPath);
                Note(e.FullPath);
            };
            _watcher.EnableRaisingEvents = true;
        }

        public TimeSpan Quiet { get; }

        /// <summary>
        /// The files that changed, relative to the folder with <c>/</c> between folders.
        /// </summary>
        public event Action<IReadOnlyCollection<string>>? Changed;

        public void Dispose()
        {
            _watcher.Dispose();
            _timer.Dispose();
        }

        private void Note(string path)
        {
            string relative = Path.GetRelativePath(_folder, path).Replace('\\', '/');
            // temporary files of tools and of the editor's own writes
            if (relative.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) || relative.EndsWith('~') || Path.GetFileName(relative).StartsWith('.'))
            {
                return;
            }
            lock (_gate)
            {
                _pending.Add(relative);
                _timer.Change(Quiet, Timeout.InfiniteTimeSpan);
            }
        }

        private void Report()
        {
            string[] changed;
            lock (_gate)
            {
                changed = [.. _pending];
                _pending.Clear();
            }
            if (changed.Length > 0)
            {
                Changed?.Invoke(changed);
            }
        }
    }
}
