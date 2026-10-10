using FAForever.MapEditor.Project.Workspaces.Serialized;

namespace FAForever.MapEditor.Project.Workspaces
{
    /// <summary>
    /// The user's choices about diagnostics: what goes well and wrong while the editor runs.
    /// </summary>
    /// <param name="Record">Whether logs and measurements are written to disk, per session.</param>
    /// <param name="Send">Whether what is recorded is sent to <paramref name="Endpoint"/>; off unless the user turns it on.</param>
    /// <param name="Endpoint">The OpenTelemetry collector (OTLP over HTTP) to send to, or null for none.</param>
    /// <param name="InstallId">A random id of this computer's editor, so that sessions of one user can be told apart from those of many.</param>
    public sealed record TelemetrySettings(bool Record, bool Send, string? Endpoint, string InstallId);

    /// <summary>
    /// What the editor remembers for the user, outside the workspace: where the workspace is, the
    /// choices about diagnostics, the window's layout and the plugins turned off.
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
        public string? Workspace => Read().Workspace;

        /// <summary>
        /// The choices about diagnostics: recording on, sending off, until the user changes them. The
        /// first read gives the editor its install id and remembers it.
        /// </summary>
        public TelemetrySettings Telemetry
        {
            get
            {
                TelemetrySettingsDto telemetry = Read().Telemetry ?? new TelemetrySettingsDto();
                if (telemetry.InstallId is null)
                {
                    telemetry = telemetry with { InstallId = Guid.NewGuid().ToString("N") };
                    TryUpdate(settings => settings with { Telemetry = telemetry });
                }
                return new TelemetrySettings(telemetry.Record, telemetry.Send, telemetry.Endpoint, telemetry.InstallId!);
            }
        }

        /// <summary>
        /// How the window is laid out and its toolbar (user stories V-4, F-1): the defaults until
        /// the user changes them.
        /// </summary>
        public LayoutSettingsDto Layout => Read().Layout ?? new LayoutSettingsDto();

        /// <summary>
        /// Remembers how the window is laid out.
        /// </summary>
        /// <exception cref="IOException">The file cannot be written.</exception>
        public void SetLayout(LayoutSettingsDto layout) => Update(settings => settings with { Layout = layout });

        /// <summary>
        /// The plugins turned off and their fields' values (user story F-15): every plugin on until
        /// the user turns one off.
        /// </summary>
        public PluginSettingsDto Plugins => Read().Plugins ?? new PluginSettingsDto();

        /// <summary>
        /// Remembers the plugins turned off and their fields' values.
        /// </summary>
        /// <exception cref="IOException">The file cannot be written.</exception>
        public void SetPlugins(PluginSettingsDto plugins) => Update(settings => settings with { Plugins = plugins });

        /// <summary>
        /// Remembers <paramref name="folder"/> as the workspace.
        /// </summary>
        public void SetWorkspace(string folder) => Update(settings => settings with { Workspace = Path.GetFullPath(folder) });

        /// <summary>
        /// Remembers the choices about diagnostics.
        /// </summary>
        public void SetTelemetry(TelemetrySettings telemetry) => Update(settings => settings with
        {
            Telemetry = new TelemetrySettingsDto
            {
                Record = telemetry.Record,
                Send = telemetry.Send,
                Endpoint = string.IsNullOrWhiteSpace(telemetry.Endpoint) ? null : telemetry.Endpoint.Trim(),
                InstallId = telemetry.InstallId,
            },
        });

        private UserSettingsDto Read()
        {
            try
            {
                return System.IO.File.Exists(File) ? ProjectJson.Read<UserSettingsDto>(File) : new UserSettingsDto();
            }
            catch (Exception exception) when (exception is MapProjectException or IOException)
            {
                return new UserSettingsDto();
            }
        }

        private void Update(Func<UserSettingsDto, UserSettingsDto> change)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(File))!);
            ProjectJson.Write(File, change(Read()));
        }

        private void TryUpdate(Func<UserSettingsDto, UserSettingsDto> change)
        {
            try
            {
                Update(change);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // the id is new on the next start too
            }
        }
    }
}
