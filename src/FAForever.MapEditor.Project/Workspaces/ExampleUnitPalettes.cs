namespace FAForever.MapEditor.Project.Workspaces
{
    /// <summary>
    /// The example unit palettes that ship with the editor (user story U-8): per faction (UEF,
    /// Aeon, Cybran, Seraphim) a T1 base, the T2 structures, the defences, the naval structures and
    /// the civilian settlements of its building style (the example unit presets,
    /// <see cref="ExampleUnitPresets"/>). They name blueprint ids and preset names only.
    /// </summary>
    /// <remarks>
    /// <see cref="Ensure"/> writes them into a workspace's <c>presets/palettes</c> once, as
    /// <see cref="ExampleUnitPresets"/> writes the unit presets: one the user deletes stays deleted
    /// (the hidden file <c>.examples</c> names the ones written), an untouched copy of an earlier
    /// version is brought up to date, one the user changed is left alone, and an untouched copy of
    /// an example that no longer ships is deleted (<see cref="ExamplePresetFiles"/>).
    /// </remarks>
    public static class ExampleUnitPalettes
    {
        /// <summary>
        /// The SHA-256 of earlier versions of the examples' files, as <c>file:hash</c>; a file changed
        /// for a release adds its old hash here (<see cref="ExamplePresetFiles.Hash"/>).
        /// </summary>
        private static readonly HashSet<string> Earlier = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The SHA-256 of examples that no longer ship, as <see cref="Earlier"/>.
        /// </summary>
        private static readonly HashSet<string> Removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static readonly ExamplePresetFiles Files = new ExamplePresetFiles("ExampleUnitPalettes/", Earlier, Removed);

        /// <summary>
        /// The examples' file names, e.g. <c>UEF T1 base.json</c>.
        /// </summary>
        public static IReadOnlyList<string> Names => Files.Names;

        /// <summary>
        /// The shipped text of an example's file.
        /// </summary>
        public static string Text(string file) => Files.Text(file);

        /// <summary>
        /// Writes the examples the workspace never had into its <c>presets/palettes</c>, brings untouched
        /// copies of earlier versions up to date and deletes untouched copies of examples that no longer ship.
        /// </summary>
        /// <returns>The files written or brought up to date now.</returns>
        public static IReadOnlyList<string> Ensure(Workspace workspace) => Ensure(Path.Combine(workspace.PresetsFolder, PresetKinds.Palettes));

        /// <summary>
        /// Writes the examples into a folder of palette presets (see <see cref="Ensure(Workspace)"/>).
        /// </summary>
        public static IReadOnlyList<string> Ensure(string palettesFolder) => Files.Ensure(palettesFolder);
    }
}
