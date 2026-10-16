using System.Text.Json;
using System.Text.Json.Nodes;
using FAForever.MapEditor.Project.Workspaces.Serialized;

namespace FAForever.MapEditor.Project.Workspaces
{
    /// <summary>
    /// Settings saved under a name to be applied again (user story F-10): a brush, a lighting, a
    /// water setup, a palette of props, a group of units or markers.
    /// </summary>
    /// <param name="Kind">What it is for, e.g. <c>brush</c>; lower-case letters, digits and dashes.</param>
    /// <param name="Name">What people call it.</param>
    /// <param name="Settings">What the feature of the kind saved; only that feature reads it.</param>
    public sealed record Preset(string Kind, string Name, JsonObject Settings)
    {
        /// <summary>
        /// A preset of <paramref name="settings"/>, written as the editor writes its JSON (camel case,
        /// enums as text).
        /// </summary>
        public static Preset Of<T>(string kind, string name, T settings) =>
            new Preset(kind, name, JsonSerializer.SerializeToNode(settings, ProjectJson.Options) as JsonObject
                ?? throw new ArgumentException("A preset holds an object", nameof(settings)));

        /// <summary>
        /// The settings as the feature's own record.
        /// </summary>
        /// <exception cref="MapProjectException">They are not what <typeparamref name="T"/> holds.</exception>
        public T Read<T>()
        {
            try
            {
                return Settings.Deserialize<T>(ProjectJson.Options) ?? throw new MapProjectException($"The preset {Name} holds no settings");
            }
            catch (JsonException exception)
            {
                throw new MapProjectException($"The preset {Name} ({Kind}): {exception.Message}", exception);
            }
        }
    }

    /// <summary>
    /// The workspace's presets: a folder per kind with a JSON file per preset
    /// (<see cref="WorkspaceLayout.Presets"/>), so they are shared with the rest of the workspace and
    /// other tools may write them. Nothing is kept in memory: every call reads the folder.
    /// </summary>
    public sealed class PresetLibrary(string folder)
    {
        public const int CurrentFormatVersion = 1;

        /// <summary>
        /// The kinds of preset the editor knows (<see cref="PresetKinds"/>); a feature may use another.
        /// </summary>
        public static IReadOnlyList<string> KnownKinds => PresetKinds.All;

        public string Folder { get; } = Path.GetFullPath(folder);

        /// <summary>
        /// The kinds that have a folder, in order.
        /// </summary>
        public IReadOnlyList<string> Kinds() => Directory.Exists(Folder)
            ? [.. Directory.EnumerateDirectories(Folder).Select(Path.GetFileName).OfType<string>().Where(IsKind).Order(StringComparer.Ordinal)]
            : [];

        /// <summary>
        /// The folder of a kind's presets, e.g. <c>presets/brushes</c>; an old kind name
        /// (<see cref="PresetKinds.Renamed"/>) gives its new folder.
        /// </summary>
        /// <exception cref="ArgumentException">Not a kind.</exception>
        public string FolderOf(string kind) => KindFolder(kind);

        /// <summary>
        /// The presets of a kind by name. A file that cannot be read is left out and named in
        /// <paramref name="unreadable"/> when it is given.
        /// </summary>
        /// <exception cref="ArgumentException">Not a kind.</exception>
        public IReadOnlyList<Preset> List(string kind, ICollection<string>? unreadable = null)
        {
            kind = PresetKinds.Of(kind);
            string kindFolder = KindFolder(kind);
            if (!Directory.Exists(kindFolder))
            {
                return [];
            }
            List<Preset> presets = [];
            foreach (string file in Directory.EnumerateFiles(kindFolder, "*.json"))
            {
                try
                {
                    presets.Add(ReadFile(file, kind));
                }
                catch (Exception exception) when (exception is MapProjectException or IOException or UnauthorizedAccessException)
                {
                    unreadable?.Add($"{file}: {exception.Message}");
                }
            }
            return [.. presets.OrderBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase)];
        }

        /// <summary>
        /// The preset of a kind with a name, or null when there is none.
        /// </summary>
        /// <exception cref="MapProjectException">Its file cannot be read.</exception>
        public Preset? Find(string kind, string name)
        {
            string file = FileOf(kind, name);
            return File.Exists(file) ? ReadFile(file, PresetKinds.Of(kind)) : null;
        }

        /// <summary>
        /// Writes a preset, replacing one of its kind with its name.
        /// </summary>
        /// <returns>The file.</returns>
        /// <exception cref="ArgumentException">The kind or name cannot be a preset's.</exception>
        public string Save(Preset preset)
        {
            string file = FileOf(preset.Kind, preset.Name);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            ProjectJson.Write(file, new PresetDto
            {
                FormatVersion = CurrentFormatVersion,
                Kind = PresetKinds.Of(preset.Kind),
                Name = preset.Name.Trim(),
                Settings = preset.Settings,
            });
            return file;
        }

        /// <summary>
        /// Deletes a preset.
        /// </summary>
        /// <returns>Whether there was one.</returns>
        public bool Delete(string kind, string name)
        {
            string file = FileOf(kind, name);
            if (!File.Exists(file))
            {
                return false;
            }
            File.Delete(file);
            return true;
        }

        /// <summary>
        /// A name of a kind no preset has yet: <paramref name="wanted"/>, else with a number after it.
        /// </summary>
        public string FreeName(string kind, string wanted)
        {
            string name = wanted.Trim();
            for (int number = 2; File.Exists(FileOf(kind, name)); number++)
            {
                name = $"{wanted.Trim()} {number}";
            }
            return name;
        }

        /// <summary>
        /// The file of a preset: its name with the characters a file name cannot hold made dashes.
        /// </summary>
        /// <exception cref="ArgumentException">The kind or name cannot be a preset's.</exception>
        public string FileOf(string kind, string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("A preset needs a name", nameof(name));
            }
            string file = FileNames.Clean(name);
            // a preset of the name in another case is the same one, as on Windows
            return FilePaths.Existing(Path.Combine(KindFolder(kind), file.TrimEnd('.') + ".json"));
        }

        /// <summary>
        /// Whether <paramref name="kind"/> can be a kind of preset: lower-case letters, digits and dashes.
        /// </summary>
        public static bool IsKind(string kind) =>
            kind.Length > 0 && kind.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');

        private string KindFolder(string kind) => IsKind(kind)
            ? Path.Combine(Folder, PresetKinds.Of(kind))
            : throw new ArgumentException($"'{kind}' is no kind of preset: lower-case letters, digits and dashes", nameof(kind));

        private static Preset ReadFile(string file, string kind)
        {
            PresetDto dto = ProjectJson.Read<PresetDto>(file);
            if (dto.FormatVersion > CurrentFormatVersion)
            {
                throw new MapProjectException($"{file} was made by a newer editor (format {dto.FormatVersion})");
            }
            // the folder decides the kind: a file copied into another folder belongs there
            return new Preset(kind, dto.Name, dto.Settings);
        }
    }
}
