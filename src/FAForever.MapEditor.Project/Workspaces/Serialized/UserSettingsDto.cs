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
    }
}
