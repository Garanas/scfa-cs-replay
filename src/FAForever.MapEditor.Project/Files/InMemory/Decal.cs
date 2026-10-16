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

        /// <summary>
        /// For a decal set (several textures under one name, placed and moved as one thing), the
        /// set's other textures: its normals, drawn with the same place, turn and size, and its masks.
        /// Null for a decal of one texture. The decal's own <see cref="Type"/> and
        /// <see cref="Textures"/> are the set's albedo and specular (its normals when it has no albedo).
        /// </summary>
        public DecalSet? Set { get; init; }
    }

    /// <summary>
    /// The textures of a decal set beyond its first decal's (<see cref="Decal.Set"/>): files named
    /// alike with a suffix each (<c>rock01_albedo</c>, <c>rock01_normals</c>, <c>rock01_spec</c>,
    /// <c>rock01_mask</c>, <c>rock01_mask_moss</c>; <see cref="Editing.DecalSets"/>).
    /// </summary>
    public sealed record DecalSet
    {
        /// <summary>
        /// The set's name: the game path of its files without suffix and extension
        /// (<c>/env/common/decals/rock01</c>).
        /// </summary>
        public required string Name { get; init; }

        /// <summary>
        /// The normals texture, its own decal at export with the same place, turn and size; null
        /// for none (or when it is the decal's own texture).
        /// </summary>
        public string? Normals { get; init; }

        /// <summary>
        /// The greyscale masks by name (<c>main</c> for the plain <c>_mask</c>), game paths; never
        /// exported, read by the decal-masks layers of mask and stratum stacks.
        /// </summary>
        public ImmutableSortedDictionary<string, string> Masks { get; init; } = ImmutableSortedDictionary.Create<string, string>(StringComparer.Ordinal);

        public bool Equals(DecalSet? other) =>
            other is not null && Name == other.Name && Normals == other.Normals
            && Masks.Count == other.Masks.Count && Masks.All(mask => other.Masks.TryGetValue(mask.Key, out string? path) && path == mask.Value);

        public override int GetHashCode() => HashCode.Combine(Name, Normals, Masks.Count);
    }
}
