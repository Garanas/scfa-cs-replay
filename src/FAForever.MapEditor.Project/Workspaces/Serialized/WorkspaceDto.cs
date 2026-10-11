namespace FAForever.MapEditor.Project.Workspaces.Serialized
{
    /// <summary>
    /// <c>workspace.json</c>: where the game's files are and where maps are exported to, and the
    /// Procedural settings maps made or imported in the workspace start with.
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

        /// <summary>
        /// What a map made or imported in the workspace regenerates on its own and makes on export;
        /// absent for the editor's defaults (<see cref="ProceduralDefaults.Default"/>).
        /// </summary>
        public WorkspaceProceduralDto? Procedural { get; init; }
    }

    /// <summary>
    /// <c>workspace.json</c>'s <c>procedural</c>: the Procedural settings a new or imported map starts with.
    /// </summary>
    public sealed record WorkspaceProceduralDto
    {
        /// <summary>
        /// The ids of the generated results that regenerate on their own (<c>walkability</c>,
        /// <c>navigationMesh</c>, <c>masksFromRules</c>, ...); absent for the defaults, empty for none.
        /// </summary>
        public List<string>? AutoGenerate { get; init; }

        /// <summary>
        /// Of the results with a choice on export (<c>normalMap</c>, <c>pathNetwork</c>), those the
        /// export makes or writes; absent for the defaults, empty for none.
        /// </summary>
        public List<string>? OnExport { get; init; }
    }
}
