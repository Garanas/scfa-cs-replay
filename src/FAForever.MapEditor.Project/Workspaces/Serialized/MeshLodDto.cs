namespace FAForever.MapEditor.Project.Workspaces.Serialized
{
    /// <summary>
    /// One level of detail of a prop's or unit's mesh. Paths are the game's, in lower case and
    /// from the root (<c>/env/evergreen/props/trees/pine06_lod0.scm</c>); a texture's is that of
    /// its PNG. A texture the archives do not have is left out.
    /// </summary>
    public sealed record MeshLodDto
    {
        /// <summary>
        /// The camera distance up to which this level is used.
        /// </summary>
        public double? Cutoff { get; init; }

        public required string Mesh { get; init; }

        public string? Shader { get; init; }

        public string? Albedo { get; init; }

        public string? Normals { get; init; }

        public string? Specular { get; init; }
    }
}
