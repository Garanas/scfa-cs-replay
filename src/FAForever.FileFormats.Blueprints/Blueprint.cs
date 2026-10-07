using FAForever.FileFormats.Lua;

namespace FAForever.FileFormats.Blueprints
{
    /// <summary>
    /// A table of a blueprint file. The typed properties cover the commonly used fields, named
    /// after their keys in the file; <see cref="Raw"/> holds the complete table, for everything else.
    /// Scalars are null when the file does not set them (the game then uses its default); lists,
    /// sets and dictionaries are empty instead.
    /// </summary>
    public abstract record BlueprintTable
    {
        /// <summary>
        /// The table as written in the file.
        /// </summary>
        public required LuaData.Table Raw { get; init; }
    }

    /// <summary>
    /// A blueprint as defined by a <c>.bp</c> file, before any of the game's post-processing
    /// (merging mod blueprints, <c>ModBlueprints</c>, extracted mesh blueprints, ...). See
    /// <c>engine/Core/Blueprints/*.lua</c> in the FA repository for what the fields mean.
    /// </summary>
    public abstract record Blueprint : BlueprintTable
    {
        /// <summary>
        /// The id the game gives it (see <see cref="BlueprintParser"/>).
        /// </summary>
        public required string BlueprintId { get; init; }

        /// <summary>
        /// The game path it came from, e.g. <c>/units/uel0101/uel0101_unit.bp</c>.
        /// </summary>
        public required string Source { get; init; }
    }

    /// <summary>
    /// A blueprint of something that exists in the world: a unit, projectile or prop.
    /// </summary>
    public abstract record BlueprintEntity : Blueprint
    {
        public string? Description { get; init; }

        /// <summary>
        /// Upper case category names, e.g. <c>TECH1</c>, <c>LAND</c>, <c>UEF</c>.
        /// </summary>
        public IReadOnlyList<string> Categories { get; init; } = [];

        public string? StrategicIconName { get; init; }

        /// <summary>
        /// The size of the hitbox.
        /// </summary>
        public double? SizeX { get; init; }

        public double? SizeY { get; init; }

        public double? SizeZ { get; init; }

        public bool HasCategory(string category) => Categories.Contains(category, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A sound, written as <c>Sound { Bank = 'UEL', Cue = 'UEL0101_Move_Loop', LodCutoff = 'UnitMove_LodCutoff' }</c>.
    /// </summary>
    public sealed record BlueprintSound : BlueprintTable
    {
        public string? Bank { get; init; }

        public string? Cue { get; init; }

        public string? LodCutoff { get; init; }

        internal static BlueprintSound Read(LuaTableReader t) => new BlueprintSound
        {
            Raw = t.Table,
            Bank = t.String("Bank"),
            Cue = t.String("Cue"),
            LodCutoff = t.String("LodCutoff"),
        };
    }

    /// <summary>
    /// The space a unit or prop takes up on the map, in map cells.
    /// </summary>
    public sealed record BlueprintFootprint : BlueprintTable
    {
        public double? SizeX { get; init; }

        public double? SizeZ { get; init; }

        public double? MaxSlope { get; init; }

        public double? MinWaterDepth { get; init; }

        internal static BlueprintFootprint Read(LuaTableReader t) => new BlueprintFootprint
        {
            Raw = t.Table,
            SizeX = t.Number("SizeX"),
            SizeZ = t.Number("SizeZ"),
            MaxSlope = t.Number("MaxSlope"),
            MinWaterDepth = t.Number("MinWaterDepth"),
        };
    }

    /// <summary>
    /// The mesh of a unit, projectile or prop (its <c>Display.Mesh</c>).
    /// </summary>
    public sealed record BlueprintDisplayMesh : BlueprintTable
    {
        public double? IconFadeInZoom { get; init; }

        public IReadOnlyList<BlueprintMeshLod> LODs { get; init; } = [];

        internal static BlueprintDisplayMesh Read(LuaTableReader t) => new BlueprintDisplayMesh
        {
            Raw = t.Table,
            IconFadeInZoom = t.Number("IconFadeInZoom"),
            LODs = t.List("LODs", BlueprintMeshLod.Read),
        };
    }
}
