using System.Collections.Immutable;

namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// How the AI's path networks are generated from the navigation mesh (the Procedural mode, user
    /// story R-3), whether the export writes them, and the edits made by hand on top of them. Kept in
    /// <c>map.json</c> (<c>procedural.pathNodes</c>); the generated network itself lives in memory and
    /// is written as markers only into the files an export writes.
    /// </summary>
    public sealed record PathNodeSettings
    {
        public static PathNodeSettings Default { get; } = new PathNodeSettings();

        /// <summary>
        /// The layers generated, in order; every layer unless told.
        /// </summary>
        public ImmutableArray<PathLayer> Layers { get; init; } = [PathLayer.Land, PathLayer.Water, PathLayer.Amphibious, PathLayer.Air];

        /// <summary>
        /// About how far apart the nodes of land, water and amphibious networks lie, in ogrids; null
        /// for the map's size (<c>PathNetworks.DefaultSpacing</c>).
        /// </summary>
        public float? Spacing { get; init; }

        /// <summary>
        /// How far apart the nodes of the air network lie, in ogrids; null for a grid of 16 by 16 over
        /// the playable area, as the game's own maps have.
        /// </summary>
        public float? AirSpacing { get; init; }

        /// <summary>
        /// How many rounds each node moves halfway toward the middle of its neighbours (0 to 10).
        /// </summary>
        public int Smoothing { get; init; } = DefaultSmoothing;

        public const int DefaultSmoothing = 3;

        /// <summary>
        /// How close two nodes may lie before one is dropped, in ogrids; null for 0.4 of the spacing.
        /// </summary>
        public float? MinimumDistance { get; init; }

        /// <summary>
        /// Whether open ground away from the skeleton gets nodes too, about a spacing apart.
        /// </summary>
        public bool Fill { get; init; } = true;

        /// <summary>
        /// Whether the export removes the map's own path nodes of the generated layers (placed by hand,
        /// or generated into the markers by an older editor). Off, both are written and each piece of
        /// the map's own nodes is linked to the generated network.
        /// </summary>
        public bool Replace { get; init; }

        /// <summary>
        /// Whether the export writes the generated networks into the map's markers; null when never
        /// chosen, which is off (the first Generate turns it on).
        /// </summary>
        public bool? OnExport { get; init; }

        /// <summary>
        /// The edits made by hand on top of the generated networks, in the order they were made;
        /// applied again after every generation (<c>PathNetworkEdits.Apply</c>).
        /// </summary>
        public ImmutableArray<PathEdit> Edits { get; init; } = [];

        public bool WritesOnExport => OnExport == true;

        public bool IsDefault => Equals(Default);

        /// <summary>
        /// What the generation reads of the settings: a change to the edits or the export leaves the
        /// generated networks current.
        /// </summary>
        public PathNodeGeneration Generation => new PathNodeGeneration(string.Join(",", Layers), Spacing, AirSpacing, Smoothing, MinimumDistance, Fill);

        public bool Equals(PathNodeSettings? other) =>
            other is not null && Layers.SequenceEqual(other.Layers) && Spacing == other.Spacing && AirSpacing == other.AirSpacing
            && Smoothing == other.Smoothing && MinimumDistance == other.MinimumDistance && Fill == other.Fill && Replace == other.Replace
            && OnExport == other.OnExport && Edits.SequenceEqual(other.Edits);

        public override int GetHashCode() => HashCode.Combine(Layers.Length, Spacing, AirSpacing, Smoothing, MinimumDistance, Replace, OnExport, Edits.Length);
    }

    /// <summary>
    /// The part of <see cref="PathNodeSettings"/> the generation reads, compared by value.
    /// </summary>
    public sealed record PathNodeGeneration(string Layers, float? Spacing, float? AirSpacing, int Smoothing, float? MinimumDistance, bool Fill);

    /// <summary>
    /// What an edit by hand does to a generated path network.
    /// </summary>
    public enum PathEditKind
    {
        /// <summary>A node placed by hand, linked to its nearest generated neighbours.</summary>
        Add,

        /// <summary>A generated node moved to another place.</summary>
        Move,

        /// <summary>A generated node removed with its links.</summary>
        Remove,

        /// <summary>A link added between two nodes.</summary>
        Link,

        /// <summary>A link between two nodes cut.</summary>
        Cut,
    }

    /// <summary>
    /// A node an edit refers to: a generated node by the place it was generated at (matched again to
    /// the nearest generated node after every generation), or a node added by hand by its number.
    /// </summary>
    /// <param name="Added">The number of a node added by hand (from 1); 0 for a generated node.</param>
    public readonly record struct PathNodeRef(float X, float Z, int Added = 0)
    {
        public bool IsAdded => Added > 0;

        public static PathNodeRef Generated(float x, float z) => new PathNodeRef(x, z);

        public override string ToString() => IsAdded ? $"added node {Added}" : $"node at ({X:0.#}, {Z:0.#})";
    }

    /// <summary>
    /// An edit by hand on top of a generated path network (one step of undo each).
    /// </summary>
    /// <param name="Node">The node: for <see cref="PathEditKind.Add"/> the new node with its place and
    /// number; for the others the node moved, removed, or the first end of the link.</param>
    public sealed record PathEdit(PathEditKind Kind, PathLayer Layer, PathNodeRef Node)
    {
        /// <summary>
        /// The other end of a link added or cut.
        /// </summary>
        public PathNodeRef Other { get; init; }

        /// <summary>
        /// Where a moved node goes, in ogrids.
        /// </summary>
        public float ToX { get; init; }

        public float ToZ { get; init; }

        /// <summary>
        /// Whether the edit refers to a node (by either end of a link).
        /// </summary>
        public bool Refers(PathNodeRef node) => Node == node || ((Kind is PathEditKind.Link or PathEditKind.Cut) && Other == node);
    }
}
