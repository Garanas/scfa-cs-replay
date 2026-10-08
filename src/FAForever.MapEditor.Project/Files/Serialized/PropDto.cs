namespace FAForever.MapEditor.Project.Files.Serialized
{
    /// <summary>
    /// A prop, e.g. a tree or a rock, with its orientation as three axes.
    /// </summary>
    public sealed record PropDto
    {
        /// <summary>
        /// E.g. <c>/env/evergreen/props/trees/pine06_s2_prop.bp</c>.
        /// </summary>
        public required string Blueprint { get; init; }

        public required Vector3Dto Position { get; init; }

        public required Vector3Dto RotationX { get; init; }

        public required Vector3Dto RotationY { get; init; }

        public required Vector3Dto RotationZ { get; init; }

        public required Vector3Dto Scale { get; init; }
    }
}
