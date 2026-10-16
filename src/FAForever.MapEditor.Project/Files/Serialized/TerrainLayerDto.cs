namespace FAForever.MapEditor.Project.Files.Serialized
{
    /// <summary>
    /// A terrain layer (stratum): an albedo texture and a normal map, each with the size of one tile. The paths are empty for an unused layer.
    /// </summary>
    public sealed record TerrainLayerDto
    {
        /// <summary>
        /// E.g. <c>/env/Desert/Layers/Des_sand_albedo.dds</c>.
        /// </summary>
        public required string AlbedoPath { get; init; }

        public required float AlbedoScale { get; init; }

        public required string NormalPath { get; init; }

        public required float NormalScale { get; init; }
    }
}
