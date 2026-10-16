namespace FAForever.MapEditor.Project.Files.Serialized
{
    /// <summary>
    /// How the path networks are generated and the edits by hand on top of them
    /// (<c>procedural.pathNodes</c> in <c>map.json</c>); every property is optional and absent for
    /// the default.
    /// </summary>
    public sealed record PathNodesDto
    {
        /// <summary>
        /// The layers generated (<c>Land</c>, <c>Water</c>, <c>Amphibious</c>, <c>Air</c>); absent for all four.
        /// </summary>
        public List<string>? Layers { get; init; }

        /// <summary>
        /// About how far apart the nodes lie, in ogrids; absent for the map's size.
        /// </summary>
        public float? Spacing { get; init; }

        /// <summary>
        /// How far apart the air nodes lie, in ogrids; absent for a grid of 16 by 16.
        /// </summary>
        public float? AirSpacing { get; init; }

        /// <summary>
        /// The rounds of smoothing; absent for 3.
        /// </summary>
        public int? Smoothing { get; init; }

        /// <summary>
        /// How close two nodes may lie, in ogrids; absent for 0.4 of the spacing.
        /// </summary>
        public float? MinimumDistance { get; init; }

        /// <summary>
        /// False when open ground away from the skeleton gets no nodes; absent for true.
        /// </summary>
        public bool? Fill { get; init; }

        /// <summary>
        /// True when the export removes the map's own path nodes of the generated layers.
        /// </summary>
        public bool? Replace { get; init; }

        /// <summary>
        /// Whether the export writes the generated networks; absent when never chosen (off).
        /// </summary>
        public bool? OnExport { get; init; }

        /// <summary>
        /// The edits by hand, in the order they were made.
        /// </summary>
        public List<PathEditDto>? Edits { get; init; }
    }

    /// <summary>
    /// An edit by hand on top of a generated path network. A node is a generated one by the place it
    /// was generated at (<c>x</c>, <c>z</c>), or one added by hand by its number (<c>added</c>).
    /// </summary>
    public sealed record PathEditDto
    {
        /// <summary>
        /// <c>Add</c>, <c>Move</c>, <c>Remove</c>, <c>Link</c> or <c>Cut</c>.
        /// </summary>
        public string Kind { get; init; } = "";

        public string Layer { get; init; } = "";

        public float X { get; init; }

        public float Z { get; init; }

        /// <summary>
        /// The number of a node added by hand; absent for a generated node.
        /// </summary>
        public int? Added { get; init; }

        /// <summary>
        /// The other end of a link added or cut.
        /// </summary>
        public float? OtherX { get; init; }

        public float? OtherZ { get; init; }

        public int? OtherAdded { get; init; }

        /// <summary>
        /// Where a moved node goes.
        /// </summary>
        public float? ToX { get; init; }

        public float? ToZ { get; init; }
    }
}
