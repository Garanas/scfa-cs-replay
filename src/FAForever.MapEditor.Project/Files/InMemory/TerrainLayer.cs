namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// A terrain layer (stratum): an albedo texture and a normal map, each with the size of one tile. The paths are empty for an unused layer.
    /// </summary>
    public sealed record TerrainLayer
    {
        /// <summary>
        /// E.g. <c>/env/Desert/Layers/Des_sand_albedo.dds</c>.
        /// </summary>
        public required string AlbedoPath { get; init; }

        public float AlbedoScale { get; init; }

        public required string NormalPath { get; init; }

        public float NormalScale { get; init; }
    }
}
