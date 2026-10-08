using System.Collections.Immutable;

namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// A map as the editor holds it, in the editor's own terms: one immutable version of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A change makes a new document that shares everything it does not change with the old one:
    /// records with <c>with</c>, lists as immutable arrays, and grids (heightmap, masks, images) as
    /// <see cref="TiledBuffer{T}"/>s that share their unchanged tiles. Keeping old versions is
    /// therefore cheap, and <see cref="Editing.EditSession"/> keeps them for undo and redo.
    /// </para>
    /// <para>
    /// What follows from something else is not stored: the size comes from the heightmap, the paths
    /// of the map's files from <see cref="Name"/>, <see cref="FileName"/> and <see cref="Version"/>,
    /// and the decoration of markers (colour, editor prop) from their kind. <see cref="MapImporter"/>
    /// and <see cref="MapExporter"/> translate between a document and the game's files;
    /// <see cref="MapProjectStore"/> keeps a document on disk.
    /// </para>
    /// </remarks>
    public sealed record MapDocument
    {
        /// <summary>
        /// The name of the map's folder without its version, e.g. <c>theta_passage</c>.
        /// </summary>
        public required string Name { get; init; }

        /// <summary>
        /// The name the map's files start with: <c>&lt;FileName&gt;.scmap</c>, <c>&lt;FileName&gt;_save.lua</c>, ...
        /// </summary>
        public required string FileName { get; init; }

        /// <summary>
        /// The version of the map in the vault; the folder gets <c>.v0004</c> from it.
        /// </summary>
        public int? Version { get; init; }

        public MapFolderName Folder => new MapFolderName(Name, Version);

        /// <summary>
        /// The width of the map in ogrids, from the heightmap.
        /// </summary>
        public int Width => Terrain.Heightmap.Width;

        /// <summary>
        /// The height of the map in ogrids, from the heightmap.
        /// </summary>
        public int Height => Terrain.Heightmap.Height;

        public required ScenarioInfo Scenario { get; init; }

        public required Terrain Terrain { get; init; }

        public required Lighting Lighting { get; init; }

        public required Water Water { get; init; }

        public required Sky Sky { get; init; }

        /// <summary>
        /// The colours of the minimap; absent in version 53.
        /// </summary>
        public Minimap? Minimap { get; init; }

        public required DecalHeader DecalHeader { get; init; }

        public ImmutableArray<Decal> Decals { get; init; } = [];

        public ImmutableArray<DecalGroup> DecalGroups { get; init; } = [];

        /// <summary>
        /// The props, each with an <see cref="Prop.Id"/> no other prop of the document has.
        /// </summary>
        public ImmutableArray<Prop> Props { get; init; } = [];

        public required MarkerSet Markers { get; init; }

        public ImmutableArray<Army> Armies { get; init; } = [];

        /// <summary>
        /// The map's script (<c>_script.lua</c>) as its bytes; null for a map without one.
        /// </summary>
        public ImmutableArray<byte>? Script { get; init; }

        /// <summary>
        /// The other files of the map's folder (textures, Lua files the script loads, ...) by their
        /// path in the folder, with <c>/</c> between folders.
        /// </summary>
        public ImmutableSortedDictionary<string, ImmutableArray<byte>> Files { get; init; } =
            ImmutableSortedDictionary.Create<string, ImmutableArray<byte>>(StringComparer.Ordinal);

        /// <summary>
        /// An id for a new prop: one more than the highest in use.
        /// </summary>
        public int NextPropId() => Props.IsEmpty ? 1 : Props.Max(prop => prop.Id) + 1;
    }
}
