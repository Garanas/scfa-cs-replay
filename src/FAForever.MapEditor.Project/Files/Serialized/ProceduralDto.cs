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
        /// The starts the navigation mesh measures routes between: <c>None</c>, <c>Nearest</c> or
        /// <c>All</c>; absent for <c>Nearest</c>.
        /// </summary>
        public string? Routes { get; init; }
    }
}
