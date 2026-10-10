using System.Security.Cryptography;
using System.Text;

namespace FAForever.MapEditor.Project.Workspaces
{
    /// <summary>
    /// The example unit presets that ship with the editor: bases per faction and size, defence
    /// points, wall rings and lines, civilian towns, wreck fields and players' starts, taken from the
    /// campaign, coop and vault maps (<c>docs/map-editor/unit-template-picks.json</c>, made with
    /// <c>faf-map dev unit-templates</c>, see <see cref="UnitTemplates"/>). They hold blueprint ids and
    /// places only, with an army role (<see cref="UnitPresetSettings.Role"/>) that picks the army of
    /// the map they are placed on.
    /// </summary>
    /// <remarks>
    /// <see cref="Ensure"/> writes them into a workspace's <c>presets/units</c> once, so every new
    /// workspace starts with them. One the user deletes is not written again: the hidden file
    /// <c>.examples</c> there names the ones written. A copy still equal to an earlier version of its
    /// example (<see cref="Earlier"/>), untouched by the user, is brought up to date; one the user
    /// changed is left alone.
    /// </remarks>
    public static class ExampleUnitPresets
    {
        private const string Prefix = "ExampleUnitPresets/";
        private const string Written = ".examples";

        /// <summary>
        /// The SHA-256 of earlier versions of the examples' files (lines ending in a line feed), as
        /// <c>file:hash</c>: a copy still equal to one was not changed by the user and is replaced by
        /// the current version. A file changed for a release adds its old hash here.
        /// </summary>
        private static readonly HashSet<string> Earlier = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
        };

        /// <summary>
        /// The examples' file names, e.g. <c>Aeon base, small.json</c>.
        /// </summary>
        public static IReadOnlyList<string> Names => [.. Resources().Select(resource => resource.File).Order(StringComparer.Ordinal)];

        /// <summary>
        /// The shipped text of an example's file.
        /// </summary>
        public static string Text(string file)
        {
            string resource = Resources().First(r => r.File == file).Resource;
            using StreamReader reader = new StreamReader(typeof(ExampleUnitPresets).Assembly.GetManifestResourceStream(resource)!);
            return reader.ReadToEnd();
        }

        /// <summary>
        /// Writes the examples the workspace never had into its <c>presets/units</c>, and brings untouched
        /// copies of earlier versions up to date.
        /// </summary>
        /// <returns>The files written or brought up to date now.</returns>
        public static IReadOnlyList<string> Ensure(Workspace workspace) => Ensure(Path.Combine(workspace.PresetsFolder, PresetKinds.Units));

        /// <summary>
        /// Writes the examples into a folder of units presets (see <see cref="Ensure(Workspace)"/>).
        /// </summary>
        public static IReadOnlyList<string> Ensure(string unitsFolder)
        {
            string written = Path.Combine(unitsFolder, Written);
            HashSet<string> had = new HashSet<string>(File.Exists(written) ? File.ReadAllLines(written) : [], StringComparer.OrdinalIgnoreCase);
            List<string> now = [];
            List<string> recorded = [];
            foreach ((string file, _) in Resources())
            {
                string path = Path.Combine(unitsFolder, file);
                string shipped = Text(file);
                if (File.Exists(path))
                {
                    string onDisk = File.ReadAllText(path);
                    if (Normal(onDisk) != Normal(shipped) && Earlier.Contains($"{file}:{Hash(onDisk)}"))
                    {
                        File.WriteAllText(path, shipped);
                        now.Add(file);
                    }
                    if (!had.Contains(file))
                    {
                        recorded.Add(file);
                    }
                    continue;
                }
                if (had.Contains(file))
                {
                    continue;
                }
                Directory.CreateDirectory(unitsFolder);
                File.WriteAllText(path, shipped);
                now.Add(file);
                recorded.Add(file);
            }
            if (recorded.Count > 0)
            {
                File.WriteAllLines(written, [.. had.Concat(recorded).Order(StringComparer.OrdinalIgnoreCase)]);
            }
            return now;
        }

        private static string Normal(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

        private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Normal(text))));

        private static IEnumerable<(string File, string Resource)> Resources() =>
            typeof(ExampleUnitPresets).Assembly.GetManifestResourceNames()
                .Where(resource => resource.StartsWith(Prefix, StringComparison.Ordinal))
                .Select(resource => (resource[Prefix.Length..], resource));
    }
}
