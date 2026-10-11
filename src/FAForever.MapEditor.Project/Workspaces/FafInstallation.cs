using FAForever.FileFormats.Lua;

namespace FAForever.MapEditor.Project.Workspaces
{
    /// <summary>
    /// What the FAF client knows about this computer, from its <c>fa_path.lua</c>: where the game
    /// is installed and where the vault is. Each is null when the file does not say.
    /// </summary>
    /// <param name="GameFolder">The game's installation (<c>fa_path</c>).</param>
    /// <param name="VaultFolder">The vault on disk (<c>custom_vault_path</c>).</param>
    /// <param name="DataFolder">FAF's <c>gamedata</c> folder with its archives (<c>env.nx2</c>, ...), next to the file.</param>
    /// <param name="GameVersion">The game version FAF last updated to (<c>GameVersion</c>), e.g. <c>3839</c>.</param>
    public sealed record FafInstallation(string? GameFolder, string? VaultFolder, string? DataFolder, string? GameVersion)
    {
        public const string PathFile = "fa_path.lua";

        /// <summary>
        /// Where the FAF client keeps its files: <c>C:\ProgramData\FAForever</c> on Windows,
        /// <c>~/.faforever</c> elsewhere.
        /// </summary>
        public static string DefaultFolder => OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "FAForever")
            : Path.Combine(UserFolders.Home, ".faforever");

        /// <summary>
        /// Reads <c>fa_path.lua</c> in <paramref name="folder"/>, by default <see cref="DefaultFolder"/>;
        /// null when there is no such file or it cannot be read.
        /// </summary>
        public static FafInstallation? Find(string? folder = null)
        {
            folder ??= DefaultFolder;
            string file = Path.Combine(folder, PathFile);
            if (!File.Exists(file))
            {
                return null;
            }

            IReadOnlyDictionary<string, LuaData> globals;
            try
            {
                globals = LuaSourceParser.Execute(File.ReadAllText(file), new Dictionary<string, LuaFunction>());
            }
            catch (Exception exception) when (exception is LuaSyntaxException or IOException or FormatException)
            {
                return null;
            }

            string? Text(string name) => globals.TryGetValue(name, out LuaData? value) && value is LuaData.String text && text.Value.Length > 0
                ? Path.GetFullPath(text.Value)
                : null;

            string data = Path.Combine(folder, "gamedata");
            return new FafInstallation(
                Text("fa_path"),
                Text("custom_vault_path"),
                Directory.Exists(data) ? Path.GetFullPath(data) : null,
                globals.TryGetValue("GameVersion", out LuaData? version) && version is LuaData.String v ? v.Value : null);
        }
    }
}
