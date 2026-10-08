namespace FAForever.MapEditor.Project.Workspaces.Serialized
{
    /// <summary>
    /// <c>workspace.json</c>: where the game's files are and where maps are exported to.
    /// </summary>
    public sealed record WorkspaceDto
    {
        public required int FormatVersion { get; init; }

        /// <summary>
        /// The game's installation, with <c>gamedata/env.scd</c> and <c>gamedata/units.scd</c>.
        /// </summary>
        public required string GameFolder { get; init; }

        /// <summary>
        /// FAF's <c>gamedata</c> folder, with <c>env.nx2</c> and <c>units.nx2</c>; absent without FAF.
        /// </summary>
        public string? FafDataFolder { get; init; }

        /// <summary>
        /// The vault on disk; maps are exported to its <c>maps</c> folder.
        /// </summary>
        public required string VaultFolder { get; init; }
    }
}
