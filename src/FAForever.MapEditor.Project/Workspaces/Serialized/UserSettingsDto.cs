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
