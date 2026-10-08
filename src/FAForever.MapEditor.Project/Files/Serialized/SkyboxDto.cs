namespace FAForever.MapEditor.Project.Files.Serialized
{
    /// <summary>
    /// The sky dome of a version 60 map: its colours, planets and clouds.
    /// </summary>
    public sealed record SkyboxDto
    {
        public required Vector3Dto Position { get; init; }

        public required float HorizonHeight { get; init; }

        public required float Scale { get; init; }

        public required float SubtractHeight { get; init; }

        public required int SubdivisionsAxis { get; init; }

        public required int SubdivisionsHeight { get; init; }

        public required float ZenithHeight { get; init; }

        public required Vector3Dto HorizonColor { get; init; }

        public required Vector3Dto ZenithColor { get; init; }

        public required float DecalGlowMultiplier { get; init; }

        public required string Albedo { get; init; }

        public required string Glow { get; init; }

        public required IReadOnlyList<PlanetDto> Planets { get; init; }

        /// <summary>
        /// The colour between horizon and zenith.
        /// </summary>
        public required string MidColor { get; init; }

        public required float CirrusMultiplier { get; init; }

        public required Vector3Dto CirrusColor { get; init; }

        public required string CirrusTexture { get; init; }

        public required IReadOnlyList<CirrusLayerDto> CirrusLayers { get; init; }

        /// <summary>
        /// The last number of the skybox, named after the map editor's field.
        /// </summary>
        public required float Clouds7 { get; init; }
    }
}
