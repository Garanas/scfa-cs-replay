using System.Text.Json.Nodes;

namespace FAForever.MapEditor.Project.Workspaces
{
    /// <summary>
    /// The kinds of preset, each a folder of the workspace's <c>presets/</c> named in the plural
    /// (<see cref="PresetLibrary"/>), and the names they had before.
    /// </summary>
    public static class PresetKinds
    {
        /// <summary>
        /// The brush tools' settings (sculpt, strata, masks, water, terrain types, walkability).
        /// </summary>
        public const string Brushes = "brushes";

        /// <summary>
        /// The stamp tool's settings: its picture, mode, size, height, base and edge. The pictures
        /// themselves are brushes (<c>brushes/stamps/*.png</c>), not presets.
        /// </summary>
        public const string Stamps = "stamps";

        public const string Lighting = "lighting";

        public const string Water = "water";

        /// <summary>
        /// The sky's cube maps, background and dome.
        /// </summary>
        public const string Sky = "sky";

        /// <summary>
        /// Palettes of props to paint with.
        /// </summary>
        public const string Props = "props";

        public const string Units = "units";

        public const string Markers = "markers";

        /// <summary>
        /// Selected things of every kind, placed again together.
        /// </summary>
        public const string Selections = "selections";

        /// <summary>
        /// Templates a new map starts from (<see cref="MapTemplates"/>): JSON files of settings and
        /// map projects in folders, which are no <see cref="Preset"/> files.
        /// </summary>
        public const string Maps = "maps";

        /// <summary>
        /// Every kind the editor knows, in the order the Help menu's folders and the guides list them.
        /// </summary>
        public static IReadOnlyList<string> All { get; } = [Brushes, Stamps, Lighting, Water, Sky, Props, Units, Markers, Selections, Maps];

        /// <summary>
        /// The kinds by the names their folders had before they were plural; a command or file that
        /// names an old one means the new one.
        /// </summary>
        public static IReadOnlyDictionary<string, string> Renamed { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["brush"] = Brushes,
            ["props-palette"] = Props,
            ["selection"] = Selections,
            ["templates"] = Maps,
        };

        /// <summary>
        /// The kind a name means: an old name is the kind it became, any other is itself.
        /// </summary>
        public static string Of(string kind) => Renamed.TryGetValue(kind, out string? renamed) ? renamed : kind;
    }

    /// <summary>
    /// A file or folder moved by <see cref="PresetFolders.Move"/>, relative to the workspace.
    /// </summary>
    public sealed record PresetMove(string From, string To);

    /// <summary>
    /// Brings a workspace's presets into one <c>presets/</c> folder of plural kinds: the folders of
    /// the old kind names (<c>brush</c>, <c>props-palette</c>, <c>selection</c>) and the map templates
    /// of <c>templates/</c> move into their new folders, and brush presets of the stamp tool into
    /// <c>stamps</c>. The editor does it when a workspace opens; a workspace without old folders
    /// stays as it is, so it happens once.
    /// </summary>
    public static class PresetFolders
    {
        /// <summary>
        /// The folder the map templates were kept in before they moved into <c>presets/maps</c>.
        /// </summary>
        public const string OldTemplates = "templates";

        /// <summary>
        /// Moves what lies in the old folders into the new ones. A name the new folder holds already
        /// gets a number after it (<c>Forest 2.json</c>); an old folder left empty is deleted.
        /// </summary>
        /// <returns>What moved, relative to the workspace.</returns>
        /// <exception cref="IOException">A file could not be moved; what moved before stays moved.</exception>
        public static IReadOnlyList<PresetMove> Move(string workspaceFolder)
        {
            List<PresetMove> moves = [];
            string presets = Path.Combine(workspaceFolder, WorkspaceLayout.Presets);
            foreach ((string old, string kind) in PresetKinds.Renamed)
            {
                string from = old == OldTemplates ? Path.Combine(workspaceFolder, OldTemplates) : Path.Combine(presets, old);
                MoveAll(workspaceFolder, from, Path.Combine(presets, kind), moves);
            }
            MoveStampPresets(workspaceFolder, Path.Combine(presets, PresetKinds.Brushes), Path.Combine(presets, PresetKinds.Stamps), moves);
            return moves;
        }

        private static void MoveAll(string workspace, string from, string to, List<PresetMove> moves)
        {
            if (!Directory.Exists(from))
            {
                return;
            }
            foreach (string entry in Directory.EnumerateFileSystemEntries(from).Order(StringComparer.Ordinal).ToList())
            {
                Directory.CreateDirectory(to);
                string target = FreePath(to, Path.GetFileName(entry), Directory.Exists(entry));
                if (Directory.Exists(entry))
                {
                    Directory.Move(entry, target);
                }
                else
                {
                    File.Move(entry, target);
                }
                moves.Add(new PresetMove(Path.GetRelativePath(workspace, entry), Path.GetRelativePath(workspace, target)));
            }
            if (!Directory.EnumerateFileSystemEntries(from).Any())
            {
                Directory.Delete(from);
            }
        }

        /// <summary>
        /// Brush presets whose tool is the stamp go to the stamps' folder.
        /// </summary>
        private static void MoveStampPresets(string workspace, string brushes, string stamps, List<PresetMove> moves)
        {
            if (!Directory.Exists(brushes))
            {
                return;
            }
            foreach (string file in Directory.EnumerateFiles(brushes, "*.json").Order(StringComparer.Ordinal).ToList())
            {
                if (!IsStampPreset(file))
                {
                    continue;
                }
                Directory.CreateDirectory(stamps);
                string target = FreePath(stamps, Path.GetFileName(file), isFolder: false);
                File.Move(file, target);
                moves.Add(new PresetMove(Path.GetRelativePath(workspace, file), Path.GetRelativePath(workspace, target)));
            }
        }

        private static bool IsStampPreset(string file)
        {
            try
            {
                return JsonNode.Parse(File.ReadAllText(file)) is JsonObject root
                    && root["settings"] is JsonObject settings
                    && settings["tool"] is JsonValue tool && tool.TryGetValue(out string? name) && name == "stamp";
            }
            catch (Exception exception) when (exception is System.Text.Json.JsonException or IOException or InvalidOperationException)
            {
                return false;
            }
        }

        /// <summary>
        /// A path in <paramref name="folder"/> for <paramref name="name"/> that holds nothing yet.
        /// </summary>
        private static string FreePath(string folder, string name, bool isFolder)
        {
            string stem = isFolder ? name : Path.GetFileNameWithoutExtension(name);
            string extension = isFolder ? "" : Path.GetExtension(name);
            string path = Path.Combine(folder, name);
            for (int number = 2; File.Exists(path) || Directory.Exists(path); number++)
            {
                path = Path.Combine(folder, $"{stem} {number}{extension}");
            }
            return path;
        }
    }
}
