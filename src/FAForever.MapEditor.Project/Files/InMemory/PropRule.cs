using System.Collections.Immutable;
using FAForever.MapEditor.Project.Editing;

namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// How the props of a rule gather (user story P-4).
    /// </summary>
    public enum PropClustering
    {
        /// <summary>Spread evenly over where the rule allows.</summary>
        None,

        /// <summary>In patches where a smooth noise is high: groves and open ground.</summary>
        Noise,

        /// <summary>In round groups around centres scattered at random: copses.</summary>
        Clusters,
    }

    /// <summary>
    /// A layer that allows or forbids the props of a rule where it covers the terrain.
    /// </summary>
    /// <param name="Layer">A mask for tools by name, or <c>stratum:N</c> (<see cref="MaskRuleSources"/>).</param>
    /// <param name="Threshold">From 0 to 1: the layer covers a place where it is at least this (and above 0).</param>
    /// <param name="Forbid">Whether props stay away from where it covers, rather than only go there.</param>
    public sealed record PropRuleCondition(string Layer, float Threshold = 0.5f, bool Forbid = false);

    /// <summary>
    /// A rule that places props from the layers when asked (user story P-4): a palette, where the
    /// layers allow and forbid, how many and how they gather and stand. The props it makes carry its
    /// <see cref="Name"/> (<see cref="Prop.Rule"/>), so generating again replaces them and never
    /// the props placed by hand.
    /// </summary>
    public sealed record PropRule
    {
        /// <summary>
        /// Tells the rule apart: unique in the map, and what its props carry.
        /// </summary>
        public required string Name { get; init; }

        /// <summary>
        /// The props it places, by weight.
        /// </summary>
        public ImmutableArray<PaletteEntry> Palette { get; init; } = [];

        /// <summary>
        /// Where it places: every allowing layer must cover a place and no forbidding one; none for
        /// anywhere on the map.
        /// </summary>
        public ImmutableArray<PropRuleCondition> Conditions { get; init; } = [];

        /// <summary>
        /// Whether fewer props grow where the allowing layers cover less: the chance of a place is
        /// the least of their values.
        /// </summary>
        public bool Fade { get; init; } = true;

        /// <summary>
        /// Props per 100 square ogrids (a square of 10 by 10) where everything allows.
        /// </summary>
        public float Density { get; init; } = 2;

        /// <summary>
        /// The least distance between two props' centres, in ogrids, to every prop of the rules.
        /// </summary>
        public float Spacing { get; init; } = 2;

        public PropClustering Clustering { get; init; } = PropClustering.None;

        /// <summary>
        /// The size of a patch (noise) or the radius of a group (clusters), in ogrids.
        /// </summary>
        public float ClusterSize { get; init; } = 24;

        /// <summary>
        /// How much of the allowed ground the patches or groups cover, from 0 to 1.
        /// </summary>
        public float ClusterCover { get; init; } = 0.5f;

        /// <summary>
        /// The least and most slope of the ground in degrees.
        /// </summary>
        public float MinSlope { get; init; }

        public float MaxSlope { get; init; } = 90;

        /// <summary>
        /// Whether props stay out of the water (on ground below its surface).
        /// </summary>
        public bool AboveWater { get; init; } = true;

        /// <summary>
        /// What fixes the places: the same rule on the same map gives the same props.
        /// </summary>
        public int Seed { get; init; } = 1;

        /// <summary>
        /// How each prop stands: aligned by its category (<see cref="PropOrientation"/>), random turn, tilt and scale.
        /// </summary>
        public PropPlacement Placement { get; init; } = new PropPlacement();

        /// <summary>
        /// The layers the rule reads.
        /// </summary>
        public IEnumerable<string> Layers => Conditions.Select(condition => condition.Layer);
    }
}
