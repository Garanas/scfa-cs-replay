namespace FAForever.MapEditor.Project.Workspaces.Serialized
{
    /// <summary>
    /// The space a unit takes up on the map, in ogrids along the map's x and z axes, with the
    /// ground it allows (<c>Footprint.MaxSlope</c>, <c>MinWaterDepth</c>).
    /// </summary>
    public sealed record FootprintDto
    {
        public required double SizeX { get; init; }

        public required double SizeZ { get; init; }

        /// <summary>
        /// The steepest ground it stands on: the rise over the run, as the blueprint gives it.
        /// </summary>
        public double? MaxSlope { get; init; }

        /// <summary>
        /// The least depth of water it stands in, in game units.
        /// </summary>
        public double? MinWaterDepth { get; init; }
    }

    /// <summary>
    /// The ground a structure reserves around itself (<c>Physics.SkirtSizeX</c> and the rest): a
    /// rectangle that starts <see cref="OffsetX"/> and <see cref="OffsetZ"/> from the footprint's
    /// corner.
    /// </summary>
    public sealed record SkirtDto
    {
        public required double SizeX { get; init; }

        public required double SizeZ { get; init; }

        public double OffsetX { get; init; }

        public double OffsetZ { get; init; }
    }
}
