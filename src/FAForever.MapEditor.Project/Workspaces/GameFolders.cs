namespace FAForever.MapEditor.Project.Workspaces
{
    /// <summary>
    /// Where Steam installs Supreme Commander: Forged Alliance, for the setup's default when FAF's
    /// <c>fa_path.lua</c> does not say (<see cref="FafInstallation"/>). On Linux the game runs under
    /// Steam's Proton, which keeps it in Steam's own library as on Windows.
    /// </summary>
    public static class GameFolders
    {
        /// <summary>
        /// The game's folder in a Steam library.
        /// </summary>
        public const string SteamFolder = "steamapps/common/Supreme Commander Forged Alliance";

        /// <summary>
        /// The folders the game is usually installed in on this system, most likely first: Steam's
        /// default libraries (on Linux <c>~/.steam/steam</c>, <c>~/.local/share/Steam</c> and the
        /// Flatpak's; on macOS Steam's own).
        /// </summary>
        public static IReadOnlyList<string> Candidates()
        {
            if (OperatingSystem.IsWindows())
            {
                string programs = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                return programs.Length == 0 ? [] : [Path.Combine(programs, "Steam", SteamFolder)];
            }
            string home = UserFolders.Home;
            return
            [
                Path.Combine(home, ".steam/steam", SteamFolder),
                Path.Combine(home, ".local/share/Steam", SteamFolder),
                Path.Combine(home, ".var/app/com.valvesoftware.Steam/.local/share/Steam", SteamFolder),
                Path.Combine(home, "Library/Application Support/Steam", SteamFolder),
            ];
        }

        /// <summary>
        /// The first of <see cref="Candidates"/> that holds the game's archives
        /// (<see cref="GameArchives.Names"/>, in any case); null when none does.
        /// </summary>
        public static string? Find() => Candidates()
            .Select(FilePaths.FindFolder)
            .OfType<string>()
            .FirstOrDefault(HasArchives);

        /// <summary>
        /// Whether <paramref name="folder"/> holds the game's archives, in any case.
        /// </summary>
        public static bool HasArchives(string folder) =>
            GameArchives.Names.All(name => FilePaths.FindFile(folder, $"gamedata/{name}.scd") is not null);
    }
}
