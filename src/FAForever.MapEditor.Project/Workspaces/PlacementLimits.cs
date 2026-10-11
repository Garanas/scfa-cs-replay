using System.Collections.Immutable;

namespace FAForever.MapEditor.Project.Workspaces
{
    /// <summary>
    /// The ground a structure reserves around its footprint: a rectangle of <paramref name="SizeX"/>
    /// by <paramref name="SizeZ"/> ogrids whose corner lies <paramref name="OffsetX"/> and
    /// <paramref name="OffsetZ"/> from the footprint's corner (negative: beyond it), when the
    /// structure is not turned.
    /// </summary>
    public sealed record Skirt(double SizeX, double SizeZ, double OffsetX, double OffsetZ);

    /// <summary>
    /// What a unit's blueprint says about where it may stand (user story U-12), each part null
    /// when the blueprint says nothing: a check skips what it lacks.
    /// </summary>
    /// <param name="Skirt">The skirt.</param>
    /// <param name="Layers">The layers it may be built on: <c>Land</c>, <c>Water</c>, <c>Seabed</c>.</param>
    /// <param name="MaxSlope">The steepest ground, as the rise over the run (<c>Footprint.MaxSlope</c>).</param>
    /// <param name="MinWaterDepth">The least depth of the water it stands in, in game units.</param>
    /// <param name="MaxGroundVariation">How much the ground under a structure's skirt may rise and fall
    /// (<c>Physics.MaxGroundVariation</c>), in game units; the engine's default is 1.</param>
    /// <param name="FlattenSkirt">Whether the structure levels the ground under its skirt
    /// (<c>Physics.FlattenSkirt</c>): the engine then checks the ground around the skirt against the
    /// height it is levelled to.</param>
    public sealed record PlacementLimits(Skirt? Skirt, ImmutableArray<string>? Layers, double? MaxSlope, double? MinWaterDepth,
        double MaxGroundVariation = PlacementLimits.DefaultGroundVariation, bool FlattenSkirt = false)
    {
        /// <summary>
        /// The engine's <c>MaxGroundVariation</c> for a blueprint that gives none.
        /// </summary>
        public const double DefaultGroundVariation = 1;
    }
}
