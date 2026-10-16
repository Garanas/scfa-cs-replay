namespace FAForever.MapEditor.Project.Files.Serialized
{
    /// <summary>
    /// How the map's generated results are made (the Procedural mode); absent when nothing differs
    /// from the editor's defaults.
    /// </summary>
    public sealed record ProceduralDto
    {
        /// <summary>
        /// The ids of the generators that regenerate on their own after a change (<c>walkability</c>).
        /// </summary>
        public List<string>? AutoGenerate { get; init; }

        /// <summary>
        /// The ids of the generators the export does not make again when out of date (<c>normalMap</c>).
        /// </summary>
        public List<string>? NotOnExport { get; init; }

        /// <summary>
        /// The masks for tools written beside the draft heightmap for World Machine or Gaea
        /// (<c>procedural/temp/draft-mask-&lt;name&gt;.png</c>); absent for none.
        /// </summary>
        public List<string>? DraftMasks { get; init; }

        /// <summary>
        /// Whether the draft heightmap and those masks are written again whenever they change;
        /// absent for no.
        /// </summary>
        public bool? WriteDraft { get; init; }

        /// <summary>
        /// How the path networks are generated, whether the export writes them, and the edits by hand
        /// on top of them; absent for the defaults.
        /// </summary>
        public PathNodesDto? PathNodes { get; init; }

        /// <summary>
        /// How the sky cube map is generated; absent for the defaults.
        /// </summary>
        public SkyCubeDto? SkyCube { get; init; }
    }
}
