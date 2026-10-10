namespace FAForever.MapEditor.Project.Files.Serialized
{
    /// <summary>
    /// The textures of a decal set beyond its first decal's: its normals and its masks.
    /// </summary>
    public sealed record DecalSetDto
    {
        /// <summary>
        /// The set's name: the game path of its files without suffix and extension.
        /// </summary>
        public required string Name { get; init; }

        /// <summary>
        /// The normals texture; absent for none.
        /// </summary>
        public string? Normals { get; init; }

        /// <summary>
        /// The masks by name (<c>main</c> for the plain <c>_mask</c>), game paths; absent for none.
        /// </summary>
        public Dictionary<string, string>? Masks { get; init; }
    }
}
