using System.Numerics;

namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// How the sun lights the terrain and the units.
    /// </summary>
    public sealed record Lighting
    {
        public float Multiplier { get; init; }

        public Vector3 SunDirection { get; init; }

        public Vector3 SunAmbience { get; init; }

        public Vector3 SunColor { get; init; }

        public Vector3 ShadowFillColor { get; init; }

        public Vector4 SpecularColor { get; init; }

        public float Bloom { get; init; }

        public Vector3 FogColor { get; init; }

        public float FogStart { get; init; }

        public float FogEnd { get; init; }
    }
}
