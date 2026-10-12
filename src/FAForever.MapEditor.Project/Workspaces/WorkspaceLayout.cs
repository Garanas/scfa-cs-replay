namespace FAForever.MapEditor.Project.Workspaces
{
    /// <summary>
    /// The files and folders of a workspace, relative to its folder.
    /// </summary>
    /// <remarks>
    /// <code>
    /// workspace.json         where the game, FAF's data and the vault are (WorkspaceDto)
    /// projects/              the map projects (MapProjectLayout)
    /// brushes/               the map maker's brush shapes: greyscale PNGs, in folders as they like
    /// presets/               saved settings to apply again: a folder per kind, a JSON file each (PresetLibrary)
    ///   brushes/ stamps/ lighting/ water/ sky/ props/ units/ markers/ selections/   (PresetKinds)
    ///   maps/                map templates a new map starts from: JSON files of its settings, map
    ///                        projects in folders (MapTemplates)
    /// plugins/               the Lua plugins: a folder each with its plugin.lua (and plugin.json)
    /// tours/                 the guided tours: a *.tour.json each (the editor's own written here, a map maker's beside them),
    ///                        and .progress.json (what was offered and which tours were finished)
    /// assets/               converted from the game's archives; made again whenever they change
    ///   manifest.json        what they were converted from, and by which editor (AssetManifestDto)
    ///   env/..., units/...   the game's paths in lower case: textures as PNG, blueprints as JSON
    ///                        (PropBlueprintDto, UnitBlueprintDto), meshes as glTF (.glb)
    /// assets.new/            a conversion in progress, which replaces assets/ when it is done
    /// .gitignore             keeps assets/ out of git: the game's files are not ours to share
    /// favourites.json        the assets starred and chosen last, for the shelf and the picker (AssetFavourites)
    /// </code>
    /// </remarks>
    public static class WorkspaceLayout
    {
        public const string Settings = "workspace.json";
        public const string Projects = "projects";
        public const string Brushes = "brushes";
        public const string Presets = "presets";
        public const string Plugins = "plugins";
        public const string Tours = "tours";
        public const string Assets = "assets";
        public const string AssetsInProgress = "assets.new";
        public const string Manifest = "manifest.json";
        public const string GitIgnore = ".gitignore";

        /// <summary>
        /// The assets starred and chosen last (<see cref="AssetFavourites"/>, user story F-9).
        /// </summary>
        public const string Favourites = "favourites.json";

        /// <summary>
        /// The folders a new workspace starts with.
        /// </summary>
        public static readonly IReadOnlyList<string> Folders = [Projects, Brushes, Presets, Plugins, Tours];

        /// <summary>
        /// Where the conversion puts the file of a game path, relative to <see cref="Assets"/>:
        /// lower case, a texture (<c>.dds</c>) as a PNG, a blueprint (<c>.bp</c>) as JSON and a mesh
        /// (<c>.scm</c>) as glTF (<c>.glb</c>).
        /// </summary>
        public static string AssetOf(string gamePath)
        {
            string path = GameArchives.Normalize(gamePath);
            return path.EndsWith(".dds", StringComparison.Ordinal) ? path[..^4] + ".png"
                : path.EndsWith(".bp", StringComparison.Ordinal) ? path[..^3] + ".json"
                : path.EndsWith(".scm", StringComparison.Ordinal) ? path[..^4] + ".glb"
                : path;
        }
    }
}
