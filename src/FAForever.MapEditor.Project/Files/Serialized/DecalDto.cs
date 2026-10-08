namespace FAForever.MapEditor.Project.Files.Serialized
{
    /// <summary>
    /// A decal on the terrain.
    /// </summary>
    public sealed record DecalDto
    {
        public required int Id { get; init; }

        public required DecalType Type { get; init; }

        /// <summary>
        /// The textures by slot; an unused slot is empty.
        /// </summary>
        public required IReadOnlyList<string> Textures { get; init; }

        public required Vector3Dto Scale { get; init; }

        public required Vector3Dto Position { get; init; }

        public required Vector3Dto Rotation { get; init; }

        /// <summary>
        /// The distance beyond which the decal is not drawn.
        /// </summary>
        public required float CutOffLod { get; init; }

        /// <summary>
        /// The distance closer than which the decal is not drawn.
        /// </summary>
        public required float NearCutOffLod { get; init; }

        /// <summary>
        /// The army that owns the decal, -1 for none.
        /// </summary>
        public required int OwnerArmy { get; init; }
    }
}
