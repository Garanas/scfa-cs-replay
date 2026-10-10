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

        /// <summary>
        /// Where the panels lie that do not lie in their own place, by panel (<c>modePanel</c>,
        /// <c>inspector</c>, <c>dock</c>, and the dock's tabs <c>problems</c>, <c>history</c>,
        /// <c>assets</c>, <c>console</c>): <c>left</c>, <c>right</c>, <c>bottom</c>, <c>float</c>
        /// (a window of its own) or, for a tab, <c>dock</c>.
        /// </summary>
        public Dictionary<string, string>? Places { get; init; }

        /// <summary>
        /// The windows of the panels that float, by panel, and of those that floated before: their
        /// place on the screen and size, and where the panel goes back to when its window closes.
        /// </summary>
        public Dictionary<string, FloatingPanelDto>? Floating { get; init; }
    }

    /// <summary>
    /// The window of a panel that floats: its place on the screen (pixels), its size
    /// (device-independent pixels) and where the panel goes back to when it closes.
    /// </summary>
    public sealed record FloatingPanelDto
    {
        public int? X { get; init; }

        public int? Y { get; init; }

        public double? Width { get; init; }

        public double? Height { get; init; }

        /// <summary>
        /// <c>left</c>, <c>right</c>, <c>bottom</c> or <c>dock</c>; absent, the panel's own place.
        /// </summary>
        public string? Returns { get; init; }
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
