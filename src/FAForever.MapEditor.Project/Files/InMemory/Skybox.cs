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
        /// The light ramp of the cumulus clouds; empty in every map known.
        /// </summary>
        public string CumulusLightRamp { get; init; } = "";

        /// <summary>
        /// The dispersion ramp of the cumulus clouds; empty in every map known.
        /// </summary>
        public string CumulusDispersionRamp { get; init; } = "";

        /// <summary>
        /// The texture of the cumulus clouds; empty in every map known.
        /// </summary>
        public string CumulusTexture { get; init; } = "";

        public float CirrusMultiplier { get; init; }

        public Vector3 CirrusColor { get; init; }

        public required string CirrusTexture { get; init; }

        /// <summary>
        /// The four cirrus layers, one per channel of the cirrus texture (the game always has four:
        /// <see cref="Editing.Skyboxes.WithFourCirrusLayers"/>).
        /// </summary>
        public ImmutableArray<CirrusLayer> CirrusLayers { get; init; } = [];
    }
}
