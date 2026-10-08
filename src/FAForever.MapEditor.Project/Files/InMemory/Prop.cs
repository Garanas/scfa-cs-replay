using System.Numerics;

namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// A prop, e.g. a tree or a rock, with its orientation as three axes.
    /// </summary>
    public sealed record Prop
    {
        /// <summary>
        /// Tells the prop apart while the map is being edited; a project does not store it.
        /// </summary>
        public required int Id { get; init; }

        /// <summary>
        /// E.g. <c>/env/evergreen/props/trees/pine06_s2_prop.bp</c>.
        /// </summary>
        public required string Blueprint { get; init; }

        public Vector3 Position { get; init; }

        public Vector3 RotationX { get; init; }

        public Vector3 RotationY { get; init; }

        public Vector3 RotationZ { get; init; }

        public Vector3 Scale { get; init; }
    }
}
