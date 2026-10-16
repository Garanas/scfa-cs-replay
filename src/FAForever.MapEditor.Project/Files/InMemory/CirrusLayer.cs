using System.Numerics;

namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// A layer of cirrus clouds.
    /// </summary>
    public sealed record CirrusLayer
    {
        public Vector2 Frequency { get; init; }

        public float Speed { get; init; }

        public Vector2 Direction { get; init; }
    }
}
