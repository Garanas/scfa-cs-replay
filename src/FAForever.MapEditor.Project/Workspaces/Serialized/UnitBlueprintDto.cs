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
        /// The background of its icon in the build menu (<c>General.Icon</c>): <c>land</c>,
        /// <c>air</c>, <c>sea</c> or <c>amph</c>.
        /// </summary>
        public string? BuildIcon { get; init; }

        /// <summary>
        /// The size of its hitbox.
        /// </summary>
        public Vector3Dto? Size { get; init; }

        /// <summary>
        /// The space it takes up on the map, in ogrids.
        /// </summary>
        public FootprintDto? Footprint { get; init; }

        /// <summary>
        /// The ground a structure reserves (<c>Physics.SkirtSizeX</c>, ...); absent when the
        /// blueprint names none.
        /// </summary>
        public SkirtDto? Skirt { get; init; }

        /// <summary>
        /// The layers it may be built on (<c>Physics.BuildOnLayerCaps</c>) without the <c>LAYER_</c>
        /// prefix: <c>Land</c>, <c>Water</c>, <c>Seabed</c>; absent when the blueprint lists none.
        /// </summary>
        public List<string>? BuildOnLayers { get; init; }

        /// <summary>
        /// How much the ground under the skirt may rise and fall (<c>Physics.MaxGroundVariation</c>);
        /// absent when the blueprint gives none (the engine's default is 1).
        /// </summary>
        public double? MaxGroundVariation { get; init; }

        /// <summary>
        /// Whether the structure levels the ground under its skirt (<c>Physics.FlattenSkirt</c>).
        /// </summary>
        public bool FlattenSkirt { get; init; }

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
