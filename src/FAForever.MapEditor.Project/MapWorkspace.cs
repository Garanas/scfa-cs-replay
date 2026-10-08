using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project
{
    /// <summary>
    /// A map being edited: its <see cref="EditSession"/> and the project folder it is saved in. The
    /// operations of the editor's File menu, which the command-line tool and the editor's automation
    /// use as well.
    /// </summary>
    public sealed class MapWorkspace
    {
        private MapDocument _savedDocument;

        private MapWorkspace(MapDocument document, string? projectFolder)
        {
            Session = new EditSession(document);
            ProjectFolder = projectFolder is null ? null : Path.GetFullPath(projectFolder);
            _savedDocument = document;
        }

        public EditSession Session { get; }

        public MapDocument Document => Session.Document;

        /// <summary>
        /// The folder the map is saved in; null for a map imported from the game's files that has
        /// not been saved as a project yet.
        /// </summary>
        public string? ProjectFolder { get; private set; }

        /// <summary>
        /// Opens the project in <paramref name="folder"/>.
        /// </summary>
        /// <exception cref="MapProjectException">The folder holds no readable project.</exception>
        public static MapWorkspace OpenProject(string folder) => new MapWorkspace(MapProjectStore.Load(folder), folder);

        /// <summary>
        /// Imports the map folder of the game or the vault, e.g. <c>maps/theta_passage.v0004</c>.
        /// It has no project folder until it is saved (<see cref="SaveAs"/>).
        /// </summary>
        /// <exception cref="MapProjectException">The folder holds no readable map.</exception>
        public static MapWorkspace ImportMap(string mapFolder) => new MapWorkspace(MapImporter.ImportFolder(mapFolder), null);

        /// <summary>
        /// Saves the map in its project folder, writing only what changed since the last save.
        /// </summary>
        /// <exception cref="InvalidOperationException">The map has no project folder yet.</exception>
        public void Save()
        {
            string folder = ProjectFolder ?? throw new InvalidOperationException("The map has no project folder yet: save it as a project first");
            MapProjectStore.Save(Document, folder, _savedDocument);
            Saved();
        }

        /// <summary>
        /// Saves the map as a project in <paramref name="folder"/>, which becomes its project folder.
        /// </summary>
        /// <exception cref="MapProjectException">The folder holds something other than a project.</exception>
        public void SaveAs(string folder)
        {
            MapProjectStore.Save(Document, folder);
            ProjectFolder = Path.GetFullPath(folder);
            Saved();
        }

        /// <summary>
        /// Writes the map as the game reads it into <paramref name="outputFolder"/>, e.g. the
        /// game's <c>maps</c> folder.
        /// </summary>
        /// <param name="version">The version to give the map, instead of its own.</param>
        /// <returns>The map folder, e.g. <c>maps/theta_passage.v0005</c>.</returns>
        public string Export(string outputFolder, int? version = null) =>
            MapExporter.WriteFolder(version is null ? Document : Document with { Version = version }, outputFolder);

        /// <summary>
        /// Reads the changed files of the project folder again, as one edit that can be undone.
        /// Files that hold what the document holds change nothing.
        /// </summary>
        /// <param name="changedFiles">The files, relative to the project folder.</param>
        /// <returns>Whether the document changed.</returns>
        public bool Reload(IReadOnlyCollection<string> changedFiles)
        {
            if (ProjectFolder is null || changedFiles.Count == 0)
            {
                return false;
            }

            bool wasModified = Session.IsModified;
            MapDocument reloaded = MapProjectStore.Reload(Document, ProjectFolder, changedFiles);
            string names = string.Join(", ", changedFiles.Select(Path.GetFileName).Distinct().Take(3)) + (changedFiles.Count > 3 ? ", ..." : "");
            if (!Session.Edit($"Reload {names}", _ => reloaded))
            {
                return false;
            }

            // the document is what the folder holds when nothing else was changed
            if (!wasModified)
            {
                _savedDocument = Document;
                Session.MarkSaved();
            }
            return true;
        }

        private void Saved()
        {
            _savedDocument = Document;
            Session.MarkSaved();
        }
    }
}
