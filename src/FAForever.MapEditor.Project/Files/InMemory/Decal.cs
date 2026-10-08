using System.Collections.Immutable;
using System.Numerics;

namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// A decal on the terrain.
    /// </summary>
    public sealed record Decal
    {
        public int Id { get; init; }

        public DecalType Type { get; init; }

        /// <summary>
        /// The textures by slot; an unused slot is empty.
        /// </summary>
        public ImmutableArray<string> Textures { get; init; } = [];

        public Vector3 Scale { get; init; }

        public Vector3 Position { get; init; }

        public Vector3 Rotation { get; init; }

        /// <summary>
        /// The distance beyond which the decal is not drawn.
        /// </summary>
        public float CutOffLod { get; init; }

        /// <summary>
        /// The distance closer than which the decal is not drawn.
        /// </summary>
        public float NearCutOffLod { get; init; }

        /// <summary>
        /// The army that owns the decal, -1 for none.
        /// </summary>
        public int OwnerArmy { get; init; }
    }
}
