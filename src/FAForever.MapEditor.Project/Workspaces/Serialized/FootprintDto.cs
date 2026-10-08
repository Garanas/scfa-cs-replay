namespace FAForever.MapEditor.Project.Workspaces.Serialized
{
    /// <summary>
    /// The space a unit takes up on the map, in ogrids along the map's x and z axes.
    /// </summary>
    public sealed record FootprintDto
    {
        public required double SizeX { get; init; }

        public required double SizeZ { get; init; }
    }
}
