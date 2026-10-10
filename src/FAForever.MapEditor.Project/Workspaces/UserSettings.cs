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

        /// <summary>
        /// What was wrong with the file when it was last read, or null when it was read in full: a
        /// part that cannot be read (a value of the wrong type) is left out and named here, the rest
        /// is read; a file that is not JSON at all is read as empty, and never written over.
        /// </summary>
        public string? Problem { get; private set; }

        private bool _notJson;

        private UserSettingsDto Read()
        {
            string text;
            try
            {
                if (!System.IO.File.Exists(File))
                {
                    Problem = null;
                    return new UserSettingsDto();
                }
                text = System.IO.File.ReadAllText(File);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Problem = exception.Message;
                return new UserSettingsDto();
            }
            System.Text.Json.Nodes.JsonObject root;
            _notJson = false;
            try
            {
                root = System.Text.Json.Nodes.JsonNode.Parse(text,
                    new System.Text.Json.Nodes.JsonNodeOptions { PropertyNameCaseInsensitive = true },
                    new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true }) as System.Text.Json.Nodes.JsonObject
                    ?? throw new System.Text.Json.JsonException("it holds no JSON object");
            }
            catch (System.Text.Json.JsonException exception)
            {
                Problem = $"{File} is not JSON: {exception.Message}";
                _notJson = true;
                return new UserSettingsDto();
            }
            // each part on its own, so one value of the wrong type loses only its part
            List<string> problems = [];
            T? Part<T>(string name) where T : class
            {
                try
                {
                    return root[name] is System.Text.Json.Nodes.JsonNode node ? System.Text.Json.JsonSerializer.Deserialize<T>(node, ProjectJson.Options) : null;
                }
                catch (Exception exception) when (exception is System.Text.Json.JsonException or InvalidOperationException or FormatException)
                {
                    problems.Add($"{name}: {exception.Message}");
                    return null;
                }
            }
            UserSettingsDto settings = new UserSettingsDto
            {
                Workspace = Part<string>("workspace"),
                Telemetry = Part<TelemetrySettingsDto>("telemetry"),
                Layout = Part<LayoutSettingsDto>("layout"),
                Plugins = Part<PluginSettingsDto>("plugins"),
            };
            Problem = problems.Count == 0 ? null : $"{File}: {string.Join("; ", problems)}";
            return settings;
        }

        /// <exception cref="MapProjectException">The file is not JSON: written over, the user's
        /// workspace and choices in it would be lost.</exception>
        private void Update(Func<UserSettingsDto, UserSettingsDto> change)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(File))!);
            UserSettingsDto settings = Read();
            if (_notJson && Problem is string problem)
            {
                throw new MapProjectException($"{problem}; it is left as it is");
            }
            ProjectJson.Write(File, change(settings));
        }

        private void TryUpdate(Func<UserSettingsDto, UserSettingsDto> change)
        {
            try
            {
                Update(change);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or MapProjectException)
            {
                // the id is new on the next start too
            }
        }
    }
}
