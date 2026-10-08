using System.Collections.Immutable;

namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// The background, the cubemaps and the sky dome.
    /// </summary>
    public sealed record Sky
    {
        public required string Background { get; init; }

        public required string Cubemap { get; init; }

        /// <summary>
        /// The cubemaps that units reflect, by name.
        /// </summary>
        public ImmutableArray<Cubemap> EnvironmentCubemaps { get; init; } = [];

        /// <summary>
        /// The sky dome; only version 60 has one.
        /// </summary>
        public Skybox? Skybox { get; init; }
    }
}
