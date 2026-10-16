namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// One of the map's two heightmaps (<see cref="Editing.HeightStacks"/>): the final, which the
    /// game gets, or the draft, the rough shape and the input of World Machine or Gaea.
    /// </summary>
    public enum HeightStackKind
    {
        /// <summary>
        /// <see cref="MapDocument.HeightStack"/>, whose composite is the terrain's heightmap: what
        /// the export, walkability, the normal map and the other generators read.
        /// </summary>
        Final,

        /// <summary>
        /// <see cref="MapDocument.DraftStack"/>, whose composite is <see cref="MapDocument.DraftHeightmap"/>.
        /// </summary>
        Draft,
    }

    /// <summary>
    /// Which of the two heightmaps a height layer belongs to (<see cref="Editing.HeightStacks.UseOf"/>).
    /// </summary>
    public enum LayerUse
    {
        /// <summary>
        /// Both: one layer with one grid, at a place of its own in each stack and shown or hidden
        /// in each on its own.
        /// </summary>
        Shared,

        /// <summary>
        /// The draft only (the blockout the procedural tool replaces).
        /// </summary>
        Draft,

        /// <summary>
        /// The final only (the procedural tool's output as its base, touch-ups on it).
        /// </summary>
        Final,
    }
}
