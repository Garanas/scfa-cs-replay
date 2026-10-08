namespace FAForever.MapEditor.Project.Workspaces.Serialized
{
    /// <summary>
    /// An archive the assets were converted from. FAF's archives (<c>.nx2</c>) change with every
    /// release, so their size, time and content are kept; the game's (<c>.scd</c>) never change.
    /// </summary>
    public sealed record ArchiveDto
    {
        public required string Path { get; init; }

        public required long Size { get; init; }

        public required DateTime LastWriteUtc { get; init; }

        /// <summary>
        /// SHA-256 of the content in hexadecimal, for FAF's archives.
        /// </summary>
        public string? Sha256 { get; init; }
    }
}
