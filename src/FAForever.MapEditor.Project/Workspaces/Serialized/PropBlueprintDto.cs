using FAForever.MapEditor.Project.Files.Serialized;

namespace FAForever.MapEditor.Project.Workspaces.Serialized
{
    /// <summary>
    /// A prop (tree, rock, wreck) as the editor uses it, from its <c>_prop.bp</c>.
    /// </summary>
    public sealed record PropBlueprintDto
    {
        /// <summary>
        /// The game's id: the blueprint's path in lower case, e.g. <c>/env/evergreen/props/trees/pine06_s2_prop.bp</c>.
        /// </summary>
        public required string Id { get; init; }

        public string? Description { get; init; }

        public List<string> Categories { get; init; } = [];

        /// <summary>
        /// The size of its hitbox.
        /// </summary>
        public Vector3Dto? Size { get; init; }

        public double? UniformScale { get; init; }

        public double? Health { get; init; }

        public double? ReclaimMass { get; init; }

        public double? ReclaimEnergy { get; init; }

        public double? ReclaimTime { get; init; }

        public List<MeshLodDto> Lods { get; init; } = [];
    }
}
