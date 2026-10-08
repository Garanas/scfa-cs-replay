using FAForever.FileFormats.Blueprints;
using FAForever.MapEditor.Project.Files.Serialized;

namespace FAForever.MapEditor.Project.Workspaces.Serialized
{
    /// <summary>
    /// A unit as the editor uses it, from its <c>_unit.bp</c>.
    /// </summary>
    public sealed record UnitBlueprintDto
    {
        /// <summary>
        /// The game's id, e.g. <c>uel0001</c>.
        /// </summary>
        public required string Id { get; init; }

        /// <summary>
        /// The name the game shows, e.g. <c>Armored Command Unit</c>.
        /// </summary>
        public string? Name { get; init; }

        public Faction? Faction { get; init; }

        /// <summary>
        /// 1 to 3, or 4 for experimentals; absent for commanders and others without a tech level.
        /// </summary>
        public int? TechLevel { get; init; }

        public List<string> Categories { get; init; } = [];

        public string? StrategicIcon { get; init; }

        /// <summary>
        /// The size of its hitbox.
        /// </summary>
        public Vector3Dto? Size { get; init; }

        /// <summary>
        /// The space it takes up on the map, in ogrids.
        /// </summary>
        public FootprintDto? Footprint { get; init; }

        public double? UniformScale { get; init; }

        public double? Health { get; init; }

        public double? MassCost { get; init; }

        public double? EnergyCost { get; init; }

        /// <summary>
        /// The share of its mass cost that its wreck holds; absent when it leaves no wreck.
        /// </summary>
        public double? WreckageMass { get; init; }

        public List<MeshLodDto> Lods { get; init; } = [];
    }
}
