using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.Project
{
    /// <summary>
    /// A map being edited: its <see cref="EditSession"/> and the project folder it is saved in. The
    /// operations of the editor's File menu, which the command-line tool and the editor's automation
    /// use as well.
    /// </summary>
    /// <remarks>
    /// The asynchronous operations read and write on a thread of the pool. Saving and exporting take
    /// the version of the document that is current when they start; since versions never change,
    /// the editing can go on meanwhile, and the save marks the version it wrote. Call them from the
    /// thread the session is used on: they come back to it to touch the session.
    /// </remarks>
    public sealed class EditedMap
    {
        private MapDocument _savedDocument;

        private EditedMap(MapDocument document, string? projectFolder)
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
        public static EditedMap OpenProject(string folder) => new EditedMap(MapProjectStore.Load(folder), folder);

        /// <summary>
        /// Imports the map folder of the game or the vault, e.g. <c>maps/theta_passage.v0004</c>.
        /// It has no project folder until it is saved (<see cref="SaveAs"/>). Decals over the whole map
        /// become the map-wide slots and decal sets one decal each (<see cref="Editing.ImportedDecals"/>),
        /// so a map the editor exported comes back as it was made.
        /// </summary>
        /// <param name="defaults">The workspace's Procedural settings the map starts with; null for the editor's own.</param>
        /// <exception cref="MapProjectException">The folder holds no readable map.</exception>
        public static EditedMap ImportMap(string mapFolder, Workspaces.ProceduralDefaults? defaults = null)
        {
            MapDocument document = Editing.ImportedDecals.Recognise(MapImporter.ImportFolder(mapFolder));
            return new EditedMap(defaults is null ? document : document with { Procedural = defaults.ApplyTo(document.Procedural) }, null);
        }

        /// <summary>
        /// A map made in the editor (<see cref="NewMap.Create"/>): it has no project folder until it
        /// is saved, and counts as changed, so that closing it asks to save it.
        /// </summary>
        public static EditedMap New(MapDocument document)
        {
            EditedMap map = new EditedMap(document, null);
            // an equal copy that is another object: the session compares by reference
            map._savedDocument = document with { };
            map.Session.MarkSaved(map._savedDocument);
            return map;
        }

        public static Task<EditedMap> OpenProjectAsync(string folder) => Task.Run(() => OpenProject(folder));

        public static Task<EditedMap> ImportMapAsync(string mapFolder, Workspaces.ProceduralDefaults? defaults = null) => Task.Run(() => ImportMap(mapFolder, defaults));

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

        /// <inheritdoc cref="Save"/>
        public async Task SaveAsync()
        {
            string folder = ProjectFolder ?? throw new InvalidOperationException("The map has no project folder yet: save it as a project first");
            MapDocument document = Document;
            MapDocument previous = _savedDocument;
            await Task.Run(() => MapProjectStore.Save(document, folder, previous));
            Saved(document);
        }

        /// <inheritdoc cref="SaveAs"/>
        public async Task SaveAsAsync(string folder)
        {
            MapDocument document = Document;
            await Task.Run(() => MapProjectStore.Save(document, folder));
            ProjectFolder = Path.GetFullPath(folder);
            Saved(document);
        }

        /// <inheritdoc cref="Export"/>
        /// <param name="document">The version of the map to write, e.g. with what only the export
        /// adds (the editor's export steps); null for the map as it is.</param>
        public Task<string> ExportAsync(string outputFolder, int? version = null, MapDocument? document = null)
        {
            MapDocument exported = document ?? Document;
            exported = version is null ? exported : exported with { Version = version };
            EditorStamp stamp = EditorStamp.Now();
            return Task.Run(() => MapExporter.WriteFolder(exported, outputFolder, stamp));
        }

        /// <summary>
        /// Writes the map as the game reads it into <paramref name="outputFolder"/>, e.g. the
        /// game's <c>maps</c> folder.
        /// </summary>
        /// <param name="version">The version to give the map, instead of its own.</param>
        /// <param name="wideDecals">Whether the map-wide decals are written too (<see cref="MapWideDecalsExport"/>:
        /// generated from the heightmap, files read relative to the project), as the editor's export does.</param>
        /// <param name="ddsTextures">Whether the map's own textures in other formats are converted to DDS
        /// and every reference pointed at them (<see cref="TextureExport"/>, with its shared cache), as the
        /// editor's export does.</param>
        /// <param name="converted">Hears what was converted, when given.</param>
        /// <returns>The map folder, e.g. <c>maps/theta_passage.v0005</c>.</returns>
        public string Export(string outputFolder, int? version = null, bool wideDecals = false, bool ddsTextures = false, Action<IReadOnlyList<ConvertedTexture>>? converted = null)
        {
            MapDocument document = version is null ? Document : Document with { Version = version };
            if (wideDecals)
            {
                document = MapWideDecalsExport.Apply(document, file =>
                {
                    string path = Path.IsPathRooted(file) || ProjectFolder is null ? file : FilePaths.InFolder(ProjectFolder, file);
                    return File.Exists(path) ? File.ReadAllBytes(path) : null;
                }, out _);
            }
            if (ddsTextures)
            {
                document = TextureExport.Apply(document, out IReadOnlyList<ConvertedTexture> textures);
                converted?.Invoke(textures);
            }
            return MapExporter.WriteFolder(document, outputFolder, EditorStamp.Now());
        }

        /// <summary>
        /// Reads the files another tool changed in the project folder again, as one edit that can
        /// be undone.
        /// </summary>
        /// <remarks>
        /// A file that holds what the last save wrote is the editor's own and is left alone, also
        /// when the document changed since (the editing goes on during a save in the background).
        /// A file another tool changed replaces its part of the current document; an image layer's
        /// file (the final's base read from a procedural tool's output, an imported layer) is read
        /// again whenever it changes, saved or not.
        /// </remarks>
        /// <param name="changedFiles">The files, relative to the project folder.</param>
        /// <returns>Whether the document changed.</returns>
        public bool Reload(IReadOnlyCollection<string> changedFiles)
        {
            if (ProjectFolder is null || changedFiles.Count == 0)
            {
                return false;
            }

            string folder = ProjectFolder;
            // the file of an image layer is always another tool's (the save never writes it), also
            // for a layer added since the last save
            string[] external = ReferenceEquals(Document, _savedDocument)
                ? [.. changedFiles]
                : [.. changedFiles.Where(file => MapProjectStore.IsImageLayerFile(Document, folder, file)
                    || !ReferenceEquals(MapProjectStore.Reload(_savedDocument, folder, [file]), _savedDocument))];
            if (external.Length == 0)
            {
                return false;
            }

            bool wasModified = Session.IsModified;
            MapDocument reloaded = MapProjectStore.Reload(Document, folder, external);
            string names = string.Join(", ", external.Select(Path.GetFileName).Distinct().Take(3)) + (external.Length > 3 ? ", ..." : "");
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

        private void Saved(MapDocument? document = null)
        {
            _savedDocument = document ?? Document;
            Session.MarkSaved(_savedDocument);
        }
    }
}
