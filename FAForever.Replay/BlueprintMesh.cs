namespace FAForever.Replay
{
    /// <summary>
    /// A mesh (<c>MeshBlueprint { ... }</c>); its id is the lower case path without <c>.bp</c>,
    /// unless the file sets one.
    /// </summary>
    public sealed record BlueprintMesh : Blueprint
    {
        /// <summary>
        /// The levels of detail, from close up to far away.
        /// </summary>
        public IReadOnlyList<BlueprintMeshLod> LODs { get; init; } = [];

        public double? IconFadeInZoom { get; init; }

        public double? UniformScale { get; init; }

        public double? SortOrder { get; init; }

        public bool? StraddleWater { get; init; }

        internal static BlueprintMesh Read(BlueprintTableReader t, string blueprintId, string source) => new BlueprintMesh
        {
            Raw = t.Table,
            BlueprintId = blueprintId,
            Source = source,
            LODs = t.List("LODs", BlueprintMeshLod.Read),
            IconFadeInZoom = t.Number("IconFadeInZoom"),
            UniformScale = t.Number("UniformScale"),
            SortOrder = t.Number("SortOrder"),
            StraddleWater = t.Bool("StraddleWater"),
        };
    }

    /// <summary>
    /// One level of detail of a mesh. File names are relative to the blueprint's folder unless
    /// they start with a slash.
    /// </summary>
    public sealed record BlueprintMeshLod : BlueprintTable
    {
        /// <summary>
        /// The camera distance up to which this level is used.
        /// </summary>
        public double? LODCutoff { get; init; }

        public string? MeshName { get; init; }

        public string? ShaderName { get; init; }

        public string? AlbedoName { get; init; }

        public string? NormalsName { get; init; }

        public string? SpecularName { get; init; }

        public string? LookupName { get; init; }

        public string? SecondaryName { get; init; }

        public bool? Scrolling { get; init; }

        internal static BlueprintMeshLod Read(BlueprintTableReader t) => new BlueprintMeshLod
        {
            Raw = t.Table,
            LODCutoff = t.Number("LODCutoff"),
            MeshName = t.String("MeshName"),
            ShaderName = t.String("ShaderName"),
            AlbedoName = t.String("AlbedoName"),
            NormalsName = t.String("NormalsName"),
            SpecularName = t.String("SpecularName"),
            LookupName = t.String("LookupName"),
            SecondaryName = t.String("SecondaryName"),
            Scrolling = t.Bool("Scrolling"),
        };
    }
}
