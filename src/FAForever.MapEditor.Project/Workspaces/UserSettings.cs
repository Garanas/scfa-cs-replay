using FAForever.MapEditor.Project.Workspaces.Serialized;

namespace FAForever.MapEditor.Project.Workspaces
{
    /// <summary>
    /// What the editor remembers for the user, outside the workspace: where the workspace is.
    /// </summary>
    /// <param name="File">The settings file; <see cref="DefaultFile"/> for the user.</param>
    public sealed record UserSettings(string File)
    {
        /// <summary>
        /// An environment variable that names another settings file, so that tests and automation
        /// leave the user's own alone.
        /// </summary>
        public const string FileVariable = "FAF_MAP_EDITOR_SETTINGS";

        /// <summary>
        /// <c>%APPDATA%\FAF Map Editor\settings.json</c> on Windows, <c>~/.config/FAF Map Editor/settings.json</c>
        /// elsewhere, unless <see cref="FileVariable"/> names another.
        /// </summary>
        public static string DefaultFile => Environment.GetEnvironmentVariable(FileVariable) is { Length: > 0 } file
            ? file
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FAF Map Editor", "settings.json");

        public static UserSettings Default => new UserSettings(DefaultFile);

        /// <summary>
        /// The workspace the editor used last, or null when none is set up or the file is unreadable.
        /// </summary>
        public string? Workspace
        {
            get
            {
                try
                {
                    return System.IO.File.Exists(File) ? ProjectJson.Read<UserSettingsDto>(File).Workspace : null;
                }
                catch (Exception exception) when (exception is MapProjectException or IOException)
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// Remembers <paramref name="folder"/> as the workspace.
        /// </summary>
        public void SetWorkspace(string folder)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(File))!);
            ProjectJson.Write(File, new UserSettingsDto { Workspace = Path.GetFullPath(folder) });
        }
    }
}
