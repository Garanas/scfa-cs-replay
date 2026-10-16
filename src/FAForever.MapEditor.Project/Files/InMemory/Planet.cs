using System.Numerics;

namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// A planet of the sky dome.
    /// </summary>
    public sealed record Planet
    {
        public Vector3 Position { get; init; }

        public float Rotation { get; init; }

        public Vector2 Scale { get; init; }

        public Vector4 Uv { get; init; }
    }
}
