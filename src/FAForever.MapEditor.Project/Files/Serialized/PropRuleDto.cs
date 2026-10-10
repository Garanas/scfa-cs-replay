using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Files.Serialized
{
    /// <summary>
    /// A rule that places props from the layers (user story P-4); <c>prop-rules.json</c> holds the
    /// map's rules in order. A value left out has the editor's default.
    /// </summary>
    public sealed record PropRuleDto
    {
        public required string Name { get; init; }

        public IReadOnlyList<PaletteEntryDto> Palette { get; init; } = [];

        public IReadOnlyList<PropRuleConditionDto> Conditions { get; init; } = [];

        public bool Fade { get; init; } = true;

        public float Density { get; init; } = 2;

        public float Spacing { get; init; } = 2;

        public PropClustering Clustering { get; init; } = PropClustering.None;

        public float ClusterSize { get; init; } = 24;

        public float ClusterCover { get; init; } = 0.5f;

        public float MinSlope { get; init; }

        public float MaxSlope { get; init; } = 90;

        public bool AboveWater { get; init; } = true;

        public int Seed { get; init; } = 1;

        public PropAlignment Alignment { get; init; } = PropAlignment.ByCategory;

        public bool RandomTurn { get; init; } = true;

        public float RandomTilt { get; init; }

        public float ScaleMin { get; init; } = 1;

        public float ScaleMax { get; init; } = 1;

        public float Sink { get; init; }
    }

    /// <summary>
    /// A prop of a rule's palette and its weight.
    /// </summary>
    public sealed record PaletteEntryDto
    {
        /// <summary>
        /// E.g. <c>/env/evergreen/props/trees/pine06_prop.bp</c>.
        /// </summary>
        public required string Blueprint { get; init; }

        public double Weight { get; init; } = 1;
    }

    /// <summary>
    /// A layer that allows (or forbids) a rule's props where it covers at least the threshold.
    /// </summary>
    public sealed record PropRuleConditionDto
    {
        /// <summary>
        /// A mask for tools by name, or <c>stratum:N</c>.
        /// </summary>
        public required string Layer { get; init; }

        public float Threshold { get; init; } = 0.5f;

        public bool? Forbid { get; init; }
    }
}
