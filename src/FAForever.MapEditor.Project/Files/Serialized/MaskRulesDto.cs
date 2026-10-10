using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Files.Serialized
{
    /// <summary>
    /// The rules a mask for tools is made from (user story K-2): <c>masks/&lt;name&gt;.rules.json</c>
    /// beside the mask's PNG, which holds the last generated result.
    /// </summary>
    public sealed record MaskRulesDto
    {
        public required IReadOnlyList<MaskRuleDto> Rules { get; init; }
    }

    /// <summary>
    /// One rule of a mask; only the parameters its kind reads are written.
    /// </summary>
    public sealed record MaskRuleDto
    {
        public required MaskRuleKind Kind { get; init; }

        public MaskCombine? Combine { get; init; }

        public bool? Invert { get; init; }

        public float? Min { get; init; }

        public float? Max { get; init; }

        public float? Soft { get; init; }

        /// <summary>
        /// A mask for tools by name, or <c>stratum:N</c>.
        /// </summary>
        public string? Source { get; init; }

        public float? Scale { get; init; }

        public int? Seed { get; init; }

        public float? Radius { get; init; }
    }
}
