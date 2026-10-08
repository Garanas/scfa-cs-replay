using System.Collections.Immutable;
using System.Numerics;

namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// The sky dome of a version 60 map: its colours, planets and clouds.
    /// </summary>
    public sealed record Skybox
    {
        public Vector3 Position { get; init; }

        public float HorizonHeight { get; init; }

        public float Scale { get; init; }

        public float SubtractHeight { get; init; }

        public int SubdivisionsAxis { get; init; }

        public int SubdivisionsHeight { get; init; }

        public float ZenithHeight { get; init; }

        public Vector3 HorizonColor { get; init; }

        public Vector3 ZenithColor { get; init; }

        public float DecalGlowMultiplier { get; init; }

        public required string Albedo { get; init; }

        public required string Glow { get; init; }

        public ImmutableArray<Planet> Planets { get; init; } = [];

        /// <summary>
        /// The colour between horizon and zenith.
        /// </summary>
        public Color32 MidColor { get; init; }

        public float CirrusMultiplier { get; init; }

        public Vector3 CirrusColor { get; init; }

        public required string CirrusTexture { get; init; }

        public ImmutableArray<CirrusLayer> CirrusLayers { get; init; } = [];

        /// <summary>
        /// The last number of the skybox, named after the map editor's field.
        /// </summary>
        public float Clouds7 { get; init; }
    }
}
