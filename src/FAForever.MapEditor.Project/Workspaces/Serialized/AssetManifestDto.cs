namespace FAForever.MapEditor.Project.Workspaces.Serialized
{
    /// <summary>
    /// <c>assets/manifest.json</c>: what the assets were converted from and by which editor, to
    /// tell when they need converting again.
    /// </summary>
    public sealed record AssetManifestDto
    {
        /// <summary>
        /// The version of the conversion's output (<see cref="AssetConverter.FormatVersion"/>).
        /// </summary>
        public required int FormatVersion { get; init; }

        /// <summary>
        /// The version of the editor that converted them, e.g. <c>1.2.0</c>.
        /// </summary>
        public required string EditorVersion { get; init; }

        public required DateTimeOffset Converted { get; init; }

        /// <summary>
        /// The archives read, in the order they are laid over each other.
        /// </summary>
        public required List<ArchiveDto> Archives { get; init; }

        public required int Textures { get; init; }

        public required int Meshes { get; init; }

        public required int Props { get; init; }

        public required int Units { get; init; }
    }
}
