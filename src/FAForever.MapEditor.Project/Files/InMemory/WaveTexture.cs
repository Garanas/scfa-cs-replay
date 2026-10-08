using System.Numerics;

namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// A normal map layer of the water surface.
    /// </summary>
    public sealed record WaveTexture
    {
        public required string Texture { get; init; }

        public float NormalRepeat { get; init; }

        public Vector2 NormalMovement { get; init; }
    }
}
