namespace FAForever.MapEditor.Project.Workspaces.Serialized
{
    /// <summary>
    /// What the editor remembers for the user, outside the workspace.
    /// </summary>
    public sealed record UserSettingsDto
    {
        /// <summary>
        /// The folder of the workspace, absent until one is set up.
        /// </summary>
        public string? Workspace { get; init; }

        /// <summary>
        /// What the editor records about how it runs, and whether it sends that; absent until the
        /// user changes it.
        /// </summary>
        public TelemetrySettingsDto? Telemetry { get; init; }

        /// <summary>
        /// How the editor's window is laid out (user story V-4) and its toolbar (F-1); absent until
        /// the user changes it.
        /// </summary>
        public LayoutSettingsDto? Layout { get; init; }

        /// <summary>
        /// The Lua plugins turned off and the values of their panels' fields (user story F-15);
        /// absent until the user changes one.
        /// </summary>
        public PluginSettingsDto? Plugins { get; init; }
    }

    /// <summary>
    /// The user's choices about the workspace's plugins: a plugin is on unless named here.
    /// </summary>
    public sealed record PluginSettingsDto
    {
        /// <summary>
        /// The folders (in the workspace's <c>plugins</c>) of the plugins turned off.
        /// </summary>
        public List<string>? Off { get; init; }

        /// <summary>
        /// Per plugin folder, the values of its panel's fields by key, as text.
        /// </summary>
        public Dictionary<string, Dictionary<string, string>>? Values { get; init; }
    }

    /// <summary>
    /// The window's layout as the user left it: which panels show, their sizes, the views and the
    /// toolbar's commands. A size or list left out is the editor's default.
    /// </summary>
    public sealed record LayoutSettingsDto
    {
        public bool ModePanel { get; init; } = true;

        public bool Inspector { get; init; } = true;

        public bool Dock { get; init; } = true;

        public bool Toolbar { get; init; } = true;

        /// <summary>
        /// The width of the mode's panel in device-independent pixels.
        /// </summary>
        public double? ModePanelWidth { get; init; }

        public double? InspectorWidth { get; init; }

        public double? DockHeight { get; init; }

        /// <summary>
        /// The part of the width the top view takes while the views are side by side, 0 to 1.
        /// </summary>
        public double? SideBySideSplit { get; init; }

        /// <summary>
        /// Which view shows: <c>Top</c>, <c>ThreeD</c> or <c>SideBySide</c>.
        /// </summary>
        public string? View { get; init; }

        /// <summary>
        /// The ids of the commands on the toolbar, in order.
        /// </summary>
        public List<string>? ToolbarCommands { get; init; }
    }

    /// <summary>
    /// The user's choices about diagnostics (see <see cref="TelemetrySettings"/>).
    /// </summary>
    public sealed record TelemetrySettingsDto
    {
        public bool Record { get; init; } = true;

        public bool Send { get; init; }

        public string? Endpoint { get; init; }

        public string? InstallId { get; init; }
    }
}
