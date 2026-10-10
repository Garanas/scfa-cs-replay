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
    /// templates/, plugins/   the user's own, for later
    /// assets/                converted from the game's archives; made again whenever they change
    ///   manifest.json        what they were converted from, and by which editor (AssetManifestDto)
    ///   env/..., units/...   the game's paths in lower case: textures as PNG, blueprints as JSON
    ///                        (PropBlueprintDto, UnitBlueprintDto), meshes as glTF (.glb)
    /// assets.new/            a conversion in progress, which replaces assets/ when it is done
    /// .gitignore             keeps assets/ out of git: the game's files are not ours to share
    /// </code>
    /// </remarks>
    public static class WorkspaceLayout
    {
        public const string Settings = "workspace.json";
        public const string Projects = "projects";
        public const string Brushes = "brushes";
        public const string Presets = "presets";
        public const string Templates = "templates";
        public const string Plugins = "plugins";
        public const string Assets = "assets";
        public const string AssetsInProgress = "assets.new";
        public const string Manifest = "manifest.json";
        public const string GitIgnore = ".gitignore";

        /// <summary>
        /// The folders a new workspace starts with.
        /// </summary>
        public static readonly IReadOnlyList<string> Folders = [Projects, Brushes, Presets, Templates, Plugins];

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
